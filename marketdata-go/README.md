# marketdata-go

Serviço gRPC de cotações (preços simulados) — estudo de gRPC com interop .NET ↔ Go.

- `GetQuote` (unário) e `StreamQuotes` (server-streaming).
- Stateless (sem DB); preço-base determinístico por produto + random walk em memória.
- Porta gRPC: env `GRPC_PORT` (default `5300`).

## Código gerado

O `.proto` fica em `contracts/proto/marketdata/v1/marketdata.proto`. O código Go gerado
(`internal/gen/marketdatav1/`) é **commitado**. Para regenerar após mudar o `.proto`:

```bash
go install github.com/bufbuild/buf/cmd/buf@latest
go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest
cd contracts/proto && PATH="$PATH:$(go env GOPATH)/bin" buf generate
```

> Explicação completa dos **dois** fluxos de codegen (Go commitado vs .NET no build) em
> [`contracts/proto/README.md`](../contracts/proto/README.md).
