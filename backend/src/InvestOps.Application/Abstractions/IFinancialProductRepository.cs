using System.Data.Common;
using InvestOps.Domain.Entities;

namespace InvestOps.Application.Abstractions;

public interface IFinancialProductRepository
{
    Task<FinancialProduct?> GetByIdAsync(DbConnection conn, Guid id, CancellationToken ct);
}
