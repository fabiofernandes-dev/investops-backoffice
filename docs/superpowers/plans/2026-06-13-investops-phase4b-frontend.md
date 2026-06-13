# InvestOps Backoffice — Fase 4b (Frontend Angular) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar o frontend Angular do backoffice (Dashboard, Operações lista+detalhe, Posições) consumindo a API real, com NgRx, Material e Jest — precedido de uma preparação de backend (CORS + nomes na lista + lookups).

**Architecture:** Angular 19 standalone + Angular Material + NgRx (store/effects, APIs standalone). Cada feature isola seu estado (`provideState`/`provideEffects` em rota lazy). HTTP com services tipados + interceptors (erro/loading). Backend ganha CORS, enriquecimento da lista de operações (nomes via join) e endpoints de lookup.

**Tech Stack:** Angular 19, Angular Material, NgRx 19, RxJS, Jest (`jest-preset-angular`), .NET 8 (prep).

**Spec:** [docs/superpowers/specs/2026-06-13-investops-phase4b-frontend-design.md](../specs/2026-06-13-investops-phase4b-frontend-design.md)

---

## Convenções

- Branch `feature/*` a partir de `developer`; PR para `developer`; revisão do usuário; 1 task → 1 PR → **pausa**.
- Comandos via **Bash tool (git-bash)**. Backend em `backend/`; frontend em `frontend/investops-web/`.
- API roda em `http://localhost:5000`; Angular dev em `http://localhost:4200`.
- API é **camelCase** (default MVC) → models TS camelCase.

---

## Chunk 1: Preparação de backend (4b.1)

### Task 4b.1: CORS + nomes na lista de operações + lookups (.NET)

**Files:**
- Modify: `backend/src/InvestOps.Api/Program.cs` (CORS)
- Create: `backend/src/InvestOps.Application/Operations/ListOperations/OperationListItemDto.cs`
- Modify: `Application/Abstractions/IOperationRepository.cs` (ListAsync → DTO), `ListOperationsHandler.cs`, `Infrastructure/.../OperationRepository.cs` (join), `Api/Operations/OperationsController.cs` (tipo de retorno), `tests/.../OperationsReadTests.cs`
- Create: `Application/Lookups/{ProductLookupDto,CustomerLookupDto,ListProductsHandler,ListCustomersHandler}.cs`, `Application/Abstractions/ILookupRepository.cs`, `Infrastructure/.../LookupRepository.cs`, `Api/Lookups/{ProductsController,CustomersController}.cs`
- Test: `tests/.../LookupsReadTests.cs`

- [ ] **Step 1: Branch** `feature/api-frontend-prep`.

- [ ] **Step 2: CORS** — em `Program.cs`, antes de `var app = builder.Build();`:
```csharp
const string DevCors = "dev-cors";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p =>
    p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));
```
Depois do build, **só em Development**, antes de `MapControllers`:
```csharp
if (app.Environment.IsDevelopment())
    app.UseCors(DevCors);
```

- [ ] **Step 3: `OperationListItemDto`** (record; datas como `DateOnly` via handler já registrado; sem timestamps — não aparecem na tela):
```csharp
public sealed record OperationListItemDto(
    Guid Id, string ExternalId, Guid AccountId, Guid ProductId,
    string CustomerName, string AccountNumber, string ProductName, string? Ticker,
    string OperationType, decimal Quantity, decimal UnitPrice, decimal GrossValue, decimal NetValue,
    DateOnly OperationDate, DateOnly SettlementDate, string Status, string? RejectionReason);
```
> **⚠️ `DateOnly` via construtor é NÃO verificado** (nenhum código atual materializa `date`→`DateOnly` por construtor de record; o `PositionDto.UpdatedAt` é `timestamptz`→`DateTime`, e o `OperationDto` constrói `DateOnly` via `From(entity)`, não do SQL). Mesma classe de armadilha que o `DateTimeOffset` na 4a. **Estratégia:** escrever o teste de integração da lista **primeiro** (Step 8) — ele revela na hora se o Dapper liga `DateOnly` no construtor. Se falhar, fallback imediato: `o.operation_date::text`/`o.settlement_date::text` no SQL e campos `string` no DTO.

- [ ] **Step 4: Enriquecer `ListAsync`** — `IOperationRepository.ListAsync` passa a retornar `(IReadOnlyList<OperationListItemDto> Items, int Total)`. Na impl, join (espelhando `PositionReadRepository`):
```sql
SELECT o.id, o.external_id, o.account_id, o.product_id,
       c.name AS customer_name, a.account_number, fp.name AS product_name, fp.ticker,
       o.operation_type, o.quantity, o.unit_price, o.gross_value, o.net_value,
       o.operation_date, o.settlement_date, o.status, o.rejection_reason
FROM operations o
JOIN investment_accounts a ON a.id = o.account_id
JOIN customers c           ON c.id = a.customer_id
JOIN financial_products fp ON fp.id = o.product_id
{where} ORDER BY {orderBy} LIMIT @Limit OFFSET @Offset;
```
`where` (filtros `o.status`/`o.product_id`/`a.customer_id`) e `COUNT(*)` com os mesmos filtros. `ListOperationsHandler` envolve em `PagedResult<OperationListItemDto>`; remove o `OperationDto.From` da lista.

> **⚠️ ORDER BY ambíguo (corrigir — bloqueante):** o join traz `customers` e `investment_accounts`, que **também têm `created_at` e `status`**. Como o whitelist do `SortClauseBuilder` é `operation_date/created_at/gross_value/status` e o **default é `created_at DESC`**, um `ORDER BY` sem prefixo lança `42702: column reference "created_at" is ambiguous` — quebrando **toda listagem sem sort** (e os 2 testes existentes que não passam `Sort`). **Fix:** qualificar a saída do `SortClauseBuilder` com `o.` (valores do dicionário → `o.operation_date`, `o.created_at`, `o.gross_value`, `o.status`; `Default = "o.created_at DESC"`). Isso **também exige atualizar** `tests/.../SortClauseBuilderTests.cs` (asserts passam a esperar `o.<coluna>`). O builder é usado **só** pela lista de operações (sem outros callers), então qualificar é seguro.

- [ ] **Step 5: Controller + testes** — `OperationsController.List` retorna `IActionResult`/`Ok(result)` (não muda assinatura; só o tipo dentro do `PagedResult` muda no handler). Verificar que **nenhum outro caller** de `IOperationRepository.ListAsync` existe além do handler. Atualizar `OperationsReadTests`: os testes `List_paginates_filtered_by_product` e `List_filters_by_status` usam **sort default** → já cobertos pelo fix do ORDER BY; asserts passam a checar nomes (`CustomerName`/`ProductName`) além de Total/contagem.

- [ ] **Step 6: Lookups** — DTOs `ProductLookupDto(Guid Id, string Name, string? Ticker)`, `CustomerLookupDto(Guid Id, string Name)`. `ILookupRepository { ListProductsAsync, ListCustomersAsync }`; impl Dapper (`SELECT id, name, ticker FROM financial_products ORDER BY name;` e `SELECT id, name FROM customers ORDER BY name;`). Handlers finos. `ProductsController` (`GET /api/products`) e `CustomersController` (`GET /api/customers`). DI: handlers `AddScoped` em `Application/DependencyInjection.cs`; `ILookupRepository`→`LookupRepository` `AddScoped` em `Infrastructure/DependencyInjection.cs`.

- [ ] **Step 7: Teste de integração dos lookups** (`LookupsReadTests`): seed produtos/clientes; `products()`/`customers()` retornam os registros.

- [ ] **Step 8: Build + suíte + smoke CORS** (`dotnet build` 0 avisos; `dotnet test` verde; opcional: `curl -H "Origin: http://localhost:4200" -I http://localhost:5000/api/operations` → header `Access-Control-Allow-Origin`) → commit + push + PR → **PAUSA**.

---

## Chunk 2: Fundação Angular (4b.2)

### Task 4b.2: scaffold + Material + Jest + NgRx + shell + core

**Files:** `frontend/investops-web/**` (novo app), `docker-compose`/CI inalterados nesta task.

- [ ] **Step 1: Branch** `feature/web-foundation`.

- [ ] **Step 2: Criar o app** (em `frontend/`):
```bash
cd /c/Projects/Investor/frontend
npx -y @angular/cli@19 new investops-web --style=scss --ssr=false --skip-git --package-manager=npm --defaults
cd investops-web
npx -y @angular/cli@19 add @angular/material --skip-confirmation
```
(remover o `frontend/.gitkeep` da Fase 0.)

- [ ] **Step 3: Trocar Karma por Jest**
```bash
npm rm karma karma-chrome-launcher karma-coverage karma-jasmine karma-jasmine-html-reporter jasmine-core @types/jasmine
npm i -D jest@^29 jest-preset-angular@^14 @types/jest
```
- `jest.config.js`: `preset: 'jest-preset-angular'`, `setupFilesAfterEnv: ['<rootDir>/setup-jest.ts']`.
- `setup-jest.ts`: `import 'jest-preset-angular/setup-jest';`
- `tsconfig.spec.json`: `types: ["jest"]`.
- `package.json` scripts: `"test": "jest"`.
- Remover `test` do `angular.json` (builder Karma) ou deixar; rodar via `npm test`.

- [ ] **Step 4: Deps NgRx**
```bash
npm i @ngrx/store@^19 @ngrx/effects@^19 @ngrx/store-devtools@^19
```

- [ ] **Step 5: `environment`** — `src/environments/environment.ts` + `environment.development.ts`: `export const environment = { apiBaseUrl: 'http://localhost:5000/api' };` (configurar `fileReplacements` se necessário; em dev usa o development).

- [ ] **Step 6: `app.config.ts`**
```ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([loadingInterceptor, errorInterceptor])),
    provideAnimations(),
    provideStore(),
    provideEffects(),
    provideStoreDevtools({ maxAge: 25 }),
  ],
};
```

- [ ] **Step 7: core/models** — interfaces camelCase: `OperationListItem`, `OperationDetail`, `Paged<T>`, `ProcessingEvent`, `AuditLog`, `Position`, `DashboardSummary`, `ProcessingMetrics`, `ProductLookup`, `CustomerLookup`.

- [ ] **Step 8: core/api** — services tipados injetáveis (`{ providedIn: 'root' }`): `OperationsApi`, `PositionsApi`, `DashboardApi`, `LookupsApi`, usando `HttpClient` + `environment.apiBaseUrl`. Query params via `HttpParams`.

- [ ] **Step 9: core/interceptors**
  - `loadingInterceptor`: incrementa um `LoadingService` (signal counter) no início; `finalize(() => dec())`.
  - `errorInterceptor`: `catchError` → publica mensagem no `NotificationService` (signal) e `rethrow`.

- [ ] **Step 10: Shell** — `app.component` standalone com `mat-sidenav-container` (sidenav com links Dashboard/Operações/Posições via `routerLink`), `mat-toolbar`, barra de progresso ligada ao `LoadingService`, `<router-outlet>`.

- [ ] **Step 11: Rotas** — `app.routes.ts` lazy: `'' → dashboard`, `'operations'`, `'operations/:id'`, `'positions'` (placeholders por enquanto; features reais nas próximas tasks).

- [ ] **Step 12: Smoke test (Jest)** — teste do `AppComponent` (renderiza o shell/toolbar). `npm test` verde.

- [ ] **Step 13: Build + test** (`npm run build`, `npm test`) → commit + push + PR → **PAUSA**.

> CI do frontend: adicionar job `web` (npm ci + npm test + npm run build) ao `.github/workflows/ci.yml` nesta task se simples; senão anotar p/ depois.

---

## Chunk 3: Feature Operações (4b.3)

### Task 4b.3: Operações — lista + detalhe (NgRx)

**Files:** `src/app/features/operations/{state,list,detail}/**`

- [ ] **Step 1: Branch** `feature/web-operations`.

- [ ] **Step 2: NgRx state** (`operations/state/`):
  - `operations.actions.ts`: `loadOperations({ query })`, `loadOperationsSuccess({ result })`, `loadOperationsFailure({ error })`; `loadDetail({ id })` + events/audit + Success/Failure.
  - `operations.reducer.ts`: `{ query, items, total, loading, error, detail, events, audit, detailLoading, detailError }`.
  - `operations.effects.ts`: `loadOperations$` → `OperationsApi.list(query)`; detalhe/eventos/auditoria (forkJoin no detalhe).
  - `operations.selectors.ts`: items/total/loading/error/detail/events/audit.

- [ ] **Step 3: TDD reducer/selectors** — testes puros (estado inicial, success popula items/total, failure popula error). `npm test`.

- [ ] **Step 4: Lista** (`operations/list/operations-list.component.ts`):
  - `MatTable` (colunas do §6), `MatPaginator` (server-side: `[length]=total`, `(page)`), `MatSort` (server-side: `(matSortChange)`, só colunas do whitelist).
  - Filtros: `FormControl` de busca com `debounceTime(300)` + `distinctUntilChanged`; selects de status (enum), produto (`LookupsApi.products()`), cliente (`LookupsApi.customers()`).
  - Qualquer mudança monta `query` (page 1-based, `sort: "campo:dir"`, status/productId/customerId) e despacha `loadOperations`.
  - Loading (spinner) e error state (mensagem + botão retry). Ação 👁 → `routerLink` detalhe; ♻ visível só se `status==='Failed'`, **disabled** + tooltip "Fase 5".
  - Registrar estado na rota: `provideState(operationsFeature)` + `provideEffects(OperationsEffects)`.

- [ ] **Step 5: TDD da lista (§13)** — (a) renderiza linhas com itens mockados (store mock); (b) filtro dispara `loadOperations` só após debounce (`fakeAsync`+`tick(300)`); (c) botão ♻ só aparece em `Failed` (dado mockado); (d) error state quando selector de erro tem valor.

- [ ] **Step 6: Detalhe** (`operations/detail/operation-detail.component.ts`): `MatTabGroup` — Dados (de `detail`), Eventos (tabela de `events`), Auditoria (tabela de `audit`). Dispara `loadDetail({id})` no init (route param).

- [ ] **Step 7: Build + test** verdes; smoke manual (API + `ng serve` → /operations carrega, filtra, abre detalhe) → commit + push + PR → **PAUSA**.

---

## Chunk 4: Posições e Dashboard (4b.4, 4b.5)

### Task 4b.4: Posições (NgRx + tabela)

**Files:** `src/app/features/positions/**`

- [ ] **Step 1: Branch** `feature/web-positions`.
- [ ] **Step 2: NgRx** (molde menor): state `{ items, total, query, loading, error }`; actions load/success/failure; effect → `PositionsApi.list(query)`; selectors.
- [ ] **Step 3: TDD reducer/selectors.**
- [ ] **Step 4: Componente** — `MatTable` paginada (colunas do §6 Posições; ordenáveis: quantity/averagePrice/grossAmount/updatedAt). Loading/error.
- [ ] **Step 5: Teste** (renderiza linhas; error state).
- [ ] **Step 6: Build + test** verdes → commit + push + PR → **PAUSA**.

### Task 4b.5: Dashboard (NgRx + cards)

**Files:** `src/app/features/dashboard/**`

- [ ] **Step 1: Branch** `feature/web-dashboard`.
- [ ] **Step 2: NgRx**: state `{ summary, metrics, loading, error }`; actions loadDashboard/success/failure; effect → `forkJoin(DashboardApi.summary(), DashboardApi.processingMetrics())`; selectors.
- [ ] **Step 3: TDD reducer/selectors.**
- [ ] **Step 4: Componente** — grid de `MatCard` (summary: importadas hoje/processadas/rejeitadas/falhas/divergências/tempo médio) + card iterando `metrics.operationsByStatus` (`keyvalue` pipe) + `outboxPending`. Loading/error.
- [ ] **Step 5: Teste** (renderiza cards com summary mockado).
- [ ] **Step 6: Build + test** verdes; **smoke end-to-end** (compose + API + worker + `ng serve`): importar uma operação → ela aparece na lista, processa no worker, posição aparece em /positions, dashboard reflete contagens → commit + push + PR → **PAUSA**.

---

## Fechamento da Fase 4b

Após 4b.5: o MVP do SPEC §14 está completo de ponta a ponta — Angular → API → RabbitMQ → Worker → posição/status → telas. Abrir release `developer → main` (Fases 1–4). Próximo (opcional): Fase 5 (qualidade sênior: retry/DLQ, OperationFailed + reprocess, health do worker, OpenTelemetry, containerizar o front).
