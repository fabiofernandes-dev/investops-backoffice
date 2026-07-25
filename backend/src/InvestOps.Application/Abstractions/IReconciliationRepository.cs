using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IReconciliationRepository
{
    Task InsertImportAsync(ReconciliationImport import, CancellationToken ct);
    Task InsertDivergencesAsync(IReadOnlyList<ReconciliationDivergence> divergences, CancellationToken ct);
    Task<ReconciliationDivergence?> GetDivergenceByIdAsync(Guid id, CancellationToken ct);
    Task UpdateDivergenceStatusAsync(Guid id, string status, DateTimeOffset resolvedAt, CancellationToken ct);
}
