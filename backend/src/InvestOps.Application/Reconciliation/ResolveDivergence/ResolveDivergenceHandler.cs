using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Reconciliation.ResolveDivergence;

public sealed class ResolveDivergenceHandler(
    IUnitOfWork unitOfWork, IReconciliationRepository repo, IAuditRepository audit, TimeProvider clock)
{
    public async Task HandleAsync(Guid divergenceId, CancellationToken ct)
    {
        var divergence = await repo.GetDivergenceByIdAsync(divergenceId, ct)
            ?? throw new ReferenceNotFoundException($"Divergência {divergenceId} não encontrada");
        if (divergence.Status != DivergenceStatus.Pending)
            throw new ConflictException($"Divergência {divergenceId} não está pendente (status {divergence.Status}).");

        var now = clock.GetUtcNow();
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(), EntityName = nameof(ReconciliationDivergence), EntityId = divergenceId.ToString(),
            Action = "Resolved", OldValue = JsonSerializer.Serialize(new { Status = divergence.Status.ToString() }),
            NewValue = JsonSerializer.Serialize(new { Status = nameof(DivergenceStatus.Resolved) }),
            UserId = null, CreatedAt = now
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            var affected = await repo.UpdateDivergenceStatusAsync(divergenceId, nameof(DivergenceStatus.Resolved), now, ct);
            if (affected == 0)
                throw new ConflictException($"Divergência {divergenceId} não está mais pendente (alteração concorrente).");
            await audit.InsertAsync(auditLog, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch { await unitOfWork.RollbackAsync(ct); throw; }
    }
}
