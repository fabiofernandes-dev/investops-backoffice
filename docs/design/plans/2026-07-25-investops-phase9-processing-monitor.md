# InvestOps Backoffice — Fase 9 (Processing Monitor) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (se houver subagents) ou superpowers:executing-plans. Steps usam checkbox (`- [ ]`). Aplique @superpowers:test-driven-development na lógica pura/regras e @superpowers:verification-before-completion antes de cada "verde".

**Goal:** Tela de monitoramento global dos eventos do worker (`processing_events`), com filtro e auto-refresh por polling — e enriquecer o worker Go para registrar falha terminal (operação `Failed` + `processing_event` `OperationFailed` com tentativas/erro reais) ao esgotar retries.

**Architecture:** 3 fatias em sequência: **worker Go** (hook `onExhausted` no consumer → `FailureRecorder` na camada `operations`, tudo numa transação `WithinTx`), **read API .NET** (`GET /api/processing-events` paginado com join, espelhando `PositionReadRepository`), **frontend Angular** (feature `processing-monitor`, espelhando `positions`/`reconciliation`, com polling). **Sem migration** — `processing_events` já tem as colunas.

**Tech Stack:** Go (pgx, RabbitMQ, slog, Testcontainers), .NET 8 (Dapper, xUnit, Testcontainers), Angular 21 (zoneless, signals, NgRx, Material, Jest).

**Spec:** [docs/design/specs/2026-07-25-investops-phase9-processing-monitor-design.md](../specs/2026-07-25-investops-phase9-processing-monitor-design.md)

---

## Convenções

- Branch `feature/*` a partir de `developer`; PR para `developer`; **1 task → 1 PR → PAUSA.**
- Go: em `worker-go/` → `go build ./... && go vet ./... && go test ./...` (integração via Testcontainers exige Docker). .NET: em `backend/` (`TreatWarningsAsErrors=true`). Frontend: em `frontend/investops-web/` → `npm test` + `npm run build`.
- Reads .NET: `session.GetConnectionAsync` + `session.Transaction`, join + `PagedResult` + `SortClauseBuilder` (whitelist), como `PositionReadRepository`. **DTO sempre.**
- API camelCase; paginação **1-based**; sort `"campo:dir"`. O endpoint de leitura **não** leva rate-limit de escrita.

## File Structure

```text
worker-go/
  internal/operations/model.go               # + StatusFailed                                     [9.1]
  internal/operations/store.go               # ProcessingEvent +AttemptCount/+ErrorMessage; TxRepo +MarkOperationFailed [9.1]
  internal/operations/events_out.go          # + EventOperationFailed                             [9.1]
  internal/operations/failure.go             # FailureRecorder + MarkExhausted                    [9.1]
  internal/operations/failure_test.go        # unit                                               [9.1]
  internal/operations/processor_test.go      # fakeTx +MarkOperationFailed stub                   [9.1]
  internal/database/store.go                 # txRepo.MarkOperationFailed; InsertProcessingEvent (attempt/error, NULL-when-empty) [9.1]
  internal/database/store_test.go            # integração: guard do MarkOperationFailed + insert  [9.1]
  internal/messaging/consumer.go             # OnExhaustedFunc + campo + SetOnExhausted + chamada no ramo DLQ [9.1]
  internal/messaging/retry_dlq_test.go       # + asserção: onExhausted chamado no esgotamento     [9.1]
  cmd/worker/main.go                         # wire recorder → consumer.SetOnExhausted            [9.1]
docs/adr/0008-terminal-failure-on-retry-exhaustion.md                                             [9.1]

backend/src/InvestOps.Application/Operations/ListProcessingEvents/
  {ProcessingEventListItemDto,ProcessingEventsQuery,ProcessingEventsSortClauseBuilder,ListProcessingEventsHandler}.cs [9.2]
backend/src/InvestOps.Application/Abstractions/IProcessingEventReadRepository.cs  # +ListAsync     [9.2]
backend/src/InvestOps.Infrastructure/Persistence/Repositories/ProcessingEventReadRepository.cs # +ListAsync [9.2]
backend/src/InvestOps.Api/ProcessingEvents/ProcessingEventsController.cs                           [9.2]
backend/src/InvestOps.Application/DependencyInjection.cs                          # + handler       [9.2]
backend/tests/InvestOps.UnitTests/ProcessingEventsSortClauseBuilderTests.cs                        [9.2]
backend/tests/InvestOps.IntegrationTests/ProcessingEventsReadTests.cs                              [9.2]

frontend/investops-web/src/app/
  core/api/processing-events.api.ts ; core/models/index.ts (+2 interfaces)                          [9.3]
  features/processing-monitor/processing-monitor.component.{ts,html,scss,spec.ts}                   [9.3]
  features/processing-monitor/state/{processing-events.actions,.effects,.feature,.feature.spec}.ts  [9.3]
  app.routes.ts (+/processing-monitor), app.component.ts (+nav) ; README.md (roadmap)               [9.3]
```

---

## Chunk 1: Worker — falha terminal (Task 9.1)

### Task 9.1: `feature/worker-operation-failed` → PR → PAUSA

- [ ] **Step 1: Branch** `feature/worker-operation-failed` a partir de `developer`.

- [ ] **Step 2: Constantes e struct** — em `operations/model.go`, no bloco de status, adicionar `StatusFailed  = "Failed"`. Em `operations/events_out.go`, no bloco de `EventOperation*`, adicionar `EventOperationFailed = "OperationFailed"`. Em `operations/store.go`, `ProcessingEvent` ganha dois campos:

```go
type ProcessingEvent struct {
	OperationID  uuid.UUID
	EventType    string
	Status       string
	Payload      string
	AttemptCount int
	ErrorMessage string
}
```

- [ ] **Step 3: `TxRepo.MarkOperationFailed`** — interface em `operations/store.go`:

```go
type TxRepo interface {
	// ...existentes...
	MarkOperationFailed(ctx context.Context, id uuid.UUID) error
}
```
Impl em `database/store.go` (guard no SQL — não clobbera resultado de negócio; não seta `rejection_reason`):
```go
func (r *txRepo) MarkOperationFailed(ctx context.Context, id uuid.UUID) error {
	const q = `UPDATE operations SET status = 'Failed', updated_at = now()
	           WHERE id = $1 AND status NOT IN ('Processed', 'Rejected');`
	_, err := r.tx.Exec(ctx, q, id)
	return err
}
```
E **atualizar `InsertProcessingEvent`** (mesmo arquivo) para persistir `attempt_count`/`error_message`, com `error_message = NULL` quando vazio (mantém Processed/Rejected inalterados):
```go
func (r *txRepo) InsertProcessingEvent(ctx context.Context, e operations.ProcessingEvent) error {
	const q = `
		INSERT INTO processing_events (id, operation_id, event_type, payload, status, attempt_count, error_message, created_at, processed_at)
		VALUES ($1, $2, $3, $4::jsonb, $5, $6, $7, now(), now());`
	var errArg any
	if e.ErrorMessage != "" {
		errArg = e.ErrorMessage
	}
	_, err := r.tx.Exec(ctx, q, uuid.New(), e.OperationID, e.EventType, e.Payload, e.Status, e.AttemptCount, errArg)
	return err
}
```
**Compilação:** adicionar o stub em `operations/processor_test.go` (`fakeTx`), senão o pacote não compila:
```go
func (f *fakeTx) MarkOperationFailed(_ context.Context, _ uuid.UUID) error { return nil }
```

- [ ] **Step 4: TDD do `FailureRecorder`** — teste primeiro em `operations/failure_test.go` (**pacote `operations`** — interno; `processor_test.go` já é `operations`, então **defina fakes próprios** neste arquivo para não colidir/duplicar). O fake `Store` deve implementar **os 4 métodos** da interface `Store` (`GetOperation`, `GetCustomerProfileByAccount`, `GetProductRiskLevel`, `WithinTx`), com `WithinTx` invocando `fn` com um `fakeTx` que **registra** as chamadas. Casos: body válido ⇒ chama `MarkOperationFailed(opID)` e `InsertProcessingEvent` com `EventType=OperationFailed`, `Status=Failed`, `AttemptCount=attempt`, `ErrorMessage=cause.Error()`; body inválido ⇒ **não** abre tx (só loga). Rodar `go test ./internal/operations/ -run FailureRecorder` → FAIL. Implementar `operations/failure.go`:

```go
package operations

import (
	"context"
	"encoding/json"
	"log/slog"
)

type FailureRecorder struct {
	store  Store
	logger *slog.Logger
}

func NewFailureRecorder(store Store, logger *slog.Logger) *FailureRecorder {
	return &FailureRecorder{store: store, logger: logger}
}

// MarkExhausted casa com messaging.OnExhaustedFunc.
func (r *FailureRecorder) MarkExhausted(ctx context.Context, body []byte, attempt int, cause error) {
	var evt OperationImportedEvent
	if err := json.Unmarshal(body, &evt); err != nil {
		r.logger.Error("evento inválido no esgotamento; ignorando", "error", err)
		return
	}
	opID := evt.Payload.OperationID
	if err := r.store.WithinTx(ctx, func(tx TxRepo) error {
		if err := tx.MarkOperationFailed(ctx, opID); err != nil {
			return err
		}
		return tx.InsertProcessingEvent(ctx, ProcessingEvent{
			OperationID:  opID,
			EventType:    EventOperationFailed,
			Status:       StatusFailed,
			AttemptCount: attempt,
			ErrorMessage: cause.Error(),
			Payload:      summary(map[string]string{"error": cause.Error()}),
		})
	}); err != nil {
		r.logger.Error("falha ao registrar operação como Failed", "operationId", opID, "error", err)
	}
}
```
Rodar de novo → PASS.

- [ ] **Step 5: Hook no `Consumer`** — em `messaging/consumer.go`: tipo + campo + setter, e chamada nil-safe no ramo de esgotamento (antes do `publishToDlq`):

```go
type OnExhaustedFunc func(ctx context.Context, body []byte, attempt int, cause error)

// no struct Consumer: onExhausted OnExhaustedFunc
func (c *Consumer) SetOnExhausted(fn OnExhaustedFunc) { c.onExhausted = fn }
```
No `handle`, dentro de `if attempt >= c.maxAttempts { ... }`, logo após o `logger.Error("esgotou tentativas...")`:
```go
if c.onExhausted != nil {
	c.onExhausted(ctx, delivery.Body, attempt, err)
}
```
(Sem callback, comportamento idêntico ao atual — protege os testes que criam `NewConsumer` sem hook.)

- [ ] **Step 6: Wire no `main.go`** — reusar um único `store` e registrar o hook:

```go
store := database.NewStore(pool)
processor := operations.NewProcessor(store, logger)
consumer.SetOnExhausted(operations.NewFailureRecorder(store, logger).MarkExhausted)
```
(substitui a linha `processor := operations.NewProcessor(database.NewStore(pool), logger)`).

- [ ] **Step 7: Testes de integração (Testcontainers)** — em `database/store_test.go` (Postgres, reusar `newPool`/`seedReferences`/`seedOperation`): (a) seed operação `Imported` → `WithinTx(MarkOperationFailed)` → status vira `Failed`; (b) seed operação `Processed` → `MarkOperationFailed` → **continua `Processed`** (guard do `WHERE`); (c) `InsertProcessingEvent` com `AttemptCount=3`/`ErrorMessage="x"` persiste os valores (e `ErrorMessage=""` grava `NULL`). Em `messaging/retry_dlq_test.go` (RabbitMQ): setar um `onExhausted` que sinaliza um canal e asserir que ele é **chamado com `attempt == maxAttempts`** quando o handler sempre falha. **⚠️ Helper:** o `startConsumer` atual cria o `Consumer` internamente e **não o expõe** — ajustar o helper (retornar o `*Consumer` ou aceitar um hook opcional) ou montar um consumer dedicado no teste para poder chamar `SetOnExhausted`.

- [ ] **Step 8: ADR 0008** — `docs/adr/0008-terminal-failure-on-retry-exhaustion.md`, formato do [ADR 0006](../adr/0006-defer-authentication-adopt-rate-limiting.md)/[0007](../adr/0007-synchronous-reconciliation.md): decisão de, no esgotamento de retries, marcar a operação `Failed` + gravar `processing_event` `OperationFailed` (observabilidade do retry/DLQ + ativa o Reprocessar); por que **não** publica `OperationFailed` no Outbox (sem consumidor); distinção transitório × rejeição de negócio; limitação da duplicata rara se o `publishToDlq` falhar após o hook.

- [ ] **Step 9: Verde** — `cd worker-go && go build ./... && go vet ./... && go test ./...` → tudo verde → commit + push + **PR para `developer`** → **PAUSA**.

---

## Chunk 2: Read API (Task 9.2)

### Task 9.2: `feature/processing-events-read` → PR → PAUSA

Espelha o vertical de leitura da Fase 4a/8 (`PositionReadRepository`/`ReconciliationReadRepository`).

- [ ] **Step 1: Branch** `feature/processing-events-read` a partir de `developer` (após 9.1 mergeada — para o filtro por `Failed` ter dado).

- [ ] **Step 2: DTO + Query + SortClauseBuilder** — TDD do sort builder primeiro (`ProcessingEventsSortClauseBuilderTests`: mapeia whitelisted; fallback default; injeção → default). Depois:

```csharp
// ProcessingEventListItemDto.cs (timestamps DateTime — convenção Npgsql/PositionDto)
namespace InvestOps.Application.Operations.ListProcessingEvents;
public sealed record ProcessingEventListItemDto(
    Guid Id, Guid OperationId, string ExternalId, string EventType, string Status,
    int AttemptCount, string? ErrorMessage, DateTime CreatedAt, DateTime? ProcessedAt);
```
```csharp
// ProcessingEventsQuery.cs
public sealed record ProcessingEventsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Status = null)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
```
```csharp
// ProcessingEventsSortClauseBuilder.cs (whitelist camelCase → colunas qualificadas com pe.)
public static class ProcessingEventsSortClauseBuilder
{
    public const string Default = "pe.created_at DESC";
    private static readonly IReadOnlyDictionary<string,string> Columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { ["createdAt"]="pe.created_at", ["status"]="pe.status", ["attemptCount"]="pe.attempt_count" };
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

- [ ] **Step 3: Repo `ListAsync`** — estender `IProcessingEventReadRepository`:
```csharp
Task<PagedResult<ProcessingEventListItemDto>> ListAsync(ProcessingEventsQuery query, CancellationToken ct);
```
Impl em `ProcessingEventReadRepository` (join + where opcional + `COUNT(*)` com o mesmo where + `SortClauseBuilder`), espelhando `ReconciliationReadRepository` (retorna `PagedResult<…>` direto):
```sql
SELECT pe.id, pe.operation_id, o.external_id,
       pe.event_type, pe.status, pe.attempt_count, pe.error_message,
       pe.created_at, pe.processed_at
FROM processing_events pe
JOIN operations o ON o.id = pe.operation_id
{where}  ORDER BY {orderBy}  LIMIT @Limit OFFSET @Offset;
```
`{where}` = `WHERE pe.status = @Status` só se `query.Status` não-vazio; `@Limit=NormalizedPageSize`, `@Offset=Offset`. Retorna `PagedResult<ProcessingEventListItemDto>(items, total, NormalizedPage, NormalizedPageSize)`.

- [ ] **Step 4: Handler + Controller + DI**
```csharp
public sealed class ListProcessingEventsHandler(IProcessingEventReadRepository repo)
{
    public Task<PagedResult<ProcessingEventListItemDto>> HandleAsync(ProcessingEventsQuery q, CancellationToken ct)
        => repo.ListAsync(q, ct);
}
```
```csharp
[ApiController] [Route("api/processing-events")]
public sealed class ProcessingEventsController(ListProcessingEventsHandler list) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] ProcessingEventsQuery q, CancellationToken ct)
        => Ok(await list.HandleAsync(q, ct));
}
```
Registrar `ListProcessingEventsHandler` (`AddScoped`) em `Application/DependencyInjection`. (O `IProcessingEventReadRepository` já está registrado.)

- [ ] **Step 5: Testes** — integração (`ProcessingEventsReadTests`, `[Collection(nameof(PostgresCollection))]`, seed via SQL espelhando `PositionsReadTests`): inserir operação + `processing_events` `Processed`/`Rejected`/`Failed` (com `attempt_count`/`error_message`) → `GET`/`ListAsync` traz o `externalId` (join), respeita paginação, filtra por `status`, e a linha `Failed` mostra `attempt_count`/`error_message`. Unit: `ProcessingEventsSortClauseBuilderTests`.

- [ ] **Step 6: Verde** — `cd backend && dotnet build InvestOps.sln -c Release` (0 warnings) + `dotnet test` → commit + push + **PR para `developer`** → **PAUSA**.

---

## Chunk 3: Frontend (Task 9.3)

### Task 9.3: `feature/web-processing-monitor` → PR → PAUSA

Espelha `positions`/`reconciliation`.

- [ ] **Step 1: Branch** `feature/web-processing-monitor` a partir de `developer` (após 9.2).

- [ ] **Step 2: Models** — em `core/models/index.ts`:
```ts
export interface ProcessingEventItem {
  id: string; operationId: string; externalId: string; eventType: string; status: string;
  attemptCount: number; errorMessage: string | null; createdAt: string; processedAt: string | null;
}
export interface ProcessingEventsQuery { page: number; pageSize: number; sort?: string; status?: string; }
```

- [ ] **Step 3: API service** — `core/api/processing-events.api.ts` (espelha `positions.api.ts`):
```ts
@Injectable({ providedIn: 'root' })
export class ProcessingEventsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/processing-events`;
  list(query: ProcessingEventsQuery): Observable<Paged<ProcessingEventItem>> {
    return this.http.get<Paged<ProcessingEventItem>>(this.baseUrl, { params: toHttpParams({ ...query }) });
  }
}
```

- [ ] **Step 4: NgRx** (`features/processing-monitor/state/`) — `load({ query, silent? })`/`loadSuccess`/`loadFailure`; state `{ query, items, total, loading, error }` (`sort` default `createdAt:desc`). **Reducer:** `on(load, (s,{query,silent}) => ({...s, query, loading: !silent, error: null}))`; `loadSuccess` seta items/total/loading:false; `loadFailure` seta error/loading:false. Effect `loadProcessingEvents$` (padrão `loadPositions$`) → `api.list(query)`.

- [ ] **Step 5: Componente** — `processing-monitor.component.{ts,html,scss}` (standalone, `OnPush`), espelha `positions.component` + auto-refresh:
  - **TS:** signals dos selectors; `displayedColumns = ['eventType','externalId','status','attemptCount','errorMessage','createdAt','processedAt']`; `statusFilter=''`; `pageIndex/pageSize/sortActive='createdAt'/sortDirection='desc'`; **`autoRefresh = signal(false)`** (view-state local). `reload(silent=false)` monta `ProcessingEventsQuery` (`status: statusFilter || undefined`) e despacha `load({query, silent})`. No construtor: `timer(0, 5000).pipe(takeUntilDestroyed())` — a cada tick, **se `autoRefresh() && !document.hidden`**, `this.reload(true)`. Botão **Atualizar** → `reload(false)`; `onPage`/`onSort`/`onStatusChange` → `reload(false)`.
  - **HTML:** `app-page-header title="Processing Monitor"`; toolbar com **botão Atualizar** + **`mat-slide-toggle`** ligado a `autoRefresh` + `mat-select` de status (`Processed`/`Rejected`/`Failed`/todos); tabela `mat-table` + `matSort` + `mat-paginator` (server-side) com as colunas do SPEC §9 — **Operação** = `externalId` como link `[routerLink]="['/operations', row.operationId]"`; **Status** = `app-status-chip`; **Erro** truncado com tooltip; datas `| date:'dd/MM/yyyy HH:mm:ss'`. `app-error-state`/`app-empty-state` como as demais.
  - `status-chip`: **sem mudança** (`Failed` já é laranja).

- [ ] **Step 6: Rota + nav + roadmap** — `app.routes.ts`: rota lazy `{ path: 'processing-monitor', providers: [provideState(processingEventsFeature), provideEffects(processingEventsEffects)], loadComponent: ... }`. `app.component.ts`: item no sidenav (ícone `monitor_heart`, label `Processing Monitor`). `README.md`: roadmap *Processing monitor* 🔲 → **✅**.

- [ ] **Step 7: Testes (Jest)** — `processing-monitor.component.spec.ts`: tabela renderiza (store mockada); filtro de status despacha `load`; **auto-refresh** — ligar o toggle, `fakeAsync`+`tick(5000)` ⇒ um novo `load({silent:true})` é despachado; desligar ⇒ para; link para `/operations/:operationId` presente; error state. `processing-events.feature.spec.ts`: `load` com `silent:false`→`loading true`; `silent:true`→`loading` permanece `false` (partindo do estado ocioso); `loadSuccess`/`loadFailure`.

- [ ] **Step 8: Verde** — `cd frontend/investops-web && npm test` + `npm run build` → commit + push + **PR para `developer`** → **PAUSA**.

---

## Fechamento da Fase 9

Após 9.3 mergeada e `developer` promovida a `main`: o worker registra falhas terminais (operação `Failed` + `processing_event` com tentativas/erro), a API expõe o histórico global, e a tela Processing Monitor fecha o **SPEC §9** com auto-refresh. Resta no roadmap apenas o **CRUD de produtos** (`POST`/`PUT`).

## Fora de escopo (reforço)
- Publicar `OperationFailed` no Outbox/RabbitMQ (sem consumidor). SSE ao vivo (é polling). Ação de retry a partir do monitor (o Reprocessar já existe em Operações). Nomes de cliente/produto no monitor (só `externalId`).
