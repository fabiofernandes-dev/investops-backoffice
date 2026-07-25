# Design — InvestOps Backoffice (Fase 9: Processing Monitor)

**Data:** 2026-07-25
**Status:** Aprovado (início da Fase 9)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§9 tela Processing Monitor, §13 testes, §5 ProcessingEvent, §6 Regra 6)
**Depende de:** Fase 4a (read API — `IDbSession`, `PagedResult`, `SortClauseBuilder`), Fase 4b (frontend + NgRx), Fase 5a (retry + DLQ no worker).

Expõe uma tela de **monitoramento global dos eventos do worker** (`processing_events`): lista paginada com filtro e **auto-refresh por polling**. Como pré-requisito, **enriquece o worker Go** para registrar uma falha terminal — ao esgotar as tentativas e rotear para a DLQ, o worker marca a operação como `Failed` e grava um `processing_event` `Failed` com **tentativas e erro reais**, dando conteúdo às colunas **Tentativas/Erro** do SPEC §9. Slice completo: **worker (Go) + read API (.NET) + frontend (Angular)**.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Escopo | **Slice completo**: worker Go + read API .NET + frontend Angular; PRs 9.1 (worker) → 9.2 (read API) → 9.3 (frontend) |
| Atualização da tela | **Lista paginada + filtro + botão Atualizar + toggle auto-refresh** (polling ~5s); **sem SSE** |
| Falha terminal no worker | Ao **esgotar retries → DLQ**, gravar `processing_event` `Failed` (`attempt_count`/`error_message` reais) **e** marcar a **operação `Failed`** |
| Semântica de falha | Só **falhas transitórias** entram no ciclo retry→DLQ→`Failed`; **rejeições de negócio** seguem `Rejected` (imediato, sem retry) — comportamento atual mantido |
| Mecanismo (worker) | **Hook `onExhausted`** no `Consumer` (mensageria), **implementado na camada `operations`** (parseia o evento, abre tx, marca `Failed` + grava o `processing_event`) |
| `event_type` do Failed | **`OperationFailed`** (novo tipo no `processing_events`); **não** publica no Outbox (não há consumidor) — evolução futura |
| Migration | **Nenhuma** — `processing_events` já tem `attempt_count`, `error_message`, `processed_at` |
| Status | `StatusFailed = "Failed"` **novo** no worker (Go); `OperationStatus.Failed` **já existe** no .NET |
| Bônus | Marcar a operação `Failed` ativa o botão **Reprocessar** (já existente na tela de operações, condicional a `Failed`) |

## 2. Worker Go — falha terminal no esgotamento de retries (PR 9.1)

Hoje o worker grava `processing_events` **só** para `OperationProcessed`/`OperationRejected`, sempre com `attempt_count = 0` e `error_message` nulo, e **nada** ao rotear para a DLQ.

- **Gatilho:** em `messaging/consumer.go`, no `handle`, no ramo de esgotamento (`attempt >= maxAttempts`), **antes** do `publishToDlq`, chamar `c.onExhausted(ctx, delivery.Body, attempt, err)` — um callback opcional (`func(ctx, body []byte, attempt int, cause error)`) no `Consumer`. Se `onExhausted` for `nil`, o comportamento é o atual (protege os testes existentes que criam `NewConsumer` sem callback). Erros do callback são **logados e não bloqueiam** o roteamento à DLQ. **Limitação conhecida:** se o `publishToDlq` falhar **após** o `onExhausted` já ter rodado, a mensagem é reenfileirada e pode re-esgotar — o `UPDATE` de status é idempotente, mas o `InsertProcessingEvent` não, então uma duplicata rara do `processing_event` `OperationFailed` é possível. Aceitável (raro).
- **Implementação (camada `operations`):** `MarkExhausted(ctx, body, attempt, cause)` — faz seu próprio `json.Unmarshal` do `OperationImportedEvent` (não há parser compartilhado — o handler em `main.go` também desserializa inline), abre `store.WithinTx` e, numa transação:
  - **Novo método no `TxRepo`** `MarkOperationFailed(ctx, id)` = `UPDATE operations SET status='Failed', updated_at=now() WHERE id=@id AND status NOT IN ('Processed','Rejected')` — o guard vive no **SQL** (não clobbera resultado de negócio; um `Failed` prévio pode ser regravado). **Não** seta `rejection_reason` (coluna semântica das rejeições de negócio; o motivo da falha fica em `processing_events.error_message`). Por ser método novo do `TxRepo`, **precisa ser implementado no `txRepo` real (`database/store.go`) e no `fakeTx` de `operations/processor_test.go`** — senão o pacote `operations` não compila e os testes atuais quebram.
  - `InsertProcessingEvent` com `EventType="OperationFailed"`, `Status=StatusFailed`, `AttemptCount=attempt`, `ErrorMessage=cause.Error()`, `payload` com um resumo do erro.
- **Mudanças de apoio:**
  - `StatusFailed = "Failed"` em `operations/model.go`.
  - `operations.ProcessingEvent` ganha `AttemptCount int` e `ErrorMessage string`; `database/store.go#InsertProcessingEvent` passa a persistir esses campos (hoje o SQL fixa `attempt_count = 0` e **não grava `error_message`** — as linhas atuais têm `error_message = NULL`). Para manter os writes `Processed`/`Rejected` **inalterados**, mapear **`ErrorMessage == "" → NULL`** (como o `UpdateOperationStatus` já faz com `reason == "" → NULL`), não string vazia. `processed_at` continua `now()` (para `Failed`, é o instante da falha).
  - Wire do `onExhausted` no `cmd/worker/main.go` (compõe o `Consumer` com o callback de `operations`).
- **Idempotência do worker:** o skip de reprocessamento (`processor.go`: `op.Status == Processed || Rejected → skip`) **não** inclui `Failed`, então uma operação `Failed` republicada via **Reprocessar** volta a ser processada normalmente.
- **Testes (Go):** (a) handler que sempre falha → após `maxAttempts`, operação vira `Failed` + `processing_event` `OperationFailed` com `attempt_count = maxAttempts` e `error_message` preenchido; (b) idempotência — como o guard está no `WHERE` do SQL (o `fakeTx` não o replica), este caso é **teste de integração (Testcontainers)**: operação já `Processed` ⇒ `MarkOperationFailed` **não** altera o status; (c) caminho `Processed`/`Rejected` existente inalterado — `attempt_count = 0` e `error_message = NULL`.

## 3. Backend read API — `GET /api/processing-events` (PR 9.2)

Lista **global** e paginada dos eventos do worker (o `GetByOperationAsync` atual é por operação e permanece).

- **DTO** `ProcessingEventListItemDto(Guid Id, Guid OperationId, string ExternalId, string EventType, string Status, int AttemptCount, string? ErrorMessage, DateTime CreatedAt, DateTime? ProcessedAt)` — `ExternalId` via **join** `processing_events → operations`. Timestamps como `DateTime` (convenção Npgsql/`PositionDto`).
- **Query/sort:** `ProcessingEventsQuery(int Page=1, int PageSize=20, string? Sort=null, string? Status=null)` (com `NormalizedPage/PageSize/Offset`); `ProcessingEventsSortClauseBuilder` (whitelist camelCase: `createdAt` [default `desc`] → `pe.created_at`, `status` → `pe.status`, `attemptCount` → `pe.attempt_count`).
- **Repo:** estende `IProcessingEventReadRepository` com `ListAsync(ProcessingEventsQuery, ct): PagedResult<ProcessingEventListItemDto>` (join + `WHERE pe.status = @Status` opcional + `COUNT(*)` com o mesmo where), espelhando `PositionReadRepository`/`ReconciliationReadRepository`.
- **Handler/Controller:** `ListProcessingEventsHandler` → `PagedResult`; `ProcessingEventsController` (`[Route("api/processing-events")]`, `[HttpGet]`) — **read-only** (sem política de rate-limit de escrita). DI dos dois.
- **Testes (integração, Testcontainers):** seed via SQL de operações + `processing_events` variados (`Processed`, `Rejected`, `Failed` com `attempt_count`/`error_message`) → lista com paginação; filtro por `status`; join traz o `externalId`; a linha `Failed` mostra `attempt_count`/`error_message`.

## 4. Frontend — feature `processing-monitor` (PR 9.3)

Espelha `positions`/`operations` (standalone, `OnPush`, signals, NgRx, Material).

- **`core/api/processing-events.api.ts`** — `list(query): Paged<ProcessingEventItem>`.
- **Models:** `ProcessingEventItem { id, operationId, externalId, eventType, status, attemptCount, errorMessage, createdAt, processedAt }`; `ProcessingEventsQuery { page, pageSize, sort?, status? }`.
- **NgRx:** `load({ query, silent? })`/`loadSuccess`/`loadFailure`; state `{ query, items, total, loading, error }`; `sort` default `createdAt:desc`. O reducer só liga `loading` quando `silent` é **falso** — assim o auto-refresh (`silent: true`) **não** pisca o loading/empty-state. **`autoRefresh` é view-state local do componente** (um `signal`, como `pageIndex`/`sort`/`statusFilter`), **não** entra no NgRx (segue o padrão das outras telas).
- **Auto-refresh:** no componente, `timer(0, 5000)` **gated** pelo signal `autoRefresh()` **e** pela visibilidade da aba (`document.hidden` / `visibilitychange`); cada tick despacha `load({ query: queryAtual, silent: true })`. Pausa quando o toggle está off ou a aba está oculta; `takeUntilDestroyed`. Botão **Atualizar** despacha `load({ query, silent: false })`; load inicial também `silent: false`.
- **Tela** (`/processing-monitor`, item no nav — ícone `monitor_heart`): tabela server-side (paginação/ordenação/filtro por status) com as colunas do **SPEC §9**: **Evento** (`eventType`), **Operação** (`externalId`, link para `/operations/{operationId}`), **Status** (chip, incl. `Failed`), **Tentativas** (`attemptCount`), **Erro** (`errorMessage`), **Criado em**, **Processado em**. Filtro `mat-select` de status (`Processed`/`Rejected`/`Failed`/todos, case-sensitive). Loading/erro/vazio como as demais telas.
- **`status-chip`:** `Failed` **já está estilizado** (laranja `#ef6c00`) — **nenhuma mudança** (o vermelho `#c62828` já é `Rejected`).
- **Testes (Jest):** tabela renderiza; filtro de status despacha `load`; **auto-refresh**: com o toggle ligado, `fakeAsync`+`tick(5000)` dispara um novo `load`; desligar para o polling; link para o detalhe da operação presente; error state.

## 5. Docs

- **README — Status & roadmap:** *Processing monitor* de 🔲 para **✅**.
- **[ADR 0008]** — "Falha terminal registrada no esgotamento de retries": por que o worker marca a operação `Failed` + grava `processing_event` `OperationFailed` no ponto da DLQ (observabilidade do retry/DLQ e ativação do Reprocessar), por que **não** publica `OperationFailed` no Outbox (sem consumidor), e a distinção transitório × rejeição de negócio.

## 6. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 9.1 | `feature/worker-operation-failed` | **Go**: `StatusFailed`; `ProcessingEvent`+`attempt_count`/`error_message`; hook `onExhausted` + `MarkExhausted` (op `Failed` condicional + `processing_event` `OperationFailed`); wire no `main.go`; **ADR 0008**; testes |
| 9.2 | `feature/processing-events-read` | **.NET**: `ProcessingEventListItemDto` + `ProcessingEventsQuery`/`SortClauseBuilder` + `ListAsync` (join) + `ListProcessingEventsHandler` + `ProcessingEventsController` + DI + testes de integração |
| 9.3 | `feature/web-processing-monitor` | **Angular**: `processing-events.api.ts` + models + NgRx + tela (tabela + filtro + Atualizar + auto-refresh) + rota/nav + `status-chip` Failed + README roadmap + testes Jest |

**Ordem:** 9.1 → 9.2 → 9.3 (o worker produz o dado; a API o expõe; a tela o consome). Cada uma um PR para `developer`.

## 7. Fora de escopo (Fase 9)

- **Publicar `OperationFailed` no Outbox/RabbitMQ** (não há consumidor) — só `processing_event` + status da operação.
- **SSE ao vivo** — a atualização é por **polling** (decisão).
- **Ação de retry/resolver a partir do monitor** — o **Reprocessar** já existe na tela de operações (agora funcional para `Failed`).
- Enriquecer o monitor com nomes de cliente/produto (só `externalId` + link) — evolução se necessário.
- Autenticação/autorização (SPEC §15, [ADR 0006](../../adr/0006-defer-authentication-adopt-rate-limiting.md)).
