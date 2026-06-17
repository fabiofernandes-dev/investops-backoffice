# worker-go

Worker de processamento de operações do InvestOps. Consome `OperationImported` do RabbitMQ, aplica as regras de negócio (suitability, posição) e grava o resultado (`Processed`/`Rejected`), publicando eventos via Outbox (relay).

## Resiliência (Fase 5a)

- **Retry com backoff:** erro **transitório** (infra/DB) → `Nack` → fila de espera `operations.retry` (TTL `RETRY_BACKOFF_MS`) → volta à principal. Contagem por `x-death`.
- **DLQ:** após `RETRY_MAX_ATTEMPTS` tentativas → `operations.dlq`. Um DLQ consumer marca a operação como `Failed` + evento `OperationFailed` (5a.2). O **Reprocessar** (na UI) reenvia.
- **Rejeição de negócio** (suitability/posição) é terminal (`Rejected`), **sem** retry.

### ⚠️ Migração de fila (rodar uma vez ao subir a Fase 5a)

A fila `operations.processing` passou de **classic → quorum** (args novos: DLX, quorum). Redeclarar uma fila existente com args diferentes falha com `406 PRECONDITION_FAILED`. Na primeira subida desta fase:

```bash
docker compose down -v   # remove TODOS os volumes (RabbitMQ e Postgres); o migrator re-semeia no próximo up
docker compose --env-file .env.example up -d --build
```

## Config (env)

| Var | Default | Descrição |
|-----|---------|-----------|
| `DATABASE_URL` | `postgres://investops:investops@localhost:5432/investops` | Postgres |
| `RABBITMQ_URL` | `amqp://investops:investops@localhost:5672/` | RabbitMQ |
| `RABBITMQ_EXCHANGE` / `_QUEUE` / `_ROUTING_KEY` | `investops` / `operations.processing` / `OperationImported` | Topologia principal |
| `OUTBOX_POLL_SECONDS` / `OUTBOX_BATCH_SIZE` | `2` / `50` | Relay do Outbox |
| `RETRY_MAX_ATTEMPTS` | `3` | Tentativas antes da DLQ |
| `RETRY_BACKOFF_MS` | `5000` | Atraso entre tentativas (TTL da wait queue) |

## Rodar

```bash
go build ./...
go test ./...   # integração usa testcontainers (precisa de Docker)
```
