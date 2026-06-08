# InvestOps Backoffice

[![CI](https://github.com/fabiofernandes-dev/investops-backoffice/actions/workflows/ci.yml/badge.svg)](https://github.com/fabiofernandes-dev/investops-backoffice/actions/workflows/ci.yml)

Plataforma fullstack de backoffice para operações de investimento, construída como ambiente de estudo para uma vaga sênior fullstack.

> **Status:** em construção — Fase 1 (fundação). Veja o [SPEC.md](SPEC.md) para a visão completa.

## Arquitetura

```text
Angular Frontend → .NET Operations API → PostgreSQL
                                       → Outbox → RabbitMQ → Go Worker → PostgreSQL
```

Dois serviços (API .NET + Worker Go) sobre banco compartilhado no MVP, integrados via **Outbox Pattern** e RabbitMQ. Decisões registradas em [docs/adr/](docs/adr/).

## Stack

| Camada | Tecnologias |
|--------|-------------|
| Frontend | Angular 18+, TypeScript, RxJS, Angular Material |
| API | .NET 8, ASP.NET Core, Dapper, FluentValidation, Serilog, OpenTelemetry |
| Worker | Go, RabbitMQ (pgx), slog |
| Infra | Docker Compose, PostgreSQL, RabbitMQ |

## Estrutura do repositório

```text
investops-backoffice/
  backend/      # solução .NET 8
    src/
      InvestOps.Api/             # ASP.NET Core (Serilog, Swagger, Health Checks)
      InvestOps.Application/      # casos de uso (Fase 2+)
      InvestOps.Domain/          # entidades e enums do domínio
      InvestOps.Infrastructure/  # persistência/Dapper (Fase 2+)
      InvestOps.Contracts/       # DTOs de evento (Fase 2+)
      InvestOps.Migrator/        # DbUp: schema + seed
    tests/                       # UnitTests, IntegrationTests
  worker-go/    # worker assíncrono em Go (Fase 3)
  frontend/     # app Angular (Fase 4)
  contracts/    # contratos de eventos JSON (Fase 2)
  docs/         # ADRs e documentação de design
  docker-compose.yml
```

## Como rodar localmente

### Pré-requisitos

- [Docker](https://www.docker.com/) + Docker Compose
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Subir a infraestrutura (PostgreSQL + RabbitMQ)

```bash
cp .env.example .env      # opcional; o compose já usa defaults
docker compose --env-file .env.example up -d
docker compose --env-file .env.example ps      # ambos devem ficar (healthy)
```

### 2. Aplicar o schema do banco (e seed opcional)

```bash
cd backend
dotnet run --project src/InvestOps.Migrator -- --seed   # schema + dados de exemplo
# ou apenas o schema:
dotnet run --project src/InvestOps.Migrator
```

### 3. Rodar a API

```bash
cd backend
dotnet run --project src/InvestOps.Api
```

- Swagger: <http://localhost:5000/swagger> (a raiz `/` redireciona para o Swagger em Development)
- Health: <http://localhost:5000/health> · liveness `/health/live` · readiness `/health/ready`

> Em `Production` o Swagger é desabilitado; apenas `/health` fica exposto.

### 4. Rodar os testes

```bash
cd backend
dotnet test
```

### Portas

| Serviço | Porta |
|---------|-------|
| .NET API | 5000 |
| Angular (Fase 4) | 4200 |
| PostgreSQL | 5432 |
| RabbitMQ (AMQP) | 5672 |
| RabbitMQ Management UI | 15672 |

### Parar tudo

```bash
docker compose --env-file .env.example down        # mantém os dados (volumes)
docker compose --env-file .env.example down -v      # apaga os dados
```

## Fluxo de desenvolvimento

- `main` — estável (release por fase)
- `developer` — integração
- `feature/*` — uma branch por task, PR para `developer`

Commits seguem [Conventional Commits](https://www.conventionalcommits.org/).
