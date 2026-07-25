# Design — InvestOps Backoffice (Fase 8: Conciliação com custodiante)

**Data:** 2026-07-24
**Status:** Aprovado (início da Fase 8)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§8 endpoints, §9 telas, §6 Regra 6 auditoria, §10 tabelas)
**Depende de:** Fase 4a (API de leitura — `IDbSession`, `PagedResult`, `SortClauseBuilder`), Fase 4b (frontend Angular + NgRx), Fase 2 (`IUnitOfWork` + auditoria transacional).

Concilia a **posição interna** (`portfolio_positions`) com a **posição do custodiante** (extrato importado): importa um extrato via **CSV**, calcula **divergências** por `(conta, produto)`, lista as divergências e permite **resolver**/**ignorar** cada uma. Slice completo: **backend** (.NET) + **tela Angular**. A comparação é **síncrona na API** — não envolve worker nem outbox.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Escopo | **Slice completo**: backend + frontend numa fase (subdividida em PRs 8.1 backend, 8.2 frontend, 8.3 docs) |
| Entrada do import | **Upload de CSV** (multipart): arquivo + `source` + `referenceDate` como campos de form |
| Formato do CSV | Colunas **`account_id,product_id,quantity`** com header; `account_id`/`product_id` são **UUID** (simples e testável; `account_number`/`ticker` fica como evolução) |
| Comparação | **Síncrona na API**, isolada num serviço de domínio puro (`ReconciliationComparer`); **quantity-only** (é o que o schema guarda) |
| Escopo da comparação | Somente os pares `(conta, produto)` **presentes no CSV**; posição interna ausente ⇒ `internalQuantity = 0`. Posições internas órfãs (não citadas no extrato) ficam **fora do MVP** (extrato pode ser parcial) |
| `resolve` | Só **transição de estado** (`Pending → Resolved`) + auditoria; **não** altera `portfolio_positions` (worker é a fonte da verdade da posição) |
| `ignore` | Transição `Pending → Ignored` + auditoria |
| Migration | **Nenhuma** — reutiliza `reconciliation_imports` e `reconciliation_divergences` (já no schema da Fase 1) |
| Mensageria | **Nenhum** evento/contrato novo (síncrono); worker e outbox intactos |
| Auditoria | `ReconciliationImported`, `DivergenceResolved`, `DivergenceIgnored`, cada uma **na mesma transação** (Regra 6) |
| Rate limiting | `import`/`resolve`/`ignore` são **escrita** → política mais rígida (mesma de `import`/`reprocess` de operações) |
| Serialização | API camelCase; paginação **1-based**; sort no formato `"campo:dir"` — consistente com Operações/Posições |

## 2. Domínio e dados (sem migration)

Reaproveita as tabelas existentes ([Script0001](../../../backend/src/InvestOps.Migrator/Scripts/Schema/Script0001_initial_schema.sql)):

```text
reconciliation_imports(id, source, reference_date, status, created_at)
reconciliation_divergences(id, reconciliation_import_id, account_id, product_id,
                           internal_quantity, custodian_quantity, status, created_at, resolved_at)
```

Estados (persistidos como `text`, como os demais enums do projeto):
- `reconciliation_imports.status` → **`Completed`** (o import só persiste após parse+comparação com sucesso; falha de parse retorna `400` antes de qualquer insert).
- `reconciliation_divergences.status` → **`Pending`** | **`Resolved`** | **`Ignored`** (o dashboard já conta `WHERE status='Pending'`).

Novos tipos de domínio (espelhando `OperationStatus`/`OperationCalculator`):
- Enums `ReconciliationImportStatus`, `DivergenceStatus`.
- Entidade `ReconciliationDivergence`.
- **`ReconciliationComparer`** (função pura, sem I/O): dado o conjunto de linhas do custodiante e as posições internas correspondentes, produz a lista de divergências.

### `ReconciliationComparer` (regra central, unit-testável)

Entrada: `custodianRows: [{ accountId, productId, custodianQuantity }]`, `internalPositions: [{ accountId, productId, quantity }]`.
Para cada linha do custodiante:
- `internalQuantity` = quantidade interna do par `(accountId, productId)` ou **`0`** se não houver posição.
- Se `internalQuantity != custodianQuantity` ⇒ emite divergência (`status = Pending`).
- Se iguais ⇒ **sem** divergência (conta como `matched`).

Saída: `[{ accountId, productId, internalQuantity, custodianQuantity }]` (só as divergentes).

## 3. Backend (.NET)

### Application — vertical slices em `Application/Reconciliation/`

- **`ImportReconciliation/`** — `ImportReconciliationHandler`, `ImportReconciliationRequest { source, referenceDate, rows[] }`, `ImportReconciliationResult { importId, source, referenceDate, rowsProcessed, matchedCount, divergenceCount }`, `ImportReconciliationValidator` (source obrigatório; referenceDate obrigatória; ≥1 linha; quantidades ≥ 0). O handler recebe as **linhas já parseadas** (o parse do CSV é feito na fronteira Infra/API — mantém o handler testável sem arquivo).
  Fluxo: valida referências (todas as `accountId`/`productId` existem, senão `ReferenceNotFoundException` → **`422`**, import atômico); carrega posições internas dos pares citados; roda o `ReconciliationComparer`; numa transação (`IUnitOfWork`): insere `reconciliation_import` (`Completed`), insere as divergências, insere `AuditLog` `ReconciliationImported` (payload: source, referenceDate, rowsProcessed, divergenceCount). Commit.
- **`ListDivergences/`** — `ListDivergencesHandler`, `DivergencesQuery { page, pageSize, sort, status? }`, `DivergenceDto`, `DivergencesSortClauseBuilder` (whitelist com **chaves camelCase da API** — mapeadas para colunas, como nos demais builders: `createdAt` [default `desc`] → `created_at`, `status`, `difference` → `custodian_quantity - internal_quantity`, valor **com sinal**, igual ao exibido no DTO). Leitura enriquecida por **join** (divergences→accounts→customers, divergences→products), espelhando `PositionReadRepository`. Retorna `PagedResult<DivergenceDto>`.
  `DivergenceDto { id, importId, accountId, customerName, accountNumber, productId, productName, ticker, internalQuantity, custodianQuantity, difference, status, createdAt, resolvedAt }` (`difference = custodianQuantity - internalQuantity`).
- **`ResolveDivergence/`** — `ResolveDivergenceHandler`: carrega a divergência (`404` se não existe); se `status != Pending` ⇒ `ConflictException` → `409`; senão `status = Resolved`, `resolvedAt = now`, `AuditLog` `DivergenceResolved`, numa transação.
- **`IgnoreDivergence/`** — idem, `status = Ignored`, `AuditLog` `DivergenceIgnored`.

### Infraestrutura

- **`IReconciliationRepository`** (write, via `IUnitOfWork`/transação): `InsertImportAsync`, `InsertDivergencesAsync`, `GetDivergenceByIdAsync`, `UpdateDivergenceStatusAsync`.
- **`IReconciliationReadRepository`** (read, via `IDbSession`): `ListDivergencesAsync(query)` (join + paginação + filtro por status), `GetInternalPositionsAsync(pairs)` (para o comparer).
- **`CsvCustodianStatementParser`** (Infra): `IFormFile`/stream → `List<CustodianRow>`. Parser mínimo **sem dependência nova** (split por linha/vírgula, ignora header e linhas vazias, `trim`). Rejeita: sem header/coluna faltando, contagem de colunas errada, `quantity` não-numérica ou negativa, UUID inválido, **e par `(account_id, product_id)` duplicado** (o comparer assume um par único; duplicata é ambígua) → erro de validação (`400`).

### API — `ReconciliationsController` (SPEC §8)

```http
POST /api/reconciliations/import                      # multipart: file (CSV), source, referenceDate → 200 ImportReconciliationResult
GET  /api/reconciliations/divergences                 # ?page&pageSize&sort&status → PagedResult<DivergenceDto>
POST /api/reconciliations/divergences/{id}/resolve    # 204
POST /api/reconciliations/divergences/{id}/ignore     # 204
```

- `import`/`resolve`/`ignore` → **política de rate limit de escrita** (registrar as rotas na policy existente).
- `import`: valida presença do arquivo, extensão `.csv`, limite de tamanho (ex.: 1 MB); `400` para CSV malformado/limite; `422` para referência inexistente.
- **Mapeamento de exceções** — feito **localmente no controller** (não há middleware global; os controllers já capturam localmente, ex.: `OperationsController.Import`): `ReferenceNotFoundException` → `422`, `ConflictException` → `409`, divergência inexistente → `404`, validação/CSV malformado → `400`. `ConflictException` é **novo** e vive em `Application` (análogo a `ReferenceNotFoundException`).

## 4. Frontend (Angular) — nova feature `reconciliation`

Espelha `operations`/`positions` (standalone + Material + NgRx; rota lazy com `provideState`/`provideEffects`). Rota **`/reconciliation`** + item no sidenav. O contador "divergências pendentes" do Dashboard vira **link** para `/reconciliation`.

- **`core/api/reconciliation.api.ts`** — `import(source, referenceDate, file): ImportReconciliationResult` (**`FormData` multipart**), `listDivergences(query): PagedResult<DivergenceDto>`, `resolve(id)`, `ignore(id)`.
- **Estado NgRx** (`features/reconciliation/state/`): `{ query, items, total, loading, error, lastImport }`; actions `loadDivergences`/`Success`/`Failure`, `importStatement`/`Success`/`Failure`, `resolveDivergence`/`ignoreDivergence` (+ Success/Failure recarrega a lista).
- **Tela** (`reconciliation.component`):
  - **Painel de import**: `input[type=file]` (CSV) + `source` (texto) + `referenceDate` (datepicker) → submit → snackbar com resumo (`N divergências / M conferidas`) e recarrega a tabela.
  - **Tabela de divergências**: `MatTable` + `MatSort` + `MatPaginator` **server-side**; filtro por **status** (Pending/Resolved/Ignored/todos). Colunas: Cliente, Conta, Produto, **Qtd interna**, **Qtd custodiante**, **Diferença** (`difference`, com destaque de sinal), Status (chip), Criado em, ações.
  - **Ações Resolver/Ignorar** renderizadas **só em linhas `Pending`** (mesmo padrão do reprocessar-só-em-`Failed`); confirmam via ação NgRx → recarrega a lista.
  - **Loading** e **error state** (mensagem + retry), como as demais telas.

## 5. Fluxos e erros

**Import**
1. UI envia `FormData` (file + source + referenceDate) → `POST /import`.
2. Controller valida arquivo → `CsvCustodianStatementParser` → linhas.
3. Handler valida referências (senão `422`), compara, persiste (import + divergências + audit) numa transação.
4. `200` com `ImportReconciliationResult` (resumo). UI mostra snackbar e recarrega.

**Resolver / Ignorar**: `POST .../resolve|ignore` → `404` se id inexistente; `409` se a divergência não está `Pending` (idempotência de estado); `204` no sucesso; auditoria gravada na transação.

**Erros de import**: CSV ausente/extensão inválida/limite → `400`; header/coluna faltando, `quantity` não-numérica/negativa, UUID inválido, par duplicado → `400` com detalhe; `accountId`/`productId` inexistente em qualquer linha → `422` (import **atômico**, nada é persistido).

## 6. Testes (SPEC §13 estendido à Fase 8)

**Unit (.NET)**
- `ReconciliationComparer`: quantidades iguais ⇒ sem divergência; diferentes ⇒ divergência; par sem posição interna ⇒ `internalQuantity = 0` + divergência; múltiplos pares.
- `CsvCustodianStatementParser`: CSV válido; header ausente/coluna faltando; `quantity` inválida/negativa; UUID inválido; **par `(account_id, product_id)` duplicado**; linhas vazias/trailing.
- `ImportReconciliationValidator`: source/referenceDate/linhas obrigatórios.

**Integração (Testcontainers)**
- Import cria `reconciliation_import` (`Completed`) + N divergências (`Pending`) + `AuditLog`; `matchedCount`/`divergenceCount` corretos.
- Import com referência inexistente ⇒ `422` e **nada** persistido (atomicidade).
- `GET /divergences` com paginação + filtro por status (join traz nomes).
- `resolve` transiciona `Pending → Resolved` (+ `resolvedAt` + audit); `ignore` idem `Ignored`.
- `resolve`/`ignore` em id inexistente ⇒ `404`; em divergência não-`Pending` ⇒ `409`.

**Frontend (Jest)**
- Tabela de divergências renderiza (store mockada).
- Filtro de status emite `loadDivergences`.
- Botões Resolver/Ignorar aparecem **só em `Pending`** e despacham a ação.
- Import: submit com arquivo dispara `importStatement`; sucesso mostra resumo; **error state** quando a API falha.

## 7. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 8.1 | `feature/reconciliation-api` | **.NET**: enums + `ReconciliationDivergence` + `ReconciliationComparer` (unit); slices `ImportReconciliation`/`ListDivergences`/`ResolveDivergence`/`IgnoreDivergence`; `IReconciliationRepository`/`IReconciliationReadRepository`; `CsvCustodianStatementParser`; `ReconciliationsController` (4 endpoints) + rate limit de escrita; auditoria transacional; testes unit + integração |
| 8.2 | `feature/web-reconciliation` | **Angular**: `reconciliation.api.ts` (multipart) + NgRx + tela (painel import + tabela + filtro + Resolver/Ignorar) + rota/nav + link do Dashboard + testes Jest |
| 8.3 | `feature/reconciliation-docs` | **Docs**: ADR 0007 (conciliação síncrona, fora do worker) + README **Status & roadmap** (marcar Conciliação ✅) |

**Ordem:** 8.1 → 8.2 → 8.3. Cada uma um PR para `developer`.

## 8. Fora de escopo (Fase 8)

- **Ajuste de posição** no `resolve` (escreveria em `portfolio_positions` fora do worker) — evolução futura ("Ajuste manual" da Regra 6).
- **Posições internas órfãs** (existem internamente mas não no extrato) — o MVP compara só os pares do CSV (extrato pode ser parcial).
- Divergência por **preço/valor** — comparação é quantity-only (limite do schema).
- CSV por **`account_number`/`ticker`** (usa UUIDs) e **caminho assíncrono** (worker/outbox) — Abordagens documentadas, não implementadas.
- Autenticação/autorização (SPEC §15, [ADR 0006](../../adr/0006-defer-authentication-adopt-rate-limiting.md)).
