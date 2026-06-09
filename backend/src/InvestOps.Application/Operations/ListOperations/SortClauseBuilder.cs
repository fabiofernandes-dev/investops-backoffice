namespace InvestOps.Application.Operations.ListOperations;

public static class SortClauseBuilder
{
    public const string Default = "created_at DESC";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["operationDate"] = "operation_date",
            ["createdAt"] = "created_at",
            ["grossValue"] = "gross_value",
            ["status"] = "status"
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
