# InvestOps Backoffice

[![CI](https://github.com/fabiofernandes-dev/investops-backoffice/actions/workflows/ci.yml/badge.svg)](https://github.com/fabiofernandes-dev/investops-backoffice/actions/workflows/ci.yml)

Plataforma fullstack de backoffice para operações de investimento

> **Status:** em construção — veja [Status & roadmap](#status--roadmap). O [SPEC.md](SPEC.md) descreve a visão completa do produto; nem tudo está implementado ainda.

## Destaques de engenharia

- **Arquitetura event-driven** com **Outbox Pattern**: publicação confiável de eventos (sem dual-write) entre a API .NET e o Worker Go via RabbitMQ.
- **Resiliência no consumo:** retry com backoff (wait queue + TTL) e **Dead Letter Queue**; falhas transitórias reprocessam, falhas de negócio são terminais.
- **Interop poliglota:** serviço de cotações em **gRPC** (Go) consumido pela API .NET, com Protobuf versionado.
- **Modular monolith** .NET 8 (Clean Architecture, Dapper, DbUp) + **SPA Angular 21 zoneless** (signals, standalone, `OnPush`, sem zone.js) servida por **edge nginx** same-origin.
- **Qualidade:** testes com Testcontainers, CI no GitHub Actions, config por ambiente, rate limiting por perfil de endpoint, 6 ADRs e specs de design por fase em [docs/](docs/).

## Status & roadmap

O [SPEC.md](SPEC.md) é a visão completa; a entrega é incremental, por fases ([docs/design/](docs/design/)):

| Área | Status |
| ---- | ------ |
| Import de operações (validação, idempotência, Outbox → RabbitMQ → Worker) | ✅ |
| Worker Go: regras de negócio, posição consolidada, retry + DLQ | ✅ |
| Read API: operations, positions, dashboard, lookups (paginação/ordenação server-side) | ✅ |
| Frontend: dashboard, operações, posições, cotações ao vivo | ✅ |
| Cotações via gRPC (.NET ↔ Go) + SSE | ✅ |
| Edge nginx (same-origin) + rate limiting | ✅ |
| CRUD de produtos (`POST`/`PUT`) | 🔲 planejado |
| Conciliação com custodiante (reconciliation) | 🔲 planejado |
| Processing monitor (UI de eventos do worker) | 🔲 planejado |
| Autenticação/autorização (OIDC) | 🚫 fora do MVP — [ADR 0006](docs/adr/0006-defer-authentication-adopt-rate-limiting.md) |

## Arquitetura

```text
Angular SPA (nginx edge) → .NET Operations API → PostgreSQL
                                               → Outbox → RabbitMQ → Go Worker → PostgreSQL
                                               → gRPC → Go MarketData
```

Quatro serviços — SPA Angular, API .NET, Worker Go e MarketData Go — sobre banco compartilhado no MVP. Integração assíncrona via **Outbox Pattern** + RabbitMQ e cotações via **gRPC**. No deploy, um **edge nginx** serve o SPA e faz proxy de `/api` para a API (same-origin). Decisões registradas em [docs/adr/](docs/adr/).

## Stack

| Camada     | Tecnologias                                                            |
| ---------- | ---------------------------------------------------------------------- |
| Frontend   | Angular 21 (zoneless, signals), TypeScript, RxJS, Angular Material, Jest |
| API        | .NET 8, ASP.NET Core, Dapper, FluentValidation, Serilog, OpenTelemetry |
| Worker     | Go, RabbitMQ, pgx, slog                                                |
| MarketData | Go, gRPC, Protobuf (buf)                                               |
| Infra      | Docker Compose, PostgreSQL, RabbitMQ, nginx                            |

## Estrutura do repositório

```text
investops-backoffice/
  backend/                      # solução .NET 8
    src/
      InvestOps.Api/            # ASP.NET Core (Serilog, Swagger, Health Checks)
      InvestOps.Application/    # casos de uso
      InvestOps.Domain/         # entidades e enums do domínio
      InvestOps.Infrastructure/ # persistência/Dapper, RabbitMQ, cliente gRPC
      InvestOps.Contracts/      # DTOs de evento
      InvestOps.Migrator/       # DbUp: schema + seed
    tests/                      # UnitTests, IntegrationTests
  worker-go/                    # worker assíncrono (consumo RabbitMQ, retry + DLQ)
  marketdata-go/                # serviço gRPC de cotações
  frontend/investops-web/       # app Angular 21 (zoneless)
  contracts/                    # contratos de eventos JSON + .proto (gRPC)
  docs/                         # ADRs e documentação de design
  docker-compose.yml            # stack completo
```

## Como rodar

Há dois caminhos:

- **A. Stack completo via Docker** — um comando sobe todos os serviços. Ideal para ver o sistema fim a fim.
- **B. Componente por componente** — infra no Docker, cada serviço no host, com hot-reload. Ideal para desenvolver.

### Pré-requisitos

- [Docker](https://www.docker.com/) + Docker Compose — obrigatório em ambos os caminhos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — API, Migrator e testes .NET
- [Go 1.26+](https://go.dev/dl/) — worker e marketdata
- [Node.js 20+](https://nodejs.org/) — frontend Angular

---

### A. Stack completo via Docker Compose

Builda e sobe **todos os serviços**: `postgres`, `rabbitmq`, `migrator` (schema + seed), `marketdata`, `worker`, `api` e `web`.

```bash
cp .env.example .env      # opcional; o compose já usa defaults
docker compose --env-file .env.example up -d --build
docker compose --env-file .env.example ps      # aguarde postgres/rabbitmq (healthy)
```

- App (SPA + API via edge nginx): <http://localhost:8080>
- RabbitMQ Management UI: <http://localhost:15672> (`investops` / `investops`)

> ⚠️ **Primeira subida da Fase 5a:** a fila `operations.processing` mudou de _classic_ para _quorum_. Se você já rodou uma fase anterior, o RabbitMQ recusa a redeclaração com `406 PRECONDITION_FAILED`. Rode uma vez para recriar os volumes:
>
> ```bash
> docker compose down -v   # apaga volumes (RabbitMQ + Postgres); o migrator re-semeia no próximo up
> docker compose --env-file .env.example up -d --build
> ```

Parar tudo:

```bash
docker compose --env-file .env.example down        # mantém os dados (volumes)
docker compose --env-file .env.example down -v      # apaga os dados
```

---

### B. Componente por componente (desenvolvimento)

Suba apenas a infra pelo Docker e rode cada serviço no host. Os defaults de cada serviço já apontam para `localhost`, então nenhuma variável de ambiente é obrigatória em dev. Siga a ordem abaixo (a API depende de MarketData; o Worker e a API dependem de Postgres + RabbitMQ).

#### 1. Infraestrutura (só PostgreSQL + RabbitMQ)

```bash
docker compose --env-file .env.example up -d postgres rabbitmq
```

> Sem os nomes dos serviços, `docker compose up` subiria **tudo** (inclusive api/worker/web já buildados).

#### 2. Schema do banco (Migrator)

```bash
cd backend
dotnet run --project src/InvestOps.Migrator -- --seed   # schema + dados de exemplo
# ou apenas o schema:
dotnet run --project src/InvestOps.Migrator
```

#### 3. MarketData (gRPC de cotações)

```bash
cd marketdata-go
go run ./cmd/server        # escuta gRPC em :5300 (env GRPC_PORT)
```

#### 4. Worker (processamento assíncrono)

```bash
cd worker-go
go run ./cmd/worker        # consome RabbitMQ, aplica regras e faz o relay do Outbox
```

Retry/backoff/DLQ e demais variáveis estão em [worker-go/README.md](worker-go/README.md).

#### 5. API .NET

```bash
cd backend
dotnet run --project src/InvestOps.Api
```

- Swagger: <http://localhost:5000/swagger> (a raiz `/` redireciona para o Swagger em Development/Staging)
- Health: <http://localhost:5000/health> · liveness `/health/live` · readiness `/health/ready`
- Consome o MarketData em `http://localhost:5300` (config `MarketData:Url`)

> Em `Production` o Swagger é desabilitado; apenas `/health` fica exposto.

#### 6. Frontend Angular

```bash
cd frontend/investops-web
npm install
npm start                  # ng serve em http://localhost:4200
```

Em dev o SPA chama a API direto em `http://localhost:5000/api` (`src/environments/environment.ts`).

---

### Ambientes (API)

A configuração é separada por ambiente via `ASPNETCORE_ENVIRONMENT`, carregando `appsettings.{Environment}.json` sobre o `appsettings.json` base.

| Ambiente              | Arquivo                        | Serilog     | Swagger |
| --------------------- | ------------------------------ | ----------- | ------- |
| Development           | `appsettings.Development.json` | Debug       | ✅      |
| Staging (homologação) | `appsettings.Staging.json`     | Information | ✅      |
| Production            | `appsettings.Production.json`  | Warning     | ❌      |

Rodar em um ambiente específico (perfis em `launchSettings.json`):

```bash
dotnet run --project src/InvestOps.Api --launch-profile staging
dotnet run --project src/InvestOps.Api --launch-profile production
# ou direto:
ASPNETCORE_ENVIRONMENT=Staging dotnet run --project src/InvestOps.Api
```

**Segredos (Staging/Production)** não ficam nos arquivos — vêm de **variáveis de ambiente**, que sobrescrevem o appsettings (`__` separa seções):

```bash
export ConnectionStrings__Postgres="Host=...;Database=...;Username=...;Password=..."
export RabbitMq__Host="rabbit.prod.interno"
export RabbitMq__Username="..."
export RabbitMq__Password="..."
export MarketData__Url="http://marketdata.prod.interno:5300"
```

### Segurança & escopo

Decisões de segurança do MVP — registradas em [ADR 0006](docs/adr/0006-defer-authentication-adopt-rate-limiting.md):

- **Rate limiting** nativo (ASP.NET Core): limite global de 100 req/min + política mais rígida (20 req/min) nos endpoints de escrita (`import`, `reprocess`); health checks isentos; `429` com `Retry-After`. Limites configuráveis via seção `RateLimiting`.
- **Autenticação/autorização**: fora do escopo do MVP, por decisão — backoffice interno assume rede privada + IdP corporativo; o ADR documenta como o design acomoda `[Authorize]` + policies por perfil.
- **SQL 100% parametrizado** (Dapper) com ordenação dinâmica por whitelist; **Swagger desabilitado em Production** (com teste garantindo); security headers no nginx.
- **Credenciais:** o `appsettings.json` base não contém credenciais — as de dev local vivem em `appsettings.Development.json`, e Staging/Production só via variáveis de ambiente. Os defaults `investops/investops` do `.env.example`/compose são **intencionais e exclusivos de demo local** (`docker compose up` sem edição é requisito); qualquer deploy real deve sobrescrevê-los.

### Testes

```bash
# Backend (.NET) — unit + integração (testcontainers exige Docker)
cd backend && dotnet test

# Worker e MarketData (Go) — integração usa testcontainers (exige Docker)
cd worker-go && go test ./...
cd marketdata-go && go test ./...

# Frontend (Angular) — Jest
cd frontend/investops-web && npm test
```

### Portas

| Serviço                         | Dev (host) | Docker             |
| ------------------------------- | ---------- | ------------------ |
| App — edge nginx (SPA + `/api`) | —          | 8080               |
| .NET API                        | 5000       | interna (via edge) |
| Angular dev server              | 4200       | —                  |
| MarketData (gRPC)               | 5300       | 5300               |
| PostgreSQL                      | 5432       | 5432               |
| RabbitMQ (AMQP)                 | 5672       | 5672               |
| RabbitMQ Management UI          | 15672      | 15672              |

## Fluxo de desenvolvimento

- `main` — estável (release por fase)
- `developer` — integração
- `feature/*` — uma branch por task, PR para `developer`

Commits seguem [Conventional Commits](https://www.conventionalcommits.org/).
