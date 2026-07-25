# InvestOps Backoffice — Fase 8 (Conciliação com custodiante) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (se houver subagents) ou superpowers:executing-plans para implementar este plano. Os steps usam checkbox (`- [ ]`) para rastreio. Aplique @superpowers:test-driven-development na lógica pura e @superpowers:verification-before-completion antes de cada "verde".

**Goal:** Conciliar a posição interna com o extrato do custodiante: importar um CSV, calcular divergências por `(conta, produto)`, listá-las e resolvê-las/ignorá-las — backend .NET + tela Angular.

**Architecture:** Comparação **síncrona na API** (sem worker/outbox). Domínio puro (`ReconciliationComparer`) espelha `OperationCalculator`; escrita transacional (`IUnitOfWork` + auditoria) espelha `ImportOperationHandler`; leitura com join + `PagedResult` + `SortClauseBuilder` (whitelist) espelha `PositionReadRepository`. Frontend: feature `reconciliation` com NgRx, espelhando `positions`/`operations`. **Sem migration** — as tabelas já existem.

**Tech Stack:** .NET 8, Dapper, Npgsql, FluentValidation, xUnit, Testcontainers; Angular 21 (zoneless, signals), NgRx, Angular Material, Jest.

**Spec:** [docs/design/specs/2026-07-24-investops-phase8-reconciliation-design.md](../specs/2026-07-24-investops-phase8-reconciliation-design.md)

---

## Convenções

- Branch `feature/*` a partir de `developer`; PR para `developer`; revisão do usuário; merge. **1 task → 1 PR → PAUSA.**
- Comandos via **Bash tool (git-bash)**; `dotnet` em `backend/`; `npm` em `frontend/investops-web/`. `TreatWarningsAsErrors=true` (resolver nullable/usings).
- Reads usam `session.GetConnectionAsync(ct)` + `session.Transaction` (nil fora de transação), como `PositionReadRepository`. Writes usam `IUnitOfWork.Begin/Commit/Rollback`, como `ImportOperationHandler`. **DTO sempre** — nunca retornar entidade.
- API camelCase; paginação **1-based**; sort `"campo:dir"`. Escrita (`import`/`resolve`/`ignore`) sob `[EnableRateLimiting(RateLimitingOptions.WritePolicy)]`.
- Mapeamento de exceções **local no controller** (não há middleware global), seguindo o precedente do `OperationsController` (`Import`→422, `Reprocess`→404 capturam a mesma `ReferenceNotFoundException`).

## File Structure

```text
backend/src/InvestOps.Domain/Reconciliation/
  ReconciliationComparer.cs            # CustodianRow, InternalPosition, DivergenceResult + Compare()  [8.1]
backend/src/InvestOps.Domain/Entities/
  ReconciliationImport.cs, ReconciliationDivergence.cs                                                 [8.1]
backend/src/InvestOps.Domain/Enums/
  DivergenceStatus.cs, ReconciliationImportStatus.cs                                                   [8.1]
backend/src/InvestOps.Application/Common/
  ConflictException.cs                                                                                 [8.1]
backend/src/InvestOps.Application/Abstractions/
  IReconciliationRepository.cs, IReconciliationReadRepository.cs                                        [8.1]
backend/src/InvestOps.Application/Reconciliation/
  ImportReconciliation/{ImportReconciliationRequest,Result,Validator,Handler,CsvCustodianStatementParser,CsvFormatException}.cs
  ListDivergences/{DivergencesQuery,DivergenceDto,DivergencesSortClauseBuilder,ListDivergencesHandler}.cs
  ResolveDivergence/ResolveDivergenceHandler.cs, IgnoreDivergence/IgnoreDivergenceHandler.cs           [8.1]
backend/src/InvestOps.Infrastructure/Persistence/Repositories/
  ReconciliationRepository.cs, ReconciliationReadRepository.cs                                          [8.1]
backend/src/InvestOps.Api/Reconciliation/ReconciliationsController.cs                                   [8.1]
backend/src/InvestOps.Application/DependencyInjection.cs, .../Infrastructure/DependencyInjection.cs (+regs)
backend/tests/InvestOps.UnitTests/{ReconciliationComparerTests,CsvCustodianStatementParserTests,DivergencesSortClauseBuilderTests}.cs
backend/tests/InvestOps.IntegrationTests/ReconciliationTests.cs                                          [8.1]

frontend/investops-web/src/app/
  core/api/reconciliation.api.ts                                                                        [8.2]
  core/models/index.ts (+Divergence, DivergencesQuery, ImportReconciliationResult)                      [8.2]
  features/reconciliation/reconciliation.component.{ts,html,scss,spec.ts}                               [8.2]
  features/reconciliation/state/{reconciliation.actions,reconciliation.effects,reconciliation.feature,reconciliation.feature.spec}.ts
  app.routes.ts (+/reconciliation), app.component.ts (+nav), features/dashboard/dashboard.component.html (+link)  [8.2]

docs/adr/0007-synchronous-reconciliation.md ; README.md (roadmap)                                       [8.3]
```

---

## Chunk 1: Backend (Task 8.1)

### Task 8.1: API de Conciliação (`feature/reconciliation-api` → PR → PAUSA)

**Files:** (ver File Structure, seção `[8.1]`)

- [ ] **Step 1: Branch** `feature/reconciliation-api` a partir de `developer`.

- [ ] **Step 2: Enums + entidades (Domain)**

```csharp
// Enums/DivergenceStatus.cs
namespace InvestOps.Domain.Enums;
public enum DivergenceStatus { Pending, Resolved, Ignored }
```
```csharp
// Enums/ReconciliationImportStatus.cs
namespace InvestOps.Domain.Enums;
public enum ReconciliationImportStatus { Completed }
```
```csharp
// Entities/ReconciliationImport.cs
namespace InvestOps.Domain.Entities;
public sealed class ReconciliationImport
{
    public required Guid Id { get; init; }
    public required string Source { get; init; }
    public required DateOnly ReferenceDate { get; init; }
    public required ReconciliationImportStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```
```csharp
// Entities/ReconciliationDivergence.cs
namespace InvestOps.Domain.Entities;
public sealed class ReconciliationDivergence
{
    public required Guid Id { get; init; }
    public required Guid ReconciliationImportId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid ProductId { get; init; }
    public required decimal InternalQuantity { get; init; }
    public required decimal CustodianQuantity { get; init; }
    public required DivergenceStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}
```

- [ ] **Step 3: TDD do `ReconciliationComparer` (Domain)** — teste primeiro em `ReconciliationComparerTests.cs`:
  - qtd interna == custodiante ⇒ **sem** divergência;
  - qtd diferente ⇒ 1 divergência com `InternalQuantity`/`CustodianQuantity` certos;
  - par sem posição interna ⇒ `InternalQuantity == 0` + divergência;
  - múltiplos pares (mix de iguais/diferentes) ⇒ só os diferentes.

  Rodar: `cd backend && dotnet test tests/InvestOps.UnitTests --filter ReconciliationComparerTests` → **FAIL** (tipo não existe). Implementar:

```csharp
// Domain/Reconciliation/ReconciliationComparer.cs
namespace InvestOps.Domain.Reconciliation;

public readonly record struct CustodianRow(Guid AccountId, Guid ProductId, decimal CustodianQuantity);
public readonly record struct InternalPosition(Guid AccountId, Guid ProductId, decimal Quantity);
public readonly record struct DivergenceResult(Guid AccountId, Guid ProductId, decimal InternalQuantity, decimal CustodianQuantity);

public static class ReconciliationComparer
{
    public static IReadOnlyList<DivergenceResult> Compare(
        IReadOnlyList<CustodianRow> custodianRows, IReadOnlyList<InternalPosition> internalPositions)
    {
        var internalByPair = internalPositions.ToDictionary(p => (p.AccountId, p.ProductId), p => p.Quantity);
        var divergences = new List<DivergenceResult>();
        foreach (var row in custodianRows)
        {
            var internalQty = internalByPair.TryGetValue((row.AccountId, row.ProductId), out var q) ? q : 0m;
            if (internalQty != row.CustodianQuantity)
                divergences.Add(new DivergenceResult(row.AccountId, row.ProductId, internalQty, row.CustodianQuantity));
        }
        return divergences;
    }
}
```
  Rodar de novo → **PASS**. Commit.

- [ ] **Step 4: TDD do `CsvCustodianStatementParser` (Application)** — teste primeiro em `CsvCustodianStatementParserTests.cs` (parser recebe `Stream`; a API passa `file.OpenReadStream()`):
  - CSV válido (header `account_id,product_id,quantity` + N linhas) ⇒ N `CustodianRow`;
  - header ausente/coluna faltando ⇒ `CsvFormatException`;
  - `quantity` não-numérica ou negativa ⇒ `CsvFormatException`;
  - UUID inválido ⇒ `CsvFormatException`;
  - par `(account_id, product_id)` **duplicado** ⇒ `CsvFormatException`;
  - linhas vazias/trailing são ignoradas.

  Implementar (sem dependência nova):

```csharp
// Application/Reconciliation/ImportReconciliation/CsvFormatException.cs
namespace InvestOps.Application.Reconciliation.ImportReconciliation;
public sealed class CsvFormatException(string message) : Exception(message);
```
```csharp
// Application/Reconciliation/ImportReconciliation/CsvCustodianStatementParser.cs
using System.Globalization;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public static class CsvCustodianStatementParser
{
    private static readonly string[] Header = ["account_id", "product_id", "quantity"];

    public static IReadOnlyList<CustodianRow> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Split('\n').Select(l => l.Trim('\r', ' ', '\t')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) throw new CsvFormatException("CSV vazio.");

        var head = lines[0].Split(',', StringSplitOptions.TrimEntries);
        if (!head.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new CsvFormatException("Header esperado: account_id,product_id,quantity.");

        var rows = new List<CustodianRow>();
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',', StringSplitOptions.TrimEntries);
            if (cols.Length != 3) throw new CsvFormatException($"Linha com {cols.Length} colunas (esperado 3): {line}");
            if (!Guid.TryParse(cols[0], out var accountId)) throw new CsvFormatException($"account_id inválido: {cols[0]}");
            if (!Guid.TryParse(cols[1], out var productId)) throw new CsvFormatException($"product_id inválido: {cols[1]}");
            if (!decimal.TryParse(cols[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty < 0)
                throw new CsvFormatException($"quantity inválida: {cols[2]}");
            if (!seen.Add((accountId, productId)))
                throw new CsvFormatException($"Par duplicado: {accountId},{productId}");
            rows.Add(new CustodianRow(accountId, productId, qty));
        }
        if (rows.Count == 0) throw new CsvFormatException("Nenhuma linha de dados.");
        return rows;
    }
}
```
  Rodar `--filter CsvCustodianStatementParserTests` → **PASS**. Commit.

- [ ] **Step 5: Request + Result + Validator (Application)**

```csharp
// ImportReconciliationRequest.cs
using InvestOps.Domain.Reconciliation;
namespace InvestOps.Application.Reconciliation.ImportReconciliation;
public sealed record ImportReconciliationRequest(string Source, DateOnly ReferenceDate, IReadOnlyList<CustodianRow> Rows);
```
```csharp
// ImportReconciliationResult.cs
namespace InvestOps.Application.Reconciliation.ImportReconciliation;
public sealed record ImportReconciliationResult(
    Guid ImportId, string Source, DateOnly ReferenceDate, int RowsProcessed, int MatchedCount, int DivergenceCount);
```
```csharp
// ImportReconciliationValidator.cs
using FluentValidation;
namespace InvestOps.Application.Reconciliation.ImportReconciliation;
public sealed class ImportReconciliationValidator : AbstractValidator<ImportReconciliationRequest>
{
    public ImportReconciliationValidator()
    {
        RuleFor(x => x.Source).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReferenceDate).NotEqual(default(DateOnly));
        RuleFor(x => x.Rows).NotEmpty();
        RuleForEach(x => x.Rows).Must(r => r.CustodianQuantity >= 0).WithMessage("quantidade negativa");
    }
}
```
(Auto-registrado por `AddValidatorsFromAssemblyContaining<ImportOperationValidator>` — mesmo assembly.)

- [ ] **Step 6: `ConflictException` (Application/Common)**

```csharp
namespace InvestOps.Application.Common;
public sealed class ConflictException(string message) : Exception(message);
```

- [ ] **Step 7: Interfaces de repositório (Application/Abstractions)**

```csharp
// IReconciliationRepository.cs  (write, via IUnitOfWork/transação)
using InvestOps.Domain.Entities;
namespace InvestOps.Application.Abstractions;
public interface IReconciliationRepository
{
    Task InsertImportAsync(ReconciliationImport import, CancellationToken ct);
    Task InsertDivergencesAsync(IReadOnlyList<ReconciliationDivergence> divergences, CancellationToken ct);
    Task<ReconciliationDivergence?> GetDivergenceByIdAsync(Guid id, CancellationToken ct);
    Task UpdateDivergenceStatusAsync(Guid id, string status, DateTimeOffset resolvedAt, CancellationToken ct);
}
```
```csharp
// IReconciliationReadRepository.cs  (read, via IDbSession)
using InvestOps.Application.Common;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Domain.Reconciliation;
namespace InvestOps.Application.Abstractions;
public interface IReconciliationReadRepository
{
    Task<PagedResult<DivergenceDto>> ListDivergencesAsync(DivergencesQuery query, CancellationToken ct);
    Task<IReadOnlyList<InternalPosition>> GetInternalPositionsAsync(
        IReadOnlyList<Guid> accountIds, IReadOnlyList<Guid> productIds, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetExistingAccountIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetExistingProductIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
}
```

- [ ] **Step 8: DTO + Query + SortClauseBuilder (Application/ListDivergences)** — TDD do sort builder primeiro (`DivergencesSortClauseBuilderTests`: mapeia whitelisted; fallback default; injeção → default), depois:

```csharp
// DivergenceDto.cs
namespace InvestOps.Application.Reconciliation.ListDivergences;
public sealed record DivergenceDto(
    Guid Id, Guid ImportId, Guid AccountId, string CustomerName, string AccountNumber,
    Guid ProductId, string ProductName, string? Ticker,
    decimal InternalQuantity, decimal CustodianQuantity, decimal Difference,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);
```
```csharp
// DivergencesQuery.cs
namespace InvestOps.Application.Reconciliation.ListDivergences;
public sealed record DivergencesQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Status = null)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
```
```csharp
// DivergencesSortClauseBuilder.cs (whitelist camelCase → colunas qualificadas com d.)
namespace InvestOps.Application.Reconciliation.ListDivergences;
public static class DivergencesSortClauseBuilder
{
    public const string Default = "d.created_at DESC";
    private static readonly IReadOnlyDictionary<string,string> Columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { ["createdAt"]="d.created_at", ["status"]="d.status", ["difference"]="(d.custodian_quantity - d.internal_quantity)" };

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

- [ ] **Step 9: Repositórios Dapper (Infrastructure)** — `using InvestOps.Infrastructure.Persistence;` (onde vive `IDbSession`). Espelhar `PositionReadRepository`/`OutboxRepository`.

  **`ReconciliationReadRepository`** (read) — `ListDivergencesAsync`:
```sql
SELECT d.id, d.reconciliation_import_id AS import_id, d.account_id,
       c.name AS customer_name, a.account_number, d.product_id, fp.name AS product_name, fp.ticker,
       d.internal_quantity, d.custodian_quantity,
       (d.custodian_quantity - d.internal_quantity) AS difference,
       d.status, d.created_at, d.resolved_at
FROM reconciliation_divergences d
JOIN investment_accounts a ON a.id = d.account_id
JOIN customers c           ON c.id = a.customer_id
JOIN financial_products fp ON fp.id = d.product_id
{where}  ORDER BY {orderBy}  LIMIT @Limit OFFSET @Offset;
```
  `{where}` = `WHERE d.status = @Status` só se `query.Status` não-vazio; `COUNT(*)` com o **mesmo** where; `{orderBy}` = `DivergencesSortClauseBuilder.Build(query.Sort)`; `@Limit=NormalizedPageSize`, `@Offset=Offset`. Retorna `PagedResult<DivergenceDto>(items, total, NormalizedPage, NormalizedPageSize)`.
  `GetInternalPositionsAsync`: `SELECT account_id, product_id, quantity FROM portfolio_positions WHERE account_id = ANY(@accountIds) AND product_id = ANY(@productIds)` → mapeia para `InternalPosition` (o comparer indexa por par; linhas extras são inofensivas). `GetExistingAccountIdsAsync`/`GetExistingProductIdsAsync`: `SELECT id FROM investment_accounts WHERE id = ANY(@ids)` / `financial_products`.

  **`ReconciliationRepository`** (write) — usa `session.GetConnectionAsync` + `session.Transaction` (a transação é aberta pelo `IUnitOfWork`, como no `OutboxRepository`):
  - `InsertImportAsync`: `INSERT INTO reconciliation_imports (id, source, reference_date, status, created_at) VALUES (@Id,@Source,@ReferenceDate,@Status,@CreatedAt)` (`Status = import.Status.ToString()`).
  - `InsertDivergencesAsync`: itera e insere cada `reconciliation_divergences (id, reconciliation_import_id, account_id, product_id, internal_quantity, custodian_quantity, status, created_at, resolved_at)`. **⚠️ Enum como string:** não há `EnumStringHandler` registrado em `DapperConfig` para `DivergenceStatus`/`ReconciliationImportStatus` — **não** faça multi-exec passando a entidade crua (Dapper bindaria o enum como `int` e quebraria na coluna `text`). Projete parâmetros anônimos com `Status = d.Status.ToString()` (ou o literal `'Pending'`), `ResolvedAt = (DateTimeOffset?)null`, como `OperationRepository.InsertAsync` faz (`.ToString()` nos enums). Idem `InsertImportAsync` (`Status = import.Status.ToString()`).
  - `GetDivergenceByIdAsync`: `SELECT id, reconciliation_import_id, account_id, product_id, internal_quantity, custodian_quantity, status, created_at, resolved_at FROM reconciliation_divergences WHERE id=@id` → **ler numa row-DTO intermediária** (com `status` como `string`) e construir o `ReconciliationDivergence` (entidade tem `required init`), com `Status = Enum.Parse<DivergenceStatus>(row.Status)`. Usar `QuerySingleOrDefaultAsync`; `null` ⇒ retorna `null`. (A resolve/ignore só precisa de `Status`, mas retornar a entidade completa mantém o repo reutilizável.)
  - `UpdateDivergenceStatusAsync`: `UPDATE reconciliation_divergences SET status=@status, resolved_at=@resolvedAt WHERE id=@id`.

- [ ] **Step 10: Handlers (Application)**

```csharp
// ImportReconciliationHandler.cs
using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.ImportOperation; // ReferenceNotFoundException (precedente do repo)
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed class ImportReconciliationHandler(
    IUnitOfWork unitOfWork, IReconciliationRepository repo, IReconciliationReadRepository read,
    IAuditRepository audit, TimeProvider clock)
{
    public async Task<ImportReconciliationResult> HandleAsync(ImportReconciliationRequest request, CancellationToken ct)
    {
        var accountIds = request.Rows.Select(r => r.AccountId).Distinct().ToList();
        var productIds = request.Rows.Select(r => r.ProductId).Distinct().ToList();

        var existingAccounts = (await read.GetExistingAccountIdsAsync(accountIds, ct)).ToHashSet();
        var missingAccount = accountIds.FirstOrDefault(id => !existingAccounts.Contains(id));
        if (missingAccount != Guid.Empty) throw new ReferenceNotFoundException($"Conta {missingAccount} não encontrada");
        var existingProducts = (await read.GetExistingProductIdsAsync(productIds, ct)).ToHashSet();
        var missingProduct = productIds.FirstOrDefault(id => !existingProducts.Contains(id));
        if (missingProduct != Guid.Empty) throw new ReferenceNotFoundException($"Produto {missingProduct} não encontrado");

        var internalPositions = await read.GetInternalPositionsAsync(accountIds, productIds, ct);
        var results = ReconciliationComparer.Compare(request.Rows, internalPositions);

        var now = clock.GetUtcNow();
        var import = new ReconciliationImport
        {
            Id = Guid.NewGuid(), Source = request.Source, ReferenceDate = request.ReferenceDate,
            Status = ReconciliationImportStatus.Completed, CreatedAt = now
        };
        var divergences = results.Select(r => new ReconciliationDivergence
        {
            Id = Guid.NewGuid(), ReconciliationImportId = import.Id, AccountId = r.AccountId, ProductId = r.ProductId,
            InternalQuantity = r.InternalQuantity, CustodianQuantity = r.CustodianQuantity,
            Status = DivergenceStatus.Pending, CreatedAt = now, ResolvedAt = null
        }).ToList();
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(), EntityName = nameof(ReconciliationImport), EntityId = import.Id.ToString(),
            Action = "Imported", OldValue = null,
            NewValue = JsonSerializer.Serialize(new { import.Source, import.ReferenceDate, RowsProcessed = request.Rows.Count, DivergenceCount = divergences.Count }),
            UserId = null, CreatedAt = now
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            await repo.InsertImportAsync(import, ct);
            await repo.InsertDivergencesAsync(divergences, ct);
            await audit.InsertAsync(auditLog, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch { await unitOfWork.RollbackAsync(ct); throw; }

        return new ImportReconciliationResult(import.Id, import.Source, import.ReferenceDate,
            request.Rows.Count, request.Rows.Count - divergences.Count, divergences.Count);
    }
}
```
```csharp
// ListDivergencesHandler.cs
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
namespace InvestOps.Application.Reconciliation.ListDivergences;
public sealed class ListDivergencesHandler(IReconciliationReadRepository read)
{
    public Task<PagedResult<DivergenceDto>> HandleAsync(DivergencesQuery query, CancellationToken ct)
        => read.ListDivergencesAsync(query, ct);
}
```
```csharp
// ResolveDivergenceHandler.cs  (IgnoreDivergenceHandler é análogo: DivergenceStatus.Ignored, Action "Ignored")
using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.ImportOperation; // ReferenceNotFoundException
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Reconciliation.ResolveDivergence;

public sealed class ResolveDivergenceHandler(
    IUnitOfWork unitOfWork, IReconciliationRepository repo, IAuditRepository audit, TimeProvider clock)
{
    public async Task HandleAsync(Guid divergenceId, CancellationToken ct)
    {
        var divergence = await repo.GetDivergenceByIdAsync(divergenceId, ct)
            ?? throw new ReferenceNotFoundException($"Divergência {divergenceId} não encontrada");
        if (divergence.Status != DivergenceStatus.Pending)
            throw new ConflictException($"Divergência {divergenceId} não está pendente (status {divergence.Status}).");

        var now = clock.GetUtcNow();
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(), EntityName = nameof(ReconciliationDivergence), EntityId = divergenceId.ToString(),
            Action = "Resolved", OldValue = JsonSerializer.Serialize(new { Status = divergence.Status.ToString() }),
            NewValue = JsonSerializer.Serialize(new { Status = nameof(DivergenceStatus.Resolved) }),
            UserId = null, CreatedAt = now
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            await repo.UpdateDivergenceStatusAsync(divergenceId, nameof(DivergenceStatus.Resolved), now, ct);
            await audit.InsertAsync(auditLog, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch { await unitOfWork.RollbackAsync(ct); throw; }
    }
}
```

- [ ] **Step 11: Controller (Api) + DI**

```csharp
// Api/Reconciliation/ReconciliationsController.cs
using FluentValidation;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.ImportOperation; // ReferenceNotFoundException
using InvestOps.Application.Reconciliation.ImportReconciliation;
using InvestOps.Application.Reconciliation.IgnoreDivergence;
using InvestOps.Application.Reconciliation.ListDivergences;
using InvestOps.Application.Reconciliation.ResolveDivergence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InvestOps.Api.Reconciliation;

[ApiController]
[Route("api/reconciliations")]
public sealed class ReconciliationsController(
    ImportReconciliationHandler importHandler,
    IValidator<ImportReconciliationRequest> importValidator,
    ListDivergencesHandler listHandler,
    ResolveDivergenceHandler resolveHandler,
    IgnoreDivergenceHandler ignoreHandler) : ControllerBase
{
    [HttpPost("import")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Import([FromForm] string source, [FromForm] DateOnly referenceDate, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Arquivo CSV obrigatório" });
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Arquivo deve ser .csv" });

        IReadOnlyList<InvestOps.Domain.Reconciliation.CustodianRow> rows;
        try { await using var stream = file.OpenReadStream(); rows = CsvCustodianStatementParser.Parse(stream); }
        catch (CsvFormatException ex) { return BadRequest(new ProblemDetails { Title = "CSV inválido", Detail = ex.Message }); }

        var request = new ImportReconciliationRequest(source, referenceDate, rows);
        var validation = await importValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var e in validation.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
            return ValidationProblem(ModelState);
        }
        try { return Ok(await importHandler.HandleAsync(request, ct)); }
        catch (ReferenceNotFoundException ex) { return UnprocessableEntity(new ProblemDetails { Title = "Referência inválida", Detail = ex.Message }); }
    }

    [HttpGet("divergences")]
    public async Task<IActionResult> Divergences([FromQuery] DivergencesQuery query, CancellationToken ct)
        => Ok(await listHandler.HandleAsync(query, ct));

    [HttpPost("divergences/{id:guid}/resolve")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct) => await Act(() => resolveHandler.HandleAsync(id, ct));

    [HttpPost("divergences/{id:guid}/ignore")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken ct) => await Act(() => ignoreHandler.HandleAsync(id, ct));

    private async Task<IActionResult> Act(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (ConflictException ex) { return Conflict(new ProblemDetails { Title = "Divergência não está pendente", Detail = ex.Message }); }
        catch (ReferenceNotFoundException ex) { return NotFound(new ProblemDetails { Title = "Divergência não encontrada", Detail = ex.Message }); }
    }
}
```
  DI: em `Application/DependencyInjection` `AddScoped` dos 4 handlers (`ImportReconciliationHandler`, `ListDivergencesHandler`, `Reconciliation.ResolveDivergence.ResolveDivergenceHandler`, `Reconciliation.IgnoreDivergence.IgnoreDivergenceHandler`). Em `Infrastructure/DependencyInjection` registrar `IReconciliationRepository→ReconciliationRepository` e `IReconciliationReadRepository→ReconciliationReadRepository`.

- [ ] **Step 12: Build** — `cd backend && dotnet build InvestOps.sln -c Release` (zero warnings). Corrigir nullable/usings.

- [ ] **Step 13: Testes de integração** (`ReconciliationTests.cs`, `[Collection(nameof(PostgresCollection))]`) — **espelhar o setup de `PositionsReadTests`/`ImportOperationTests`** (`NpgsqlConnectionFactory` + `DbSession` + `UnitOfWork` + repos reais; seed via **SQL direto**). Casos:
  - **Import cria import + divergências + audit**: seed conta/cliente/produto + 1 posição (qty 100). Rows: par com qty 150 (diverge) + par com qty igual à interna (confere). Handler → `DivergenceCount==1`, `MatchedCount==1`; 1 linha em `reconciliation_imports` (status `Completed`), 1 em `reconciliation_divergences` (`Pending`, internal 100 / custodian 150), 1 `audit_logs` (`entity_name='ReconciliationImport'`, `action='Imported'`).
  - **Atomicidade**: rows com `productId` inexistente ⇒ handler lança `ReferenceNotFoundException` e **nada** persiste (`SELECT count(*)` = 0 nas 3 tabelas).
  - **List** paginação + filtro por status: inserir N divergências (SQL); `ListDivergencesAsync` traz nomes (join), respeita `pageSize`, e `Status='Pending'` filtra.
  - **Resolve/Ignore**: seed 1 divergência `Pending`; `ResolveDivergenceHandler.HandleAsync` ⇒ status `Resolved` + `resolved_at` setado + `audit_logs` `Resolved`. `Ignore` ⇒ `Ignored`.
  - **Conflito**: divergência já `Resolved` ⇒ `ResolveDivergenceHandler` lança `ConflictException`. **Not found**: id aleatório ⇒ `ReferenceNotFoundException`.

  Rodar: `dotnet test backend/InvestOps.sln -c Release` → **PASS**.

- [ ] **Step 14: Verde total** (`dotnet build` + `dotnet test`) → commit + push + **PR para `developer`** → **PAUSA** para revisão do usuário.

---

## Chunk 2: Frontend (Task 8.2)

### Task 8.2: Tela de Conciliação (`feature/web-reconciliation` → PR → PAUSA)

**Files:** (ver File Structure, seção `[8.2]`)

- [ ] **Step 1: Branch** `feature/web-reconciliation` a partir de `developer` (após 8.1 mergeada).

- [ ] **Step 2: Models** — em `core/models/index.ts` adicionar:

```ts
export interface Divergence {
  id: string; importId: string; accountId: string; customerName: string; accountNumber: string;
  productId: string; productName: string; ticker: string | null;
  internalQuantity: number; custodianQuantity: number; difference: number;
  status: string; createdAt: string; resolvedAt: string | null;
}
export interface DivergencesQuery { page: number; pageSize: number; sort?: string; status?: string; }
export interface ImportReconciliationResult {
  importId: string; source: string; referenceDate: string;
  rowsProcessed: number; matchedCount: number; divergenceCount: number;
}
```

- [ ] **Step 3: API service** — `core/api/reconciliation.api.ts` (espelha `positions.api.ts`; import é **multipart** via `FormData`):

```ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Divergence, DivergencesQuery, ImportReconciliationResult, Paged } from '../models';
import { toHttpParams } from './http-params.util';

@Injectable({ providedIn: 'root' })
export class ReconciliationApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/reconciliations`;

  import(source: string, referenceDate: string, file: File): Observable<ImportReconciliationResult> {
    const form = new FormData();
    form.append('source', source);
    form.append('referenceDate', referenceDate);
    form.append('file', file);
    return this.http.post<ImportReconciliationResult>(`${this.baseUrl}/import`, form);
  }
  listDivergences(query: DivergencesQuery): Observable<Paged<Divergence>> {
    return this.http.get<Paged<Divergence>>(`${this.baseUrl}/divergences`, { params: toHttpParams({ ...query }) });
  }
  resolve(id: string): Observable<void> { return this.http.post<void>(`${this.baseUrl}/divergences/${id}/resolve`, {}); }
  ignore(id: string): Observable<void> { return this.http.post<void>(`${this.baseUrl}/divergences/${id}/ignore`, {}); }
}
```

- [ ] **Step 4: NgRx state** (`features/reconciliation/state/`) — espelha `positions` (load) + `operations` (mutação com snackbar/reload):
  - **actions**: `Load {query}` / `Load Success {result: Paged<Divergence>}` / `Load Failure {error}`; `Import {source, referenceDate, file}` / `Import Success {result}` / `Import Failure {error}`; `Resolve {id}` / `Ignore {id}`.
  - **feature (state)**: `{ query: DivergencesQuery, items: Divergence[], total, loading, error, importing: boolean, importError: string | null, lastImport: ImportReconciliationResult | null }`; `initial.query = { page:1, pageSize:20, sort:'createdAt:desc' }`. Reducers: `load`→loading; `loadSuccess`→items/total; `import`→importing=true/importError=null; `importSuccess`→importing=false/lastImport; `importFailure`→importing=false/importError. Export selectors (`selectItems, selectTotal, selectLoading, selectError, selectQuery, selectImporting, selectImportError, selectLastImport`).
  - **effects**:
    - `loadDivergences$`: `ofType(load)` → `api.listDivergences(query)` → success/failure (igual `loadPositions$`).
    - `importStatement$`: `ofType(import)` → `api.import(source, referenceDate, file)` → `importSuccess({result})` / `importFailure`. `catchError` mapeia `422`→"Referência inválida (conta/produto).", `400`→"CSV inválido.", senão genérico (padrão do `importErrorMessage` de operações).
    - `importNavigate$` (`dispatch:false`): `ofType(importSuccess)` → `snackBar.open(`${result.divergenceCount} divergência(s), ${result.matchedCount} conferida(s).`)` + dispatch `load({query})` (recarrega). *(pode ser um effect com dispatch que devolve `load` com `withLatestFrom(selectQuery)`.)*
    - `resolve$` / `ignore$`: `ofType(resolve|ignore)` → `api.resolve|ignore(id)` → `tap(snackBar)` → `withLatestFrom(store.select(selectQuery))` → `map(([,query]) => load({query}))` (recarrega), `catchError` snackbar de erro (padrão do `reprocessOperation$`).

- [ ] **Step 5: Componente** — `reconciliation.component.ts` (standalone, `OnPush`) + `.html`. Espelha `positions.component` (tabela server-side) e adiciona o painel de import.
  - **TS**: signals dos selectors; `displayedColumns = ['customerName','accountNumber','productName','internalQuantity','custodianQuantity','difference','status','actions']`; `statusFilter = ''` (''=todos); `pageIndex/pageSize/sortActive='createdAt'/sortDirection='desc'`; `selectedFile: File | null`; form fields `source`/`referenceDate` (pode ser `FormBuilder` como `operation-import`). `reload()` monta `DivergencesQuery` (inclui `status: statusFilter || undefined`) e dispatch `load`. `onFile(e)` guarda `input.files[0]`. `submitImport()` valida arquivo+source+data e dispatch `import`. **⚠️ `referenceDate`:** o datepicker devolve `Date`; formatar para **`yyyy-MM-dd`** antes de enviar (ex.: `formatDate(value, 'yyyy-MM-dd', 'en-US')` do `@angular/common`), pois o backend faz model-binding em `[FromForm] DateOnly`. `resolve(id)`/`ignore(id)` dispatch as ações. `onPage`/`onSort`/`onStatusChange` recarregam.
  - **HTML**: `app-page-header title="Conciliação"`; `mat-card` de import (input file `accept=".csv"`, `source`, `referenceDate` datepicker, botão "Importar extrato" com `[disabled]="importing()"`); filtro `mat-select` de status (valores exatamente **`Pending`/`Resolved`/`Ignored`** + ''=todos, pois o filtro no backend é `d.status = @Status` case-sensitive); tabela `mat-table` + `matSort` + `mat-paginator` (igual posições) com as colunas; coluna **difference** com classe de sinal (`[class.neg]="d.difference < 0"`); coluna **actions**: `@if (d.status === 'Pending') { <button (click)="resolve(d.id)">Resolver</button> <button (click)="ignore(d.id)">Ignorar</button> }`; `app-error-state` e `app-empty-state` como em posições.

- [ ] **Step 6: Rota + navegação + link do dashboard**
  - `app.routes.ts`: nova rota lazy `{ path: 'reconciliation', providers: [provideState(reconciliationFeature), provideEffects(reconciliationEffects)], loadComponent: () => import('./features/reconciliation/reconciliation.component').then(m => m.ReconciliationComponent) }` (importar `reconciliationFeature` e `* as reconciliationEffects`, no topo, como as outras features).
  - `app.component.ts`: novo `<a mat-list-item routerLink="/reconciliation" ...>` com `<mat-icon>rule</mat-icon>` + `Conciliação`.
  - `features/dashboard/dashboard.component.html`: no card "Divergências pendentes", envolver com `routerLink="/reconciliation"` (importar `RouterLink` no componente do dashboard).

- [ ] **Step 7: Testes Jest** (`reconciliation.component.spec.ts`, `reconciliation.feature.spec.ts`) — espelham `positions.*.spec`/`operations`:
  - **feature.spec**: `load`→loading true; `loadSuccess`→items/total, loading false; `import`→importing true; `importSuccess`→importing false + lastImport; `importFailure`→importError.
  - **component.spec** (store mockada via `provideMockStore`/stub de `selectSignal`): tabela renderiza itens; mudar filtro de status dispatch `load`; botões **Resolver/Ignorar** aparecem só quando `status==='Pending'` e dispatcham a ação; `submitImport()` com arquivo dispatch `import`; **error state** quando `selectError` tem valor.
  - Rodar: `cd frontend/investops-web && npm test` → **PASS** (as 30 existentes + as novas).

- [ ] **Step 8: Build** — `npm run build` (sem erros). **Verde total** (`npm test` + `npm run build`) → commit + push + **PR para `developer`** → **PAUSA**.

---

## Chunk 3: Docs (Task 8.3)

### Task 8.3: ADR + roadmap (`feature/reconciliation-docs` → PR → PAUSA)

- [ ] **Step 1: Branch** `feature/reconciliation-docs` a partir de `developer` (após 8.2).

- [ ] **Step 2: ADR 0007** — `docs/adr/0007-synchronous-reconciliation.md`, **seguindo o formato do [ADR 0006](../adr/0006-defer-authentication-adopt-rate-limiting.md)** (Título, Status: Aceito, Contexto, Decisão, Consequências). Registrar: a conciliação é **síncrona na API** (compara o extrato contra `portfolio_positions` numa transação), **fora** do worker/outbox — porque é leitura da posição atual, não um fluxo assíncrono; `resolve`/`ignore` são transições de estado + auditoria e **não** ajustam posição (o worker é a fonte da verdade). Alternativas consideradas (assíncrona via worker; ajuste de posição no resolve) e por que ficaram fora do MVP.

- [ ] **Step 3: README roadmap** — em [README.md](../../README.md) na tabela **Status & roadmap**, mudar a linha *"Conciliação com custodiante (reconciliation)"* de `🔲 planejado` para **✅**. Opcional: uma nota curta "Como usar" com um CSV de exemplo (`account_id,product_id,quantity`).

- [ ] **Step 4:** commit + push + **PR para `developer`** → **PAUSA**.

---

## Fechamento da Fase 8

Após 8.3 mergeada e `developer` promovida a `main`: a Conciliação fica completa ponta a ponta (import CSV → divergências → resolver/ignorar, com auditoria), fechando o item **Conciliação** do roadmap. Restam no SPEC: **Processing monitor** (UI de eventos do worker) e **CRUD de produtos** — cada um em sua fase.
```
