# InvestOps Backoffice — Fase 3 (Worker Go) Implementation Plan

> ✅ **Fase concluída e entregue.** Documento histórico de execução — os checkboxes registram o plano original (tudo entregue). O estado atual do produto está no [README — Status & roadmap](../../../README.md#status--roadmap).

**Goal:** Implementar o worker Go que consome `OperationImported`, aplica as regras de negócio, atualiza posição/status/`processing_events` e publica os eventos downstream via relay próprio (publisher por serviço).

**Architecture:** Worker Go (pgx + amqp091-go) sobre o banco compartilhado. Regras puras e testáveis (`rules`/`positions`), um `Processor` que orquestra carga→decisão→persistência transacional, consumer RabbitMQ (`prefetch=1`, ack após commit) e um relay de outbox próprio (`source='worker'`). O .NET passa a filtrar `source='api'` para os serviços ficarem independentes.

**Tech Stack:** Go 1.23, `jackc/pgx/v5` (+ `pgx-shopspring-decimal`), `rabbitmq/amqp091-go`, `shopspring/decimal`, `google/uuid`, `stretchr/testify`, `testcontainers-go`.

**Spec:** [docs/design/specs/2026-06-09-investops-phase3-go-worker-design.md](../specs/2026-06-09-investops-phase3-go-worker-design.md)

---

## Convenções de execução

- Branch `feature/*` a partir de `developer`; PR para `developer`; revisão do usuário; merge.
- Comandos via **Bash tool (git-bash)**.
- Commits Conventional + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- Go: comandos rodados em `worker-go/` (`cd /c/Projects/Investor/worker-go`). Go 1.26 instalado (amd64); `go.mod` declara `go 1.23`.
- **Docker rodando** para os testes de integração (testcontainers-go) e para o compose.
- Cadência: 1 task → 1 PR → **pausa para revisão**.
- **SKIP LOCKED / multi-instância de relay** fica para a Fase 5; no MVP o particionamento por `source` (conjuntos disjuntos, instância única de cada relay) já garante correção. Será sinalizado nos PRs.

## File Structure

```text
backend/src/InvestOps.Migrator/Scripts/Schema/Script0002_outbox_source_and_position_unique.sql  # P3.1
backend/src/InvestOps.Infrastructure/Persistence/Repositories/OutboxRepository.cs                # P3.1 (filtro source='api')

worker-go/
  go.mod, go.sum
  cmd/worker/main.go                       # P3.2 composição
  internal/
    config/config.go                       # P3.2
    observability/logger.go                # P3.2
    database/pool.go                        # P3.2 (pgxpool + registro decimal)
    messaging/consumer.go                   # P3.2 (declara fila/binding, consome)
    rules/suitability.go                    # P3.3
    rules/position.go                       # P3.3
    positions/average_price.go              # P3.3
    operations/model.go                     # P3.3 (Operation, Customer, Product, Position, Outcome)
    operations/decide.go                    # P3.3 (Decide puro)
    operations/processor.go                 # P3.4 (orquestra com Store)
    database/store.go                        # P3.4 (Store/TxRepo pgx)
    messaging/contracts.go                   # P3.5 (envelope + payloads)
    messaging/publisher.go                   # P3.5 (relay outbox source='worker')
  Dockerfile                                # P3.2
contracts/events/operation-processed.v1.json, operation-rejected.v1.json, position-updated.v1.json  # P3.5
docker-compose.yml                          # P3.2 (serviço worker)
```

---

## Chunk 1: Pré-requisito .NET, fundação Go e regras

### Task P3.1: Outbox multi-source + índice único + casing JSON (.NET)

**Files:**
- Create: `backend/src/InvestOps.Migrator/Scripts/Schema/Script0002_outbox_source_and_position_unique.sql`
- Create: `backend/src/InvestOps.Contracts/Events/EventJson.cs` (opções JSON compartilhadas — camelCase)
- Modify: `backend/src/InvestOps.Infrastructure/Persistence/Repositories/OutboxRepository.cs` (filtro `source='api'`)
- Modify: `backend/src/InvestOps.Application/Operations/ImportOperation/ImportOperationHandler.cs` (usar `EventJson.Options`)
- Modify: `backend/tests/InvestOps.IntegrationTests/ImportOperationTests.cs` (query jsonb passa a camelCase)

> **Casing do envelope (decisão):** padronizar o fio em **camelCase** (convenção de mercado p/ eventos JSON e o que os contratos em `contracts/events/` já declaram). Hoje o .NET usa `JsonSerializer.Serialize(envelope)` **sem opções → PascalCase**, divergindo do contrato. Corrigimos isso aqui (antes do worker), para o worker Go consumir/produzir o mesmo formato dos contratos.

- [ ] **Step 1: Branch** `feature/outbox-multisource` a partir de `developer`.

- [ ] **Step 2: Migration**

```sql
ALTER TABLE outbox_messages ADD COLUMN source text NOT NULL DEFAULT 'api';

DROP INDEX IF EXISTS idx_positions_account_product;
CREATE UNIQUE INDEX ux_positions_account_product ON portfolio_positions(account_id, product_id);
```

- [ ] **Step 3: Publisher .NET filtra a própria origem** — em `OutboxRepository.GetPendingAsync`, o `WHERE` passa a `status = 'Pending' AND source = 'api'`. (Inserts do .NET continuam sem `source` → `DEFAULT 'api'`.)

- [ ] **Step 4: Padronizar JSON em camelCase**

```csharp
// InvestOps.Contracts/Events/EventJson.cs
using System.Text.Json;
namespace InvestOps.Contracts.Events;
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web); // camelCase + leitura case-insensitive
}
```
Em `ImportOperationHandler`, trocar `JsonSerializer.Serialize(envelope)` por `JsonSerializer.Serialize(envelope, EventJson.Options)`. O payload passa a ter chaves camelCase (`payload`, `operationId`, ...).

- [ ] **Step 5: Ajustar teste afetado** — em `ImportOperationTests`, a query jsonb passa de `payload->'Payload'->>'OperationId'` para `payload->'payload'->>'operationId'`. (Confirma que o casing mudou de fato.)

- [ ] **Step 6: Validar** — `dotnet build` 0 avisos; `dotnet test` verde (Testcontainers reaplica migrations). Confirmar que `OutboxPublisherTests` continua verde: o INSERT manual da fixture omite `source` → `DEFAULT 'api'` → `GetPendingAsync(source='api')` ainda retorna.

- [ ] **Step 7: Commit + push + PR** → **PAUSA**.

---

### Task P3.2: Fundação do worker Go (módulo, config, pool, consumer, Docker)

**Files:**
- Create: `worker-go/go.mod`, `cmd/worker/main.go`, `internal/config/config.go`, `internal/observability/logger.go`, `internal/database/pool.go`, `internal/messaging/consumer.go`, `worker-go/Dockerfile`
- Modify: `docker-compose.yml` (serviço `worker`)
- Test: `internal/messaging/consumer_test.go` (testcontainers: conexão + consumo de 1 mensagem)

- [ ] **Step 1: Branch** `feature/worker-foundation`.

- [ ] **Step 2: Init do módulo + deps**

```bash
cd /c/Projects/Investor/worker-go
go mod init investops/worker
go get github.com/jackc/pgx/v5 github.com/jackc/pgx-shopspring-decimal github.com/shopspring/decimal
go get github.com/rabbitmq/amqp091-go github.com/google/uuid
go get github.com/stretchr/testify
go get github.com/testcontainers/testcontainers-go github.com/testcontainers/testcontainers-go/modules/postgres github.com/testcontainers/testcontainers-go/modules/rabbitmq
```
(remover o `worker-go/.gitkeep` da Fase 0.)

- [ ] **Step 3: `config`** — lê env com defaults locais:

```go
package config

import "os"

type Config struct {
	DatabaseURL string
	RabbitMQURL string
	Exchange    string
	Queue       string
	RoutingKey  string
}

func Load() Config {
	return Config{
		DatabaseURL: env("DATABASE_URL", "postgres://investops:investops@localhost:5432/investops"),
		RabbitMQURL: env("RABBITMQ_URL", "amqp://investops:investops@localhost:5672/"),
		Exchange:    env("RABBITMQ_EXCHANGE", "investops"),
		Queue:       env("RABBITMQ_QUEUE", "operations.processing"),
		RoutingKey:  env("RABBITMQ_ROUTING_KEY", "OperationImported"),
	}
}

func env(key, def string) string {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		return v
	}
	return def
}
```

- [ ] **Step 4: `observability.Logger`** — `slog` JSON com helper para escopo de correlationId.

```go
package observability

import (
	"log/slog"
	"os"
)

func New() *slog.Logger {
	return slog.New(slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))
}
```

- [ ] **Step 5: `database.Pool`** — `pgxpool` com registro do tipo decimal:

```go
package database

import (
	"context"
	pgxdecimal "github.com/jackc/pgx-shopspring-decimal"
	"github.com/jackc/pgx/v5/pgxpool"
)

func NewPool(ctx context.Context, url string) (*pgxpool.Pool, error) {
	cfg, err := pgxpool.ParseConfig(url)
	if err != nil {
		return nil, err
	}
	cfg.AfterConnect = func(ctx context.Context, conn *pgx.Conn) error {
		pgxdecimal.Register(conn.TypeMap())
		return nil
	}
	return pgxpool.NewWithConfig(ctx, cfg)
}
```
(ajustar import de `pgx` conforme necessário.)

- [ ] **Step 6: `messaging.Consumer`** — declara exchange (idempotente), fila durável, binding; consome com `prefetch=1`; chama um `handler func([]byte) error`; **ack** em sucesso, **nack(requeue)** em erro.

- [ ] **Step 7: `cmd/worker/main.go`** — compõe config → pool → consumer; por enquanto o handler só loga o evento recebido (processamento entra na P3.4). Trata `SIGINT/SIGTERM` para shutdown limpo.

- [ ] **Step 8: `Dockerfile`** (multi-stage):

```dockerfile
FROM golang:1.26.4-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o /out/worker ./cmd/worker

FROM gcr.io/distroless/static-debian12
COPY --from=build /out/worker /worker
ENTRYPOINT ["/worker"]
```

- [ ] **Step 9: Serviço no `docker-compose.yml`**

```yaml
  worker:
    build: ./worker-go
    environment:
      DATABASE_URL: postgres://${POSTGRES_USER:-investops}:${POSTGRES_PASSWORD:-investops}@postgres:5432/${POSTGRES_DB:-investops}
      RABBITMQ_URL: amqp://${RABBITMQ_USER:-investops}:${RABBITMQ_PASSWORD:-investops}@rabbitmq:5672/
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
```

- [ ] **Step 10: Teste de integração (testcontainers-go)** — sobe RabbitMQ, publica uma mensagem no exchange/routingKey, o consumer recebe e o handler captura → assert recebido. (Postgres não é necessário nesta task.)

- [ ] **Step 11: `go build ./...` + `go test ./...` + `go vet ./...`** verdes; `docker compose build worker` ok → commit + push + PR → **PAUSA**.

---

### Task P3.3: Regras puras + decisão (`Decide`)

**Files:**
- Create: `internal/rules/suitability.go`, `internal/rules/position.go`, `internal/positions/average_price.go`, `internal/operations/model.go`, `internal/operations/decide.go`
- Test: `*_test.go` correspondentes

- [ ] **Step 1: Branch** `feature/worker-rules`.

- [ ] **Step 2: TDD suitability** — teste primeiro:

```go
func TestSuitability(t *testing.T) {
	assert.True(t, rules.SuitabilityViolated("Conservative", 3))  // > 2
	assert.False(t, rules.SuitabilityViolated("Conservative", 2))
	assert.False(t, rules.SuitabilityViolated("Aggressive", 5))
}
```
Implementar:
```go
package rules

func MaxRiskLevel(profile string) int {
	switch profile {
	case "Conservative": return 2
	case "Moderate":     return 4
	case "Aggressive":   return 5
	default:             return 0
	}
}

func SuitabilityViolated(profile string, riskLevel int) bool {
	return riskLevel > MaxRiskLevel(profile)
}
```

- [ ] **Step 3: TDD posição insuficiente** (Sell): `InsufficientPosition(posQty, sellQty decimal.Decimal) bool` → `posQty.LessThan(sellQty)`. Inclui caso posição 0.

- [ ] **Step 4: TDD preço médio** (Regra 5):

```go
func TestAveragePrice(t *testing.T) {
	// 100@10 + 100@20 → 150
	got := positions.NewAveragePrice(d("100"), d("10"), d("100"), d("20"))
	assert.True(t, got.Equal(d("15")))
}
```
```go
package positions
import "github.com/shopspring/decimal"
func NewAveragePrice(curQty, curAvg, addQty, price decimal.Decimal) decimal.Decimal {
	newQty := curQty.Add(addQty)
	if newQty.IsZero() { return decimal.Zero }
	return curQty.Mul(curAvg).Add(addQty.Mul(price)).Div(newQty)
}
```

- [ ] **Step 5: Modelos + `Decide` puro** (`operations/model.go`, `operations/decide.go`):

`Decide(op Operation, profile string, riskLevel int, pos Position, posExists bool) Outcome` retorna:
- `Buy` com suitability violada → `Rejected{InvestorProfileNotCompatible}`
- `Sell` com posição insuficiente (ou inexistente) → `Rejected{InsufficientPosition}`
- `Buy` válido → `Processed` + nova posição (preço médio) + `PositionChanged=true`
- `Sell` válido → `Processed` + posição reduzida (média mantida) + `PositionChanged=true`
- `Deposit/Withdraw/Income/Fee` → `Processed`, `PositionChanged=false`

- [ ] **Step 6: TDD do `Decide`** — casos: buy ok, reject por perfil, sell insuficiente, sell ok, tipo sem posição. Asserts exatos com `decimal`.

- [ ] **Step 7: `go test ./...` + `go vet`** verdes → commit + push + PR → **PAUSA**.

---

## Chunk 2: Persistência, processor e relay

### Task P3.4: Persistência + processor transacional

**Files:**
- Create: `internal/database/store.go` (Store/TxRepo pgx), `internal/operations/processor.go`
- Modify: `cmd/worker/main.go` (handler usa o processor)
- Test: `internal/operations/processor_test.go` (fake Store, unit) + `internal/database/store_test.go` (testcontainers: consume→processa→banco)

- [ ] **Step 1: Branch** `feature/worker-persistence`.

- [ ] **Step 2: Interfaces `Store`/`TxRepo`** (em `operations`, implementadas em `database`):

```go
type Store interface {
	GetOperation(ctx context.Context, id uuid.UUID) (Operation, bool, error)
	GetCustomerProfileByAccount(ctx context.Context, accountID uuid.UUID) (string, error)
	GetProductRiskLevel(ctx context.Context, productID uuid.UUID) (int, error)
	WithinTx(ctx context.Context, fn func(TxRepo) error) error
}

type TxRepo interface {
	GetPositionForUpdate(ctx context.Context, accountID, productID uuid.UUID) (Position, bool, error)
	UpsertPosition(ctx context.Context, p Position) error
	UpdateOperationStatus(ctx context.Context, id uuid.UUID, status, reason string) error
	InsertProcessingEvent(ctx context.Context, e ProcessingEvent) error
	InsertOutbox(ctx context.Context, m OutboxMessage) error
}
```

- [ ] **Step 3: TDD do `Processor`** com **fake Store** (sem banco): 
  - idempotência (op já `Processed` → não chama `WithinTx`, retorna nil/ack);
  - buy válido → upsert posição + status `Processed` + processing_event + outbox `OperationProcessed`+`PositionUpdated`;
  - reject por perfil/posição → status `Rejected` + outbox `OperationRejected`.
  (Fake `TxRepo` registra chamadas para assert.)

- [ ] **Step 3b: Envelope de entrada (camelCase + decimal)** — definir os structs do `EventEnvelope`/`OperationImportedV1` de **entrada** com tags JSON **camelCase** (formato padronizado na P3.1) e campos numéricos (`quantity`, `unitPrice`) como `decimal.Decimal` (nunca `float64`). Teste de desserialização afirmando valores decimais exatos (ex.: `unitPrice` `25.40`). `operationDate` como `date` (string `2006-01-02`).

- [ ] **Step 4: `Processor.Process`** — recebe o evento desserializado; carrega op (idempotência por status), perfil, risco; `WithinTx`: `GetPositionForUpdate` → `Decide` → aplica (upsert/status/processing_event/outbox `source='worker'`). Commit no retorno nil.

- [ ] **Step 5: Implementar `Store`/`TxRepo` com pgx** (`database/store.go`):
  - `WithinTx`: `pool.BeginTx` → `fn(txRepo)` → `Commit`/`Rollback`.
  - `GetPositionForUpdate`: `SELECT ... FOR UPDATE`; retorna `exists=false` se não houver.
  - `UpsertPosition`: `INSERT (id, account_id, product_id, quantity, average_price, gross_amount, updated_at) VALUES (...) ON CONFLICT (account_id, product_id) DO UPDATE SET quantity=, average_price=, gross_amount=, updated_at=now()`. **O braço de INSERT precisa fornecer `id` (`uuid.New()`)** — `portfolio_positions.id` é PK sem default.
  - `UpdateOperationStatus`: `UPDATE operations SET status=, rejection_reason=, updated_at=now() WHERE id=`.
  - `InsertProcessingEvent`: `event_type`, `status`, `attempt_count`, `payload::jsonb`, `processed_at=now()`.
  - `InsertOutbox`: `INSERT ... (event_type, version, payload::jsonb, status='Pending', source='worker', created_at=now())`.

- [ ] **Step 6: Wire no `main.go`** — handler = `processor.Process(ctx, evt)`; ack se nil, nack(requeue) se erro de infra.

- [ ] **Step 7: Teste de integração (testcontainers Postgres)** — sobe Postgres com os scripts de schema aplicados **em ordem** (`Script0001` depois `Script0002`) via `postgres.WithInitScripts(...)`. Os caminhos devem ser **ancorados no arquivo de teste** (não no cwd): computar a raiz do repo a partir de `runtime.Caller(0)` (o cwd dos testes Go é o dir do pacote, ex.: `worker-go/internal/database/`, então um caminho começando em `backend/...` NÃO resolve). Seed via SQL. Então `Processor.Process` com `Store` real → assert posição/status/processing_events/outbox no banco. Cobrir: buy válido, reject perfil, reject sell insuficiente.

```go
// helper para achar a raiz do repo a partir do arquivo de teste
func schemaDir(t *testing.T) string {
	_, file, _, _ := runtime.Caller(0)                 // .../worker-go/internal/database/store_test.go
	root := filepath.Join(filepath.Dir(file), "..", "..", "..")  // → repo root
	return filepath.Join(root, "backend", "src", "InvestOps.Migrator", "Scripts", "Schema")
}
// postgres.Run(ctx, "postgres:16-alpine", postgres.WithInitScripts(script0001, script0002), ...)
```

> Nota: o worker Go não tem acesso ao DbUp .NET; o teste aplica os `.sql` de schema diretamente (fonte única de schema no Migrator), na ordem `0001 → 0002`.

- [ ] **Step 8: `go test ./...` + `go vet`** verdes; smoke manual (compose up + import via API → worker processa → status `Processed`/posição no banco) → commit + push + PR → **PAUSA**.

---

### Task P3.5: Relay de outbox do worker + contratos + end-to-end

**Files:**
- Create: `internal/messaging/contracts.go`, `internal/messaging/publisher.go`, `contracts/events/operation-processed.v1.json`, `operation-rejected.v1.json`, `position-updated.v1.json`
- Modify: `cmd/worker/main.go` (inicia o relay em goroutine)
- Test: `internal/messaging/publisher_test.go` (testcontainers Postgres+RabbitMQ: outbox `source='worker'` → publicado)

- [ ] **Step 1: Branch** `feature/worker-publisher`.

- [ ] **Step 2: Contratos neutros** (`contracts/events/*.v1.json`) — envelope + payloads do §11 (Processed/Rejected/PositionUpdated), em **camelCase** (consistente com `operation-imported.v1.json` e com o formato padronizado na P3.1).

- [ ] **Step 3: `contracts.go`** — structs Go do envelope + payloads com tags JSON **camelCase** (`eventId/eventType/version/occurredAt/correlationId/payload` e campos do payload), espelhando o que o .NET emite após a P3.1. Campos monetários como `decimal.Decimal`.

> Importante: o formato do envelope é **camelCase** nos dois serviços (padronizado na P3.1). As tags dos structs Go devem produzir exatamente essas chaves para consumidores futuros lerem .NET e Go de forma uniforme.

- [ ] **Step 4: `publisher.Relay`** — loop (intervalo configurável): numa transação, `SELECT ... FROM outbox_messages WHERE status='Pending' AND source='worker' ORDER BY created_at LIMIT @n`; publica cada no exchange `investops` com `PublishWithContext` (routingKey=`event_type`, `ContentType:"application/json"`, `DeliveryMode: amqp.Persistent` — paridade com o `RabbitMqEventBus` .NET); `UPDATE ... SET status='Published', published_at=now()`. Erro → incrementa `attempts`/`last_error`, mantém `Pending`.

- [ ] **Step 5: TDD/integração do relay** (testcontainers Postgres+RabbitMQ): insere outbox `source='worker'` Pending → roda um ciclo do relay → mensagem chega na fila (consumer de teste com binding) e linha vira `Published`. Confirmar que o relay **não** toca linhas `source='api'`.

- [ ] **Step 6: Wire no `main.go`** — inicia `Relay` em goroutine junto do consumer; shutdown coordenado.

- [ ] **Step 7: End-to-end (compose)** — `docker compose up` (API + worker + infra); `POST /import` (Buy válido com seed) → operação vira `Processed`, posição criada, e eventos `OperationProcessed`/`PositionUpdated` publicados no RabbitMQ (verificar via Management API / fila de teste). Reject (conservador + risco alto) → `Rejected`/`OperationRejected`.

- [ ] **Step 8: `go test ./...` + `go vet`** verdes; e2e ok → commit + push + PR → **PAUSA**.

---

## Fechamento da Fase 3

Após P3.5 mergeada: abrir PR `developer → main` (release das Fases 1–3). Critérios: worker consome, aplica regras, atualiza posição/status, grava eventos e os publica via relay próprio; serviços independentes; testes unit + testcontainers verdes; `docker compose up` sobe API + worker + infra (SPEC §14). Próximo: **Fase 4 — frontend Angular**.
