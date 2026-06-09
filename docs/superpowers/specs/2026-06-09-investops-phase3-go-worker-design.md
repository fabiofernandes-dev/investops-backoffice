# Design — InvestOps Backoffice (Fase 3: Worker Go)

**Data:** 2026-06-09
**Status:** Aprovado (início da Fase 3)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§4, §6, §7, §11, §13, §18)
**Fases anteriores:** [fundação](2026-06-07-investops-foundation-design.md) · [importação](2026-06-07-investops-phase2-operation-import-design.md)

A Fase 3 implementa o **segundo serviço**: um worker em Go que consome `OperationImported` do RabbitMQ, aplica as regras de negócio (suitability, posição insuficiente, idempotência), calcula preço médio, atualiza posição e status, grava `processing_events` e **publica os eventos downstream** (Processed/Rejected/PositionUpdated) via seu próprio relay de outbox. Banco compartilhado (ADR 0005).

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Eventos downstream | Worker **publica** OperationProcessed/Rejected/PositionUpdated |
| Publicação | **Publisher por serviço**: worker grava na outbox (`source='worker'`) na mesma transação e tem **relay próprio**; publisher .NET filtra `source='api'` |
| Posição única | **UNIQUE** em `portfolio_positions(account_id, product_id)` + `ON CONFLICT` (upsert) |
| Testes | **Unit** (regras puras) + **testcontainers-go** (consume→processa→banco; publish→RabbitMQ) |
| Container | **Dockerfile** + serviço no `docker-compose` (SPEC §12/§14); roda local via `go run` |
| Stack | Go, `pgx/v5`, `rabbitmq/amqp091-go`, `slog` |

## 2. Arquitetura

```text
RabbitMQ (exchange investops, topic)
   │  routingKey OperationImported
   ▼
worker-go (consumer, prefetch=1)
   ├─ rules/      regras puras (suitability, posição) — testáveis sem I/O
   ├─ positions/  preço médio + upsert
   ├─ operations/ processor (orquestra: carrega → aplica regras → transação)
   ├─ database/   pool pgx + repositórios + outbox (source='worker')
   ├─ messaging/  consumer + relay de publicação (source='worker')
   └─ observability/ slog + correlationId
   ▼
PostgreSQL (compartilhado)  +  RabbitMQ (eventos downstream)
```

### Estrutura de arquivos (SPEC §4)

```text
worker-go/
  cmd/worker/main.go
  internal/
    config/config.go            # env: DB_*, RABBITMQ_*
    observability/logger.go     # slog JSON + correlationId
    database/
      pool.go                   # pgxpool
      operations.go             # load/update operação
      references.go             # account→customer, product, position (FOR UPDATE)
      positions.go              # upsert posição (ON CONFLICT)
      processing_events.go      # insert
      outbox.go                 # insert (source='worker') + relay (pending source='worker')
    messaging/
      consumer.go               # declara fila/binding, consome, ack/nack
      publisher.go              # relay: lê outbox source='worker' → RabbitMQ
      contracts.go              # structs dos eventos (envelope + payloads)
    rules/
      suitability.go            # Regra 1
      position.go               # Regra 2
    positions/
      average_price.go          # Regra 5
    operations/
      processor.go              # orquestra o fluxo numa transação
  go.mod
  Dockerfile
```

## 3. Mudanças no lado .NET (pré-requisito — P3.1)

Para o "publisher por serviço" funcionar sem o worker depender do host .NET:

- **Migration** (no Migrator .NET, schema centralizado), `Scripts/Schema/Script0002_*.sql`:
  - `ALTER TABLE outbox_messages ADD COLUMN source text NOT NULL DEFAULT 'api';`
  - `CREATE UNIQUE INDEX ux_positions_account_product ON portfolio_positions(account_id, product_id);` (remove/!substitui o índice não-único `idx_positions_account_product`).
- **Publisher .NET**: `GetPendingAsync` passa a filtrar `WHERE status='Pending' AND source='api'` (rows do .NET defaultam para `'api'`; nenhuma mudança no import — o `DEFAULT` cobre).
- **Concorrência**: o particionamento por `source` já dá conjuntos disjuntos (cada relay toca só as suas linhas). `FOR UPDATE SKIP LOCKED` entra como salvaguarda para escalar instâncias de um mesmo relay — adoto a leitura dos pendentes dentro de transação (lock curto durante o publish; para alto volume, "claim-then-publish" fica para a Fase 5).

## 4. Fluxo de processamento (worker)

1. Consumer declara fila durável `operations.processing` + binding (`OperationImported`) no exchange `investops`; `prefetch=1`.
2. Desserializa `EventEnvelope<OperationImportedV1>`; propaga `correlationId` no log scope.
3. Carrega a operação. **Idempotência (Regra 3):** se `Status ∈ {Processed, Rejected}` → **ack** e ignora. No caminho feliz o worker espera `Imported` (ele é o único escritor dos estados terminais); qualquer outro estado inesperado segue o processamento normal.
4. Abre transação; carrega conta→cliente, produto e **posição atual (`SELECT ... FOR UPDATE`)**. Posição inexistente = quantidade 0.
5. Aplica regras (ver §5). Determina desfecho:
   - **Rejeitada:** `Status=Rejected` + `RejectionReason`; `processing_events` (Rejected); outbox `OperationRejected`.
   - **Processada:** atualiza posição (ver §6); `Status=Processed`; `processing_events` (Processed); outbox `OperationProcessed` (+ `PositionUpdated` quando há mudança de posição).
6. Commit → **ack**.

> **`processing_events`:** `event_type` recebe o desfecho (`OperationProcessed`/`OperationRejected`), `status` o estado, `processed_at = now()`, `attempt_count` incrementa em reprocessamentos. `payload` guarda um resumo (jsonb).
7. Erro de infra (DB/conexão) antes do commit → rollback → **nack(requeue)**; a mensagem volta. Limite de tentativas/DLQ + evento `OperationFailed` ficam para a **Fase 5**.

## 5. Regras de negócio (puras, testáveis)

- **Suitability (Regra 1)** — aplica a `Buy`:
  `Conservative→max 2`, `Moderate→max 4`, `Aggressive→max 5`. Se `product.RiskLevel > max` → rejeita `InvestorProfileNotCompatible`.
- **Posição insuficiente (Regra 2)** — aplica a `Sell`: se `posição.Quantity < operação.Quantity` → rejeita `InsufficientPosition`. **Sell sem posição** (primeira operação é uma venda) = posição 0 → rejeita `InsufficientPosition`.
- **Tipos sem regra de posição no MVP** (`Deposit/Withdraw/Income/Fee`): marcados `Processed` **sem alterar posição** e **sem** `PositionUpdated`; o `OperationProcessed` carrega a quantidade da operação e campos de posição zerados. Sinalizado como simplificação do MVP.

## 6. Preço médio e posição (Regra 5)

- **Buy:** `novaQtd = qtd + q`; `novaMédia = (qtd*média + q*preço) / novaQtd`; `grossAmount = novaQtd * novaMédia`.
- **Sell:** `novaQtd = qtd - q`; `média` inalterada; `grossAmount = novaQtd * média`.
- **Upsert** via `INSERT ... ON CONFLICT (account_id, product_id) DO UPDATE` (requer o índice único da §3). Primeira compra cria a posição.
- **Precisão:** usar tipo decimal exato (`shopspring/decimal` ou `pgx` numeric), **nunca `float64`**, para casar com `numeric(18,6)`/`numeric(18,2)` e permitir asserts exatos nos testes.

## 7. Eventos downstream + contratos

Contratos neutros em `contracts/events/` (SPEC §4/§11) + structs Go espelho:

| Evento | routingKey | Payload |
|--------|-----------|---------|
| `operation-processed.v1.json` | OperationProcessed | operationId, accountId, productId, quantity, averagePrice, grossAmount |
| `operation-rejected.v1.json` | OperationRejected | operationId, reason |
| `position-updated.v1.json` | PositionUpdated | accountId, productId, quantity, averagePrice, grossAmount |

Envelope idêntico ao da Fase 2 (`eventId/eventType/version/occurredAt/correlationId/payload`), `correlationId` propagado do evento de entrada. Gravados na `outbox_messages` (`source='worker'`) na transação do processamento; o **relay do worker** publica (espelhando `ORDER BY created_at LIMIT batch` do relay .NET) e marca `Published`. `operation-failed.v1.json` (§4) fica para a Fase 5.

> **Propriedade conhecida (at-least-once):** o RabbitMQ entrega ao menos uma vez; consumidores futuros de `OperationProcessed`/`PositionUpdated` podem ver duplicatas e devem ser idempotentes. Fora do escopo resolver na Fase 3 (não há consumidor no MVP).

## 8. Observabilidade

`slog` em JSON; cada processamento loga com `correlationId`, `operationId`, `externalId`, desfecho e duração. (OpenTelemetry completo é Fase 5.)

## 9. Testes (SPEC §13)

- **Unit** (sem I/O): suitability (compatível/incompatível por perfil), posição insuficiente em Sell, preço médio (buy acumula, sell mantém média), idempotência (operação já processada não reprocessa). Processor com repositórios fake/in-memory para o caminho feliz e rejeições.
- **Integração (testcontainers-go: Postgres + RabbitMQ):**
  - consome `OperationImported` → processa `Buy` válido → posição/`Status=Processed`/`processing_events` no banco;
  - rejeita por perfil e por posição insuficiente;
  - publica `OperationProcessed`/`PositionUpdated` (relay) → mensagem chega na fila.

## 10. Docker & Compose

- `worker-go/Dockerfile` (multi-stage build Go → imagem mínima).
- Serviço `worker` no `docker-compose.yml` (depende de `postgres` e `rabbitmq` saudáveis; env de conexão). Completa o §14 ("compose sobe todo o ambiente").

## 11. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| P3.1 | `feature/outbox-multisource` | Migration (`source` + índice único de posição); publisher .NET filtra `source='api'` (+ SKIP LOCKED); testes ajustados |
| P3.2 | `feature/worker-foundation` | módulo Go, config, pool pgx, consumer RabbitMQ (loga evento), slog, Dockerfile + serviço no compose; smoke testcontainers (conexão/consumo) |
| P3.3 | `feature/worker-rules` | regras puras (suitability, posição, preço médio) + processor com repos fake; unit tests (§13) |
| P3.4 | `feature/worker-persistence` | repos pgx + transação (status + upsert posição + processing_event + outbox `source='worker'`); integração consume→processa→banco |
| P3.5 | `feature/worker-publisher` | relay de outbox do worker + contratos dos eventos downstream; integração publish→RabbitMQ; end-to-end |

**Ordem:** P3.1 → P3.2 → P3.3 → P3.4 → P3.5.

## 12. Fora de escopo (Fase 3)

- Retry com limite, DLQ e evento `OperationFailed` (Fase 5).
- Reprocessamento manual de `Failed` via API (depende de DLQ/Fase 5).
- "Claim-then-publish" para alto volume e multi-instância de relay (Fase 5).
- OpenTelemetry/tracing distribuído completo (Fase 5).
- Frontend (Fase 4).
