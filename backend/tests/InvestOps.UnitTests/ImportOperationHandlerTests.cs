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

    private sealed class Mocks
    {
        public IDbConnectionFactory ConnectionFactory { get; } = Substitute.For<IDbConnectionFactory>();
        public IOperationRepository Operations { get; } = Substitute.For<IOperationRepository>();
        public IFinancialProductRepository Products { get; } = Substitute.For<IFinancialProductRepository>();
        public IInvestmentAccountRepository Accounts { get; } = Substitute.For<IInvestmentAccountRepository>();
        public IOutboxRepository Outbox { get; } = Substitute.For<IOutboxRepository>();
        public IAuditRepository Audit { get; } = Substitute.For<IAuditRepository>();
    }

    private static (ImportOperationHandler Handler, Mocks Mocks) Build(
        Operation? existing = null, FinancialProduct? product = null, bool accountExists = true)
    {
        var m = new Mocks();
        m.ConnectionFactory.OpenConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Substitute.For<DbConnection>());
        m.Operations.GetByExternalIdAsync(Arg.Any<DbConnection>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);
        m.Products.GetByIdAsync(Arg.Any<DbConnection>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(product);
        m.Accounts.ExistsAsync(Arg.Any<DbConnection>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(accountExists);

        var handler = new ImportOperationHandler(
            m.ConnectionFactory, m.Operations, m.Products, m.Accounts, m.Outbox, m.Audit, TimeProvider.System);
        return (handler, m);
    }

    [Fact]
    public async Task Returns_existing_when_external_id_already_imported()
    {
        var existing = new Operation { Id = Guid.NewGuid(), Status = OperationStatus.Imported };
        var (handler, mocks) = Build(existing: existing);

        var result = await handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default);

        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.Id);
        await mocks.Operations.DidNotReceive()
            .InsertAsync(Arg.Any<DbConnection>(), Arg.Any<DbTransaction>(), Arg.Any<Operation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_product_not_found()
    {
        var (handler, _) = Build(existing: null, product: null);

        await Assert.ThrowsAsync<ReferenceNotFoundException>(
            () => handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Throws_when_account_not_found()
    {
        var product = new FinancialProduct { Id = Guid.NewGuid(), LiquidityDays = 1 };
        var (handler, _) = Build(existing: null, product: product, accountExists: false);

        await Assert.ThrowsAsync<ReferenceNotFoundException>(
            () => handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default));
    }
}
