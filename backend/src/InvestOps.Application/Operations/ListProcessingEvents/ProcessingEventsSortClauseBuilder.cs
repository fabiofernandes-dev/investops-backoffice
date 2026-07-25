namespace InvestOps.Application.Operations.ListProcessingEvents;

public static class ProcessingEventsSortClauseBuilder
{
    public const string Default = "pe.created_at DESC";

    private static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = "pe.created_at",
            ["status"] = "pe.status",
            ["attemptCount"] = "pe.attempt_count"
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
