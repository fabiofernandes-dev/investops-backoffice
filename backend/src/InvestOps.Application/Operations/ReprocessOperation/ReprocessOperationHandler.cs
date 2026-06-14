using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Contracts.Events;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Operations.ReprocessOperation;

// Reprocessa uma operação republicando o evento OperationImported via Outbox.
// O worker é idempotente: se a operação já está Processed/Rejected ele ignora;
// se está Imported/Failed, processa. Útil para operações cujo evento nunca chegou ao worker.
public sealed class ReprocessOperationHandler(
    IUnitOfWork unitOfWork,
    IOperationRepository operations,
    IOutboxRepository outbox,
    IAuditRepository audit,
    TimeProvider clock)
{
    public async Task HandleAsync(Guid operationId, Guid correlationId, CancellationToken ct)
    {
        var operation = await operations.GetByIdAsync(operationId, ct)
            ?? throw new ReferenceNotFoundException($"Operação {operationId} não encontrada");

        var now = clock.GetUtcNow();

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
            Payload = JsonSerializer.Serialize(envelope, EventJson.Options),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            CreatedAt = now
        };

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(Operation),
            EntityId = operation.Id.ToString(),
            Action = "Reprocess",
            OldValue = null,
            NewValue = JsonSerializer.Serialize(new { operation.ExternalId, Status = operation.Status.ToString() }),
            UserId = null,
            CreatedAt = now
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            await outbox.InsertAsync(outboxMessage, ct);
            await audit.InsertAsync(auditLog, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch
        {
            await unitOfWork.RollbackAsync(ct);
            throw;
        }
    }
}
