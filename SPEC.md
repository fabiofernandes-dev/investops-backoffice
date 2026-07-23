# SPEC.md — InvestOps Backoffice

> **Nota:** este documento é a **visão completa** do produto e serve de norte para o desenvolvimento.
> A entrega é incremental — o que já está implementado vs. planejado está no [README — Status & roadmap](README.md#status--roadmap).

## 1. Visão geral

Criar uma plataforma fullstack de backoffice para investimentos chamada **InvestOps Backoffice**.

O sistema deve simular um ambiente real de operações financeiras, com:

- Backoffice operacional em Angular.
- API principal em .NET 8.
- Worker assíncrono em Go.
- PostgreSQL como banco principal.
- RabbitMQ para mensageria.
- Processamento de operações financeiras.
- Regras de negócio densas.
- Cálculo de posição consolidada.
- Auditoria.
- Monitoramento de falhas.
- Conciliação entre posição interna e custodiante.

Este projeto tem como objetivo demonstrar arquitetura fullstack de nível sênior aplicada a uma plataforma de backoffice de investimentos.

---

## 2. Stack obrigatória

### Frontend

```text
Angular 18+
TypeScript
RxJS
Angular Material
Reactive Forms
Server-side pagination
HTTP Interceptors
Lazy loading
```

### Backend principal

```text
.NET 8
ASP.NET Core Web API
PostgreSQL
Dapper
FluentValidation
Serilog
OpenTelemetry
Health Checks
Swagger/OpenAPI
```

### Worker assíncrono

```text
Go
RabbitMQ
PostgreSQL
pgx
slog ou zap
Docker
```

### Infra

```text
Docker Compose
PostgreSQL
RabbitMQ Management
Redis opcional
```

---

## 3. Arquitetura desejada

A solução terá dois serviços principais:

```text
Angular Frontend
       |
       v
.NET Operations API
       |
       v
PostgreSQL
       |
       v
Outbox Publisher
       |
       v
RabbitMQ
       |
       v
Go Processing Worker
       |
       v
PostgreSQL
```

Para o MVP, será usado **banco compartilhado** entre API .NET e Worker Go.

Decisão arquitetural:

```text
Para reduzir complexidade inicial, o projeto usará banco compartilhado entre a API e o Worker.
A separação será feita por responsabilidade de serviço, contratos de evento e organização de código.
Em uma evolução futura, cada serviço poderia ter seu próprio banco.
```

---

## 4. Estrutura esperada do repositório

```text
investops-backoffice/
  README.md
  SPEC.md
  docker-compose.yml

  docs/
    adr/
      0001-use-modular-monolith-for-api.md
      0002-use-rabbitmq-for-async-processing.md
      0003-use-outbox-pattern.md
      0004-use-go-for-processing-worker.md
      0005-use-postgresql-shared-database-for-mvp.md

  contracts/
    events/
      operation-imported.v1.json
      operation-processed.v1.json
      operation-rejected.v1.json
      operation-failed.v1.json
      position-updated.v1.json

  backend/
    InvestOps.sln
    src/
      InvestOps.Api/
      InvestOps.Application/
      InvestOps.Domain/
      InvestOps.Infrastructure/
      InvestOps.Contracts/
    tests/
      InvestOps.UnitTests/
      InvestOps.IntegrationTests/

  worker-go/
    cmd/
      worker/
        main.go
    internal/
      config/
      database/
      messaging/
      operations/
      positions/
      observability/
    go.mod
    Dockerfile

  frontend/
    investops-web/
```

---

## 5. Domínio

### Customer

Representa o investidor.

```text
Id
Name
DocumentNumber
InvestorProfile: Conservative | Moderate | Aggressive
Status: Active | Blocked
CreatedAt
```

### InvestmentAccount

```text
Id
CustomerId
AccountNumber
Status: Active | Blocked | Closed
CreatedAt
```

### FinancialProduct

```text
Id
ProductType: FixedIncome | Fund | Stock | Derivative
Name
Ticker
RiskLevel
LiquidityDays
IsActive
```

### Operation

```text
Id
ExternalId
AccountId
ProductId
OperationType: Buy | Sell | Deposit | Withdraw | Income | Fee
Quantity
UnitPrice
GrossValue
NetValue
OperationDate
SettlementDate
Status: Imported | Validated | Rejected | Processed | Failed
RejectionReason
CreatedAt
UpdatedAt
```

### PortfolioPosition

```text
Id
AccountId
ProductId
Quantity
AveragePrice
GrossAmount
UpdatedAt
```

### ProcessingEvent

```text
Id
OperationId
EventType
Payload
Status
AttemptCount
ErrorMessage
CreatedAt
ProcessedAt
```

### AuditLog

```text
Id
EntityName
EntityId
Action
OldValue
NewValue
UserId
CreatedAt
```

### OutboxMessage

```text
Id
EventType
Version
Payload
Status: Pending | Published | Failed
Attempts
CreatedAt
PublishedAt
LastError
```

---

## 6. Regras de negócio

### Regra 1 — Suitability

Cliente conservador não pode comprar produto de risco alto.

```text
Conservative permite RiskLevel até 2
Moderate permite RiskLevel até 4
Aggressive permite RiskLevel até 5
```

Se a regra falhar:

```text
Operation.Status = Rejected
RejectionReason = InvestorProfileNotCompatible
```

---

### Regra 2 — Venda com posição insuficiente

Operação do tipo `Sell` não pode deixar posição negativa.

Se o cliente tiver 100 unidades e tentar vender 150:

```text
Operation.Status = Rejected
RejectionReason = InsufficientPosition
```

---

### Regra 3 — Idempotência

`ExternalId` deve ser único.

A mesma operação não pode ser importada nem processada duas vezes.

---

### Regra 4 — Data de liquidação

```text
SettlementDate = OperationDate + FinancialProduct.LiquidityDays
```

Para o MVP, considerar dias corridos. Em uma evolução futura, considerar calendário de dias úteis.

---

### Regra 5 — Preço médio

Em compras:

```text
newAveragePrice =
((currentQuantity * currentAveragePrice) + (newQuantity * newUnitPrice))
/ (currentQuantity + newQuantity)
```

Em vendas:

```text
AveragePrice permanece igual.
Quantity diminui.
GrossAmount = Quantity * AveragePrice.
```

---

### Regra 6 — Auditoria

Ações críticas devem gerar auditoria:

```text
Importação de operação
Reprocessamento
Rejeição
Falha
Resolução de divergência
Ajuste manual
```

---

## 7. Fluxo principal

### Importação de operação

```text
1. Angular envia uma operação para a API .NET.
2. API valida DTO de entrada.
3. API verifica ExternalId duplicado.
4. API salva Operation com status Imported.
5. API grava OutboxMessage do tipo OperationImported na mesma transação.
6. Outbox Publisher publica mensagem no RabbitMQ.
7. Go Worker consome OperationImported.
8. Go Worker carrega cliente, conta, produto e posição atual.
9. Go Worker aplica regras de negócio.
10. Se inválida, marca Operation como Rejected.
11. Se válida, atualiza posição e marca Operation como Processed.
12. Worker grava ProcessingEvent.
13. Angular consulta status atualizado pela API.
```

---

## 8. API endpoints

### Operations

```http
POST /api/operations/import
GET /api/operations
GET /api/operations/{id}
POST /api/operations/{id}/reprocess
GET /api/operations/{id}/events
GET /api/operations/{id}/audit
```

### Positions

```http
GET /api/positions
GET /api/positions/by-account/{accountId}
GET /api/positions/by-customer/{customerId}
```

### Dashboard

```http
GET /api/dashboard/summary
GET /api/dashboard/processing-metrics
```

### Products

```http
GET /api/products
POST /api/products
PUT /api/products/{id}
```

### Customers

```http
GET /api/customers
GET /api/customers/{id}
```

### Reconciliation

```http
POST /api/reconciliations/import
GET /api/reconciliations/divergences
POST /api/reconciliations/divergences/{id}/resolve
POST /api/reconciliations/divergences/{id}/ignore
```

---

## 9. Frontend Angular

Criar módulos/features:

```text
dashboard
operations
operation-detail
positions
reconciliation
processing-monitor
shared
core
```

### Tela Dashboard

Exibir:

```text
Operações importadas hoje
Operações processadas
Operações rejeitadas
Operações com falha
Divergências pendentes
Tempo médio de processamento
```

### Tela Operations

Tabela com:

```text
ExternalId
Cliente
Conta
Produto
Tipo
Quantidade
Valor bruto
Status
Data da operação
Data de liquidação
```

Requisitos:

```text
Paginação server-side
Ordenação server-side
Filtro por status
Filtro por produto
Filtro por cliente
Debounce com RxJS
Loading state
Error state
Botão de detalhe
Botão de reprocessar para operações Failed
```

### Tela Operation Detail

Abas:

```text
Dados da operação
Eventos
Auditoria
Payload original
Erros
```

### Tela Positions

Tabela de posição consolidada:

```text
Cliente
Conta
Produto
Quantidade
Preço médio
Valor bruto
Última atualização
```

### Tela Processing Monitor

Exibir eventos processados pelo worker:

```text
Evento
Operação
Status
Tentativas
Erro
Criado em
Processado em
```

---

## 10. Banco de dados

Criar migrations ou scripts SQL para:

```text
customers
investment_accounts
financial_products
operations
portfolio_positions
processing_events
audit_logs
outbox_messages
reconciliation_imports
reconciliation_divergences
```

Índices obrigatórios:

```sql
CREATE UNIQUE INDEX ux_operations_external_id
ON operations(external_id);

CREATE INDEX idx_operations_status
ON operations(status);

CREATE INDEX idx_operations_operation_date
ON operations(operation_date);

CREATE INDEX idx_operations_account_product
ON operations(account_id, product_id);

CREATE INDEX idx_positions_account_product
ON portfolio_positions(account_id, product_id);

CREATE INDEX idx_processing_events_status_created_at
ON processing_events(status, created_at);

CREATE INDEX idx_outbox_messages_status_created_at
ON outbox_messages(status, created_at);
```

---

## 11. Eventos

### OperationImported v1

```json
{
  "eventId": "uuid",
  "eventType": "OperationImported",
  "version": 1,
  "occurredAt": "2026-06-07T10:00:00Z",
  "correlationId": "uuid",
  "payload": {
    "operationId": "uuid",
    "externalId": "EXT-000001",
    "accountId": "uuid",
    "productId": "uuid",
    "operationType": "Buy",
    "quantity": 100,
    "unitPrice": 25.4,
    "operationDate": "2026-06-07"
  }
}
```

### OperationProcessed v1

```json
{
  "eventId": "uuid",
  "eventType": "OperationProcessed",
  "version": 1,
  "occurredAt": "2026-06-07T10:01:00Z",
  "correlationId": "uuid",
  "payload": {
    "operationId": "uuid",
    "accountId": "uuid",
    "productId": "uuid",
    "quantity": 100,
    "averagePrice": 25.4,
    "grossAmount": 2540.0
  }
}
```

### OperationRejected v1

```json
{
  "eventId": "uuid",
  "eventType": "OperationRejected",
  "version": 1,
  "occurredAt": "2026-06-07T10:01:00Z",
  "correlationId": "uuid",
  "payload": {
    "operationId": "uuid",
    "reason": "InvestorProfileNotCompatible"
  }
}
```

---

## 12. Docker Compose

O projeto deve subir localmente com:

```text
PostgreSQL
RabbitMQ Management
Operations API
Go Worker
Angular App
```

Portas sugeridas:

```text
Angular: 4200
.NET API: 5000
PostgreSQL: 5432
RabbitMQ: 5672
RabbitMQ Management: 15672
```

---

## 13. Testes obrigatórios

### Backend .NET

Criar testes unitários para:

```text
Suitability validation
Insufficient position validation
Average price calculation
Duplicate ExternalId validation
Settlement date calculation
```

Criar testes de integração para:

```text
Import operation
Create outbox message
List operations with pagination
Reprocess failed operation
```

### Worker Go

Criar testes para:

```text
Consume OperationImported event
Reject operation by investor profile
Reject sell operation with insufficient position
Process buy operation successfully
Update portfolio position
```

### Angular

Criar testes para:

```text
Operations list renders
Filter emits request with debounce
Reprocess button appears only for Failed status
Error state appears when API fails
```

---

## 14. Critérios de aceite do MVP

O MVP estará pronto quando:

```text
Docker Compose sobe todo o ambiente.
Angular acessa a API.
API importa operação.
API grava OutboxMessage.
Mensagem chega ao RabbitMQ.
Go Worker consome mensagem.
Worker processa operação.
Worker atualiza status da operação.
Worker atualiza posição consolidada.
Tela Angular mostra operação atualizada.
Tela Angular mostra posição consolidada.
Testes principais passam.
README explica como rodar o projeto.
```

---

## 15. Restrições importantes

```text
Não criar CRUD genérico sem regra.
Não colocar toda lógica nos controllers.
Não deixar regra de domínio espalhada.
Não usar banco em memória.
Não ignorar idempotência.
Não ignorar logs.
Não criar microservices demais.
Não implementar autenticação complexa no MVP.
Não usar frontend fake sem integração real.
```

---

## 16. Ordem recomendada de execução

### Fase 1 — Fundação

```text
Infra local
Solução .NET
Projetos base
Health check
Swagger
Conexão PostgreSQL
Scripts SQL iniciais
```

### Fase 2 — Primeiro fluxo

```text
Importação de operação
Validação
Idempotência por ExternalId
OutboxMessage
Teste unitário
Teste de integração
```

### Fase 3 — Worker Go

```text
Consumo RabbitMQ
Processamento de operação
Regras de negócio
Atualização de posição
Eventos de processamento
```

### Fase 4 — Angular

```text
Shell da aplicação
Tela de operações
Tela de detalhe
Tela de posições
Integração real com API
```

### Fase 5 — Prontidão para produção

```text
Logs estruturados
CorrelationId
Retry
DLQ
Auditoria
README arquitetural
ADRs
Testes principais
```

---

## 17. Observações finais

Este projeto deve priorizar clareza arquitetural e boas decisões técnicas.

O mais importante é conseguir explicar:

```text
Por que existem dois serviços.
Por que Go foi usado no worker.
Por que a API ficou em .NET.
Por que foi usado Outbox Pattern.
Como a idempotência foi tratada.
Como o sistema lida com falhas.
Como consultas pesadas foram otimizadas.
Como o Angular consome dados de forma performática.
```
