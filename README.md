# InvestOps Backoffice

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
  backend/      # solução .NET 8 (API, Application, Domain, Infrastructure, Contracts + testes)
  worker-go/    # worker assíncrono em Go
  frontend/     # app Angular
  contracts/    # contratos de eventos (JSON)
  docs/         # ADRs e documentação de design
  docker-compose.yml
```

## Como rodar localmente

> Será documentado ao final da Fase 1 (task T6). Pré-requisitos atuais: Docker, .NET 8 SDK.

## Fluxo de desenvolvimento

- `main` — estável (release por fase)
- `developer` — integração
- `feature/*` — uma branch por task, PR para `developer`

Commits seguem [Conventional Commits](https://www.conventionalcommits.org/).
