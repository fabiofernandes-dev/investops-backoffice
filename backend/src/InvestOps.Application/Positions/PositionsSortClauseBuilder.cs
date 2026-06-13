namespace InvestOps.Application.Positions;

public static class PositionsSortClauseBuilder
{
    public const string Default = "p.updated_at DESC";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["updatedAt"] = "p.updated_at",
            ["grossAmount"] = "p.gross_amount",
            ["quantity"] = "p.quantity",
            ["averagePrice"] = "p.average_price"
        };

    public static string Build(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return Default;

        var parts = sort.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Columns.TryGetValue(parts[0], out var column))
            return Default;

        var ascending = parts.Length >= 2 && parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        return $"{column} {(ascending ? "ASC" : "DESC")}";
    }
}
