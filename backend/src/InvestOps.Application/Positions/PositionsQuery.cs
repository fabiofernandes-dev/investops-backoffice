namespace InvestOps.Application.Positions;

public sealed record PositionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Sort = null,
    Guid? AccountId = null,
    Guid? ProductId = null,
    Guid? CustomerId = null)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
