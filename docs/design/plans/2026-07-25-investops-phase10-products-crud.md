# InvestOps Backoffice — Fase 10 (CRUD de Produtos) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (se houver subagents) ou superpowers:executing-plans. Steps usam checkbox (`- [ ]`). Aplique @superpowers:test-driven-development nos validators e @superpowers:verification-before-completion antes de cada "verde".

**Goal:** Completar o gerenciamento de produtos financeiros — `GET` completo + `POST`/`PUT`/`DELETE` (lógico) com ticker único — e a tela Angular de gestão. Fecha o SPEC §8 Products.

**Architecture:** Vertical `Products` (.NET) espelhando os slices existentes; ticker único garantido por **índice parcial** (migration) + captura de `23505` → `409` (sem TOCTOU). O `GET /api/products` passa a servir o produto completo e **substitui/remove** o stack de produtos de `Lookups`. Frontend: feature `products` espelhando `reconciliation`/`operation-import`.

**Tech Stack:** .NET 8 (Dapper, Npgsql, FluentValidation, DbUp, xUnit, Testcontainers), Angular 21 (zoneless, signals, NgRx, Material, Jest).

**Spec:** [docs/design/specs/2026-07-25-investops-phase10-products-crud-design.md](../specs/2026-07-25-investops-phase10-products-crud-design.md)

---

## Convenções

- Branch `feature/*` a partir de `developer`; PR para `developer`; **1 task → 1 PR → PAUSA.**
- .NET em `backend/` (`TreatWarningsAsErrors=true`). Frontend em `frontend/investops-web/`. Reads via `IDbSession`; escrita single-table sem UoW (sem auditoria). **DTO sempre.**
- API camelCase; escrita (`POST`/`PUT`/`DELETE`) sob `[EnableRateLimiting(RateLimitingOptions.WritePolicy)]`. Mapeamento de exceção **local no controller** (`ConflictException`→`409`), 404 por linhas-afetadas.

## File Structure

```text
backend/src/InvestOps.Migrator/Scripts/Schema/Script0003_financial_products_ticker_unique.sql   [10.1]
backend/src/InvestOps.Application/Products/
  {ProductDto,CreateProductRequest,UpdateProductRequest,CreateProductValidator,UpdateProductValidator,
   ListProductsHandler,CreateProductHandler,UpdateProductHandler,DeleteProductHandler}.cs         [10.1]
backend/src/InvestOps.Application/Abstractions/IFinancialProductRepository.cs   # +List/Insert/Update/Deactivate [10.1]
backend/src/InvestOps.Infrastructure/Persistence/Repositories/FinancialProductRepository.cs # impl (23505→Conflict) [10.1]
backend/src/InvestOps.Api/Products/ProductsController.cs                         # GET/POST/PUT/DELETE [10.1]
# REMOÇÃO do Lookups de produtos:
backend/src/InvestOps.Api/Lookups/ProductsController.cs           (excluir — relocado)            [10.1]
backend/src/InvestOps.Application/Lookups/{ListProductsHandler,ProductLookupDto}.cs (excluir)     [10.1]
backend/src/InvestOps.Application/Abstractions/ILookupRepository.cs   (- ListProductsAsync)        [10.1]
backend/src/InvestOps.Infrastructure/Persistence/Repositories/LookupRepository.cs (- ListProductsAsync) [10.1]
backend/src/InvestOps.Application/DependencyInjection.cs   (- Lookups.ListProductsHandler; + 4 Products handlers) [10.1]
backend/tests/InvestOps.IntegrationTests/LookupsReadTests.cs   (- Products_returns_seeded_product)  [10.1]
backend/tests/InvestOps.UnitTests/{CreateProductValidatorTests,UpdateProductValidatorTests}.cs     [10.1]
backend/tests/InvestOps.IntegrationTests/ProductsCrudTests.cs                                       [10.1]

frontend/investops-web/src/app/
  core/api/products.api.ts ; core/models/index.ts (+Product, +ProductRequest)                       [10.2]
  features/products/products-list.component.{ts,html,scss,spec.ts}
  features/products/product-form.component.{ts,html,scss,spec.ts}
  features/products/state/{products.actions,products.effects,products.feature,products.feature.spec}.ts
  app.routes.ts (+/products,+/products/new,+/products/:id), app.component.ts (+nav) ; README.md      [10.2]
```

---

## Chunk 1: Backend (Task 10.1)

### Task 10.1: `feature/products-crud` → PR → PAUSA

- [ ] **Step 1: Branch** `feature/products-crud` a partir de `developer` (já criada; a spec está nela).

- [ ] **Step 2: Migration** — `Scripts/Schema/Script0003_financial_products_ticker_unique.sql`:
```sql
CREATE UNIQUE INDEX ux_financial_products_ticker
ON financial_products (ticker)
WHERE ticker IS NOT NULL;
```
(EmbeddedResource automático; roda após Script0002 e antes do seed.)

- [ ] **Step 3: DTO + Requests + Validators (TDD dos validators)** — teste primeiro `CreateProductValidatorTests` (enum inválido; `RiskLevel` 0 e 6; nome vazio; `LiquidityDays` -1; ticker >20) **e `UpdateProductValidatorTests`** (mesmos casos sobre `UpdateProductRequest`, cobrindo a spec §5). Depois:
```csharp
// Application/Products/ProductDto.cs
namespace InvestOps.Application.Products;
public sealed record ProductDto(Guid Id, string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays, bool IsActive);
```
```csharp
// CreateProductRequest.cs (sem IsActive — nasce ativo) / UpdateProductRequest.cs (com IsActive)
public sealed record CreateProductRequest(string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays);
public sealed record UpdateProductRequest(string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays, bool IsActive);
```
```csharp
// CreateProductValidator.cs (UpdateProductValidator é idêntico p/ UpdateProductRequest)
using FluentValidation;
using InvestOps.Domain.Enums;
namespace InvestOps.Application.Products;
public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ProductType)
            .Must(v => Enum.TryParse<ProductType>(v, ignoreCase: false, out _) && !int.TryParse(v, out _))
            .WithMessage("productType inválido");
        RuleFor(x => x.RiskLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.LiquidityDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Ticker).MaximumLength(20);
    }
}
```

- [ ] **Step 4: Repositório** — estender `IFinancialProductRepository`:
```csharp
Task<FinancialProduct?> GetByIdAsync(Guid id, CancellationToken ct);        // existente
Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct);
Task InsertAsync(FinancialProduct product, CancellationToken ct);
Task<int> UpdateAsync(FinancialProduct product, CancellationToken ct);       // linhas afetadas → 404
Task<int> DeactivateAsync(Guid id, CancellationToken ct);                    // soft delete → 404
```
Impl em `FinancialProductRepository` (`using Npgsql;`, `using InvestOps.Application.Common;`, `using InvestOps.Application.Products;`). Enum como **string** no parâmetro; `23505` → `ConflictException`:
```csharp
public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct)
{
    const string sql = "SELECT id, product_type, name, ticker, risk_level, liquidity_days, is_active FROM financial_products ORDER BY name;";
    var conn = await session.GetConnectionAsync(ct);
    var rows = await conn.QueryAsync<ProductDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
    return rows.AsList();
}

public async Task InsertAsync(FinancialProduct p, CancellationToken ct)
{
    const string sql = """
        INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active)
        VALUES (@Id, @ProductType, @Name, @Ticker, @RiskLevel, @LiquidityDays, @IsActive);
        """;
    var conn = await session.GetConnectionAsync(ct);
    try
    {
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { p.Id, ProductType = p.ProductType.ToString(), p.Name, p.Ticker, p.RiskLevel, p.LiquidityDays, p.IsActive },
            session.Transaction, cancellationToken: ct));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        throw new ConflictException($"Ticker '{p.Ticker}' já está em uso.");
    }
}

public async Task<int> UpdateAsync(FinancialProduct p, CancellationToken ct)
{
    const string sql = """
        UPDATE financial_products
        SET product_type=@ProductType, name=@Name, ticker=@Ticker, risk_level=@RiskLevel, liquidity_days=@LiquidityDays, is_active=@IsActive
        WHERE id=@Id;
        """;
    var conn = await session.GetConnectionAsync(ct);
    try
    {
        return await conn.ExecuteAsync(new CommandDefinition(sql,
            new { p.Id, ProductType = p.ProductType.ToString(), p.Name, p.Ticker, p.RiskLevel, p.LiquidityDays, p.IsActive },
            session.Transaction, cancellationToken: ct));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        throw new ConflictException($"Ticker '{p.Ticker}' já está em uso.");
    }
}

public async Task<int> DeactivateAsync(Guid id, CancellationToken ct)
{
    const string sql = "UPDATE financial_products SET is_active = false WHERE id = @id;";
    var conn = await session.GetConnectionAsync(ct);
    return await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
}
```

- [ ] **Step 5: Handlers**
```csharp
public sealed class ListProductsHandler(IFinancialProductRepository repo)
{ public Task<IReadOnlyList<ProductDto>> HandleAsync(CancellationToken ct) => repo.ListAsync(ct); }

public sealed class CreateProductHandler(IFinancialProductRepository repo)
{
    public async Task<ProductDto> HandleAsync(CreateProductRequest r, CancellationToken ct)
    {
        var p = new FinancialProduct
        {
            Id = Guid.NewGuid(),
            ProductType = Enum.Parse<ProductType>(r.ProductType),
            Name = r.Name,
            Ticker = string.IsNullOrWhiteSpace(r.Ticker) ? null : r.Ticker,
            RiskLevel = r.RiskLevel, LiquidityDays = r.LiquidityDays, IsActive = true
        };
        await repo.InsertAsync(p, ct);
        return new ProductDto(p.Id, p.ProductType.ToString(), p.Name, p.Ticker, p.RiskLevel, p.LiquidityDays, p.IsActive);
    }
}

public sealed class UpdateProductHandler(IFinancialProductRepository repo)
{
    public async Task<bool> HandleAsync(Guid id, UpdateProductRequest r, CancellationToken ct)
    {
        var p = new FinancialProduct
        {
            Id = id, ProductType = Enum.Parse<ProductType>(r.ProductType), Name = r.Name,
            Ticker = string.IsNullOrWhiteSpace(r.Ticker) ? null : r.Ticker,
            RiskLevel = r.RiskLevel, LiquidityDays = r.LiquidityDays, IsActive = r.IsActive
        };
        return await repo.UpdateAsync(p, ct) > 0;
    }
}

public sealed class DeleteProductHandler(IFinancialProductRepository repo)
{ public async Task<bool> HandleAsync(Guid id, CancellationToken ct) => await repo.DeactivateAsync(id, ct) > 0; }
```

- [ ] **Step 6: Controller** — `Api/Products/ProductsController.cs`:
```csharp
using FluentValidation;
using InvestOps.Application.Common;
using InvestOps.Application.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InvestOps.Api.Products;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    ListProductsHandler list,
    CreateProductHandler create, IValidator<CreateProductRequest> createValidator,
    UpdateProductHandler update, IValidator<UpdateProductRequest> updateValidator,
    DeleteProductHandler delete) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await list.HandleAsync(ct));

    [HttpPost]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var v = await createValidator.ValidateAsync(request, ct);
        if (!v.IsValid) { foreach (var e in v.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage); return ValidationProblem(ModelState); }
        try { var dto = await create.HandleAsync(request, ct); return Created($"/api/products/{dto.Id}", dto); }
        catch (ConflictException ex) { return Conflict(new ProblemDetails { Title = "Ticker duplicado", Detail = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var v = await updateValidator.ValidateAsync(request, ct);
        if (!v.IsValid) { foreach (var e in v.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage); return ValidationProblem(ModelState); }
        try { return await update.HandleAsync(id, request, ct) ? NoContent() : NotFound(); }
        catch (ConflictException ex) { return Conflict(new ProblemDetails { Title = "Ticker duplicado", Detail = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(RateLimitingOptions.WritePolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await delete.HandleAsync(id, ct) ? NoContent() : NotFound();
}
```

- [ ] **Step 7: Remoção do Lookups de produtos + DI** —
  - Excluir `Api/Lookups/ProductsController.cs`, `Application/Lookups/ListProductsHandler.cs`, `Application/Lookups/ProductLookupDto.cs`.
  - `ILookupRepository`: remover `ListProductsAsync`. `LookupRepository`: remover a impl `ListProductsAsync` (o resto — customers/accounts — permanece).
  - `Application/DependencyInjection.cs`: remover `services.AddScoped<Lookups.ListProductsHandler>();`; adicionar `AddScoped<Products.ListProductsHandler>()`, `Products.CreateProductHandler`, `Products.UpdateProductHandler`, `Products.DeleteProductHandler`. (Validators auto-registrados via `AddValidatorsFromAssemblyContaining`.)
  - `tests/InvestOps.IntegrationTests/LookupsReadTests.cs`: remover o teste `Products_returns_seeded_product` **e apenas o helper `ProductsHandler()` órfão**. **Manter** os `using InvestOps.Application.Lookups;` e `using Dapper;` (ainda usados pelos handlers/SQL de customers/accounts); os testes de customers/accounts ficam.

- [ ] **Step 8: Build** — `cd backend && dotnet build InvestOps.sln -c Release` (0 warnings; resolver referências órfãs ao `ProductLookupDto`/handler removidos).

- [ ] **Step 9: Testes de integração** — `ProductsCrudTests` (`[Collection(nameof(PostgresCollection))]`, seed/SQL como `PositionsReadTests`): POST cria → `GET` lista com os campos certos (productType string, isActive true); **POST ticker duplicado → 409** (criar um produto com um ticker — ou reusar o seed `PETR4` — e então um segundo POST com o mesmo ticker); **dois POST com ticker nulo → ambos ok** (índice parcial); PUT altera campos e `is_active` + **404** em id inexistente + **409** ao usar ticker de outro produto; **DELETE** seta `is_active=false` (confirma via GET/SQL) + **404** em id inexistente; re-DELETE de inativo → **204**. Rodar `dotnet test InvestOps.sln -c Release` → verde.

- [ ] **Step 10: Verde total** → commit + push + **PR para `developer`** → **PAUSA**.

---

## Chunk 2: Frontend (Task 10.2)

### Task 10.2: `feature/web-products` → PR → PAUSA

Espelha `reconciliation` (NgRx + mutação) e `operation-import` (form reativo).

- [ ] **Step 1: Branch** `feature/web-products` a partir de `developer` (após 10.1 mergeada).

- [ ] **Step 2: Models + API** — em `core/models/index.ts`:
```ts
export interface Product { id: string; productType: string; name: string; ticker: string | null; riskLevel: number; liquidityDays: number; isActive: boolean; }
export interface ProductRequest { productType: string; name: string; ticker: string | null; riskLevel: number; liquidityDays: number; isActive?: boolean; }
```
`core/api/products.api.ts` (mirror `positions.api.ts`): `list(): Product[]`, `create(req): Product`, `update(id, req): void`, `remove(id): void` (`http.delete`).

- [ ] **Step 3: NgRx** (`features/products/state/`) — state `{ items, loading, error, saving, saveError }`; actions `load`/`loadSuccess`/`loadFailure`, `save({id?, request})`/`saveSuccess`/`saveFailure`, `remove({id})`/`removeSuccess`/`removeFailure`. Effects: `load$`→`api.list`; `save$`→ `id ? api.update(id,req) : api.create(req)` → snackbar + `router.navigate(['/products'])` + reload; `remove$`→`api.remove(id)` → snackbar + reload. `saveErrorMessage` mapeia `409`→"Ticker já existe.", `400/422`→"Dados inválidos.".

- [ ] **Step 4: Telas** — mirror `positions.component` (tabela) + `operation-import.component` (form):
  - **`products-list.component`**: `MatTable` (Nome, Ticker, Tipo, Risco, Liquidez, **Ativo** via `app-status-chip`/ícone, ações). Botão **Novo produto** (`routerLink="/products/new"`). Linha: **Editar** (`routerLink="/products/{id}"`) e **Excluir** (confirma → `remove`); linhas com `!isActive` esmaecidas. Loading/erro/vazio.
  - **`product-form.component`** (`/products/new` e `/products/:id`): form reativo — select `ProductType` (`FixedIncome|Fund|Stock|Derivative`), `name` (obrigatório), `ticker` (opcional), `riskLevel` (1–5), `liquidityDays` (≥0); **toggle `isActive` só quando editando**. No modo edição, popula do item do estado (carrega a lista via `load` se vazia e seleciona por id). Submete `save({ id?, request })`; erro de ticker (`saveError`) exibido no campo/snackbar.
- [ ] **Step 5: Rotas + nav + roadmap** — `app.routes.ts`: rota lazy `/products` com `provideState(productsFeature)`/`provideEffects` e filhos `''`(lista)/`new`/`:id`(form). `app.component.ts`: nav item (ícone `inventory_2`, label **Produtos**). `README.md`: roadmap *CRUD de produtos* 🔲 → **✅**.

- [ ] **Step 6: Testes (Jest)** — lista renderiza; **Novo/Editar** submetem `save` com o request certo (create sem `id`, update com `id`); **Excluir** despacha `remove`; **409** mostra "Ticker já existe"; form valida (nome vazio, risco fora de 1–5). Reducer specs (`save`→saving; `saveFailure`→saveError).

- [ ] **Step 7: Verde** — `npm test` + `npm run build` → commit + push + **PR para `developer`** → **PAUSA**.

---

## Fechamento da Fase 10

Após 10.2 mergeada e `developer` promovida a `main`: o CRUD de produtos fica completo (com ticker único e exclusão lógica) e o **roadmap do SPEC fica 100% coberto** (só auth segue fora do MVP por decisão — ADR 0006).

## Fora de escopo (reforço)
Hard delete; auditoria de produto; paginação do `GET /api/products`; filtrar dropdowns por `isActive`.
