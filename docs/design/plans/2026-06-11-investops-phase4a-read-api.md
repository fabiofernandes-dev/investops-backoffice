# InvestOps Backoffice — Fase 4a (API de Leitura) Implementation Plan

> ✅ **Fase concluída e entregue.** Documento histórico de execução — os checkboxes registram o plano original (tudo entregue). O estado atual do produto está no [README — Status & roadmap](../../../README.md#status--roadmap).

**Goal:** Adicionar os endpoints de leitura que o frontend (Fase 4b) precisa: eventos/auditoria de operação, posições consolidadas e dashboard.

**Architecture:** Reusa o padrão da Fase 2 — controllers finos → handlers (serviços simples) → repositórios Dapper via `IDbSession` (leitura sem transação) → DTOs. Paginação reusa `PagedResult<T>`; ordenação de posições usa um `PositionsSortClauseBuilder` dedicado (whitelist anti-injection). Testes de integração com Testcontainers.

**Tech Stack:** .NET 8, Dapper, Npgsql, xUnit, Testcontainers, PostgreSQL.

**Spec:** [docs/design/specs/2026-06-11-investops-phase4a-read-api-design.md](../specs/2026-06-11-investops-phase4a-read-api-design.md)

---

## Convenções

- Branch `feature/*` a partir de `developer`; PR para `developer`; revisão do usuário; merge. 1 task → 1 PR → **pausa**.
- Comandos via **Bash tool (git-bash)**; `dotnet` em `backend/`. `TreatWarningsAsErrors=true`.
- Reads usam `session.GetConnectionAsync(ct)` + `session.Transaction` (nil fora de transação), como `OperationRepository`.
- DTO sempre — nunca retornar entidade de domínio.

## File Structure (4a)

```text
backend/src/InvestOps.Application/
  Abstractions/IProcessingEventReadRepository.cs        # 4a.1
  Abstractions/IAuditRepository.cs (+ GetByOperationAsync) # 4a.1
  Abstractions/IPositionReadRepository.cs               # 4a.2
  Abstractions/IDashboardRepository.cs                  # 4a.3
  Operations/GetOperationEvents/{Handler,ProcessingEventDto}.cs   # 4a.1
  Operations/GetOperationAudit/{Handler,AuditLogDto}.cs           # 4a.1
  Positions/{PositionDto,PositionsQuery,PositionsSortClauseBuilder,
             ListPositionsHandler,GetPositionsByAccountHandler,GetPositionsByCustomerHandler}.cs  # 4a.2
  Dashboard/{DashboardSummaryDto,ProcessingMetricsDto,GetDashboardSummaryHandler,GetProcessingMetricsHandler}.cs # 4a.3
backend/src/InvestOps.Infrastructure/Persistence/Repositories/
  ProcessingEventReadRepository.cs, AuditRepository.cs (+read), PositionReadRepository.cs, DashboardRepository.cs
backend/src/InvestOps.Api/
  Operations/OperationsController.cs (+ events, audit)
  Positions/PositionsController.cs                       # 4a.2
  Dashboard/DashboardController.cs                       # 4a.3
DI: Application + Infrastructure (handlers/repos scoped)
```

---

## Chunk 1: Eventos/Auditoria, Posições e Dashboard

### Task 4a.1: `GET /operations/{id}/events` e `/audit`

**Files:**
- Create: `Application/Operations/GetOperationEvents/ProcessingEventDto.cs`, `GetOperationEventsHandler.cs`
- Create: `Application/Operations/GetOperationAudit/AuditLogDto.cs`, `GetOperationAuditHandler.cs`
- Create: `Application/Abstractions/IProcessingEventReadRepository.cs`
- Modify: `Application/Abstractions/IAuditRepository.cs` (+ `GetByOperationAsync`)
- Create: `Infrastructure/Persistence/Repositories/ProcessingEventReadRepository.cs`
- Modify: `Infrastructure/Persistence/Repositories/AuditRepository.cs` (+ read)
- Modify: `Api/Operations/OperationsController.cs` (+ 2 actions), DI (Application/Infrastructure)
- Test: `tests/InvestOps.IntegrationTests/OperationEventsAuditTests.cs`

- [ ] **Step 1: Branch** `feature/operation-events-audit` a partir de `developer`.

- [ ] **Step 2: DTOs**

```csharp
// ProcessingEventDto.cs
namespace InvestOps.Application.Operations.GetOperationEvents;
public sealed record ProcessingEventDto(
    string EventType, string Status, int AttemptCount, string? ErrorMessage,
    DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);
```
```csharp
// AuditLogDto.cs
namespace InvestOps.Application.Operations.GetOperationAudit;
public sealed record AuditLogDto(
    string Action, string? OldValue, string? NewValue, string? UserId, DateTimeOffset CreatedAt);
```

- [ ] **Step 3: Interfaces de repo**

```csharp
// IProcessingEventReadRepository.cs
using InvestOps.Application.Operations.GetOperationEvents;
namespace InvestOps.Application.Abstractions;
public interface IProcessingEventReadRepository
{
    Task<IReadOnlyList<ProcessingEventDto>> GetByOperationAsync(Guid operationId, CancellationToken ct);
}
```
`IAuditRepository` ganha:
```csharp
Task<IReadOnlyList<AuditLogDto>> GetByOperationAsync(Guid operationId, CancellationToken ct);
```

- [ ] **Step 4: Repos Dapper**

```csharp
// ProcessingEventReadRepository.cs
public sealed class ProcessingEventReadRepository(IDbSession session) : IProcessingEventReadRepository
{
    public async Task<IReadOnlyList<ProcessingEventDto>> GetByOperationAsync(Guid operationId, CancellationToken ct)
    {
        const string sql = """
            SELECT event_type, status, attempt_count, error_message, created_at, processed_at
            FROM processing_events WHERE operation_id = @operationId ORDER BY created_at;""";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProcessingEventDto>(new CommandDefinition(sql, new { operationId }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
```
`AuditRepository.GetByOperationAsync` (mesmo arquivo da escrita; o repo passa a depender de `IDbSession` — já depende):
```csharp
const string sql = """
    SELECT action, old_value, new_value, user_id, created_at
    FROM audit_logs WHERE entity_name='Operation' AND entity_id=@entityId ORDER BY created_at;""";
var conn = await session.GetConnectionAsync(ct);
var rows = await conn.QueryAsync<AuditLogDto>(new CommandDefinition(sql, new { entityId = operationId.ToString() }, session.Transaction, cancellationToken: ct));
return rows.AsList();
```

- [ ] **Step 5: Handlers** (abrem nada — o repo usa a sessão scoped)

```csharp
public sealed class GetOperationEventsHandler(IProcessingEventReadRepository repo)
{
    public Task<IReadOnlyList<ProcessingEventDto>> HandleAsync(Guid id, CancellationToken ct) => repo.GetByOperationAsync(id, ct);
}
// análogo: GetOperationAuditHandler(IAuditRepository repo)
```

- [ ] **Step 6: Controller** — em `OperationsController` adicionar:

```csharp
[HttpGet("{id:guid}/events")]
public async Task<IActionResult> Events(Guid id, CancellationToken ct)
    => Ok(await eventsHandler.HandleAsync(id, ct));

[HttpGet("{id:guid}/audit")]
public async Task<IActionResult> Audit(Guid id, CancellationToken ct)
    => Ok(await auditHandler.HandleAsync(id, ct));
```
(injetar `GetOperationEventsHandler` e `GetOperationAuditHandler` no construtor.)

- [ ] **Step 7: DI** — registrar handlers (`AddScoped`) em `Application/DependencyInjection`; `IProcessingEventReadRepository`→impl em `Infrastructure/DependencyInjection`.

- [ ] **Step 8: Teste de integração** — seed via **SQL direto** (como `OperationsReadTests`): operação + 1 `processing_events` + 1 `audit_logs` (`entity_name='Operation'`, `entity_id=op.Id.ToString()`, `action='Imported'`, `new_value` jsonb). **Não** depender do import handler (mantém o teste isolado). Asserts: `GET .../events` retorna 1 com `EventType`/`Status` certos; `GET .../audit` retorna o registro `Imported`; operação inexistente → `[]`. (Usa `NpgsqlConnectionFactory` + `DbSession`.)

- [ ] **Step 9: Build + testes** verdes → commit + push + PR → **PAUSA**.

---

### Task 4a.2: Posições (`GET /positions`, by-account, by-customer)

**Files:**
- Create: `Application/Positions/PositionDto.cs`, `PositionsQuery.cs`, `PositionsSortClauseBuilder.cs`, `ListPositionsHandler.cs`, `GetPositionsByAccountHandler.cs`, `GetPositionsByCustomerHandler.cs`
- Create: `Application/Abstractions/IPositionReadRepository.cs`
- Create: `Infrastructure/Persistence/Repositories/PositionReadRepository.cs`
- Create: `Api/Positions/PositionsController.cs`
- Modify: DI
- Test (unit): `tests/InvestOps.UnitTests/PositionsSortClauseBuilderTests.cs`
- Test (integração): `tests/InvestOps.IntegrationTests/PositionsReadTests.cs`

- [ ] **Step 1: Branch** `feature/positions-read`.

- [ ] **Step 2: DTO + Query + SortالبBuilder**

```csharp
// PositionDto.cs
public sealed record PositionDto(
    Guid Id, Guid AccountId, Guid ProductId,
    string CustomerName, string AccountNumber, string ProductName, string? Ticker,
    decimal Quantity, decimal AveragePrice, decimal GrossAmount, DateTimeOffset UpdatedAt);
```
```csharp
// PositionsQuery.cs
public sealed record PositionsQuery(
    int Page = 1, int PageSize = 20, string? Sort = null,
    Guid? AccountId = null, Guid? ProductId = null, Guid? CustomerId = null)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
```
```csharp
// PositionsSortClauseBuilder.cs (whitelist, colunas qualificadas com p.)
public static class PositionsSortClauseBuilder
{
    public const string Default = "p.updated_at DESC";
    private static readonly IReadOnlyDictionary<string,string> Columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { ["updatedAt"]="p.updated_at", ["grossAmount"]="p.gross_amount", ["quantity"]="p.quantity", ["averagePrice"]="p.average_price" };

    public static string Build(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return Default;
        var parts = sort.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Columns.TryGetValue(parts[0], out var column)) return Default;
        var ascending = parts.Length >= 2 && parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        return $"{column} {(ascending ? "ASC" : "DESC")}";
    }
}
```

- [ ] **Step 3: TDD do `PositionsSortClauseBuilder`** — teste primeiro (mapeia whitelisted; fallback default; injeção → default). Implementar. Verde.

- [ ] **Step 4: Interface + repo**

```csharp
public interface IPositionReadRepository
{
    Task<(IReadOnlyList<PositionDto> Items, int Total)> ListAsync(PositionsQuery query, CancellationToken ct);
    Task<IReadOnlyList<PositionDto>> GetByAccountAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<PositionDto>> GetByCustomerAsync(Guid customerId, CancellationToken ct);
}
```
SQL base (join para nomes), aliases batendo com o DTO (snake_case → PascalCase via `MatchNamesWithUnderscores`):
```sql
SELECT p.id, p.account_id, p.product_id,
       c.name AS customer_name, a.account_number, fp.name AS product_name, fp.ticker,
       p.quantity, p.average_price, p.gross_amount, p.updated_at
FROM portfolio_positions p
JOIN investment_accounts a ON a.id = p.account_id
JOIN customers c            ON c.id = a.customer_id
JOIN financial_products fp  ON fp.id = p.product_id
{where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;
```
- `ListAsync`: usa `DynamicParameters` (como `OperationRepository.ListAsync`) — `where` dinâmico com `p.account_id`/`p.product_id`/`a.customer_id`; `COUNT(*)` com os **mesmos** filtros; `orderBy = PositionsSortClauseBuilder.Build(query.Sort)`; adiciona `@Limit`=`NormalizedPageSize` e `@Offset`=`Offset`. Os repos importam `using InvestOps.Infrastructure.Persistence;` (onde vive `IDbSession`).
- `GetByAccountAsync`/`GetByCustomerAsync`: mesmo SELECT com `WHERE p.account_id=@id` / `WHERE c.id=@id` (sem paginação).

- [ ] **Step 5: Handlers** — `ListPositionsHandler` → `PagedResult<PositionDto>`; `GetPositionsByAccountHandler`/`GetPositionsByCustomerHandler` → `PositionDto[]`.

- [ ] **Step 6: `PositionsController`**

```csharp
[ApiController] [Route("api/positions")]
public sealed class PositionsController(ListPositionsHandler list, GetPositionsByAccountHandler byAccount, GetPositionsByCustomerHandler byCustomer) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] PositionsQuery q, CancellationToken ct) => Ok(await list.HandleAsync(q, ct));
    [HttpGet("by-account/{accountId:guid}")] public async Task<IActionResult> ByAccount(Guid accountId, CancellationToken ct) => Ok(await byAccount.HandleAsync(accountId, ct));
    [HttpGet("by-customer/{customerId:guid}")] public async Task<IActionResult> ByCustomer(Guid customerId, CancellationToken ct) => Ok(await byCustomer.HandleAsync(customerId, ct));
}
```

- [ ] **Step 7: DI** — registrar 3 handlers + `IPositionReadRepository`.

- [ ] **Step 8: Teste de integração** — seed cliente/conta/produto + posição (via SQL); `GET /positions` retorna a posição com **nomes** corretos; paginação (insere N, pede pageSize); filtro por `productId`; `by-account` e `by-customer` retornam o conjunto certo; ids inexistentes → `[]`.

- [ ] **Step 9: Build + testes** verdes → commit + push + PR → **PAUSA**.

---

### Task 4a.3: Dashboard (`/summary`, `/processing-metrics`)

**Files:**
- Create: `Application/Dashboard/{DashboardSummaryDto,ProcessingMetricsDto,GetDashboardSummaryHandler,GetProcessingMetricsHandler}.cs`
- Create: `Application/Abstractions/IDashboardRepository.cs`
- Create: `Infrastructure/Persistence/Repositories/DashboardRepository.cs`
- Create: `Api/Dashboard/DashboardController.cs`
- Modify: DI
- Test (integração): `tests/InvestOps.IntegrationTests/DashboardReadTests.cs`

- [ ] **Step 1: Branch** `feature/dashboard-read`.

- [ ] **Step 2: DTOs**

```csharp
public sealed record DashboardSummaryDto(
    long ImportedToday, long Processed, long Rejected, long Failed,
    long DivergencesPending, double? AverageProcessingSeconds);

public sealed record ProcessingMetricsDto(
    IReadOnlyDictionary<string,long> OperationsByStatus, long OutboxPending);
```

- [ ] **Step 3: Interface + repo**

```csharp
public interface IDashboardRepository
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<ProcessingMetricsDto> GetProcessingMetricsAsync(CancellationToken ct);
}
```
`GetSummaryAsync` — uma query única com subselects, ou várias `ExecuteScalarAsync`:
```sql
SELECT
  (SELECT count(*) FROM operations WHERE created_at >= date_trunc('day', now()))            AS imported_today,
  (SELECT count(*) FROM operations WHERE status='Processed')                                 AS processed,
  (SELECT count(*) FROM operations WHERE status='Rejected')                                  AS rejected,
  (SELECT count(*) FROM operations WHERE status='Failed')                                    AS failed,
  (SELECT count(*) FROM reconciliation_divergences WHERE status='Pending')                   AS divergences_pending,
  (SELECT avg(extract(epoch from (processed_at - created_at)))::double precision FROM processing_events WHERE processed_at IS NOT NULL) AS average_processing_seconds;
```
mapear com `QuerySingleAsync<DashboardSummaryDto>`. **Cast `::double precision`** garante `double?` limpo (sem o cast, `avg(...)` é `numeric` → Dapper mapeia para `decimal` e quebra no `double?`); NULL quando não há eventos processados.

`GetProcessingMetricsAsync` — **materializar o dicionário na mão** (Dapper não projeta direto em `IReadOnlyDictionary`):
```csharp
var byStatusRows = await conn.QueryAsync<(string Status, long Count)>(
    new CommandDefinition("SELECT status, count(*) AS count FROM operations GROUP BY status;", transaction: session.Transaction, cancellationToken: ct));
var byStatus = byStatusRows.ToDictionary(r => r.Status, r => r.Count);
var outboxPending = await conn.ExecuteScalarAsync<long>(
    new CommandDefinition("SELECT count(*) FROM outbox_messages WHERE status='Pending';", transaction: session.Transaction, cancellationToken: ct));
return new ProcessingMetricsDto(byStatus, outboxPending);
```

- [ ] **Step 4: Handlers** finos (delegam ao repo) + **`DashboardController`** (`GET /api/dashboard/summary`, `/processing-metrics`).

- [ ] **Step 5: DI** — registrar handlers + `IDashboardRepository`.

- [ ] **Step 6: Teste de integração** — seed: importa N operações (status variados via SQL) + processing_events com `processed_at`; `summary` retorna contagens corretas e `AverageProcessingSeconds` > 0; sem eventos processados → `AverageProcessingSeconds` null; `processing-metrics` agrupa por status e conta outbox pendente.

- [ ] **Step 7: Build + testes** verdes → commit + push + PR → **PAUSA**.

---

## Fechamento da Fase 4a

Após 4a.3 mergeada: a API expõe tudo que o frontend precisa para Operações (lista+detalhe completo), Posições e Dashboard. Próximo: **Fase 4b — frontend Angular** (com CORS habilitado na API, foundation, e as telas consumindo estes endpoints).
