using System.Text.Json;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Operations.ImportOperation;
using InvestOps.Domain.Entities;
using InvestOps.Domain.Enums;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed class ImportReconciliationHandler(
    IUnitOfWork unitOfWork, IReconciliationRepository repo, IReconciliationReadRepository read,
    IAuditRepository audit, TimeProvider clock)
{
    public async Task<ImportReconciliationResult> HandleAsync(ImportReconciliationRequest request, CancellationToken ct)
    {
        var accountIds = request.Rows.Select(r => r.AccountId).Distinct().ToList();
        var productIds = request.Rows.Select(r => r.ProductId).Distinct().ToList();

        var existingAccounts = (await read.GetExistingAccountIdsAsync(accountIds, ct)).ToHashSet();
        var missingAccount = accountIds.FirstOrDefault(id => !existingAccounts.Contains(id));
        if (missingAccount != Guid.Empty) throw new ReferenceNotFoundException($"Conta {missingAccount} não encontrada");
        var existingProducts = (await read.GetExistingProductIdsAsync(productIds, ct)).ToHashSet();
        var missingProduct = productIds.FirstOrDefault(id => !existingProducts.Contains(id));
        if (missingProduct != Guid.Empty) throw new ReferenceNotFoundException($"Produto {missingProduct} não encontrado");

        var internalPositions = await read.GetInternalPositionsAsync(accountIds, productIds, ct);
        var results = ReconciliationComparer.Compare(request.Rows, internalPositions);

        var now = clock.GetUtcNow();
        var import = new ReconciliationImport
        {
            Id = Guid.NewGuid(), Source = request.Source, ReferenceDate = request.ReferenceDate,
            Status = ReconciliationImportStatus.Completed, CreatedAt = now
        };
        var divergences = results.Select(r => new ReconciliationDivergence
        {
            Id = Guid.NewGuid(), ReconciliationImportId = import.Id, AccountId = r.AccountId, ProductId = r.ProductId,
            InternalQuantity = r.InternalQuantity, CustodianQuantity = r.CustodianQuantity,
            Status = DivergenceStatus.Pending, CreatedAt = now, ResolvedAt = null
        }).ToList();
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(), EntityName = nameof(ReconciliationImport), EntityId = import.Id.ToString(),
            Action = "Imported", OldValue = null,
            NewValue = JsonSerializer.Serialize(new { import.Source, import.ReferenceDate, RowsProcessed = request.Rows.Count, DivergenceCount = divergences.Count }),
            UserId = null, CreatedAt = now
        };

        await unitOfWork.BeginAsync(ct);
        try
        {
            await repo.InsertImportAsync(import, ct);
            await repo.InsertDivergencesAsync(divergences, ct);
            await audit.InsertAsync(auditLog, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch { await unitOfWork.RollbackAsync(ct); throw; }

        return new ImportReconciliationResult(import.Id, import.Source, import.ReferenceDate,
            request.Rows.Count, request.Rows.Count - divergences.Count, divergences.Count);
    }
}
