# Design — InvestOps Backoffice (Fase 4a: API de Leitura)

**Data:** 2026-06-11
**Status:** Aprovado (início da Fase 4a)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§8, §9)
**Contexto:** pré-requisito da Fase 4b (frontend Angular). Adiciona os endpoints de leitura que as telas do SPEC §9 precisam.

A Fase 4 foi decomposta em **4a (esta — API de leitura)** e **4b (frontend Angular)**. A 4a entrega, em .NET (estilo Fase 2: handlers + Dapper via `IDbSession` + DTOs + controllers finos), os endpoints que faltam para Operações (abas Eventos/Auditoria), Posições e Dashboard.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Decomposição | **4a (API)** antes de **4b (Angular)** |
| Escopo 4a | **Completo**: operations events/audit + positions + dashboard |
| Padrão | Reusa o existente: handler/serviço simples + repositórios Dapper (`IDbSession`, leitura sem transação) + DTOs + controllers finos |
| Paginação | Reusa `PagedResult<T>` + `SortClauseBuilder` (whitelist) da Fase 2 |

## 2. Endpoints

### Operações (no `OperationsController` existente)
- `GET /api/operations/{id}/events` → `ProcessingEventDto[]` (ordenado por `created_at`)
- `GET /api/operations/{id}/audit` → `AuditLogDto[]` (audit_logs do `entity_id` da operação, `entity_name='Operation'`)

### Posições (`PositionsController` novo)
- `GET /api/positions` → `PagedResult<PositionDto>` (paginação/ordenação server-side; filtros opcionais `accountId`, `productId`, `customerId`)
- `GET /api/positions/by-account/{accountId}` → `PositionDto[]`
- `GET /api/positions/by-customer/{customerId}` → `PositionDto[]`

### Dashboard (`DashboardController` novo)
- `GET /api/dashboard/summary` → `DashboardSummaryDto` (cards do SPEC §9)
- `GET /api/dashboard/processing-metrics` → `ProcessingMetricsDto` (saúde do pipeline)

## 3. DTOs

```csharp
ProcessingEventDto(string EventType, string Status, int AttemptCount, string? ErrorMessage,
                   DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt)

AuditLogDto(string Action, string? OldValue, string? NewValue, string? UserId, DateTimeOffset CreatedAt)

PositionDto(Guid Id, Guid AccountId, Guid ProductId,
            string CustomerName, string AccountNumber, string ProductName, string? Ticker,
            decimal Quantity, decimal AveragePrice, decimal GrossAmount, DateTimeOffset UpdatedAt)

DashboardSummaryDto(long ImportedToday, long Processed, long Rejected, long Failed,
                    long DivergencesPending, double? AverageProcessingSeconds) // null quando não há eventos processados

ProcessingMetricsDto(IReadOnlyDictionary<string,long> OperationsByStatus, long OutboxPending)
```

## 4. Consultas (Dapper, via `IDbSession`)

- **Events**: `SELECT ... FROM processing_events WHERE operation_id=@id ORDER BY created_at;`
- **Audit**: `SELECT ... FROM audit_logs WHERE entity_name='Operation' AND entity_id=@id ORDER BY created_at;` (`entity_id` é text → comparar com `id.ToString()`). Confirmado: a API grava `EntityName = nameof(Operation)` (= `"Operation"`) no import; só a API escreve `audit_logs` (o worker escreve `processing_events`).
- **Positions (lista)**: join `portfolio_positions p JOIN investment_accounts a ON a.id=p.account_id JOIN customers c ON c.id=a.customer_id JOIN financial_products fp ON fp.id=p.product_id`, com `WHERE` opcional, `ORDER BY` (whitelist), `LIMIT/OFFSET` + `COUNT(*)`. Os filtros (`accountId`/`productId`/`customerId`) aplicam **tanto no COUNT quanto na página** (como em `OperationRepository.ListAsync`). Ordenação via **`PositionsSortClauseBuilder` dedicado** (colunas próprias: `updated_at`, `gross_amount`, `quantity`, `average_price`) — não dá para reusar o `SortClauseBuilder` de operações (colunas diferentes); com teste unit próprio.
- **jsonb na leitura**: `audit_logs.old_value/new_value` (jsonb) são lidos como `string` pelo Npgsql por padrão — **sem type handler** (a escrita usa `::jsonb`, já existente). `processing_events.payload` não é exposto no DTO.
- **Positions (by-account/by-customer)**: mesmo join, filtrando por `p.account_id` / `c.id`.
- **Dashboard summary**:
  - `ImportedToday`: `count(*) FROM operations WHERE created_at >= date_trunc('day', now())`
  - `Processed/Rejected/Failed`: `count(*) ... WHERE status=...`
  - `DivergencesPending`: `count(*) FROM reconciliation_divergences WHERE status='Pending'` (tabela existe; hoje retorna 0)
  - `AverageProcessingSeconds`: `avg(extract(epoch from (processed_at - created_at))) FROM processing_events WHERE processed_at IS NOT NULL`
- **Processing metrics**:
  - `OperationsByStatus`: `SELECT status, count(*) FROM operations GROUP BY status`
  - `OutboxPending`: `count(*) FROM outbox_messages WHERE status='Pending'`

## 5. Arquitetura / arquivos

```text
Application/
  Common/PagedQuery.cs (se útil; ou reusa ListOperationsQuery-like)
  Operations/GetOperationEvents/   (handler + ProcessingEventDto)
  Operations/GetOperationAudit/    (handler + AuditLogDto)
  Positions/ (ListPositions, GetByAccount, GetByCustomer handlers + PositionDto + PositionsQuery)
  Dashboard/ (GetSummary, GetProcessingMetrics handlers + DTOs)
  Abstractions/IPositionReadRepository.cs, IProcessingEventReadRepository.cs,
               IDashboardRepository.cs, (IAuditRepository ganha GetByOperationAsync)
Infrastructure/Persistence/Repositories/ (implementações Dapper)
Api/
  Operations/OperationsController.cs  (+ 2 actions)
  Positions/PositionsController.cs    (novo)
  Dashboard/DashboardController.cs    (novo)
DI: registra os novos handlers/repos (scoped)
```

> Reads não usam transação — os repositórios de leitura usam `session.GetConnectionAsync` com `session.Transaction` (que é `nil` fora de transação, como já fazem os reads de operações).

## 6. Erros & validação
- `GET /{id}/events|audit`: se a operação não existe, retorna `[]` (sem 404 — é uma coleção). (Alternativa: 404; escolho `[]` por simplicidade do consumidor.)
- Positions list: `pageSize` cap 100; `sort` por whitelist; filtros inválidos → conjunto vazio.
- by-account/by-customer com id inexistente → `[]`.

## 7. Testes (Testcontainers + unit onde houver lógica)
- **Integração** (Postgres real): events/audit retornam os registros da operação; positions lista com join (nomes corretos) + paginação + filtros; by-account/by-customer; dashboard summary (contagens + média) e metrics (group by). Seeds via SQL.
- **Unit**: whitelist de sort de positions (se um `SortClauseBuilder` próprio); validação de status/filtros.

## 8. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 4a.1 | `feature/operation-events-audit` | `GET /operations/{id}/events` e `/audit` + repos + DTOs + testes |
| 4a.2 | `feature/positions-read` | `GET /positions` (paginado) + by-account + by-customer (join nomes) + testes |
| 4a.3 | `feature/dashboard-read` | `GET /dashboard/summary` + `/processing-metrics` + testes |

**Ordem:** 4a.1 → 4a.2 → 4a.3. Cada uma um PR para `developer`.

## 9. Fora de escopo (4a)
- `POST /operations/{id}/reprocess` (depende de operações `Failed`/DLQ — Fase 5).
- Endpoints de reconciliation, products (CRUD), customers (além do que o join usa) — fases próprias.
- CORS e o frontend em si — **Fase 4b**.
- `processing-metrics` avançado (séries temporais/gráficos) — começa simples (contagens).
