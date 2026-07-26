# InvestOps Backoffice — Fase 6 (gRPC Market Data) Implementation Plan

> ✅ **Fase concluída e entregue.** Documento histórico de execução — os checkboxes registram o plano original (tudo entregue). O estado atual do produto está no [README — Status & roadmap](../../../README.md#status--roadmap).

**Goal:** Adicionar cotações ao backoffice via gRPC: serviço Go `marketdata-go` (GetQuote unário + StreamQuotes streaming, preços simulados), API .NET como client gRPC + bridge SSE, e um ticker ao vivo no Angular.

**Architecture:** `.proto` único compartilhado (codegen Go commitado; .NET via Grpc.Tools). `marketdata-go` é stateless (sem DB), com preço-base determinístico por produto + random walk em memória. A API .NET consome o gRPC (unário e stream) e repassa ao browser via REST/SSE. O Angular consome com `EventSource`.

**Tech Stack:** Go 1.26 (google.golang.org/grpc, shopspring/decimal), buf (codegen), .NET 8 (Grpc.Net.Client, Grpc.Tools), Angular 19 (EventSource), Docker/compose.

**Spec:** [docs/design/specs/2026-06-14-investops-phase6-grpc-marketdata-design.md](../specs/2026-06-14-investops-phase6-grpc-marketdata-design.md)

---

## Convenções
- Branch `feature/*` a partir de `developer`; 1 task → 1 PR → **pausa**.
- Preço sempre **decimal** (Go `shopspring/decimal`, .NET `decimal` com `InvariantCulture`); no proto é **string**.
- Código gerado Go **commitado**; sem `protoc`/`buf` no CI ou no Docker.

---

## Chunk 1: marketdata-go + contrato (6.1)

### Task 6.1: serviço gRPC Go + `.proto` + compose + CI

**Files:**
- Create: `contracts/proto/marketdata/v1/marketdata.proto`
- Create: `contracts/proto/buf.yaml`, `contracts/proto/buf.gen.yaml` (codegen local)
- Create: `marketdata-go/go.mod`, `cmd/server/main.go`, `internal/pricing/engine.go` (+test), `internal/grpcsvc/server.go` (+test), `internal/gen/marketdatav1/*` (gerado, commitado), `Dockerfile`
- Modify: `docker-compose.yml` (serviço `marketdata`), `.github/workflows/ci.yml` (job `marketdata`)

- [ ] **Step 1: Branch** `feature/marketdata-grpc-go`.

- [ ] **Step 2: `.proto`** — criar `contracts/proto/marketdata/v1/marketdata.proto` (conteúdo do design §3).

- [ ] **Step 3: Módulo Go** — `marketdata-go/go.mod`: `module investops/marketdata`, `go 1.26`; `go get google.golang.org/grpc google.golang.org/protobuf github.com/shopspring/decimal`.

- [ ] **Step 4: Codegen (buf, local)** — instalar plugins/buf via `go install` (não dependem de protoc binário):
  ```bash
  go install github.com/bufbuild/buf/cmd/buf@latest
  go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
  go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest
  ```
  `contracts/proto/buf.yaml` (`version: v2`, `modules: [{path: .}]`). `contracts/proto/buf.gen.yaml` — **usar `module=` (não `source_relative`)** para o caminho de saída ser derivado do `go_package` (gera em `internal/gen/marketdatav1/`, batendo com o design):
  ```yaml
  version: v2
  plugins:
    - local: protoc-gen-go
      out: ../../marketdata-go
      opt: [module=investops/marketdata]
    - local: protoc-gen-go-grpc
      out: ../../marketdata-go
      opt: [module=investops/marketdata]
  ```
  Rodar `cd contracts/proto && buf generate`. Com `go_package = "investops/marketdata/internal/gen/marketdatav1;marketdatav1"` + `module=investops/marketdata` + `out=../../marketdata-go`, o gerado cai em **`marketdata-go/internal/gen/marketdatav1/marketdata.pb.go`** (pacote `marketdatav1`). **Commitar o gerado.** Documentar o comando num `marketdata-go/README.md`.
  > `buf` traz seu próprio compilador — **não precisa do binário `protoc`**, só `buf` + `protoc-gen-go` + `protoc-gen-go-grpc` (go install). Confirmar o diretório gerado antes de seguir (o import no `server.go` é `investops/marketdata/internal/gen/marketdatav1`).

- [ ] **Step 5: TDD engine** (`internal/pricing/engine_test.go` primeiro):
  - `New().Quote(id)` num engine novo == `BasePrice(id)` (determinístico: mesmo id → mesmo base).
  - `Next(id)` retorna > 0 e dentro de ~±2% do anterior; chamadas repetidas continuam > 0.
  - ids diferentes → bases diferentes (alta probabilidade).
  Implementar `engine.go`:
  ```go
  package pricing
  import ("hash/fnv"; "math/rand"; "sync"; "github.com/shopspring/decimal")
  type Engine struct { mu sync.Mutex; current map[string]decimal.Decimal }
  func New() *Engine { return &Engine{current: map[string]decimal.Decimal{}} }
  func BasePrice(id string) decimal.Decimal {
      h := fnv.New32a(); _, _ = h.Write([]byte(id))
      cents := int64(1000 + h.Sum32()%49000) // 10.00 .. 509.99
      return decimal.NewFromInt(cents).Div(decimal.NewFromInt(100))
  }
  func (e *Engine) Quote(id string) decimal.Decimal {
      e.mu.Lock(); defer e.mu.Unlock()
      if p, ok := e.current[id]; ok { return p }
      p := BasePrice(id); e.current[id] = p; return p
  }
  func (e *Engine) Next(id string) decimal.Decimal {
      e.mu.Lock(); defer e.mu.Unlock()
      cur, ok := e.current[id]; if !ok { cur = BasePrice(id) }
      factor := decimal.NewFromFloat((rand.Float64()*2 - 1) * 0.005) // ±0.5%
      next := cur.Add(cur.Mul(factor)).Round(2)
      if next.IsZero() || next.IsNegative() { next = BasePrice(id) }
      e.current[id] = next; return next
  }
  ```
  > `math/rand` é aceitável aqui (preços simulados). Sem seed → varia por execução; os testes checam invariantes (>0, faixa), não valores fixos.

- [ ] **Step 6: gRPC server** (`internal/grpcsvc/server.go`): implementa `MarketDataServer`:
  - `GetQuote(ctx, req)` → `Quote{ProductId, Price: engine.Quote(id).String(), AsOf: timestamppb.Now()}`.
  - `StreamQuotes(req, stream)`: `ticker := time.NewTicker(time.Second)`; `defer ticker.Stop()`; loop `select { case <-stream.Context().Done(): return nil; case <-ticker.C: for _, id := range req.ProductIds { stream.Send(&Quote{... engine.Next(id).String() ...}) } }`. Envia uma rajada imediata antes do primeiro tick (opcional).

- [ ] **Step 7: TDD server (bufconn)** (`server_test.go`): sobe o server num `bufconn.Listen`, cria client gerado; `GetQuote` retorna preço parseável > 0; `StreamQuotes` (com `ctx` cancelável) recebe ≥1 `Quote` e respeita o cancelamento (encerra ao cancelar o contexto). testify.

- [ ] **Step 8: `main.go`** — lê `GRPC_PORT` (default 5300); `net.Listen("tcp", ":"+port)`; `grpc.NewServer()`; registra o server; `slog` de start; `Serve`. Tratamento de sinal (SIGINT/SIGTERM → `GracefulStop`).

- [ ] **Step 9: Dockerfile** — multi-stage `golang:1.26-alpine` → `gcr.io/distroless/static-debian12` (espelha o `worker-go/Dockerfile` — usar a **mesma tag**); `CGO_ENABLED=0`. Context `./marketdata-go` (gerado já commitado, não precisa do `.proto`).

- [ ] **Step 10: compose + CI** — `docker-compose.yml`: serviço `marketdata` (`build: ./marketdata-go`, `GRPC_PORT=5300`, `ports: ["5300:5300"]`, `restart: on-failure`). **A API roda no host** (não está no compose) → consome `http://localhost:5300` via a porta publicada; **sem `depends_on`** (não há serviço `api` no compose). CI: novo job `marketdata` (sibling de `worker`: `working-directory: marketdata-go`, `go-version-file: marketdata-go/go.mod`, `go build/vet/test`).

- [ ] **Step 11: Build + vet + test** (`cd marketdata-go && go build ./... && go vet ./... && go test ./...`) → commit + push + PR → **PAUSA**.

---

## Chunk 2: API .NET — client gRPC + SSE (6.2)

### Task 6.2: client gRPC + endpoints de cotação

**Files:**
- Modify: `backend/src/InvestOps.Infrastructure/InvestOps.Infrastructure.csproj` (pacotes gRPC + `<Protobuf GrpcServices="Client">`)
- Create: `backend/src/InvestOps.Application/Abstractions/IMarketDataGateway.cs`, `backend/src/InvestOps.Application/Quotes/QuoteDto.cs`
- Create: `backend/src/InvestOps.Infrastructure/MarketData/MarketDataGateway.cs`
- Create: `backend/src/InvestOps.Api/Quotes/QuotesController.cs`, `backend/src/InvestOps.Api/Quotes/SseFormatter.cs` (namespaces `InvestOps.Api.Quotes`)
- Modify: `backend/src/InvestOps.Infrastructure/DependencyInjection.cs` (AddGrpcClient + gateway), `backend/src/InvestOps.Api/appsettings.json` (MarketData:Url)
- Test: `backend/tests/InvestOps.UnitTests/SseFormatterTests.cs`

- [ ] **Step 1: Branch** `feature/api-grpc-client`.

- [ ] **Step 2: csproj (Infrastructure)** — **na `InvestOps.Infrastructure.csproj`** (onde vive o gateway; a `Api` referencia a Infra, não o contrário — então o client gerado precisa estar na Infra): pacotes `Grpc.Net.Client`, `Grpc.Net.ClientFactory`, `Google.Protobuf`, `Grpc.Tools` (PrivateAssets=all) e:
  ```xml
  <ItemGroup>
    <Protobuf Include="..\..\..\contracts\proto\marketdata\v1\marketdata.proto" GrpcServices="Client" />
  </ItemGroup>
  ```
  `dotnet build` gera o client (`InvestOps.MarketData.V1.MarketData.MarketDataClient`) **dentro do assembly Infrastructure**. (Grpc.Tools traz o próprio protoc — sem dependência externa.)

- [ ] **Step 3: DTO + gateway** — `QuoteDto(string ProductId, decimal Price, DateTimeOffset AsOf)` em `Application/Quotes`. `IMarketDataGateway` em `Application/Abstractions`: `Task<QuoteDto> GetQuoteAsync(string id, CancellationToken ct)` + `IAsyncEnumerable<QuoteDto> StreamQuotesAsync(IEnumerable<string> ids, CancellationToken ct)`. Impl `MarketDataGateway` (Infrastructure) injeta o client gerado; mapeia com `decimal.Parse(q.Price, CultureInfo.InvariantCulture)` e `q.AsOf.ToDateTimeOffset()`. Stream: `using var call = client.StreamQuotes(req, cancellationToken: ct); await foreach (var q in call.ResponseStream.ReadAllAsync(ct)) yield return Map(q);` (usar `[EnumeratorCancellation]`).

- [ ] **Step 4: DI (em `AddInfrastructure`)** — em `Infrastructure/DependencyInjection.cs`: `services.AddGrpcClient<MarketData.MarketDataClient>(o => o.Address = new Uri(configuration["MarketData:Url"]!));` + `services.AddScoped<IMarketDataGateway, MarketDataGateway>();`. `appsettings.json`: `"MarketData": { "Url": "http://localhost:5300" }` (dev host; compose sobrescreve com `MARKETDATA__URL` se a API for containerizada futuramente).
  > **h2c**: net8 fala HTTP/2 cleartext com `http://` por padrão — **não** setar `Http2UnencryptedSupport`.

- [ ] **Step 5: `SseFormatter` (helper puro) + TDD** — `Api/Quotes/SseFormatter.cs` (namespace `InvestOps.Api.Quotes`). Teste primeiro:
  ```csharp
  // Format produz "data: {json camelCase}\n\n"; FormatError produz "event: error\ndata: {...}\n\n"
  ```
  Implementar `SseFormatter.Format(QuoteDto)` e `FormatError(string)` usando `JsonSerializer` com `JsonSerializerDefaults.Web`. Unit testável sem `HttpContext`.

- [ ] **Step 6: `QuotesController`** (`Api/Quotes/QuotesController.cs`, namespace `InvestOps.Api.Quotes`):
  - `GET /api/quotes/{productId}`: `try Ok(await gateway.GetQuoteAsync(id, ct)); catch (RpcException ex) when (ex.StatusCode==StatusCode.Unavailable) → StatusCode(503, ProblemDetails)`.
  - `GET /api/quotes/stream?productIds=a,b`: setar `ContentType=text/event-stream`, `CacheControl=no-cache`, `Headers["X-Accel-Buffering"]="no"`, `HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering()`; `await foreach (q in gateway.StreamQuotesAsync(ids, HttpContext.RequestAborted)) { await Response.WriteAsync(SseFormatter.Format(q)); await Response.Body.FlushAsync(); }`; `catch (OperationCanceledException) {}`; `catch (RpcException) { write FormatError + flush }`.

- [ ] **Step 7: Build + test** (`dotnet build` 0 avisos; `dotnet test` — SseFormatter) → commit + push + PR → **PAUSA**.
  > Integração .NET↔Go fica no smoke da 6.3 (compose), não no CI.

---

## Chunk 3: Angular — ticker de cotações (6.3)

### Task 6.3: tela Cotações (SSE)

**Files:** `frontend/investops-web/src/app/core/api/quotes.api.ts`, `core/models` (Quote), `features/quotes/quotes.component.{ts,html,scss,spec}`, `app.routes.ts`, `app.component.ts` (nav)

- [ ] **Step 1: Branch** `feature/web-quotes` (a partir de `developer`; depende apenas dos shared components `app-page-header`/`app-error-state` do #29, já na `developer` — não do #30).

- [ ] **Step 2: model + api** — `Quote { productId: string; price: number; asOf: string }`. `QuotesApi`:
  - `getQuote(id)`: `http.get<Quote>(.../quotes/${id})`.
  - `stream(productIds: string[]): Observable<Quote>`: `new Observable` que abre `new EventSource(`${apiBaseUrl}/quotes/stream?productIds=${ids.join(',')}`)`; `es.onmessage = e => sub.next(JSON.parse(e.data))`; `es.addEventListener('error', ...)` (erro de app); teardown `() => es.close()`.

- [ ] **Step 3: componente Cotações** — carrega `LookupsApi.products()`; abre `QuotesApi.stream(ids)`; mantém `signal<Map<productId, {price, prev}>>`; tabela: Produto, Preço (currency), ▲/▼ (compara com anterior), horário. `OnDestroy`/`takeUntilDestroyed` encerra o stream. `styleUrl` + `.scss`. Reusa `app-page-header`, `app-error-state`.

- [ ] **Step 4: rota + nav** — `/quotes` (lazy) em `app.routes.ts`; item "Cotações" no shell (`app.component`), ícone `trending_up`.

- [ ] **Step 5: testes Jest** — `quotes.component` renderiza linhas a partir de cotações injetadas (mock do `QuotesApi.stream` com `of(...)` + `LookupsApi` stub); indicador ▲/▼ correto ao chegar novo preço. `QuotesApi` opcional (EventSource é difícil de testar; cobrir o parse via um helper puro se necessário).

- [ ] **Step 6: Build + test** (`npm test`, `npm run build`).

- [ ] **Step 7: Smoke e2e (compose)** — `docker compose up -d --build` (inclui `marketdata`), API (host ou compose) + `ng serve`; abrir `/quotes` (Playwright) → ticker atualizando ao vivo; `GET /api/quotes/{id}` retorna 200. → commit + push + PR → **PAUSA**.

---

## Fechamento da Fase 6
Após 6.3: o backoffice tem cotações ao vivo via gRPC (.NET↔Go) + SSE. `docker compose up` sobe **API + worker + marketdata + Angular + infra**. Escopo de gRPC coberto: `.proto` compartilhado, unário + server-streaming, interop poliglota, bridge para o browser.
