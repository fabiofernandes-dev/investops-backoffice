# Contrato gRPC compartilhado (`.proto`)

`marketdata/v1/marketdata.proto` é a **fonte única** do contrato gRPC de Market Data, consumida pelos dois lados (Go e .NET). Há **dois fluxos de geração de código**, propositalmente diferentes:

| Lado | Ferramenta | Onde fica o gerado | Commitado? | Quando roda |
|------|-----------|--------------------|-----------|-------------|
| **Go** (`marketdata-go`, servidor) | `buf` (+ `protoc-gen-go`/`-go-grpc`) | `marketdata-go/internal/gen/marketdatav1/*.pb.go` | **Sim** | manual, ao mudar o `.proto` |
| **.NET** (`InvestOps.Infrastructure`, client) | `Grpc.Tools` | `obj/Debug/net8.0/Marketdata.cs` + `MarketdataGrpc.cs` | **Não** (em `obj/`) | automático, a cada `dotnet build` |

Por que diferente: o ecossistema Go não tem um "build step" que gere código a partir de `.proto`, então geramos com `buf` e **commitamos** (evita depender de `protoc`/`buf` no CI e no Docker). Já o .NET integra a geração ao MSBuild via `Grpc.Tools`, então o código é **regenerado no build** e não precisa ser versionado.

## Gerar o lado Go (após mudar o `.proto`)

```bash
# 1) instalar buf + plugins (vão para $(go env GOPATH)/bin) — buf traz o próprio compilador, sem protoc
go install github.com/bufbuild/buf/cmd/buf@latest
go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest

# 2) gerar (config em contracts/proto/buf.gen.yaml; out via opção module=)
cd contracts/proto
PATH="$PATH:$(go env GOPATH)/bin" buf generate

# 3) commitar o gerado em marketdata-go/internal/gen/marketdatav1/
```
> No Windows, o **primeiro** `buf generate` pode demorar ~30s (antivírus escaneando os plugins `.exe` recém-compilados) — não é erro.

## Gerar o lado .NET

Nada a fazer manualmente: o `InvestOps.Infrastructure.csproj` referencia o `.proto` via
`<Protobuf Include="..\..\..\contracts\proto\marketdata\v1\marketdata.proto" GrpcServices="Client" />`,
e o `Grpc.Tools` gera o client (`InvestOps.MarketData.V1.MarketData.MarketDataClient`) durante `dotnet build`.
O `Grpc.Tools` traz o próprio `protoc` — sem dependência externa.

## Regra ao alterar o contrato
1. Edite o `.proto`. 2. Regenere o Go (passos acima) e **commite**. 3. O .NET pega automático no próximo build. 4. Garanta compatibilidade (campos novos com novos números; não reusar/renumerar).
