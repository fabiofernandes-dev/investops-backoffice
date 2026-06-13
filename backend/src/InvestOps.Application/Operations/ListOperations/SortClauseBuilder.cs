namespace InvestOps.Application.Operations.ListOperations;

public static class SortClauseBuilder
{
    // Colunas qualificadas com o alias "o" (operations): a lista faz join com customers/accounts,
    // que também têm created_at/status — sem o prefixo o ORDER BY seria ambíguo (42702).
    public const string Default = "o.created_at DESC";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["operationDate"] = "o.operation_date",
            ["createdAt"] = "o.created_at",
            ["grossValue"] = "o.gross_value",
            ["status"] = "o.status"
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
