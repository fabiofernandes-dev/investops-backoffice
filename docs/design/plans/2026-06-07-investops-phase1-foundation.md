# InvestOps Backoffice — Fase 1 (Fundação) Implementation Plan

**Goal:** Estabelecer a fundação do InvestOps Backoffice — infra local (Docker), esqueleto da solução .NET 8, API com health/Swagger/Serilog, modelo de domínio e migrations — sem nenhum fluxo de negócio.

**Architecture:** Modular monolith em .NET 8 (Clean Architecture: Domain ← Application ← Infrastructure; Api como composition root; Contracts para DTOs de evento). Persistência PostgreSQL via Dapper; schema versionado com DbUp. Mensageria (RabbitMQ) sobe no Compose mas só é consumida a partir da Fase 3.

**Tech Stack:** .NET 8, ASP.NET Core, Dapper, Npgsql, DbUp, Serilog, Swashbuckle, xUnit, Docker Compose, PostgreSQL 16, RabbitMQ 3.13-management.

---

## Pré-condições (T0 — concluída)

T0 (`chore/repo-bootstrap`) **já foi executada**: repo git inicializado, branches `main` e `developer` criadas e publicadas em `github.com/fabiofernandes-dev/investops-backoffice` (privado, default `developer`), com `.gitignore`/`.editorconfig`/`.gitattributes`, README inicial e estrutura de pastas. Logo, `git checkout developer` e `gh pr create` já têm base. As regras de `.gitattributes` incluem `*.sql text eol=lf` (relevante para a estabilidade do hash dos scripts DbUp na T5).

## Convenções de execução (todas as tasks)

- **Ambiente de execução:** os comandos abaixo são rodados pelo **Bash tool (git-bash)** — paths estilo `/c/Projects/Investor` e `/dev/null` são válidos nesse contexto. (No PowerShell use `C:\Projects\Investor` e `$null`.)
- Cada task = uma branch `feature/*` criada **a partir de `developer`** → PR para `developer` → revisão do usuário → merge.
- Antes de cada task: `git -C /c/Projects/Investor checkout developer && git -C /c/Projects/Investor pull`.
- Commits: Conventional Commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- Comandos `dotnet`/`git` usam `-C`/`--project` para evitar `cd`. `global.json` (T2) garante SDK 8 em toda a subárvore `backend/`.
- Cadência padrão: implemento a task, abro o PR e **pauso para sua revisão** antes da próxima.

## File Structure (visão geral da Fase 1)

```text
docker-compose.yml            # T1 — Postgres + RabbitMQ
.env.example                  # T1 — variáveis (porta/credenciais)
backend/
  global.json                 # T2 — pin SDK 8.0.421
  Directory.Build.props       # T2 — net8.0, nullable, warnings
  InvestOps.sln               # T2
  src/
    InvestOps.Domain/         # T4 — Enums/ + Entities/ (POCOs)
    InvestOps.Contracts/      # T2 — placeholder (DTOs de evento na Fase 2)
    InvestOps.Application/    # T2 — placeholder (use cases na Fase 2)
    InvestOps.Infrastructure/ # T2 — placeholder (Dapper/repos na Fase 2)
    InvestOps.Api/            # T3 — Program.cs, appsettings, health, swagger
    InvestOps.Migrator/       # T5 — DbUp + Scripts/Script0001_initial_schema.sql
  tests/
    InvestOps.UnitTests/      # T4 — smoke de enums/entidades
    InvestOps.IntegrationTests/ # T3 — /health/live via WebApplicationFactory
docs/adr/0001..0005-*.md      # T6
README.md                     # T6 — "como rodar localmente"
```

---

## Chunk 1: Infraestrutura e esqueleto .NET

### Task T1: Docker Compose (PostgreSQL + RabbitMQ)

**Files:**
- Create: `docker-compose.yml`
- Create: `.env.example`

- [ ] **Step 1: Criar branch**

```bash
git -C /c/Projects/Investor checkout developer
git -C /c/Projects/Investor checkout -b feature/infra-compose
```

- [ ] **Step 2: Escrever `.env.example`**

```dotenv
# PostgreSQL
POSTGRES_USER=investops
POSTGRES_PASSWORD=investops
POSTGRES_DB=investops
POSTGRES_PORT=5432

# RabbitMQ
RABBITMQ_USER=investops
RABBITMQ_PASSWORD=investops
RABBITMQ_PORT=5672
RABBITMQ_MGMT_PORT=15672
```

- [ ] **Step 3: Escrever `docker-compose.yml`**

```yaml
name: investops

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-investops}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-investops}
      POSTGRES_DB: ${POSTGRES_DB:-investops}
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-investops} -d ${POSTGRES_DB:-investops}"]
      interval: 5s
      timeout: 5s
      retries: 10

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER:-investops}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:-investops}
    ports:
      - "${RABBITMQ_PORT:-5672}:5672"
      - "${RABBITMQ_MGMT_PORT:-15672}:15672"
    volumes:
      - rabbitmqdata:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 10

volumes:
  pgdata:
  rabbitmqdata:
```

- [ ] **Step 4: Validar a sintaxe do compose**

Run: `docker compose -f /c/Projects/Investor/docker-compose.yml --env-file /c/Projects/Investor/.env.example config`
Expected: imprime a config resolvida, sem erro.

- [ ] **Step 5: Subir e verificar saúde**

Run: `docker compose --env-file .env.example up -d` (a partir de `c:\Projects\Investor`)
Then: `docker compose ps`
Expected: ambos os serviços `running (healthy)` após alguns segundos.
Verificar RabbitMQ UI: `curl -s -o /dev/null -w "%{http_code}" http://localhost:15672` → `200`.
Verificar Postgres: `docker compose exec -T postgres pg_isready -U investops` → `accepting connections`.

- [ ] **Step 6: Derrubar (deixar limpo)**

Run: `docker compose down`

- [ ] **Step 7: Commit + push + PR**

```bash
git -C /c/Projects/Investor add docker-compose.yml .env.example
git -C /c/Projects/Investor commit -m "feat(infra): docker-compose com PostgreSQL e RabbitMQ" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git -C /c/Projects/Investor push -u origin feature/infra-compose
gh pr create --base developer --head feature/infra-compose --title "feat(infra): docker-compose (PostgreSQL + RabbitMQ)" --body "<resumo + como testar>"
```
**PAUSA para revisão do usuário.**

---

### Task T2: Esqueleto da solução .NET 8

**Files:**
- Create: `backend/global.json`, `backend/Directory.Build.props`, `backend/InvestOps.sln`
- Create: 6 projetos `src/*` + 2 projetos `tests/*`

- [ ] **Step 1: Branch**

```bash
git -C /c/Projects/Investor checkout developer && git -C /c/Projects/Investor pull
git -C /c/Projects/Investor checkout -b feature/backend-solution
```

- [ ] **Step 2: `backend/global.json`** (garante SDK 8 em toda a subárvore)

```json
{
  "sdk": {
    "version": "8.0.421",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 3: `backend/Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Criar solução e projetos** (rodar em `backend/`; cada `dotnet new` sem `-f` herda net8.0 do `Directory.Build.props` + global.json)

```bash
cd /c/Projects/Investor/backend  # apenas para os comandos dotnet new desta task
dotnet new sln -n InvestOps
dotnet new classlib -o src/InvestOps.Domain
dotnet new classlib -o src/InvestOps.Contracts
dotnet new classlib -o src/InvestOps.Application
dotnet new classlib -o src/InvestOps.Infrastructure
dotnet new web        -o src/InvestOps.Api
dotnet new xunit      -o tests/InvestOps.UnitTests
dotnet new xunit      -o tests/InvestOps.IntegrationTests
```
Remover boilerplate gerado pelos templates (garante o "0 Warning(s)" do Step 7):

```bash
rm -f src/InvestOps.Domain/Class1.cs src/InvestOps.Contracts/Class1.cs \
      src/InvestOps.Application/Class1.cs src/InvestOps.Infrastructure/Class1.cs \
      tests/InvestOps.UnitTests/UnitTest1.cs tests/InvestOps.IntegrationTests/UnitTest1.cs
```

- [ ] **Step 5: Adicionar projetos à solução**

```bash
dotnet sln add src/InvestOps.Domain src/InvestOps.Contracts src/InvestOps.Application src/InvestOps.Infrastructure src/InvestOps.Api tests/InvestOps.UnitTests tests/InvestOps.IntegrationTests
```

- [ ] **Step 6: Referências entre projetos** (direção da dependência da Clean Architecture)

```bash
dotnet add src/InvestOps.Application reference src/InvestOps.Domain src/InvestOps.Contracts
dotnet add src/InvestOps.Infrastructure reference src/InvestOps.Application
dotnet add src/InvestOps.Api reference src/InvestOps.Application src/InvestOps.Infrastructure
dotnet add tests/InvestOps.UnitTests reference src/InvestOps.Domain src/InvestOps.Application
dotnet add tests/InvestOps.IntegrationTests reference src/InvestOps.Api
```

- [ ] **Step 7: Build limpo**

Run: `dotnet build /c/Projects/Investor/backend/InvestOps.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Commit + push + PR** (mensagem `feat(backend): esqueleto da solução .NET 8`) → **PAUSA**.

---

## Chunk 2: API e domínio

### Task T3: Fundação da API (Serilog + Swagger + Health Checks)

**Files:**
- Modify: `backend/src/InvestOps.Api/Program.cs`
- Modify: `backend/src/InvestOps.Api/appsettings.json` (template `dotnet new web` já cria `appsettings.json` + `appsettings.Development.json`; sobrescrevemos só o primeiro, mantendo o Development padrão)
- Test: `backend/tests/InvestOps.IntegrationTests/HealthEndpointTests.cs`

- [ ] **Step 1: Branch** `feature/api-foundation` a partir de `developer`.

- [ ] **Step 2: Pacotes NuGet** (versões 8.x p/ net8.0)

```bash
cd /c/Projects/Investor/backend
dotnet add src/InvestOps.Api package Serilog.AspNetCore
dotnet add src/InvestOps.Api package Swashbuckle.AspNetCore
dotnet add src/InvestOps.Api package AspNetCore.HealthChecks.NpgSql
dotnet add src/InvestOps.Api package Npgsql
dotnet add tests/InvestOps.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 3: Escrever o teste de integração PRIMEIRO (TDD)** — liveness não depende de banco (hermético)

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InvestOps.IntegrationTests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Liveness_returns_healthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Rodar o teste e ver FALHAR**

Run: `dotnet test backend/tests/InvestOps.IntegrationTests`
Expected: falha de compilação (`Program` inacessível) — confirma o vermelho.

- [ ] **Step 5: `appsettings.json`**

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=investops;Username=investops;Password=investops"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "investops",
    "Password": "investops"
  },
  "Serilog": {
    "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.AspNetCore": "Warning" } },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 6: `Program.cs`** (liveness sem DB; readiness com Npgsql; Swagger; Serilog)

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });

app.Run();

public partial class Program { }
```

- [ ] **Step 7: Rodar o teste e ver PASSAR**

Run: `dotnet test backend/tests/InvestOps.IntegrationTests`
Expected: PASS (1 teste).

- [ ] **Step 8: Smoke manual do Swagger/health (opcional, com compose no ar)**

Run: `dotnet run --project backend/src/InvestOps.Api` → abrir `/swagger` e `GET /health/live`.

- [ ] **Step 9: Commit + push + PR** (`feat(api): Serilog, Swagger e health checks`) → **PAUSA**.

---

### Task T4: Modelo de domínio (enums + entidades)

**Files:**
- Create: `backend/src/InvestOps.Domain/Enums/*.cs` (InvestorProfile, CustomerStatus, AccountStatus, ProductType, OperationType, OperationStatus, RejectionReason, OutboxStatus)
- Create: `backend/src/InvestOps.Domain/Entities/*.cs` (Customer, InvestmentAccount, FinancialProduct, Operation, PortfolioPosition, OutboxMessage, ProcessingEvent, AuditLog)
- Test: `backend/tests/InvestOps.UnitTests/DomainEnumsTests.cs`

> Fase 1 mantém entidades como POCOs fiéis ao SPEC §5 (sem regras — regras chegam nas Fases 2/3). Os enums centralizam o vocabulário do domínio e são a parte com teste de regressão barato.

- [ ] **Step 1: Branch** `feature/domain-model`.

- [ ] **Step 2: Teste PRIMEIRO** — garante o vocabulário dos enums do SPEC

```csharp
using InvestOps.Domain.Enums;

namespace InvestOps.UnitTests;

public class DomainEnumsTests
{
    [Theory]
    [InlineData(nameof(InvestorProfile.Conservative))]
    [InlineData(nameof(InvestorProfile.Moderate))]
    [InlineData(nameof(InvestorProfile.Aggressive))]
    public void InvestorProfile_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(InvestorProfile), name));

    [Theory]
    [InlineData("Imported")] [InlineData("Validated")] [InlineData("Rejected")]
    [InlineData("Processed")] [InlineData("Failed")]
    public void OperationStatus_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(OperationStatus), name));
}
```

- [ ] **Step 3: Rodar e ver FALHAR** (`dotnet test backend/tests/InvestOps.UnitTests`) — enums ainda não existem.

- [ ] **Step 4: Criar enums** (exemplo; demais análogos ao SPEC §5)

```csharp
namespace InvestOps.Domain.Enums;

public enum InvestorProfile { Conservative, Moderate, Aggressive }
public enum CustomerStatus { Active, Blocked }
public enum AccountStatus { Active, Blocked, Closed }
public enum ProductType { FixedIncome, Fund, Stock, Derivative }
public enum OperationType { Buy, Sell, Deposit, Withdraw, Income, Fee }
public enum OperationStatus { Imported, Validated, Rejected, Processed, Failed }
public enum RejectionReason { None, InvestorProfileNotCompatible, InsufficientPosition, DuplicateExternalId }
public enum OutboxStatus { Pending, Published, Failed }
```

- [ ] **Step 5: Criar entidades** (exemplo `Operation`; demais fiéis ao SPEC §5)

```csharp
using InvestOps.Domain.Enums;

namespace InvestOps.Domain.Entities;

public class Operation
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = default!;
    public Guid AccountId { get; set; }
    public Guid ProductId { get; set; }
    public OperationType OperationType { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GrossValue { get; set; }
    public decimal NetValue { get; set; }
    public DateOnly OperationDate { get; set; }
    public DateOnly SettlementDate { get; set; }
    public OperationStatus Status { get; set; }
    public RejectionReason? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 6: Rodar e ver PASSAR** (`dotnet test backend/tests/InvestOps.UnitTests`).

- [ ] **Step 7: Commit + push + PR** (`feat(domain): enums e entidades do domínio`) → **PAUSA**.

---

## Chunk 3: Persistência e documentação

### Task T5: Migrations com DbUp

**Files:**
- Create: `backend/src/InvestOps.Migrator/` (console), `Program.cs`, `Scripts/Script0001_initial_schema.sql`
- Modify: `backend/InvestOps.sln` (add Migrator)

- [ ] **Step 1: Branch** `feature/db-migrations`.

- [ ] **Step 2: Projeto migrator + pacote**

```bash
cd /c/Projects/Investor/backend
dotnet new console -o src/InvestOps.Migrator
dotnet sln add src/InvestOps.Migrator
dotnet add src/InvestOps.Migrator package dbup-postgresql
```

- [ ] **Step 3: `Program.cs` do migrator** (scripts embutidos; connection string por arg/env)

```csharp
using System.Reflection;
using DbUp;

var connectionString =
    args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("INVESTOPS_DB")
    ?? "Host=localhost;Port=5432;Database=investops;Username=investops;Password=investops";

var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}
Console.WriteLine("Migrations aplicadas com sucesso.");
return 0;
```

- [ ] **Step 4: `.csproj` do migrator** — embutir os `.sql`

```xml
<ItemGroup>
  <EmbeddedResource Include="Scripts\*.sql" />
</ItemGroup>
```

- [ ] **Step 5: `Scripts/Script0001_initial_schema.sql`** — 10 tabelas (§10) + 7 índices

```sql
CREATE TABLE customers (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    document_number text NOT NULL,
    investor_profile text NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE investment_accounts (
    id uuid PRIMARY KEY,
    customer_id uuid NOT NULL REFERENCES customers(id),
    account_number text NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE financial_products (
    id uuid PRIMARY KEY,
    product_type text NOT NULL,
    name text NOT NULL,
    ticker text,
    risk_level smallint NOT NULL,
    liquidity_days int NOT NULL,
    is_active boolean NOT NULL DEFAULT true
);

CREATE TABLE operations (
    id uuid PRIMARY KEY,
    external_id text NOT NULL,
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    operation_type text NOT NULL,
    quantity numeric(18,6) NOT NULL,
    unit_price numeric(18,6) NOT NULL,
    gross_value numeric(18,2) NOT NULL,
    net_value numeric(18,2) NOT NULL,
    operation_date date NOT NULL,
    settlement_date date NOT NULL,
    status text NOT NULL,
    rejection_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE portfolio_positions (
    id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    quantity numeric(18,6) NOT NULL,
    average_price numeric(18,6) NOT NULL,
    gross_amount numeric(18,2) NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE processing_events (
    id uuid PRIMARY KEY,
    operation_id uuid NOT NULL REFERENCES operations(id),
    event_type text NOT NULL,
    payload jsonb,
    status text NOT NULL,
    attempt_count int NOT NULL DEFAULT 0,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz
);

CREATE TABLE audit_logs (
    id uuid PRIMARY KEY,
    entity_name text NOT NULL,
    entity_id text NOT NULL,
    action text NOT NULL,
    old_value jsonb,
    new_value jsonb,
    user_id text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE outbox_messages (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    version int NOT NULL,
    payload jsonb NOT NULL,
    status text NOT NULL,
    attempts int NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    last_error text
);

CREATE TABLE reconciliation_imports (
    id uuid PRIMARY KEY,
    source text NOT NULL,
    reference_date date NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE reconciliation_divergences (
    id uuid PRIMARY KEY,
    reconciliation_import_id uuid NOT NULL REFERENCES reconciliation_imports(id),
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    internal_quantity numeric(18,6) NOT NULL,
    custodian_quantity numeric(18,6) NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz
);

CREATE UNIQUE INDEX ux_operations_external_id ON operations(external_id);
CREATE INDEX idx_operations_status ON operations(status);
CREATE INDEX idx_operations_operation_date ON operations(operation_date);
CREATE INDEX idx_operations_account_product ON operations(account_id, product_id);
CREATE INDEX idx_positions_account_product ON portfolio_positions(account_id, product_id);
CREATE INDEX idx_processing_events_status_created_at ON processing_events(status, created_at);
CREATE INDEX idx_outbox_messages_status_created_at ON outbox_messages(status, created_at);
```

- [ ] **Step 6: Aplicar contra o Postgres do Compose e verificar**

Run: `docker compose --env-file .env.example up -d postgres`
Then: `dotnet run --project backend/src/InvestOps.Migrator`
Expected: `Migrations aplicadas com sucesso.`
Verificar tabelas: `docker compose exec -T postgres psql -U investops -d investops -c "\dt"` → 10 tabelas + `schemaversions`.
Verificar índices: `... -c "\di"` → 7 índices nomeados + PKs.
Idempotência: rodar o migrator de novo → "No new scripts need to be executed".

> **Nota de teste:** verificação de migrations é manual nesta fase (DbUp é idempotente e rastreia `schemaversions`). Teste automatizado de schema com **Testcontainers** entra junto da suíte de integração da Fase 2.

- [ ] **Step 7: Commit + push + PR** (`feat(db): schema inicial via DbUp`) → **PAUSA**.

---

### Task T6: Documentação (README + ADRs)

**Files:**
- Modify: `README.md` (seção "Como rodar localmente")
- Create: `docs/adr/0001-use-modular-monolith-for-api.md` … `0005-use-postgresql-shared-database-for-mvp.md`

- [ ] **Step 1: Branch** `feature/docs-foundation`.

- [ ] **Step 2: README — passos reais validados nas tasks anteriores**

Conteúdo: pré-requisitos (.NET 8 SDK, Docker), `docker compose up -d`, `dotnet run --project backend/src/InvestOps.Migrator`, `dotnet run --project backend/src/InvestOps.Api`, URLs (`/swagger`, `/health`), `dotnet test`, tabela de portas (4200/5000/5432/5672/15672).

- [ ] **Step 3: ADRs** — formato consistente (Status / Context / Decision / Consequences)

```markdown
# 1. Modular monolith para a API

Status: Accepted

## Context
<por que não microservices desde o início>

## Decision
<API .NET como modular monolith>

## Consequences
<prós/contras + caminho de evolução>
```

Repetir para: 0002 RabbitMQ (processamento assíncrono), 0003 Outbox Pattern, 0004 Go no worker, 0005 PostgreSQL compartilhado no MVP (todos derivados do SPEC §3, §4, §17).

- [ ] **Step 4: Conferir critérios de aceite (SPEC §14)** — checklist do design doc §6 todo marcável.

- [ ] **Step 5: Commit + push + PR** (`docs: README de execução e ADRs 0001–0005`) → **PAUSA**.

---

## Fechamento da Fase 1

Após T6 mergeada em `developer`: abrir PR `developer → main` (release da Fase 1) e reavaliar antes da Fase 2 (primeiro fluxo: importação de operação).
