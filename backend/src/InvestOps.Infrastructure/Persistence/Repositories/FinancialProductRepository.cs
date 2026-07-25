using Dapper;
using InvestOps.Application.Abstractions;
using InvestOps.Application.Common;
using InvestOps.Application.Products;
using InvestOps.Domain.Entities;
using Npgsql;

namespace InvestOps.Infrastructure.Persistence.Repositories;

public sealed class FinancialProductRepository(IDbSession session) : IFinancialProductRepository
{
    public async Task<FinancialProduct?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = "SELECT * FROM financial_products WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<FinancialProduct>(
            new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct)
    {
        const string sql = "SELECT id, product_type, name, ticker, risk_level, liquidity_days, is_active FROM financial_products ORDER BY name;";
        var conn = await session.GetConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProductDto>(new CommandDefinition(sql, transaction: session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task InsertAsync(FinancialProduct product, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active)
            VALUES (@Id, @ProductType, @Name, @Ticker, @RiskLevel, @LiquidityDays, @IsActive);
            """;
        var conn = await session.GetConnectionAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(sql,
                new { product.Id, ProductType = product.ProductType.ToString(), product.Name, product.Ticker, product.RiskLevel, product.LiquidityDays, product.IsActive },
                session.Transaction, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException($"Ticker '{product.Ticker}' já está em uso.");
        }
    }

    public async Task<int> UpdateAsync(FinancialProduct product, CancellationToken ct)
    {
        const string sql = """
            UPDATE financial_products
            SET product_type=@ProductType, name=@Name, ticker=@Ticker, risk_level=@RiskLevel, liquidity_days=@LiquidityDays, is_active=@IsActive
            WHERE id=@Id;
            """;
        var conn = await session.GetConnectionAsync(ct);
        try
        {
            return await conn.ExecuteAsync(new CommandDefinition(sql,
                new { product.Id, ProductType = product.ProductType.ToString(), product.Name, product.Ticker, product.RiskLevel, product.LiquidityDays, product.IsActive },
                session.Transaction, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException($"Ticker '{product.Ticker}' já está em uso.");
        }
    }

    public async Task<int> DeactivateAsync(Guid id, CancellationToken ct)
    {
        const string sql = "UPDATE financial_products SET is_active = false WHERE id = @id;";
        var conn = await session.GetConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, session.Transaction, cancellationToken: ct));
    }
}
