# Design — InvestOps Backoffice (Fase 5a: Retry + DLQ + OperationFailed)

**Data:** 2026-06-15
**Status:** Aprovado (início da Fase 5a)
**Contexto:** primeira sub-fase da Fase 5 (qualidade sênior). Hoje o worker faz `Nack(requeue=true)` → **loop infinito** em erro transitório. Esta fase adiciona **retry com atraso (backoff) + limite + DLQ + status `Failed` + evento `OperationFailed`**.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Filas | **Quorum** (durável/moderno) |
| Retry | **Backoff por TTL + wait queue** (atraso entre tentativas) |
| Limite | Contagem por **`x-death`** (não `x-delivery-limit`) |
| Terminal | Após N tentativas → **DLQ**; um **consumer da DLQ** marca `Failed` + `OperationFailed` |
| Falha vs Rejeição | **Rejeição de negócio** (suitability, posição insuficiente) = `Rejected` terminal, **sem retry**. **Erro transitório/infra** (DB, exceção) = retry → `Failed` |

> **Por que `x-death` e não `x-delivery-limit`:** o backoff exige o round-trip pela wait queue (DLX→TTL→DLX), que **reenfileira** a mensagem e **zera** o `delivery-count` do quorum. Então o limite é contado pelo header `x-death` (que **acumula** a cada dead-letter). Filas continuam quorum; o `x-delivery-limit` não é usado.

## 2. Topologia RabbitMQ

```text
investops (topic) ──rk OperationImported──► operations.processing (quorum)
                                              │ x-dead-letter-exchange: investops.retry
                                              ▼ (Nack requeue=false em erro transitório)
investops.retry (direct) ─────────────────► operations.retry (quorum)
                                              │ x-message-ttl: BACKOFF_MS
                                              │ x-dead-letter-exchange: investops (rk OperationImported)
                                              ▼ (após o TTL, volta à principal → retry com atraso)
                                            operations.processing  ... (repete; x-death incrementa)

  Após N tentativas: o worker PUBLICA na investops.dlq ──► operations.dlq (quorum)
                                                              ▼
                                            [DLQ consumer] marca Failed + OperationFailed
```
- `operations.processing` (quorum): `x-dead-letter-exchange=investops.retry`.
- `operations.retry` (quorum): `x-message-ttl=BACKOFF_MS`, `x-dead-letter-exchange=investops`, `x-dead-letter-routing-key=OperationImported`.
- `operations.dlq` (quorum): ligada a `investops.dlq` (direct/fanout).
- Exchanges novos: `investops.retry`, `investops.dlq` (durable).

> **Quem declara:** o **`NewConsumer`** (consumer principal) declara **toda** a topologia no startup — os 3 exchanges, as 3 filas (com seus args) e os bindings. O DLQ consumer apenas **consome** `operations.dlq` (já declarada). Garante ordem determinística.

## 3. Fluxo de retry no consumer (worker)

**Semântica exata da contagem:** `RETRY_MAX_ATTEMPTS` = **total de tentativas de processamento** (default 3). `deathCount(delivery)` = nº de rejeições anteriores da fila `operations.processing` (entrada `x-death` com `queue=operations.processing` **e** `reason=rejected`; ausente na 1ª entrega → 0). A entrega atual é a tentativa `deathCount+1`.

`handle()` muda: em **erro** do handler (transitório):
1. `attempt := deathCount(delivery) + 1`.
2. Se `attempt < RETRY_MAX_ATTEMPTS`: `Nack(requeue=false)` → `investops.retry` → wait queue (TTL) → volta à principal (backoff). Loga `attempt`.
3. Se `attempt >= RETRY_MAX_ATTEMPTS` (esta foi a última tentativa e falhou): **publica** a mensagem em `investops.dlq` (mantém body + headers, incl. `x-death`) e dá `Ack` na original. → garante **exatamente N tentativas** (com MAX=3: entregas com deathCount 0,1,2 → na de count 2 vai p/ DLQ).
- **Sucesso** ou **rejeição de negócio** (handler retorna `nil`): `Ack` (sem retry). (Rejeição já grava `Rejected` no processor.)
- **Mensagem malformada**: o tratamento **continua no handler do `main.go`** (unmarshal falha → loga → retorna `nil` → `Ack`), portanto **fora** do caminho de retry/DLQ.

> **`deathCount` (parse robusto):** `Headers["x-death"]` é `[]interface{}` de `amqp.Table`; filtrar pela entrada com `queue=="operations.processing"` **e** `reason=="rejected"` (NÃO usar índice 0 — após o TTL a entrada mais recente é `{queue: operations.retry, reason: expired}`); ler `count` (`int64`). Unit test cobre.
> **Canal/goroutine:** o publish na DLQ é **síncrono, na própria goroutine do consume** (prefetch=1 → sequencial), pelo canal do `Consumer` — amqp091 channel não é concorrente-safe; NÃO publicar pelo relay.

## 4. DLQ consumer (novo)

Consumer separado em `operations.dlq` (goroutine ao lado do consumer principal + relay), com **canal próprio** (idealmente conexão própria) — não compartilha o canal do consumer principal. Para cada mensagem, dentro de **`WithinTx`**:
- Lê a operação **`FOR UPDATE`** (novo método no `TxRepo`, ex. `GetOperationStatusForUpdate(operationId)`); se status já é terminal (`Failed`/`Processed`/`Rejected`) → **skip** (idempotente) e `Ack`.
- Senão: `UpdateOperationStatus(Failed, reason=nil)` + `InsertProcessingEvent{eventType:OperationFailed, status:Failed, attemptCount, errorMessage}` + `InsertOutbox(OperationFailed, source='worker')`; commit; `Ack`.
- **Onde mora o erro:** o texto do erro vai em **`processing_events.error_message`** (não em `rejection_reason`, que é para rejeição de negócio e fica **NULL** em `Failed`). O evento `OperationFailed` carrega `reason` genérico (`"ProcessingError"`) + `attempts`.

**Mudanças de suporte necessárias:**
- `operations/model.go`: adicionar `StatusFailed = "Failed"`.
- `operations/store.go` `ProcessingEvent`: adicionar campos **`AttemptCount int`** e **`ErrorMessage string`**.
- `database/store.go` `InsertProcessingEvent`: hoje grava `attempt_count=0` e **não** grava `error_message` → passar a gravar ambos.
- `TxRepo`: novo `GetOperationStatusForUpdate(ctx, operationId) (string, bool, error)` (SELECT status ... FOR UPDATE).

## 5. Evento + contrato
- `contracts/events/operation-failed.v1.json` (camelCase): **espelha o envelope** dos outros (`eventId`, `eventType` const `OperationFailed`, `version` const 1, `occurredAt`, `correlationId`, `payload`). Payload: `operationId` (uuid), `reason` (**string livre**, ex. `ProcessingError` — **não** o enum de rejeição), `attempts` (integer).
- `worker-go/internal/operations/events_out.go`: `operationFailedOutbox(...)` (envelope camelCase, `EventOperationFailed`, version 1).

## 6. Config
- `RETRY_MAX_ATTEMPTS` (default **3**), `RETRY_BACKOFF_MS` (default **5000**) — em `internal/config`; **validar > 0** (viram args da fila no declare; valor ruim "congela" na fila → exigiria outro `down -v` p/ corrigir).
- `RETRY_BACKOFF_MS` deve ser **injetável** nos testes (o teste declara a `operations.retry` com TTL curto, ex. 200ms — o valor é gravado na fila no declare, não dá p/ usar o default 5000).

## 7. .NET / frontend (já prontos)
- `OperationStatus.Failed` já existe; dashboard conta `Failed`; o frontend mostra chip `Failed` + **Reprocessar** (4b/reprocess). Nada a mudar além de, opcionalmente, o contrato `operation-failed.v1.json` (doc/neutralidade).

## 8. Migração (one-time)
Os **args da fila mudaram** (classic→quorum + DLX) → redeclarar a fila existente falha com **406 PRECONDITION_FAILED**. **Recriar uma vez**: `docker compose down -v` na primeira subida desta fase. ⚠️ `-v` remove **todos** os volumes nomeados (inclui `pgdata`) — o `migrator` re-semeia no próximo up, então ok; mas deixar isso explícito no README/PR (não é só o volume do RabbitMQ).

## 9. Erros / bordas
- Poison message que **sempre** falha: 3 tentativas com backoff → DLQ → `Failed` (não fica em loop). ✅ (o gap atual).
- Worker reiniciando no meio do retry: a mensagem está durável na fila quorum; retoma. (Reconnect in-process = 5c.)
- O publish na DLQ + Ack não é atômico (sem publisher confirms): risco mínimo aceitável; anotar (confirms = extensão).
- **Dead-letter strategy do quorum** é `at_most_once` por padrão (pode descartar em falha no dead-letter). Para o estudo é aceitável (mesma classe do item acima); `at_least_once` exigiria `x-dead-letter-strategy` + publisher confirms na fila quorum — anotado como limitação/extensão.

## 10. Testes
- **Go integração (testcontainers RabbitMQ+Postgres)**: handler que **sempre falha** (poison) → exatamente **`RETRY_MAX_ATTEMPTS`** tentativas (TTL injetado curto, ex. 200ms) → a operação vira **`Failed`** com `processing_event.error_message` preenchido e `attempt_count` = N, e há outbox **`OperationFailed`**; uma operação que **processa** **não** vai para a DLQ; **idempotência** do DLQ consumer (reentrega de uma já-`Failed` não duplica).
- **Go unit**: `deathCount` (filtra por `queue`+`reason`, ausência→0, `int64`); `operationFailedOutbox` (shape camelCase).

## 11. Quebra em tasks (PRs)
| # | Branch | Entrega |
|---|--------|---------|
| 5a.1 | `feature/worker-retry-dlq` | topologia (quorum + retry TTL + DLQ) + consumer com retry/x-death + DLQ consumer + OperationFailed (evento/contrato/processing_event) + config + testes + nota de migração |

(Uma task só — coeso. Se crescer, split: topologia/consumer | DLQ consumer/OperationFailed.)

## 12. Fora de escopo (5a)
- Publisher confirms / outbox de-dup transacional no publish-para-DLQ (extensão).
- Reconnect in-process (5c), observabilidade/trace do retry (5b).
- UI de inspeção da DLQ / reprocesso em lote.
