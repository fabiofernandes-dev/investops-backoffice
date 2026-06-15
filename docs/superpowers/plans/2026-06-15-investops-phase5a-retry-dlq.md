# InvestOps Backoffice — Fase 5a (Retry + DLQ + OperationFailed) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir o requeue infinito do worker por **retry com backoff + limite + DLQ**, finalizando operações que falham de forma transitória como **`Failed`** + evento **`OperationFailed`**.

**Architecture:** Filas **quorum**; em erro transitório o worker `Nack(requeue=false)` → DLX `investops.retry` → wait queue (TTL) → volta à principal (backoff); contagem por **`x-death`**; após N tentativas o consumer **publica na DLQ**; um **DLQ consumer** (canal próprio) marca `Failed` + `processing_event` + outbox `OperationFailed`. Rejeição de negócio segue `Rejected` (sem retry).

**Tech Stack:** Go 1.26, rabbitmq/amqp091-go (quorum + DLX + TTL), pgx, testcontainers.

**Spec:** [docs/superpowers/specs/2026-06-15-investops-phase5a-retry-dlq-design.md](../specs/2026-06-15-investops-phase5a-retry-dlq-design.md)

---

## Convenções
- Branch `feature/*` a partir de `developer`; 1 task → 1 PR → **pausa**.
- **Migração:** a fila muda classic→quorum (args novos) → 1ª subida desta fase exige `docker compose down -v` (limpa **todos** os volumes; o migrator re-semeia). Documentar no PR/README.
- Nomes (consts em `internal/messaging`): exchanges `investops` (existe), `investops.retry`, `investops.dlq`; filas `operations.processing`, `operations.retry`, `operations.dlq`.

---

## Chunk 1: Topologia + retry no consumer (5a.1)

### Task 5a.1: filas quorum + retry/backoff + roteamento p/ DLQ

**Files:**
- Modify: `worker-go/internal/config/config.go` (RetryMaxAttempts, RetryBackoff), `worker-go/cmd/worker/main.go` (passar config ao consumer)
- Modify: `worker-go/internal/messaging/consumer.go` (topologia + retry/x-death/DLQ)
- Test: `worker-go/internal/messaging/consumer_death_test.go` (unit deathCount), `worker-go/internal/messaging/retry_dlq_test.go` (integração)

- [ ] **Step 1: Branch** `feature/worker-retry-dlq`.

- [ ] **Step 2: Config** — em `config.go`: `RetryMaxAttempts int` (env `RETRY_MAX_ATTEMPTS`, default 3) e `RetryBackoff time.Duration` (env `RETRY_BACKOFF_MS`, default 5000ms). Validar > 0 (senão usar default).

- [ ] **Step 3: TDD `deathCount`** (`consumer_death_test.go` primeiro) — tabela:
  - sem header `x-death` → 0.
  - `x-death` com entrada `{queue: operations.processing, reason: rejected, count: int64(2)}` (+ uma `{operations.retry, expired, 2}`) → **2** (filtra por queue+reason, não índice 0).
  Implementar em `consumer.go`:
  ```go
  func deathCount(d amqp.Delivery) int64 {
      raw, ok := d.Headers["x-death"]
      if !ok { return 0 }
      arr, ok := raw.([]interface{}); if !ok { return 0 }
      for _, e := range arr {
          t, ok := e.(amqp.Table); if !ok { continue }
          if t["queue"] == queueMain && t["reason"] == "rejected" {
              if c, ok := t["count"].(int64); ok { return c }
          }
      }
      return 0
  }
  ```

- [ ] **Step 4: Topologia** — adicionar **campos a `ConsumerConfig`**: `MaxAttempts int`, `Backoff time.Duration` (não mudar a assinatura para posicional — mantém `NewConsumer(cfg, logger)`). `NewConsumer` guarda `cfg.MaxAttempts`/`cfg.Backoff` no struct e declara (idempotente) tudo:
  ```go
  // exchanges
  ch.ExchangeDeclare("investops", topic, durable=true, ...)        // já existe
  ch.ExchangeDeclare(exchangeRetry, "fanout", true, false, false, false, nil)
  ch.ExchangeDeclare(exchangeDlq,   "fanout", true, false, false, false, nil)
  // filas (quorum)
  ch.QueueDeclare(queueMain, true, false, false, false, amqp.Table{
      "x-queue-type": "quorum", "x-dead-letter-exchange": exchangeRetry })
  ch.QueueDeclare(queueRetry, true, false, false, false, amqp.Table{
      "x-queue-type": "quorum", "x-message-ttl": int32(backoff.Milliseconds()),
      "x-dead-letter-exchange": "investops", "x-dead-letter-routing-key": cfg.RoutingKey })
  ch.QueueDeclare(queueDlq, true, false, false, false, amqp.Table{ "x-queue-type": "quorum" })
  // bindings
  ch.QueueBind(queueMain, cfg.RoutingKey, "investops", false, nil)   // já existe
  ch.QueueBind(queueRetry, "", exchangeRetry, false, nil)
  ch.QueueBind(queueDlq, "", exchangeDlq, false, nil)
  ch.Qos(1,0,false)
  ```
  Guardar `maxAttempts` e `dlqExchange` no struct `Consumer`.
  > `x-message-ttl` deve ser inteiro (ms). Use `int32(backoff.Milliseconds())`.

- [ ] **Step 5: `handle()` com retry/DLQ** — substitui o `Nack(false,true)`:
  ```go
  if err := handler(msgCtx, delivery.Body); err != nil {
      attempt := deathCount(delivery) + 1
      if attempt >= c.maxAttempts {
          c.logger.Error("esgotou tentativas; enviando p/ DLQ", "attempt", attempt, "error", err)
          if perr := c.ch.PublishWithContext(context.WithoutCancel(ctx), c.dlqExchange, "", false, false, amqp.Publishing{
              ContentType: delivery.ContentType, Body: delivery.Body, Headers: delivery.Headers, DeliveryMode: amqp.Persistent,
          }); perr != nil { c.logger.Error("falha ao publicar na DLQ", "error", perr); _ = delivery.Nack(false, false); return }
          _ = delivery.Ack(false)
          return
      }
      c.logger.Warn("falha; reenfileirando p/ retry", "attempt", attempt, "error", err)
      _ = delivery.Nack(false, false) // requeue=false -> DLX (investops.retry) -> wait queue (backoff)
      return
  }
  _ = delivery.Ack(false)
  ```
  (Publish na própria goroutine do consume — prefetch=1, sequencial.)

- [ ] **Step 6: main.go + teste existente** — popular `ConsumerConfig{... MaxAttempts: cfg.RetryMaxAttempts, Backoff: cfg.RetryBackoff}`. **Atualizar `consumer_test.go`** (`TestConsumerReceivesMessage`) p/ setar `MaxAttempts`/`Backoff` no cfg (senão `MaxAttempts=0` mandaria toda 1ª falha direto p/ DLQ). O handler do `main` continua tratando unmarshal inválido com `return nil` (Ack, fora do retry).

- [ ] **Step 7: TDD integração** (`retry_dlq_test.go`, testcontainers RabbitMQ+Postgres) — backoff curto (200ms) e maxAttempts 3:
  - Publica `OperationImported` cujo handler **sempre falha** (handler stub que retorna erro). Após ~N×(processamento+200ms), espia a **`operations.dlq`** (`ch.Get`/consume) e assere que a mensagem chegou (1 msg) e que ela **não** está mais na principal. (O DLQ consumer vem na 5a.2 — aqui validamos só o roteamento.)
  - Handler que **tem sucesso** (ack) → DLQ vazia.
  > Sobe o `Consumer` real com um `Handler` injetado; usa o factory de schema dos outros testes Go para o Postgres (não estritamente necessário aqui, mas mantém o padrão).

- [ ] **Step 8: Migração** — adicionar nota no `worker-go/README.md` (ou um `docs` curto): "Fase 5a mudou a fila para quorum+DLX → rode `docker compose down -v` uma vez."

- [ ] **Step 9: Build + vet + test** (`cd worker-go && go build ./... && go vet ./... && go test ./...`) → commit + push + PR → **PAUSA**.

---

## Chunk 2: DLQ consumer + OperationFailed (5a.2)

### Task 5a.2: marcar Failed + evento OperationFailed

**Files:**
- Modify: `worker-go/internal/operations/model.go` (StatusFailed), `store.go` (ProcessingEvent fields + TxRepo.GetOperationStatusForUpdate), `events_out.go` (operationFailedOutbox), `database/store.go` (InsertProcessingEvent + GetOperationStatusForUpdate)
- Create: `worker-go/internal/operations/failure.go` (FailureHandler) + test, `worker-go/internal/messaging/dlq_consumer.go`, `contracts/events/operation-failed.v1.json`
- Modify: `worker-go/cmd/worker/main.go` (goroutine do DLQ consumer)

- [ ] **Step 1: Branch** `feature/worker-dlq-failed` (a partir de `developer` com 5a.1 mergeado).

- [ ] **Step 2: model + store** —
  - `model.go`: `StatusFailed = "Failed"`.
  - `store.go` `ProcessingEvent`: `AttemptCount int`, `ErrorMessage string`. `TxRepo` ganha `GetOperationStatusForUpdate(ctx, id) (status string, exists bool, err error)`.
  - `database/store.go`: `InsertProcessingEvent` passa a gravar `attempt_count` e `error_message` (hoje grava 0 e omite error_message); implementar `GetOperationStatusForUpdate` (`SELECT status FROM operations WHERE id=$1 FOR UPDATE;`).
  - ⚠️ **Atualizar o `fakeTx`** em `worker-go/internal/operations/processor_test.go` p/ implementar o novo método `GetOperationStatusForUpdate` (senão o pacote `operations` não compila nos testes).

- [ ] **Step 3: evento + contrato** —
  - `events_out.go`: const `EventOperationFailed = "OperationFailed"`; `operationFailedOutbox(operationID uuid.UUID, reason string, attempts int) OutboxMessage` (envelope camelCase, version 1, payload `{operationId, reason, attempts}`).
  - `contracts/events/operation-failed.v1.json` (espelha os outros; `eventType` const `OperationFailed`, `version` const 1; payload `operationId` uuid, `reason` string, `attempts` integer).

- [ ] **Step 4: TDD FailureHandler** (`failure_test.go` com store fake) — opção: testar idempotência via integração (Step 6). Implementar `failure.go`:
  ```go
  func (p *Processor) HandleFailure(ctx context.Context, operationID uuid.UUID, attempts int, errMsg string) error {
      return p.store.WithinTx(ctx, func(tx TxRepo) error {
          status, exists, err := tx.GetOperationStatusForUpdate(ctx, operationID)
          if err != nil { return err }
          if !exists || status == StatusProcessed || status == StatusRejected || status == StatusFailed {
              return nil // idempotente
          }
          if err := tx.UpdateOperationStatus(ctx, operationID, StatusFailed, ""); err != nil { return err }
          if err := tx.InsertProcessingEvent(ctx, ProcessingEvent{
              OperationID: operationID, EventType: EventOperationFailed, Status: StatusFailed,
              AttemptCount: attempts, ErrorMessage: errMsg,
          }); err != nil { return err }
          return tx.InsertOutbox(ctx, operationFailedOutbox(operationID, "ProcessingError", attempts))
      })
  }
  ```
  (Reusa o `Processor`/`store` existentes — adicionar o método.)

- [ ] **Step 5: DLQ consumer** (`dlq_consumer.go`) — struct com **conexão/canal próprios**; consome `operations.dlq`; para cada msg: `json.Unmarshal` → `OperationImportedEvent`; **`attempts := deathCount(d) + 1`** (a última tentativa foi **Ack**'d sem gerar `x-death`, então o header marca `maxAttempts-1`; +1 = total real = N); chama `processor.HandleFailure(ctx, evt.Payload.OperationID, attempts, "Falha no processamento após retries")` (atenção: campo é `OperationID`, com D maiúsculo); `Ack` em sucesso, `Nack(requeue=true)` em erro de infra. `Run(ctx)`/`Close()` no molde do consumer/relay. (O `dlq_consumer.go` está no **mesmo pacote `messaging`** → usa `deathCount` direto, sem exportar.)

- [ ] **Step 6: main.go** — abrir o DLQ consumer e rodar `go dlqConsumer.Run(ctx)` ao lado do consumer principal + relay; `defer dlqConsumer.Close()`.

- [ ] **Step 7: TDD integração** (estende `retry_dlq_test.go` ou novo) — poison message + DLQ consumer ativo: após N tentativas a operação vira **`Failed`** (`processing_events` com `error_message` e `attempt_count`=N) e há **outbox `OperationFailed`** (source='worker'); operação que processa **não** vira Failed; **idempotência**: re-publicar na DLQ uma op já-`Failed` não duplica.

- [ ] **Step 8: Build + vet + test** verdes; **smoke e2e** (compose `down -v` + `up -d --build`): importar uma operação que force erro transitório é difícil sem hook — validar pelo teste de integração; opcional: confirmar no RabbitMQ mgmt que as filas `operations.retry`/`operations.dlq` existem. → commit + push + PR → **PAUSA**.

---

## Fechamento da Fase 5a
O worker para de fazer requeue infinito: erro transitório → 3 tentativas com backoff → DLQ → `Failed` + `OperationFailed`, e o **Reprocessar** (já existente) reenvia. Próximas sub-fases: 5b (observabilidade), 5c (health/reconnect), 5d (mTLS).
