# Design — InvestOps Backoffice (Fase 10: CRUD de Produtos)

**Data:** 2026-07-25
**Status:** Aprovado (início da Fase 10)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§8 Products, §5 FinancialProduct, §6 Regra 1 suitability usa RiskLevel)
**Depende de:** Fase 4a (read API — `IDbSession`), Fase 4b (frontend + NgRx), Fase 2 (validators/rate-limit, `ConflictException`).

Completa o gerenciamento de **produtos financeiros**: além do `GET` que hoje alimenta os dropdowns, adiciona **criar/editar/excluir (lógico)** com **ticker único**. Slice completo: **backend** (.NET) + **tela Angular** de gestão. Fecha o último item do roadmap (SPEC §8 Products).

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Escopo | **Slice completo**: backend + tela; PRs 10.1 (backend) → 10.2 (frontend) |
| `GET /api/products` | Passa a retornar o **`ProductDto` completo** (superset do lookup atual) — dropdowns de operações continuam funcionando |
| Ticker único | **Índice único parcial** (onde `ticker IS NOT NULL`) via migration **Script0003** + captura de `23505` → **`409`**; ticker é **opcional** |
| DELETE | **Lógico (soft delete)**: `DELETE /api/products/{id}` seta `is_active=false` (sem hard delete — FKs de operations/positions/reconciliation_divergences apontam para `product_id`). PUT edita tudo, incl. **reativar** via `isActive` |
| Auditoria | **Nenhuma** no CRUD de produto (fora da Regra 6) — anotado como evolução |
| Rate limiting | `POST`/`PUT`/`DELETE` são escrita → política mais rígida |
| Migration | **Script0003** (a Schema já vai até `Script0002`) |
| Enums | `ProductType` como **string** no DTO/Request (convenção do projeto), parse no handler |

## 2. Backend (.NET)

### Migration — `Scripts/Schema/Script0003_financial_products_ticker_unique.sql`
```sql
CREATE UNIQUE INDEX ux_financial_products_ticker
ON financial_products (ticker)
WHERE ticker IS NOT NULL;
```
Descoberta automática pelo DbUp (`EmbeddedResource Scripts\**\*.sql`, journal `schemaversions`). O seed atual (LFT/MULTI/PETR4/WINFUT, únicos) é compatível.

### Application — vertical `Application/Products/`
- **`ProductDto`** `(Guid Id, string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays, bool IsActive)`.
- **`ListProducts`** — `ListProductsHandler` → `IReadOnlyList<ProductDto>` (todos os produtos, ativos e inativos, **`ORDER BY name`**; a tela de gestão precisa de ambos). **Substitui e remove** o stack de listagem de produtos de `Lookups` — ver **"Remoção do Lookups de produtos"** abaixo.
- **`CreateProduct`** — `CreateProductRequest(string ProductType, string Name, string? Ticker, int RiskLevel, int LiquidityDays)` (**sem `IsActive`** — produto nasce **ativo**; reativar/desativar é via PUT), `CreateProductValidator`, `CreateProductHandler` → insere `FinancialProduct` (novo `Guid`, `IsActive=true`), retorna `ProductDto`.
- **`UpdateProduct`** — `UpdateProductRequest` (mesmos campos), `UpdateProductValidator`, `UpdateProductHandler` → `404` se não existe (linhas afetadas), atualiza todos os campos (incl. `IsActive`).
- **`DeleteProduct`** — `DeleteProductHandler` → **soft delete** (`is_active=false`); `404` se não existe.
- **Validação (FluentValidation, espelha `ImportOperationValidator`):** `Name` NotEmpty ≤200; `ProductType` deve ser um valor válido do enum (`Enum.TryParse<ProductType>(v, ignoreCase:false) && !int.TryParse`); `RiskLevel` InclusiveBetween(1,5); `LiquidityDays` ≥0; `Ticker` ≤20 (opcional). Auto-registrados pelo `AddValidatorsFromAssemblyContaining`.

### Remoção do Lookups de produtos (parte da PR 10.1)
A listagem de produtos sai de `Lookups` (agora vem do vertical `Products`). Para não quebrar o build/tests, a PR **deve**:
- **Remover** `Api/Lookups/ProductsController.cs` (relocado para `Api/Products/`), `Application/Lookups/ListProductsHandler.cs`, `ProductLookupDto` (backend), e `ListProductsAsync` de `ILookupRepository`/`LookupRepository` (o `LookupRepository` **permanece** — customers/accounts o usam).
- **Editar `Application/DependencyInjection.cs`:** remover `AddScoped<Lookups.ListProductsHandler>()`; adicionar os handlers de `Products`.
- **Atualizar `tests/InvestOps.IntegrationTests/LookupsReadTests.cs`:** remover o teste `Products_returns_seeded_product` (superado pelos novos testes de `Products`); os testes de customers/accounts permanecem.

### Infraestrutura — `IFinancialProductRepository` (estendido; `GetByIdAsync` preservado)
```csharp
Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct);
Task InsertAsync(FinancialProduct product, CancellationToken ct);
Task<int> UpdateAsync(FinancialProduct product, CancellationToken ct);   // linhas afetadas → 404
Task<int> DeactivateAsync(Guid id, CancellationToken ct);                // soft delete → 404
```
- SQL parametrizado; `ListAsync` faz **`ORDER BY name`** (como o lookup atual, mantém ordenação estável para a tabela e os dropdowns); `product_type` gravado como **string** (`ToString()`), como os demais enums. `Insert`/`Update` deixam o **índice único** falhar com `PostgresException` `SqlState 23505` → traduzido para `ConflictException` (na infra ou no handler).

### API — `ProductsController` (relocado para `Api/Products/`, remove o de `Api/Lookups/`)
```http
GET    /api/products              -> 200 ProductDto[]
POST   /api/products              -> 201 ProductDto | 409 (ticker) | 400/422 (validação)
PUT    /api/products/{id}         -> 204 | 404 | 409 (ticker) | 400/422
DELETE /api/products/{id}         -> 204 | 404          (soft delete: is_active=false)
```
- `POST`/`PUT`/`DELETE` → `[EnableRateLimiting(RateLimitingOptions.WritePolicy)]`. Mapeamento local (sem middleware global): `ConflictException` → `409`, produto inexistente → `404`, validação → `400`. `POST` valida via `IValidator<CreateProductRequest>` (padrão do `OperationsController.Import`).

## 3. Frontend (Angular) — feature `products`

Espelha `positions`/`reconciliation` (standalone, `OnPush`, signals, NgRx, Material).

- **`core/api/products.api.ts`** — `list()`, `create(req)`, `update(id, req)`, `remove(id)`.
- **Models:** `Product { id, productType, name, ticker, riskLevel, liquidityDays, isActive }`; `ProductRequest { productType, name, ticker, riskLevel, liquidityDays, isActive? }` (`isActive` só no PUT). (O `ProductLookup` existente e `LookupsApi.products()` seguem funcionando — subconjunto estrutural do `ProductDto` — usados pelos dropdowns de **operações** e pela tela de **cotações** `quotes.component`.)
- **NgRx** (`features/products/state/`): state `{ items, loading, error, saving, saveError }`; actions `load`/`loadSuccess`/`loadFailure`, `save({id?, request})`/`saveSuccess`/`saveFailure`, `remove({id})`/`removeSuccess`/`removeFailure`. Effects: `load$` (list), `save$` (create se `!id`, senão update → snackbar + reload + navega para a lista), `remove$` (delete → snackbar + reload). `saveErrorMessage` mapeia `409`→"Ticker já existe", `400/422`→"Dados inválidos".
- **Telas:**
  - **`/products`** — tabela: Nome, Ticker, Tipo, Risco, Liquidez, **Ativo** (chip/ícone), ações **Editar** e **Excluir** (confirma → `DELETE` soft; linhas inativas aparecem esmaecidas). Botão **Novo produto**.
  - **`/products/new`** e **`/products/:id`** — formulário reativo (select `ProductType`, `name`, `ticker` opcional, `riskLevel` 1–5, `liquidityDays` ≥0; toggle `isActive` **só na edição** — novo produto nasce ativo). O `:id` edita a partir do item já carregado no estado (carrega a lista se vazia). Submete via `save`.
  - Rota lazy `/products` (+ `new`/`:id`) com `provideState`/`provideEffects`; item no nav (ícone `inventory_2`, label **Produtos**).
- **Testes Jest:** tabela renderiza; **Novo/Editar** despacham `save` com o request certo; **Excluir** despacha `remove`; erro `409` mostra "Ticker já existe"; validação do form (campos obrigatórios / risco fora de 1–5).

## 4. Erros e regras

- **Ticker duplicado** (POST/PUT com ticker já usado por outro produto) → `409`. Nulos nunca conflitam (índice parcial).
- **PUT/DELETE em id inexistente** → `404` (linhas afetadas = 0).
- **Validação** (enum inválido, risco fora de 1–5, nome vazio, liquidez negativa) → `400`/`422`.
- **Soft delete idempotente** o suficiente: excluir um produto já inativo ainda retorna `204` (o `UPDATE ... WHERE id=@id` afeta a linha); só `404` quando o id não existe.
- Produtos inativos **continuam** aparecendo no `GET` (a tela de gestão os mostra); os dropdowns de operações podem, numa evolução, filtrar por `isActive` (fora de escopo).

## 5. Testes

**Unit (.NET):** `CreateProductValidator`/`UpdateProductValidator` (enum inválido, risco fora de 1–5, nome vazio, liquidez negativa, ticker longo).
**Integração (Testcontainers):** POST cria e o `GET` lista; POST com ticker duplicado → `409`; POST com ticker nulo × 2 → **ok** (índice parcial); PUT atualiza campos + `404` em id inexistente + `409` em ticker de outro produto; DELETE seta `is_active=false` + `404` em id inexistente.
**Frontend (Jest):** conforme §3.

## 6. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 10.1 | `feature/products-crud` | **.NET**: Script0003 (índice único parcial); vertical `Products` (List completo + Create + Update + Delete-soft + validators); `IFinancialProductRepository` estendido; `ProductsController` relocado (`GET`/`POST`/`PUT`/`DELETE`) + rate-limit + `409` no ticker; **remoção do stack de produtos de `Lookups`** (handler/DTO/repo-method + DI + `LookupsReadTests`, ver §2); testes unit + integração |
| 10.2 | `feature/web-products` | **Angular**: `products.api.ts` + models + NgRx + tela (lista + form novo/editar + excluir + ativar/desativar) + rota/nav + README roadmap ✅ + testes Jest |

**Ordem:** 10.1 → 10.2. Cada uma um PR para `developer`.

## 7. Fora de escopo (Fase 10)

- **Hard delete** (quebraria FKs de operations/positions/reconciliation_divergences) — o DELETE é **lógico**.
- **Auditoria** do CRUD de produto (fora da Regra 6).
- Filtrar dropdowns de operações por `isActive` — evolução.
- Paginação do `GET /api/products` (poucos produtos; lista simples, como o lookup atual).
- Autenticação/autorização (SPEC §15, [ADR 0006](../../adr/0006-defer-authentication-adopt-rate-limiting.md)).
