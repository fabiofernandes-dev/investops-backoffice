# InvestOps Backoffice — Fase 2 (Importação de Operação) Implementation Plan

**Goal:** Implementar o primeiro fluxo de negócio — importar uma operação pela API .NET (validação, idempotência, gravação transacional de Operation+OutboxMessage+AuditLog), publicar os eventos no RabbitMQ via um Outbox Publisher, e expor a leitura (GET by id + lista paginada).

**Architecture:** Clean Architecture preenchendo os projetos da Fase 1. Controllers finos → serviços de caso de uso (Application) → repositórios Dapper (Infrastructure). Transação única no handler de import. `OutboxPublisher` (BackgroundService) entrega eventos ao RabbitMQ. Testes de integração com Testcontainers (Postgres/RabbitMQ efêmeros) usando as migrations reais.

**Tech Stack:** .NET 8, Dapper, Npgsql, FluentValidation, RabbitMQ.Client, xUnit, Testcontainers, PostgreSQL, RabbitMQ.

**Spec:** [docs/design/specs/2026-06-07-investops-phase2-operation-import-design.md](../specs/2026-06-07-investops-phase2-operation-import-design.md)

---

## Convenções de execução

- Branch `feature/*` a partir de `developer`; PR para `developer`; revisão do usuário; merge.
- Comandos via **Bash tool (git-bash)**: paths `/c/Projects/Investor`, `/dev/null` válidos.
- Commits Conventional + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- `dotnet`/`git` com `-C`/`--project`/`cd backend` quando necessário. SDK 8 fixado pelo `global.json`.
- Cadência: 1 task → 1 PR → **pausa para revisão**.
- **Docker precisa estar rodando** para os testes de integração (Testcontainers).
- **`TreatWarningsAsErrors=true`** (vem do `Directory.Build.props` da Fase 1): todo warning quebra o build. Resolver nullable/analyzers/usings não usados em cada task (inclusive remover stubs como `NotImplementedException` assim que substituídos).
- **Versões de pacote:** o plano fixa Npgsql (`8.0.*`) e RabbitMQ.Client (`6.*`); ao instalar, confirmar a versão resolvida (RabbitMQ.Client **6.x** usa a API síncrona `IModel`/`BasicPublish` — 7.x é async e quebraria o código).

## File Structure (Fase 2)

```text
contracts/events/operation-imported.v1.json          # P2.2 contrato neutro

backend/src/InvestOps.Contracts/
  Events/EventEnvelope.cs                             # P2.2
  Events/OperationImportedV1.cs                       # P2.2

backend/src/InvestOps.Domain/
  Operations/OperationCalculator.cs                  # P2.2 (gross/net/settlement, puro)

backend/src/InvestOps.Application/
  Abstractions/IDbConnectionFactory.cs               # P2.1
  Abstractions/IOperationRepository.cs               # P2.2/P2.4
  Abstractions/IFinancialProductRepository.cs        # P2.2
  Abstractions/IInvestmentAccountRepository.cs       # P2.2
  Abstractions/IOutboxRepository.cs                  # P2.2 / P2.3
  Abstractions/IAuditRepository.cs                   # P2.2
  Abstractions/IEventBus.cs                          # P2.3 (publicação)
  Common/PagedResult.cs                              # P2.4
  Operations/ImportOperation/*.cs                    # P2.2 (Request/Result/Handler/Validator)
  Operations/GetOperationById/*.cs                   # P2.4
  Operations/ListOperations/*.cs                     # P2.4
  DependencyInjection.cs                             # P2.1 AddApplication

backend/src/InvestOps.Infrastructure/
  Persistence/NpgsqlConnectionFactory.cs             # P2.1
  Persistence/Dapper/EnumStringHandler.cs            # P2.1
  Persistence/Dapper/DapperConfig.cs                 # P2.1
  Persistence/Repositories/*.cs                      # P2.2/P2.4
  Messaging/RabbitMqOptions.cs                       # P2.3
  Messaging/RabbitMqEventBus.cs                      # P2.3 (IEventBus)
  Messaging/OutboxPublisher.cs                       # P2.3 (BackgroundService)
  DependencyInjection.cs                             # P2.1 AddInfrastructure

backend/src/InvestOps.Migrator/
  MigrationRunner.cs                                 # P2.1 (extraído do Program.cs; reusado nos testes)

backend/src/InvestOps.Api/
  Operations/OperationsController.cs                 # P2.2/P2.4
  Program.cs                                         # P2.1+ (AddApplication/AddInfrastructure)

backend/tests/InvestOps.IntegrationTests/
  Infrastructure/PostgresFixture.cs                  # P2.1 (Testcontainers + migrations)
  Infrastructure/RabbitMqFixture.cs                  # P2.3
  ...Tests.cs
```

---

## Chunk 1: Acesso a dados e fluxo de import

### Task P2.1: Fundação de acesso a dados (connection factory, enum↔text, Testcontainers)

**Files:**
- Create: `backend/src/InvestOps.Application/Abstractions/IDbConnectionFactory.cs`
- Create: `backend/src/InvestOps.Application/DependencyInjection.cs`
- Create: `backend/src/InvestOps.Infrastructure/Persistence/NpgsqlConnectionFactory.cs`
- Create: `backend/src/InvestOps.Infrastructure/Persistence/Dapper/EnumStringHandler.cs`
- Create: `backend/src/InvestOps.Infrastructure/Persistence/Dapper/DapperConfig.cs`
- Create: `backend/src/InvestOps.Infrastructure/DependencyInjection.cs`
- Create: `backend/src/InvestOps.Migrator/MigrationRunner.cs`
- Modify: `backend/src/InvestOps.Migrator/Program.cs` (usar MigrationRunner)
- Modify: `backend/src/InvestOps.Api/Program.cs` (AddApplication + AddInfrastructure)
- Create: `backend/tests/InvestOps.IntegrationTests/Infrastructure/PostgresFixture.cs`
- Test: `backend/tests/InvestOps.IntegrationTests/ConnectionSmokeTests.cs`

- [ ] **Step 1: Branch** `git -C /c/Projects/Investor checkout developer && git -C /c/Projects/Investor pull --ff-only && git -C /c/Projects/Investor checkout -b feature/data-access`

- [ ] **Step 2: Pacotes**

```bash
cd /c/Projects/Investor/backend
dotnet add src/InvestOps.Infrastructure package Dapper
dotnet add src/InvestOps.Infrastructure package Npgsql --version "8.0.*"
# Application usa IServiceCollection no AddApplication → precisa das abstractions de DI:
dotnet add src/InvestOps.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/InvestOps.Application package Microsoft.Extensions.Configuration.Abstractions
dotnet add tests/InvestOps.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/InvestOps.IntegrationTests reference src/InvestOps.Migrator
```
> `InvestOps.IntegrationTests` já referencia `InvestOps.Api` (traz Application+Infrastructure transitivamente desde a T3); só falta o `Migrator` (para o `MigrationRunner`). Referenciar um projeto `Exe` é válido — o assembly fica referenciável e os `.sql` embutidos resolvem por `Assembly.GetExecutingAssembly()`.

- [ ] **Step 3: `IDbConnectionFactory`** (Application depende só de `System.Data.Common`)

```csharp
using System.Data.Common;

namespace InvestOps.Application.Abstractions;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: `NpgsqlConnectionFactory`**

```csharp
using System.Data.Common;
using InvestOps.Application.Abstractions;
using Npgsql;

namespace InvestOps.Infrastructure.Persistence;

public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
```

- [ ] **Step 5: Enum handler + registro** (enums persistem como `text`)

```csharp
// EnumStringHandler.cs
using System.Data;
using Dapper;

namespace InvestOps.Infrastructure.Persistence.Dapper;

public sealed class EnumStringHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
{
    public override T Parse(object value) => Enum.Parse<T>((string)value);

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString();
    }
}
```

```csharp
// DapperConfig.cs
using Dapper;
using InvestOps.Domain.Enums;

namespace InvestOps.Infrastructure.Persistence.Dapper;

public static class DapperConfig
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        SqlMapper.AddTypeHandler(new EnumStringHandler<InvestorProfile>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<CustomerStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<AccountStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<ProductType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OperationType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OperationStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<RejectionReason>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OutboxStatus>());
        _registered = true;
    }
}
```

> `AddTypeHandler<T>` cobre também `Nullable<T>` (ex.: `RejectionReason?`): em valor `NULL` no banco, Dapper retorna `null` sem chamar o handler; ao gravar `null`, envia `DBNull`. Por isso `rejection_reason` fica SQL NULL no import.

- [ ] **Step 6: DI (Application + Infrastructure)**

```csharp
// InvestOps.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // handlers e validators registrados nas tasks seguintes
        return services;
    }
}
```

```csharp
// InvestOps.Infrastructure/DependencyInjection.cs
using InvestOps.Application.Abstractions;
using InvestOps.Infrastructure.Persistence;
using InvestOps.Infrastructure.Persistence.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        DapperConfig.Register();
        var connectionString = configuration.GetConnectionString("Postgres")!;
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));
        return services;
    }
}
```

- [ ] **Step 7: Extrair `MigrationRunner`** (reuso entre console e testes)

```csharp
// InvestOps.Migrator/MigrationRunner.cs
using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace InvestOps.Migrator;

public static class MigrationRunner
{
    public static DatabaseUpgradeResult Run(string connectionString, bool includeSeed)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var schema = Build(connectionString, assembly, "schemaversions", ".Scripts.Schema.");
        var schemaResult = schema.PerformUpgrade();
        if (!schemaResult.Successful || !includeSeed) return schemaResult;

        var seed = Build(connectionString, assembly, "seedversions", ".Scripts.Seed.");
        return seed.PerformUpgrade();
    }

    private static UpgradeEngine Build(string cs, Assembly asm, string journal, string marker) =>
        DeployChanges.To
            .PostgresqlDatabase(cs)
            .WithScriptsEmbeddedInAssembly(asm, n => n.Contains(marker, StringComparison.Ordinal))
            .JournalToPostgresqlTable("public", journal)
            .LogToConsole()
            .Build();
}
```

- [ ] **Step 8: `Program.cs` do Migrator usa o runner**

```csharp
using InvestOps.Migrator;

var connectionString =
    args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable("INVESTOPS_DB")
    ?? "Host=localhost;Port=5432;Database=investops;Username=investops;Password=investops";

var result = MigrationRunner.Run(connectionString, includeSeed: args.Contains("--seed"));
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}
Console.WriteLine("Migrations concluídas com sucesso.");
return 0;
```

- [ ] **Step 9: Wire DI no `Api/Program.cs`** — adicionar após `AddSwaggerGen()`:

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
```
(com `using InvestOps.Application;` e `using InvestOps.Infrastructure;`)

- [ ] **Step 10: `PostgresFixture` (Testcontainers + migrations reais)**

```csharp
using DotNet.Testcontainers.Builders;
using InvestOps.Migrator;
using Testcontainers.PostgreSql;
using Xunit;

namespace InvestOps.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var result = MigrationRunner.Run(ConnectionString, includeSeed: false);
        if (!result.Successful) throw result.Error;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

- [ ] **Step 11: Teste de fumaça de conexão**

```csharp
using Dapper;
using InvestOps.IntegrationTests.Infrastructure;
using InvestOps.Infrastructure.Persistence;
using Xunit;

namespace InvestOps.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ConnectionSmokeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Opens_connection_and_schema_is_applied()
    {
        var factory = new NpgsqlConnectionFactory(fixture.ConnectionString);
        await using var conn = await factory.OpenConnectionAsync();

        var tableCount = await conn.ExecuteScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema='public' and table_name in ('operations','outbox_messages','audit_logs');");

        Assert.Equal(3L, tableCount);   // count(*) é bigint → mapear para long
    }
}
```

- [ ] **Step 12: Build + test**

```bash
cd /c/Projects/Investor/backend
dotnet build InvestOps.sln          # 0 erros/avisos
dotnet test tests/InvestOps.IntegrationTests --filter ConnectionSmokeTests   # 1 passed (precisa Docker)
```
Expected: smoke passa (container Postgres sobe, migrations aplicam, 3 tabelas existem).

- [ ] **Step 13: Commit + push + PR** (`feat(data): fundação de acesso a dados (Dapper + Testcontainers)`) → **PAUSA**.

---

### Task P2.2: Importação de operação (contrato, cálculo, handler, endpoint)

**Files:**
- Create: `contracts/events/operation-imported.v1.json`
- Create: `backend/src/InvestOps.Contracts/Events/EventEnvelope.cs`, `Events/OperationImportedV1.cs`
- Create: `backend/src/InvestOps.Domain/Operations/OperationCalculator.cs`
- Create: `backend/src/InvestOps.Application/Abstractions/IOperationRepository.cs`, `IFinancialProductRepository.cs`, `IInvestmentAccountRepository.cs`, `IOutboxRepository.cs`, `IAuditRepository.cs`
- Create: `backend/src/InvestOps.Application/Operations/ImportOperation/` (Request, Result, Validator, Handler, errors)
- Create: `backend/src/InvestOps.Infrastructure/Persistence/Repositories/` (Operation, FinancialProduct, InvestmentAccount, Outbox, Audit)
- Create: `backend/src/InvestOps.Api/Operations/OperationsController.cs`
- Modify: DI (`Application`/`Infrastructure`), `Program.cs` (controllers)
- Test (unit): `OperationCalculatorTests`, `ImportOperationHandlerTests`, `ImportOperationValidatorTests`
- Test (integração): `ImportOperationTests`

- [ ] **Step 1: Branch** `feature/operation-import` a partir de `developer`.

- [ ] **Step 2: Pacotes**
```bash
cd /c/Projects/Investor/backend
dotnet add src/InvestOps.Application package FluentValidation
# AddValidatorsFromAssemblyContaining é registrado no AddApplication → o pacote de DI vai no Application:
dotnet add src/InvestOps.Application package FluentValidation.DependencyInjectionExtensions
```
> O controller injeta `IValidator<ImportOperationRequest>` (interface do pacote `FluentValidation`, já transitivo na Api via Application). Não é preciso adicionar o pacote de DI na Api.

- [ ] **Step 3: TDD do cálculo — teste primeiro** (`OperationCalculatorTests`)

```csharp
using InvestOps.Domain.Operations;

namespace InvestOps.UnitTests;

public class OperationCalculatorTests
{
    [Fact]
    public void GrossValue_is_quantity_times_unit_price()
        => Assert.Equal(2540.00m, OperationCalculator.GrossValue(100m, 25.40m));

    [Fact]
    public void SettlementDate_is_operation_date_plus_liquidity_days()
        => Assert.Equal(new DateOnly(2026, 6, 10),
            OperationCalculator.SettlementDate(new DateOnly(2026, 6, 7), liquidityDays: 3));
}
```

- [ ] **Step 4: Rodar → falha** (`dotnet test tests/InvestOps.UnitTests --filter OperationCalculator`).

- [ ] **Step 5: `OperationCalculator`** (lógica de domínio pura)

```csharp
namespace InvestOps.Domain.Operations;

public static class OperationCalculator
{
    public static decimal GrossValue(decimal quantity, decimal unitPrice) => quantity * unitPrice;

    public static DateOnly SettlementDate(DateOnly operationDate, int liquidityDays)
        => operationDate.AddDays(liquidityDays);
}
```

- [ ] **Step 6: Rodar → passa.**

- [ ] **Step 7: Contrato do evento** — JSON neutro + C#

`contracts/events/operation-imported.v1.json` (conforme SPEC §11): envelope com `eventId/eventType/version/occurredAt/correlationId` + `payload` (`operationId, externalId, accountId, productId, operationType, quantity, unitPrice, operationDate`).

```csharp
// InvestOps.Contracts/Events/EventEnvelope.cs
namespace InvestOps.Contracts.Events;

public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    TPayload Payload);
```

```csharp
// InvestOps.Contracts/Events/OperationImportedV1.cs
namespace InvestOps.Contracts.Events;

public sealed record OperationImportedV1(
    Guid OperationId,
    string ExternalId,
    Guid AccountId,
    Guid ProductId,
    string OperationType,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly OperationDate)
{
    public const string EventType = "OperationImported";
    public const int Version = 1;
}
```

- [ ] **Step 8: Interfaces de repositório** (Application/Abstractions). Assinaturas com `DbConnection`/`DbTransaction` controladas pelo handler:

```csharp
using System.Data.Common;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IOperationRepository
{
    Task<Operation?> GetByExternalIdAsync(DbConnection conn, string externalId, CancellationToken ct);
    Task InsertAsync(DbConnection conn, DbTransaction tx, Operation op, CancellationToken ct);
    Task<Operation?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct);
}

public interface IFinancialProductRepository
{
    Task<FinancialProduct?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct);
}

public interface IInvestmentAccountRepository
{
    Task<bool> ExistsAsync(DbConnection conn, Guid id, CancellationToken ct);
}

public interface IOutboxRepository
{
    Task InsertAsync(DbConnection conn, DbTransaction tx, OutboxMessage message, CancellationToken ct);
}

public interface IAuditRepository
{
    Task InsertAsync(DbConnection conn, DbTransaction tx, AuditLog log, CancellationToken ct);
}
```

- [ ] **Step 9: Request/Result/Validator/erros do import**

```csharp
// ImportOperationRequest.cs
namespace InvestOps.Application.Operations.ImportOperation;

public sealed record ImportOperationRequest(
    string ExternalId, Guid AccountId, Guid ProductId,
    string OperationType, decimal Quantity, decimal UnitPrice, DateOnly OperationDate);

// ImportOperationResult.cs  (Created indica se criou ou se já existia — idempotência)
public sealed record ImportOperationResult(Guid Id, string Status, bool Created);
```

```csharp
// ImportOperationValidator.cs
using FluentValidation;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Operations.ImportOperation;

public sealed class ImportOperationValidator : AbstractValidator<ImportOperationRequest>
{
    public ImportOperationValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OperationType).Must(v => Enum.TryParse<OperationType>(v, out _))
            .WithMessage("operationType inválido");
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OperationDate).NotEqual(default(DateOnly));
    }
}
```

Erro de validação referencial (conta/produto inexistente) → resultado de domínio. Usar uma exceção de aplicação simples:

```csharp
// ReferenceNotFoundException.cs
namespace InvestOps.Application.Operations.ImportOperation;

public sealed class ReferenceNotFoundException(string message) : Exception(message);
```

- [ ] **Step 10: TDD do handler — teste primeiro** (`ImportOperationHandlerTests`, repos mockados com NSubstitute)

```bash
dotnet add tests/InvestOps.UnitTests package NSubstitute
```

```csharp
using System.Data.Common;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;
using NSubstitute;

namespace InvestOps.UnitTests;

public class ImportOperationHandlerTests
{
    private static ImportOperationRequest ValidRequest() => new(
        "EXT-1", Guid.NewGuid(), Guid.NewGuid(), "Buy", 100m, 25.40m, new DateOnly(2026, 6, 7));

    [Fact]
    public async Task Returns_existing_when_external_id_already_imported()
    {
        var existing = new Operation { Id = Guid.NewGuid(), Status = OperationStatus.Imported };
        var ops = Substitute.For<IOperationRepository>();
        ops.GetByExternalIdAsync(Arg.Any<DbConnection>(), "EXT-1", Arg.Any<CancellationToken>())
           .Returns(existing);

        var handler = BuildHandler(ops: ops);
        var result = await handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default);

        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.Id);
        await ops.DidNotReceive().InsertAsync(Arg.Any<DbConnection>(), Arg.Any<DbTransaction>(), Arg.Any<Operation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_product_not_found()
    {
        var handler = BuildHandler(productExists: false);
        await Assert.ThrowsAsync<ReferenceNotFoundException>(
            () => handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default));
    }

    // BuildHandler: monta o handler com substitutes; conexão/transação fake via IDbConnectionFactory
    // que retorna uma DbConnection substituível (ou usar um teste de integração para o caminho feliz completo).
}
```

> Notas:
> - `BuildHandler(...)` é um helper flexível do teste com substitutes opcionais (parâmetros nomeados `ops`, `productExists`, etc.), construindo `ImportOperationHandler` com `IDbConnectionFactory` mockado (retorna uma `DbConnection` substituível) e `TimeProvider` fake. Implementar antes dos `[Fact]`.
> - O **caminho feliz completo** (transação gravando 3 tabelas) é coberto no teste de **integração** (Step 15), pois envolve transação real. Os unit tests focam: idempotência (não insere) e validação referencial (lança).

- [ ] **Step 11: Rodar → falha.**

- [ ] **Step 12: `ImportOperationHandler`** (orquestra; transação única)

```csharp
using InvestOps.Application.Abstractions;
using InvestOps.Contracts.Events;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;
using InvestOps.Domain.Operations;
using System.Text.Json;

namespace InvestOps.Application.Operations.ImportOperation;

public sealed class ImportOperationHandler(
    IDbConnectionFactory connectionFactory,
    IOperationRepository operations,
    IFinancialProductRepository products,
    IInvestmentAccountRepository accounts,
    IOutboxRepository outbox,
    IAuditRepository audit,
    TimeProvider clock)
{
    public async Task<ImportOperationResult> HandleAsync(
        ImportOperationRequest request, Guid correlationId, CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);

        var existing = await operations.GetByExternalIdAsync(conn, request.ExternalId, ct);
        if (existing is not null)
            return new ImportOperationResult(existing.Id, existing.Status.ToString(), Created: false);

        var product = await products.GetByIdAsync(conn, request.ProductId, ct)
            ?? throw new ReferenceNotFoundException($"Produto {request.ProductId} não encontrado");
        if (!await accounts.ExistsAsync(conn, request.AccountId, ct))
            throw new ReferenceNotFoundException($"Conta {request.AccountId} não encontrada");

        var now = clock.GetUtcNow();
        var operationType = Enum.Parse<OperationType>(request.OperationType);
        var gross = OperationCalculator.GrossValue(request.Quantity, request.UnitPrice);

        var operation = new Operation
        {
            Id = Guid.NewGuid(),
            ExternalId = request.ExternalId,
            AccountId = request.AccountId,
            ProductId = request.ProductId,
            OperationType = operationType,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            GrossValue = gross,
            NetValue = gross,
            OperationDate = request.OperationDate,
            SettlementDate = OperationCalculator.SettlementDate(request.OperationDate, product.LiquidityDays),
            Status = OperationStatus.Imported,
            RejectionReason = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        var envelope = new EventEnvelope<OperationImportedV1>(
            EventId: Guid.NewGuid(),
            EventType: OperationImportedV1.EventType,
            Version: OperationImportedV1.Version,
            OccurredAt: now,
            CorrelationId: correlationId,
            Payload: new OperationImportedV1(
                operation.Id, operation.ExternalId, operation.AccountId, operation.ProductId,
                operation.OperationType.ToString(), operation.Quantity, operation.UnitPrice, operation.OperationDate));

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = OperationImportedV1.EventType,
            Version = OperationImportedV1.Version,
            Payload = JsonSerializer.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            CreatedAt = now
        };

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(Operation),
            EntityId = operation.Id.ToString(),
            Action = "Imported",
            OldValue = null,
            NewValue = JsonSerializer.Serialize(new { operation.ExternalId, operation.Status }),
            UserId = null,
            CreatedAt = now
        };

        await using var tx = await conn.BeginTransactionAsync(ct);
        await operations.InsertAsync(conn, tx, operation, ct);
        await outbox.InsertAsync(conn, tx, outboxMessage, ct);
        await audit.InsertAsync(conn, tx, auditLog, ct);
        await tx.CommitAsync(ct);

        return new ImportOperationResult(operation.Id, operation.Status.ToString(), Created: true);
    }
}
```

- [ ] **Step 13: Repositórios Dapper** (Infrastructure). Pontos críticos: `payload`/`new_value` são `jsonb` → usar cast `::jsonb`; enums via handler (já registrado).

```csharp
// OperationRepository.cs (trecho de insert)
const string insert = """
    INSERT INTO operations
      (id, external_id, account_id, product_id, operation_type, quantity, unit_price,
       gross_value, net_value, operation_date, settlement_date, status, rejection_reason,
       created_at, updated_at)
    VALUES
      (@Id, @ExternalId, @AccountId, @ProductId, @OperationType, @Quantity, @UnitPrice,
       @GrossValue, @NetValue, @OperationDate, @SettlementDate, @Status, @RejectionReason,
       @CreatedAt, @UpdatedAt);
    """;
await conn.ExecuteAsync(new CommandDefinition(insert, op, tx, cancellationToken: ct));
```

```csharp
// OutboxRepository.cs (jsonb cast)
const string insert = """
    INSERT INTO outbox_messages (id, event_type, version, payload, status, attempts, created_at)
    VALUES (@Id, @EventType, @Version, @Payload::jsonb, @Status, @Attempts, @CreatedAt);
    """;
```

```csharp
// AuditRepository.cs (old_value/new_value jsonb)
const string insert = """
    INSERT INTO audit_logs (id, entity_name, entity_id, action, old_value, new_value, user_id, created_at)
    VALUES (@Id, @EntityName, @EntityId, @Action, @OldValue::jsonb, @NewValue::jsonb, @UserId, @CreatedAt);
    """;
```

Reads (`GetByExternalIdAsync`, `GetByIdAsync`, product, account) usam `QuerySingleOrDefaultAsync` com mapeamento de colunas snake_case → propriedades. **Habilitar** `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` no `DapperConfig.Register()`.

- [ ] **Step 14: Controller + DI + validação**

```csharp
// OperationsController.cs
using FluentValidation;
using InvestOps.Application.Operations.ImportOperation;
using Microsoft.AspNetCore.Mvc;

namespace InvestOps.Api.Operations;

[ApiController]
[Route("api/operations")]
public sealed class OperationsController(
    ImportOperationHandler importHandler,
    IValidator<ImportOperationRequest> importValidator) : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import(ImportOperationRequest request, CancellationToken ct)
    {
        var validation = await importValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var e in validation.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var correlationId = GetCorrelationId();
        try
        {
            var result = await importHandler.HandleAsync(request, correlationId, ct);
            return result.Created
                ? CreatedAtAction(nameof(GetById), new { id = result.Id }, result)
                : Ok(result);
        }
        catch (ReferenceNotFoundException ex)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Referência inválida", Detail = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct) => throw new NotImplementedException(); // P2.4

    private Guid GetCorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-Id", out var v) && Guid.TryParse(v, out var id)
            ? id : Guid.NewGuid();
}
```

DI: registrar `ImportOperationHandler`, repos, validator, `TimeProvider.System`, controllers.
- `Application/DependencyInjection.cs`: `services.AddScoped<ImportOperationHandler>();` + `services.AddValidatorsFromAssemblyContaining<ImportOperationValidator>();` + `services.AddSingleton(TimeProvider.System);`
- `Infrastructure/DependencyInjection.cs`: `AddScoped` dos 5 repositórios.
- `Program.cs`: `builder.Services.AddControllers();` + `app.MapControllers();`

- [ ] **Step 15: Teste de integração do import** (`ImportOperationTests`, `[Collection(PostgresCollection)]`)

Cobre: import cria `operations` (Imported) + `outbox_messages` (Pending) + `audit_logs` na mesma transação; 2ª chamada com mesmo `ExternalId` não duplica e retorna `Created=false`. Monta os repos reais + `NpgsqlConnectionFactory(fixture.ConnectionString)`; insere produto/conta antes (ou usa seed). Asserta contagens via Dapper.

- [ ] **Step 16: Build + unit + integração** verdes; smoke manual do `POST /import` via Swagger (opcional).

- [ ] **Step 17: Commit + push + PR** (`feat(operations): importação de operação com idempotência e outbox`) → **PAUSA**.

---

## Chunk 2: Publisher e leitura

### Task P2.3: Outbox Publisher → RabbitMQ

**Files:**
- Create: `backend/src/InvestOps.Application/Abstractions/IEventBus.cs`
- Create: `backend/src/InvestOps.Infrastructure/Messaging/RabbitMqOptions.cs`, `RabbitMqEventBus.cs`, `OutboxPublisher.cs`
- Modify: `IOutboxRepository` (buscar pendentes + marcar publicado/falha), `Infrastructure/DependencyInjection.cs`, `appsettings.json`
- Test (integração): `OutboxPublisherTests` (`RabbitMqFixture` + `PostgresFixture`)

- [ ] **Step 1: Branch** `feature/outbox-publisher`.

- [ ] **Step 2: Pacotes**
```bash
cd /c/Projects/Investor/backend
dotnet add src/InvestOps.Infrastructure package RabbitMQ.Client --version "6.*"
dotnet add tests/InvestOps.IntegrationTests package Testcontainers.RabbitMq
```

- [ ] **Step 3: `IEventBus` + `RabbitMqOptions`**

```csharp
// IEventBus.cs (Application)
namespace InvestOps.Application.Abstractions;
public interface IEventBus
{
    Task PublishAsync(string eventType, string payloadJson, CancellationToken ct);
}
```
```csharp
// RabbitMqOptions.cs
namespace InvestOps.Infrastructure.Messaging;
public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "investops";
    public string Password { get; set; } = "investops";
    public string Exchange { get; set; } = "investops";
    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;
}
```

- [ ] **Step 4: `RabbitMqEventBus`** (declara exchange topic durável; publica persistente)

```csharp
using System.Text;
using InvestOps.Application.Abstractions;
using RabbitMQ.Client;

namespace InvestOps.Infrastructure.Messaging;

public sealed class RabbitMqEventBus(RabbitMqOptions options) : IEventBus, IDisposable
{
    private readonly IConnection _connection = new ConnectionFactory
    {
        HostName = options.Host, Port = options.Port,
        UserName = options.Username, Password = options.Password
    }.CreateConnection();

    public Task PublishAsync(string eventType, string payloadJson, CancellationToken ct)
    {
        using var channel = _connection.CreateModel();
        channel.ExchangeDeclare(options.Exchange, ExchangeType.Topic, durable: true);
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        channel.BasicPublish(options.Exchange, routingKey: eventType, basicProperties: props,
            body: Encoding.UTF8.GetBytes(payloadJson));
        return Task.CompletedTask;
    }

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 5: Estender `IOutboxRepository`** — pendentes + marcar:

```csharp
Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(DbConnection conn, int batchSize, CancellationToken ct);
Task MarkPublishedAsync(DbConnection conn, Guid id, DateTimeOffset publishedAt, CancellationToken ct);
Task MarkFailedAsync(DbConnection conn, Guid id, string error, CancellationToken ct);
```
- SQL pendentes: `SELECT ... FROM outbox_messages WHERE status='Pending' ORDER BY created_at LIMIT @batchSize` (usa índice). Usar `QueryAsync` **buffered** (default) para fechar o reader antes dos updates na mesma conexão (evita "command already in progress" do Npgsql).
- `MarkPublishedAsync`: `SET status='Published', published_at=@PublishedAt`.
- `MarkFailedAsync`: `SET attempts = attempts + 1, last_error = @Error` — **mantém `status='Pending'`** para reprocessar no próximo ciclo (DLQ/limite ficam para a Fase 5).

- [ ] **Step 6: `OutboxPublisher : BackgroundService`**

```csharp
using InvestOps.Application.Abstractions;
using InvestOps.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InvestOps.Infrastructure.Messaging;

public sealed class OutboxPublisher(
    IDbConnectionFactory connectionFactory,
    IOutboxRepository outbox,
    IEventBus bus,
    RabbitMqOptions options,
    TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha no ciclo do OutboxPublisher");
            }
            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(ct);
        var pending = await outbox.GetPendingAsync(conn, options.BatchSize, ct);
        foreach (var message in pending)
        {
            try
            {
                await bus.PublishAsync(message.EventType, message.Payload, ct);
                await outbox.MarkPublishedAsync(conn, message.Id, clock.GetUtcNow(), ct);
            }
            catch (Exception ex)
            {
                await outbox.MarkFailedAsync(conn, message.Id, ex.Message, ct);
                logger.LogWarning(ex, "Falha ao publicar outbox {Id}", message.Id);
            }
        }
    }
}
```

- [ ] **Step 7: DI + appsettings**

Adicionar ao `appsettings.json` na seção `RabbitMq` as chaves `Exchange` (`"investops"`), `PollIntervalSeconds` (`2`), `BatchSize` (`50`).

DI em `Infrastructure/DependencyInjection.cs` — registrar o **POCO resolvido** (os construtores pedem `RabbitMqOptions`, não `IOptions<>`):

```csharp
var rabbitOptions = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
services.AddSingleton(rabbitOptions);
services.AddSingleton<IEventBus, RabbitMqEventBus>();
services.AddHostedService<OutboxPublisher>();
```
(precisa de `Microsoft.Extensions.Configuration.Binder` para `.Get<T>()` — adicionar à Infrastructure se ainda não vier transitivo.)

- [ ] **Step 8: `RabbitMqFixture`** (Testcontainers)

```csharp
using Testcontainers.RabbitMq;
using Xunit;

namespace InvestOps.IntegrationTests.Infrastructure;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine").Build();

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5672);
    public string Username => "rabbitmq";   // default do módulo Testcontainers
    public string Password => "rabbitmq";

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

- [ ] **Step 9: Teste de integração do publisher** (`OutboxPublisherTests`)

Cenário: insere uma `OutboxMessage` Pending no Postgres (fixture), roda `PublishPendingAsync` apontando para o RabbitMQ (fixture); asserta que (a) a mensagem virou `Published` no banco e (b) chegou na fila — declara uma fila e binding (`#` ou `OperationImported`) no exchange `investops` antes de publicar, e consome 1 mensagem.

- [ ] **Step 10: Build + testes** verdes. Smoke manual opcional: `docker compose up`, `dotnet run` da API, `POST /import`, ver no RabbitMQ Management a mensagem publicada.

- [ ] **Step 11: Commit + push + PR** (`feat(messaging): outbox publisher para RabbitMQ`) → **PAUSA**.

---

### Task P2.4: Leitura (GET by id + lista paginada)

**Files:**
- Create: `backend/src/InvestOps.Application/Common/PagedResult.cs`
- Create: `backend/src/InvestOps.Application/Operations/GetOperationById/` (Handler, `OperationDto`)
- Create: `backend/src/InvestOps.Application/Operations/ListOperations/` (`ListOperationsQuery`, Handler)
- Modify: `IOperationRepository` (ListAsync), `OperationRepository`, `OperationsController` (GetById, List), DI
- Test (unit): `ListOperationsQuery` whitelist de sort
- Test (integração): `GetOperationByIdTests`, `ListOperationsTests`

- [ ] **Step 1: Branch** `feature/operations-read`.

- [ ] **Step 2: `PagedResult` + `OperationDto` + `ListOperationsQuery`**

```csharp
// PagedResult.cs
namespace InvestOps.Application.Common;
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
```
```csharp
// ListOperationsQuery.cs
namespace InvestOps.Application.Operations.ListOperations;
public sealed record ListOperationsQuery(
    int Page = 1, int PageSize = 20, string? Sort = null,
    string? Status = null, Guid? ProductId = null, Guid? CustomerId = null)
{
    public static readonly IReadOnlySet<string> SortableColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "operation_date", "created_at", "gross_value", "status" };

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);   // cap em 100 (design §5)
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
```
> O handler/repo usa `NormalizedPage`/`NormalizedPageSize`/`Offset` (nunca os valores crus) no `LIMIT/OFFSET` e no `PagedResult`.

- [ ] **Step 3: TDD do parser de sort (whitelist) — teste primeiro**

Teste: `SortClauseBuilder.Build("operationDate:desc")` → `"operation_date DESC"`; coluna fora da whitelist → fallback seguro (`created_at DESC`); injeção (`"; DROP"`) → fallback. (Implementar um pequeno helper `SortClauseBuilder` em Application com mapeamento camelCase→coluna + whitelist.)

- [ ] **Step 4: Rodar → falha; implementar `SortClauseBuilder`; rodar → passa.**

- [ ] **Step 5: `IOperationRepository.ListAsync` + impl Dapper**

```csharp
Task<(IReadOnlyList<Operation> Items, int Total)> ListAsync(DbConnection conn, ListOperationsQuery query, CancellationToken ct);
```
SQL: `WHERE` dinâmico com filtros opcionais (`status`, `product_id`, join para `customer_id`), `ORDER BY {whitelistedClause}`, `LIMIT @PageSize OFFSET @Offset`; `COUNT(*)` separado com os mesmos filtros. Validar `status` contra `OperationStatus` (ignora filtro inválido ou retorna vazio).

- [ ] **Step 6: Handlers + controller (GetById, List) + DI**

`GetOperationByIdHandler` → `OperationDto?`; controller retorna `Ok(dto)` ou `NotFound()`. `ListOperationsHandler` → `PagedResult<OperationDto>`. Registrar handlers no DI; implementar `GetById` (substituindo o `NotImplementedException` da P2.2) e `[HttpGet]` List com `[FromQuery] ListOperationsQuery`.

- [ ] **Step 7: Testes de integração** — `GetOperationByIdTests` (200/404), `ListOperationsTests` (paginação: insere N operações, pede page/pageSize, asserta `Items.Count`/`Total`; filtro por status).

- [ ] **Step 8: Build + testes** verdes.

- [ ] **Step 9: Commit + push + PR** (`feat(operations): consulta por id e lista paginada`) → **PAUSA**.

---

## Fechamento da Fase 2

Após P2.4 mergeada: abrir PR `developer → main` (release da Fase 2). Critérios: import idempotente grava operação+outbox+audit; publisher entrega ao RabbitMQ; leitura paginada funciona; testes (unit + integração Testcontainers) verdes. Próximo: **Fase 3 (worker Go)** consome `OperationImported`, aplica regras (suitability, posição), atualiza posição e status.
