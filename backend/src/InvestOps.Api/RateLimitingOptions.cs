namespace InvestOps.Api;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string WritePolicy = "writes";

    public int PermitLimit { get; init; } = 100;
    public int WritePermitLimit { get; init; } = 20;
    public int WindowSeconds { get; init; } = 60;
}
