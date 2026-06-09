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
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
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
        m.Operations.GetByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);
        m.Products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(product);
        m.Accounts.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(accountExists);

        var handler = new ImportOperationHandler(
            m.UnitOfWork, m.Operations, m.Products, m.Accounts, m.Outbox, m.Audit, TimeProvider.System);
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
        await mocks.Operations.DidNotReceive().InsertAsync(Arg.Any<Operation>(), Arg.Any<CancellationToken>());
        await mocks.UnitOfWork.DidNotReceive().BeginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commits_operation_outbox_and_audit_on_happy_path()
    {
        var product = new FinancialProduct { Id = Guid.NewGuid(), LiquidityDays = 2 };
        var (handler, mocks) = Build(existing: null, product: product, accountExists: true);

        var result = await handler.HandleAsync(ValidRequest(), Guid.NewGuid(), default);

        Assert.True(result.Created);
        await mocks.UnitOfWork.Received(1).BeginAsync(Arg.Any<CancellationToken>());
        await mocks.Operations.Received(1).InsertAsync(Arg.Any<Operation>(), Arg.Any<CancellationToken>());
        await mocks.Outbox.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await mocks.Audit.Received(1).InsertAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
        await mocks.UnitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
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
