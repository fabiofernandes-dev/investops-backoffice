# Design — InvestOps Backoffice (Fase 6: Integração gRPC — Market Data)

**Data:** 2026-06-14
**Status:** Aprovado (início da Fase 6)
**Contexto:** feature de estudo de **gRPC** com interop **.NET ↔ Go**. Adiciona cotações (preços) ao backoffice.

Novo serviço Go (`marketdata-go`) expõe um servidor **gRPC** de cotações (preços simulados). A **API .NET** é **client gRPC** e faz **bridge** para o browser via **SSE**. O Angular mostra um **ticker ao vivo**.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Servidor gRPC | **Novo serviço `marketdata-go`** (single responsibility), no docker-compose |
| Entrega ao browser | **Bridge SSE via API .NET** (API é client gRPC; repassa o stream como Server-Sent Events) |
| RPCs | `GetQuote` (unário) + `StreamQuotes` (**server-streaming**) |
| Preços | **Simulados** (base determinística por productId + random walk); sem DB |
| Contrato | `.proto` compartilhado; codegen Go (`protoc-gen-go`/`-go-grpc`) e .NET (`Grpc.Tools`) |
| Transporte | gRPC **plaintext (h2c)** em dev; TLS/mTLS fora de escopo (Fase futura) |
| Decimais | preço como **string decimal** no proto (sem float; consistente com `shopspring/decimal`/`decimal`) |

## 2. Arquitetura

```text
Angular  ──REST/SSE──►  API .NET  ──gRPC──►  marketdata-go
(ticker)   EventSource   (client)   unário/    (server, preços
                                     stream      simulados)
```

- **Sem dependências de infra**: `marketdata-go` **não usa Postgres nem RabbitMQ** — autossuficiente. Mantém apenas o **preço atual por produto em memória efêmera** (para o random walk evoluir); reinício volta à base; dados **simulados**. Gera cotações para qualquer `productId`.
- A API .NET continua a única porta do browser (o browser nunca fala gRPC direto).

## 3. Contrato `.proto`

`contracts/proto/marketdata/v1/marketdata.proto`:
```proto
syntax = "proto3";
package marketdata.v1;
import "google/protobuf/timestamp.proto";
option go_package = "investops/marketdata/internal/gen/marketdatav1;marketdatav1";
option csharp_namespace = "InvestOps.MarketData.V1";

service MarketData {
  rpc GetQuote(GetQuoteRequest) returns (Quote);
  rpc StreamQuotes(StreamQuotesRequest) returns (stream Quote);
}
message GetQuoteRequest { string product_id = 1; }
message StreamQuotesRequest { repeated string product_ids = 1; }
message Quote {
  string product_id = 1;
  string price = 2;                       // decimal como string (sem float)
  google.protobuf.Timestamp as_of = 3;
}
```

**Codegen — decisão:** o **código gerado é commitado no repo** (Go `.pb.go`/`_grpc.pb.go` em `marketdata-go/internal/gen/marketdatav1/`; .NET gera em build via `Grpc.Tools`, não commitado). Isso evita depender de `protoc` no CI e no build Docker. Comando documentado (rodado localmente ao mudar o `.proto`):
```bash
protoc -I contracts/proto \
  --go_out=marketdata-go --go_opt=module=investops/marketdata \
  --go-grpc_out=marketdata-go --go-grpc_opt=module=investops/marketdata \
  contracts/proto/marketdata/v1/marketdata.proto
```
O `contracts/proto/` é introduzido na **task 6.1**.

## 4. `marketdata-go` (servidor)

```text
marketdata-go/
  go.mod                        # module investops/marketdata (novo módulo, raiz em marketdata-go/)
  cmd/server/main.go            # config (porta) + grpc.NewServer + Serve
  internal/pricing/engine.go    # base price determinística (hash do productId) + random walk; mutex-guarded
  internal/pricing/engine_test.go
  internal/grpcsvc/server.go    # implementa MarketDataServer: GetQuote, StreamQuotes
  internal/grpcsvc/server_test.go  # bufconn (listener em memória) + client gerado
  internal/gen/marketdatav1/    # código gerado (COMMITADO no repo)
  Dockerfile                    # multi-stage golang -> distroless (build: ./marketdata-go; não precisa do .proto, pois o gerado está commitado)
```
- **Módulo Go**: novo `go.mod` com module path **`investops/marketdata`** (distinto do `investops/worker`); raiz em `marketdata-go/`.
- **Engine**: `BasePrice(productId)` determinístico (hash → faixa, ex. 10–500); `Next(productId)` aplica passo aleatório pequeno (±~0.5%), nunca ≤ 0; usa `shopspring/decimal`. Estado compartilhado (preço atual por produto) **protegido por `sync.Mutex`** (múltiplos streams = chamadas concorrentes a `Next`).
- **GetQuote**: retorna a cotação atual do produto.
- **StreamQuotes**: **cada stream tem seu próprio `time.Ticker`** (~1s); a cada tick emite uma `Quote` por `product_id` pedido, até o client desconectar (`stream.Context().Done()`).
- Porta gRPC: env `GRPC_PORT` (default `5300`). Build Docker não precisa do `.proto` (gerado commitado) → `build: ./marketdata-go` funciona.

## 5. API .NET (client gRPC + bridge)

- **Codegen**: `.proto` referenciado no `InvestOps.Api.csproj` via `Grpc.Tools` (`<Protobuf Include="..\..\..\contracts\proto\marketdata\v1\marketdata.proto" GrpcServices="Client" />`); gera o client em build (não commitado). Pacotes: `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools`.
- **`IMarketDataGateway`** (Application/Abstractions) + impl em Infrastructure encapsulando o client gRPC (`GrpcChannel.ForAddress(MarketData:Url)`); mapeia `Quote`→`QuoteDto(string ProductId, decimal Price, DateTimeOffset AsOf)`. **Parse do preço com `decimal.Parse(quote.Price, CultureInfo.InvariantCulture)`** (host pt-BR misparsearia o ponto sem isso).
- **h2c (definitivo)**: o alvo é **net8.0**; `GrpcChannel.ForAddress("http://marketdata:5300")` fala HTTP/2 cleartext com o servidor Go por padrão (SocketsHttpHandler). **Não** é necessário o switch `Http2UnencryptedSupport` (aquilo era .NET Core 3.x) — não adicionar.
- **Endpoints** (`QuotesController`):
  - `GET /api/quotes/{productId}` → unário → `QuoteDto`. `RpcException` com `StatusCode.Unavailable` → **503**.
  - `GET /api/quotes/stream?productIds=a,b,c` → **SSE**:
    - `Response.ContentType = "text/event-stream"`, `Headers.CacheControl = "no-cache"`, `Headers["X-Accel-Buffering"] = "no"`.
    - **Desligar buffering**: `HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering()`.
    - Para cada cotação do stream gRPC: escrever `data: {json}\n\n` e **`await Response.Body.FlushAsync(ct)`** (senão o browser recebe em lote).
    - Encerrar quando o cliente desconecta (`HttpContext.RequestAborted`); erro do gRPC → frame `event: error\ndata: {json}\n\n`.
  - **Helper puro `SseFormatter.Format(QuoteDto)`/`FormatError(...)`** (sem `HttpContext`) para a serialização do frame — testável em unit.
- **Config**: `MarketData:Url` (appsettings + env `MARKETDATA__URL`); em compose aponta para `http://marketdata:5300`; local `http://localhost:5300`.
- **CORS**: a política atual (Dev: `WithOrigins(localhost:4200)`, `UseCors` antes do `MapControllers`) já cobre o SSE — `EventSource` não envia headers/credenciais custom. Em compose/prod é mesma origem (sem CORS).

## 6. Angular — tela Cotações

- `core/api/quotes.api.ts`: `getQuote(id)` (HttpClient) + `stream(productIds): Observable<Quote>` via **`EventSource`** (SSE) embrulhado em Observable. Trata **dois** casos: `onmessage` (cotação) e listener explícito de `event: error` (erro de app vindo do servidor) — distinto do `onerror` do `EventSource` (queda de conexão, que reconecta sozinho). `teardown` fecha o `EventSource`.
- `features/quotes/quotes.component`: carrega produtos (`LookupsApi.products()`), abre o stream para os ids, mostra **ticker** (tabela: Produto, Preço, ▲/▼ vs. anterior, horário). `OnDestroy` fecha o `EventSource`.
- Rota `/quotes` + item "Cotações" no shell. (Estado leve no componente; NgRx opcional — um stream contínuo cabe melhor em signal local. Decisão: **signals no componente**, sem NgRx aqui, e justifico no doc.)

## 7. docker-compose

Novo serviço `marketdata` (build `./marketdata-go`, porta 5300, `restart: on-failure`). API ganha `MARKETDATA__URL=http://marketdata:5300` e `depends_on: marketdata`. (Local sem compose: `MarketData:Url=http://localhost:5300`.)

## 8. Testes

- **Go**: engine (preço > 0, determinismo da base, passo dentro do limite); gRPC via **bufconn** (GetQuote retorna; StreamQuotes emite N e respeita cancelamento de contexto).
- **.NET**: mapeamento `Quote`→`QuoteDto` (parse invariant-culture); `SseFormatter.Format` (helper puro). (Integração .NET↔Go fica no smoke e2e via compose, não no CI unitário.)
- **Angular**: `quotes.component` renderiza linhas a partir de cotações mockadas; `QuotesApi.stream` parseia eventos. (EventSource mockado.)
- **CI**: novo job `marketdata` (go build/vet/test). **Sem `protoc` no CI** — o código gerado está commitado. Jobs `web`/`backend` já cobrem os outros.

## 9. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 6.1 | `feature/marketdata-grpc-go` | `.proto` + `marketdata-go` (engine + gRPC GetQuote/StreamQuotes + bufconn tests) + Dockerfile + compose + CI |
| 6.2 | `feature/api-grpc-client` | API .NET: codegen client + `IMarketDataGateway` + `QuotesController` (unário + SSE) + config + testes |
| 6.3 | `feature/web-quotes` | Angular: `QuotesApi` (SSE) + tela Cotações (ticker) + nav + testes |

**Ordem:** 6.1 → 6.2 → 6.3. Smoke e2e final (compose: marketdata + API + Angular) na 6.3.

## 10. Fora de escopo
- TLS/mTLS no gRPC (h2c em dev); auth/propagação de credencial.
- Feed de mercado real (preços simulados).
- gRPC-Web direto no browser (usamos bridge SSE).
- Persistência de cotações / histórico.
- Reconnect automático sofisticado do SSE (o `EventSource` já reconecta; sem backoff custom).
