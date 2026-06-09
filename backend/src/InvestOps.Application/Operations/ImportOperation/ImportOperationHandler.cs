using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Contracts.Events;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;
using InvestOps.Domain.Operations;

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
            NewValue = JsonSerializer.Serialize(new { operation.ExternalId, Status = operation.Status.ToString() }),
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
