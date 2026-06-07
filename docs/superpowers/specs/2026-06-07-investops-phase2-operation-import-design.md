# Design — InvestOps Backoffice (Fase 2: Importação de Operação)

**Data:** 2026-06-07
**Status:** Aprovado (início da Fase 2)
**Spec de produto:** [SPEC.md](../../../SPEC.md) (§5, §6, §7, §8, §11, §13, §17)
**Design da fundação:** [2026-06-07-investops-foundation-design.md](2026-06-07-investops-foundation-design.md)

A Fase 2 implementa o **primeiro fluxo de negócio**: importar uma operação pela API .NET, com validação, idempotência, gravação transacional de `Operation` + `OutboxMessage` + `AuditLog`, **publisher** que entrega os eventos ao RabbitMQ, e o lado de **leitura** (consulta por id e lista paginada). As regras de suitability/posição ficam no worker (Fase 3).

---

## 1. Decisões (confirmadas)

| Tema | Decisão | Motivo |
|------|---------|--------|
| Outbox | Gravar **+ publisher** (outbox → RabbitMQ) | Completa o lado produtor; MVP §14 exige "mensagem chega ao RabbitMQ". |
| Casos de uso | **Serviços simples** (sem MediatR) | Menos indireta, mais legível/testável. |
| Endpoints | `POST import` + `GET /{id}` + **lista paginada** | Slice coeso; cobre testes §13 e destrava o frontend. |
| Duplicado | **Idempotente simples**: mesmo `ExternalId` → `200` com a operação existente | `ExternalId` é a chave de idempotência (Regra 3); torna retries/redelivery seguros. |
| Auditoria | **AuditLog mínimo no import** (mesma transação) | Regra 6 (importação gera auditoria). Tela de auditoria fica p/ Fase 5. |
| Testes integração | **Testcontainers** (Postgres + RabbitMQ efêmeros) | Hermético/reproduzível; não depende do compose no ar. |
| Suitability/posição | **Fora da Fase 2** (vão no worker, Fase 3) | SPEC §7: API só importa (status `Imported`); regras no worker. |

## 2. Arquitetura & componentes

Preenche os projetos vazios da Fase 1, respeitando a direção da Clean Architecture.

```text
Api            Controllers finos + FluentValidation
   │           OperationsController: POST /import, GET /{id}, GET (lista)
   ▼
Application    Serviços de caso de uso (injetados):
   │             ImportOperationHandler, GetOperationByIdHandler, ListOperationsHandler
   │           DTOs de request/response; interfaces de repositório e de bus
   ▼
Domain         Entidades/enums (já existem) + cálculos de domínio
   ▲             OperationFactory/cálculo: GrossValue, NetValue, SettlementDate
Infrastructure Dapper repos + IDbConnectionFactory + OutboxPublisher (BackgroundService) + RabbitMQ
Contracts      OperationImportedV1 (envelope + payload) ↔ contracts/events/operation-imported.v1.json
```

**Onde cada coisa vive (arquivos principais):**

```text
contracts/events/operation-imported.v1.json        # contrato neutro do evento (§11)

backend/src/InvestOps.Contracts/
  Events/EventEnvelope.cs                            # eventId, eventType, version, occurredAt, correlationId, payload<T>
  Events/OperationImportedV1.cs                      # payload do evento

backend/src/InvestOps.Domain/
  Operations/OperationCalculator.cs                  # GrossValue/NetValue/SettlementDate (lógica pura, testável)

backend/src/InvestOps.Application/
  Abstractions/IDbConnectionFactory.cs
  Abstractions/IOperationRepository.cs, IFinancialProductRepository.cs,
               IInvestmentAccountRepository.cs, IOutboxRepository.cs, IAuditRepository.cs
  Operations/ImportOperation/ImportOperationRequest.cs, ImportOperationResult.cs,
             ImportOperationHandler.cs, ImportOperationValidator.cs
  Operations/GetOperationById/GetOperationByIdHandler.cs, OperationDto.cs
  Operations/ListOperations/ListOperationsQuery.cs, ListOperationsHandler.cs, PagedResult.cs

backend/src/InvestOps.Infrastructure/
  Persistence/NpgsqlConnectionFactory.cs
  Persistence/Dapper/EnumStringHandlers.cs           # mapeia enums ↔ text
  Persistence/Repositories/*.cs                      # implementações Dapper
  Messaging/RabbitMqConnection.cs, RabbitMqTopology.cs
  Messaging/OutboxPublisher.cs                       # BackgroundService
  DependencyInjection.cs                             # AddInfrastructure(IServiceCollection, IConfiguration)

backend/src/InvestOps.Api/
  Operations/OperationsController.cs
  Program.cs                                         # registra Application + Infrastructure + validação + publisher
```

**Acesso a dados:** `IDbConnectionFactory` abre `NpgsqlConnection`. Repositórios usam **Dapper**. A transação do import é controlada no `ImportOperationHandler` (uma `NpgsqlTransaction` compartilhada entre operation/outbox/audit). Leituras abrem conexão própria.

**Pontos de mapeamento (atenção na implementação):**
- **Enums ↔ `text`:** handlers de Dapper convertem enum para string e vice-versa. O handler de `RejectionReason?` deve mapear **`NULL` do banco ↔ `null` em C#** (e nunca escrever a string `"None"`); no import, `rejection_reason` fica **SQL NULL**.
- **Colunas `jsonb`** (`outbox_messages.payload`, `audit_logs.old_value/new_value`): inserir uma `string` direto falha (`42804: column is of type jsonb but expression is of type text`). Usar **cast explícito** (`@Payload::jsonb`) ou parâmetro tipado `NpgsqlDbType.Jsonb`. Leitura de `jsonb` para `string` funciona normalmente.

## 3. Fluxo do import (`POST /api/operations/import`)

Request:
```json
{ "externalId": "EXT-000001", "accountId": "uuid", "productId": "uuid",
  "operationType": "Buy", "quantity": 100, "unitPrice": 25.40, "operationDate": "2026-06-07" }
```

1. **Validação de formato** (FluentValidation): `externalId` não vazio; `accountId`/`productId` não vazios; `operationType` válido; `quantity > 0`; `unitPrice >= 0`; `operationDate` válida.
2. **Idempotência:** busca operação por `ExternalId`. Se existe → retorna `200` com a operação existente (não cria nada).
3. **Validação referencial:** conta e produto existem? Se não → `422` (erro claro em vez de violar FK).
4. **Cálculo de domínio** (`OperationCalculator`):
   - `GrossValue = Quantity * UnitPrice`
   - `NetValue = GrossValue` (sem taxas no import do MVP)
   - `SettlementDate = OperationDate + product.LiquidityDays` (dias corridos — Regra 4)
5. **Transação** (uma só): insere `Operation` (status `Imported`, `RejectionReason = null`, timestamps) + `OutboxMessage` (`OperationImported` v1; payload = envelope JSON com `eventId`, `occurredAt`, `correlationId`; status `Pending`) + `AuditLog` (`EntityName=Operation`, `Action=Imported`).
6. Resposta `201` com `{ id, status }` (e `Location` para `GET /{id}`).

**CorrelationId:** lido do header `X-Correlation-Id` se presente; senão gerado. Embutido no envelope do evento e nos logs (Serilog). **Não** é persistido como coluna na Fase 2 — vive no payload do evento + escopo de log.

## 4. Outbox Publisher

`OutboxPublisher : BackgroundService` (registrado no host da API):

- Loop com intervalo curto (ex.: 1–2s) — configurável via `appsettings`.
- Lê `outbox_messages WHERE status='Pending' ORDER BY created_at LIMIT N` (usa `idx_outbox_messages_status_created_at`).
- Publica cada mensagem no RabbitMQ: exchange **`investops`** (tipo `topic`, durável), routing key = `eventType` (ex.: `OperationImported`), mensagem persistente.
- Sucesso → `status='Published'`, `published_at=now()`. Falha → `attempts++`, `last_error`, permanece `Pending` (retry no próximo ciclo). DLQ/limite de tentativas fica para a Fase 5.
- Topologia: o publisher **declara o exchange**; a fila e o binding do consumidor são declarados pelo worker (Fase 3).
- **Premissa:** uma **única instância** do publisher (roda no host da API). Se um dia houver múltiplas, o poll precisará de `FOR UPDATE SKIP LOCKED` — fora do escopo agora.

## 5. Leitura

- `GET /api/operations/{id}` → `OperationDto` ou `404`.
- `GET /api/operations` → paginação/ordenação/filtros **server-side**:
  - Query: `page` (1-based, default 1), `pageSize` (default 20, máx 100), `sort` (ex.: `operationDate:desc`), filtros `status`, `productId`, `customerId`.
  - **ORDER BY com whitelist** de colunas permitidas (anti-SQL-injection); filtro `status` validado contra `OperationStatus`.
  - `customerId` exige join `operations → investment_accounts`.
  - Retorna `PagedResult { items, total, page, pageSize }` (total via `COUNT(*)` com os mesmos filtros).

## 6. Tratamento de erros

| Situação | HTTP |
|----------|------|
| Validação de formato falha | 400 (ProblemDetails com erros) |
| Conta/produto inexistente | 422 |
| Operação não encontrada (GET) | 404 |
| ExternalId já existe | 200 (idempotente, retorna existente) |
| Erro inesperado | 500 (logado via Serilog) |

## 7. Testes (SPEC §13)

**Unit** (`InvestOps.UnitTests`):
- `OperationCalculator`: gross value; settlement date (operationDate + liquidityDays).
- `ImportOperationHandler` (repos/bus mockados): duplicado retorna existente; conta/produto inexistente → erro; caminho feliz monta Operation+Outbox+Audit corretos.
- `ImportOperationValidator`: casos inválidos.

**Integração** (`InvestOps.IntegrationTests`, **Testcontainers**):
- Import grava `Operation` (Imported) **e** `OutboxMessage` na mesma transação.
- Import idempotente: 2ª chamada com mesmo `ExternalId` não cria duplicado, retorna a existente.
- Lista com paginação/filtros retorna o conjunto certo.
- Reprocess de operação `Failed` — **não** nesta fase (SPEC lista, mas depende do worker; fica p/ Fase 3).
- Publisher: mensagem `Pending` é publicada e marcada `Published` (RabbitMQ via Testcontainers; consome da fila pra assertar).

## 8. Quebra em tasks (PRs)

| # | Branch | Entrega | Pronto quando |
|---|--------|---------|---------------|
| P2.1 | `feature/data-access` | `IDbConnectionFactory`, `NpgsqlConnectionFactory`, handlers de enum↔text, interfaces de repo, `AddInfrastructure` DI | build limpo; teste de fumaça de conexão (Testcontainers) lê `now()` |
| P2.2 | `feature/operation-import` | contrato do evento (json + C#), `OperationCalculator`, `ImportOperationHandler` + validator + repos (operation/product/account/outbox/audit) + transação + `POST /import` | unit (gross/settlement/duplicado) + integração (operation+outbox gravados; idempotência) verdes |
| P2.3 | `feature/outbox-publisher` | `OutboxPublisher` + RabbitMQ (connection/topology) + registro no host | integração (Testcontainers RabbitMQ): mensagem publicada e marcada `Published` |
| P2.4 | `feature/operations-read` | `GET /{id}` + lista paginada/ordenação/filtros + repos de leitura | integração: GET by id (200/404) e lista paginada/filtrada |

**Ordem:** P2.1 → P2.2 → P2.3 → P2.4. Cada uma é um PR para `developer`, revisado por você.

## 9. Fora de escopo (Fase 2)

- Regras de suitability e posição insuficiente (worker, Fase 3).
- Preço médio / atualização de posição (worker, Fase 3).
- Reprocessamento de operação `Failed` (depende do worker).
- Retry/DLQ avançado, tela de auditoria, OpenTelemetry (Fase 5).
- Endpoints de products/customers/positions/reconciliation/dashboard (fases próprias).
