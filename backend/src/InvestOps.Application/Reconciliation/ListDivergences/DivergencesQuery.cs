namespace InvestOps.Application.Reconciliation.ListDivergences;

public sealed record DivergencesQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Status = null)
{
    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public int Offset => (NormalizedPage - 1) * NormalizedPageSize;
}
