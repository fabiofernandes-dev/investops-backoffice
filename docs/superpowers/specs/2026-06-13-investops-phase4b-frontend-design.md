# Design — InvestOps Backoffice (Fase 4b: Frontend Angular)

**Data:** 2026-06-13
**Status:** Aprovado (início da Fase 4b)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§9, §13)
**Depende de:** Fase 4a (API de leitura). A 4b inclui uma **preparação de backend** (enriquecimento + lookups + CORS) antes do Angular.

Frontend Angular do backoffice: shell com navegação e telas de **Dashboard**, **Operações** (lista + detalhe) e **Posições**, consumindo a API real. Estado com **NgRx**.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Stack | Angular 19, **standalone components** + **Angular Material** |
| Estado | **NgRx** clássico (store/actions/reducers/effects/selectors), APIs standalone (`provideStore`/`provideState`/`provideEffects`) |
| Shell | **Sidenav lateral** (Material) + toolbar |
| HTTP | services tipados + **interceptors** (erro global + loading) |
| Dev/CORS | **CORS na API** (origem `http://localhost:4200`, só em Development); sem proxy do Angular |
| Testes | **Jest** (`jest-preset-angular` ≥14, jest ≥29) |
| Filtros Operações | **status + produto + cliente** (+ busca por externalId com debounce) |
| Escopo | Backend prep + Foundation + **Operações** (lista+detalhe) + **Posições** + **Dashboard** |
| Rotas | lazy por feature; default → Dashboard |
| Serialização | API é **camelCase** (default do ASP.NET MVC) → models TS camelCase; **sem** interceptor de transformação de chave |

## 2. Preparação de backend (.NET — task 4b.1, pré-requisito)

A lista de Operações precisa de **nomes** (Cliente/Conta/Produto) e os filtros precisam de **dados de lookup** — nada disso existe ainda.

- **CORS** (Development): permitir `http://localhost:4200` (GET/POST + headers). Produção: mesmo host/ingress, sem CORS.
- **Enriquecer a lista de operações com nomes**: novo `OperationListItemDto` (campos da operação + `customerName`, `accountNumber`, `productName`, `ticker`) retornado por `GET /operations`, via **join** (operations→accounts→customers, operations→products), espelhando o `PositionReadRepository`. `ListOperationsHandler`/repo/testes de lista atualizados. O **detalhe** (`GET /{id}`) continua com `OperationDto` (mostra os dados da própria operação; nomes não são necessários na aba Dados).
- **Endpoints de lookup** (para os dropdowns): `GET /api/products` → `ProductLookupDto[] { id, name, ticker }`; `GET /api/customers` → `CustomerLookupDto[] { id, name }`. **Novos controllers read-only** (`ProductsController`/`CustomersController` — não existem hoje). Leitura simples (sem paginação), via `IDbSession`.

> Sort do `OperationListItemDto`: o whitelist do `ListOperationsQuery`/`SortClauseBuilder` continua sobre colunas de `operations` (`operation_date`, `created_at`, `gross_value`, `status`) — os nomes (join) **não** são ordenáveis server-side.

## 3. Arquitetura (Angular)

```text
frontend/investops-web/
  src/environments/environment.ts   # apiBaseUrl: 'http://localhost:5000/api'
  src/app/
    app.config.ts            # provideRouter, provideHttpClient(withInterceptors[...]), provideStore, provideEffects, provideAnimations
    app.routes.ts            # lazy: '' → dashboard, 'operations', 'operations/:id', 'positions'
    app.component.ts         # shell: mat-sidenav + mat-toolbar + <router-outlet>
    core/
      api/                   # OperationsApi, PositionsApi, DashboardApi, LookupsApi (products/customers)
      models/                # interfaces espelhando os DTOs (camelCase)
      interceptors/          # error.interceptor.ts, loading.interceptor.ts
      state/                 # loading global (signal), notification (signal)
    features/
      dashboard/             # componente + NgRx (summary/metrics)
      operations/
        list/  detail/  state/   # actions/reducer/effects/selectors
      positions/             # componente + NgRx
    shared/                  # status-chip, error-state, etc.
```

Cada feature isola seu estado: `provideState(featureKey, reducer)` + `provideEffects(...)` na rota lazy. `core/` singleton; `shared/` dumb.

## 4. Camada core (Angular)

- **Models** (camelCase, espelham a API): `OperationListItemDto` (com nomes), `OperationDto` (detalhe), `PagedResult<T>`, `ProcessingEventDto`, `AuditLogDto`, `PositionDto`, `DashboardSummaryDto`, `ProcessingMetricsDto`, `ProductLookupDto`, `CustomerLookupDto`.
- **API services**:
  - `OperationsApi`: `list(query): PagedResult<OperationListItemDto>`, `getById(id)`, `events(id)`, `audit(id)`.
  - `PositionsApi`: `list(query)`, `byAccount(id)`, `byCustomer(id)`.
  - `DashboardApi`: `summary()`, `processingMetrics()`.
  - `LookupsApi`: `products()`, `customers()`.
  - Base URL de `environment.apiBaseUrl`.
- **Query → params (mapeamento 1:1 com a API)**: `page` (**1-based**, como a API), `pageSize`, `sort` no formato **`"campo:dir"`** (ex.: `operationDate:desc`), `status`, `productId`, `customerId`. Posições: `accountId`/`productId`/`customerId`.
- **Interceptors**:
  - `errorInterceptor`: mapeia erro HTTP → mensagem; publica em `NotificationService` (signal); telas mostram **error state**.
  - `loadingInterceptor`: contador de requisições em voo (signal), **decrementa em `finalize`** (não trava a barra se a request falhar) → barra de progresso no shell.

## 5. NgRx por feature

Molde (ex.: **operations**):
- **actions**: `loadOperations({query})` / `Success({result})` / `Failure({error})`; `loadOperation({id})`, `loadEvents({id})`, `loadAudit({id})` (+ Success/Failure).
- **state**: `{ query, items, total, loading, error, selected, events, audit }`.
- **effects**: `loadOperations$` → `OperationsApi.list(query)` → success/failure; idem detalhe/eventos/auditoria.
- **selectors**: `selectOperations`, `selectTotal`, `selectLoading`, `selectError`, `selectSelected`…

**positions**: `{ items, total, query, loading, error }`. **dashboard**: `{ summary, metrics, loading, error }`. Os dropdowns de lookup (products/customers) podem ficar em estado próprio simples ou carregados no componente da lista via `LookupsApi` (estado leve).

## 6. Telas (SPEC §9)

### Operações — lista (`/operations`)
`MatTable` + `MatSort` + `MatPaginator` (tudo **server-side**). Colunas: ExternalId, Cliente, Conta, Produto, Tipo, Quantidade, Valor bruto, Status, Data operação, Data liquidação, ações.
- **Ordenáveis** (server-side, whitelist): Data operação (`operationDate`), Valor bruto (`grossValue`), Status (`status`), Criado (`createdAt`). **Não** ordenáveis: ExternalId, Cliente, Conta, Produto (nomes via join).
- **Filtros**: busca por externalId (**debounce RxJS ~300ms**), dropdown **status** (enum), dropdown **produto** (`LookupsApi.products()`), dropdown **cliente** (`LookupsApi.customers()`).
- Qualquer mudança (página/sort/filtro) → `loadOperations({query})`. **Loading** e **error state** (mensagem + retry).
- Ações: 👁 detalhe; ♻ **reprocessar só quando `status === 'Failed'`** — botão renderizado condicionalmente, porém **desabilitado** com tooltip "disponível na Fase 5" (endpoint `POST /{id}/reprocess` não existe).

### Operações — detalhe (`/operations/:id`) — `MatTabGroup`
- **Dados** (de `GET /{id}`), **Eventos** (`/{id}/events`), **Auditoria** (`/{id}/audit`).

### Posições (`/positions`)
`MatTable` paginada: Cliente, Conta, Produto, Quantidade, Preço médio, Valor bruto, Última atualização (de `GET /positions`). Ordenáveis server-side (whitelist do `PositionsSortClauseBuilder`): Quantidade (`quantity`), Preço médio (`averagePrice`), Valor bruto (`grossAmount`), Última atualização (`updatedAt`). Colunas de nome (join) não são ordenáveis.

### Dashboard (`/` landing)
Grid de `MatCard` com `summary` (importadas hoje, processadas, rejeitadas, falhas, divergências, tempo médio) + um card iterando `processingMetrics.operationsByStatus` (dicionário) e exibindo `outboxPending`.

### Importar operação (`/operations/new`) — **fora do SPEC §9, adicionada para demo**
Formulário reativo que dispara o fluxo assíncrono ponta a ponta a partir da UI (`POST /operations/import`). Campos: **externalId** (obrigatório), **conta** (select via lookup), **produto** (select via lookup), **tipo** (Buy/Sell), **quantidade** (>0), **preço unitário** (>0), **data da operação** (obrigatória). `settlementDate` é calculada no servidor. Submit → sucesso: feedback (snackbar) + navega para a operação/lista; erro 422 (`ReferenceNotFound`)/validação → mensagem. Via **NgRx** (action `importOperation` → effect → POST → success: navega + recarrega lista; failure: erro), consistente com a arquitetura.
**Backend necessário:** `GET /api/accounts` → `AccountLookupDto { id, accountNumber, customerName }` (join accounts→customers) para o select de conta.

## 7. Testes (Jest) — SPEC §13

- **Operations list renders** (store/selector mockado com itens).
- **Filtro emite request com debounce** (`fakeAsync`/`tick`: muda o termo, só dispara `loadOperations` após ~300ms).
- **Botão reprocessar só aparece em `Failed`** — render condicional; usa **dados mockados** com `status:'Failed'` (o worker raramente gera Failed hoje; o teste não depende de dado vivo).
- **Error state** aparece quando a API falha (selector de erro).
- Reducers/selectors testados puros; effects com `provideMockActions` onde fizer sentido.

## 8. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 4b.1 | `feature/api-frontend-prep` | **.NET**: CORS + enriquecer lista de operações com nomes (`OperationListItemDto` + join) + `GET /products` e `GET /customers` (lookup) + testes |
| 4b.2 | `feature/web-foundation` | `ng new` (Material, **Jest** — remover Karma), app.config (router/http/store/effects), shell sidenav, core (models, api services, interceptors), environment |
| 4b.3 | `feature/web-operations` | feature Operações: NgRx + lista (tabela/paginação/sort/filtros/debounce/loading/error) + detalhe (abas) + testes §13 |
| 4b.4 | `feature/web-positions` | feature Posições: NgRx + tabela paginada + testes |
| 4b.5 | `feature/web-dashboard` | feature Dashboard: NgRx + cards (summary/metrics) + testes |
| 4b.7 | `feature/web-import` | **Importar operação** (fora do SPEC §9, p/ demo): `GET /accounts` (.NET) + formulário reativo + `POST /operations/import` via NgRx + nav link + testes |
| 4b.6 | `feature/web-theme` | **Tema corporativo + polish**: tema Material custom (paleta/densidade/tipografia), locale pt-BR global, componentes compartilhados (status-chip/page-header/empty-state/error-state), polish de todas as telas |

**Ordem:** 4b.1 → 4b.2 → 4b.3 → 4b.4 → 4b.5 → **4b.7 (importar)** → **4b.6 (tema, por último)**. Cada uma um PR para `developer`. As telas trazem os essenciais visuais (chips, pt-BR, loading/erro/vazio); a 4b.6 (última) eleva à identidade corporativa e estiliza tudo, inclusive o formulário de importação.

## 9. Jest no Angular 19 (nota de setup p/ 4b.2)
- `ng new` gera Karma/Jasmine → **remover** e configurar `jest-preset-angular` (jest.config, setup-jest, `tsconfig.spec`, script `"test": "jest"`).
- Pinar versões compatíveis (`jest-preset-angular` ≥14, `jest` ≥29). Fallback documentado: builder experimental de Jest do Angular ou Vitest.
- CI: adicionar job do frontend (jest) se simples; senão, anotar para depois.

## 10. Fora de escopo (4b)
- Ação de **reprocessar** de fato (endpoint é Fase 5) — só UI condicional.
- Telas de Reconciliação e Processing Monitor — fases próprias.
- Autenticação (SPEC §15).
- Containerização do frontend (nginx + compose:4200) — depois; agora `ng serve`.
- CRUD de products/customers (só lookups de leitura aqui).
