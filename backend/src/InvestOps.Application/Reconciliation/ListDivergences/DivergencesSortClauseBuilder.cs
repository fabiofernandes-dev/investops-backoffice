namespace InvestOps.Application.Reconciliation.ListDivergences;

public static class DivergencesSortClauseBuilder
{
    public const string Default = "d.created_at DESC";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = "d.created_at",
            ["status"] = "d.status",
            ["difference"] = "(d.custodian_quantity - d.internal_quantity)"
        };

    public static string Build(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return Default;
        var parts = sort.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Columns.TryGetValue(parts[0], out var column)) return Default;
        var ascending = parts.Length >= 2 && parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        return $"{column} {(ascending ? "ASC" : "DESC")}";
    }
}
