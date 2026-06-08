using InvestOps.Migrator;

var connectionString =
    args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable("INVESTOPS_DB")
    ?? "Host=localhost;Port=5432;Database=investops;Username=investops;Password=investops";

var result = MigrationRunner.Run(connectionString, includeSeed: args.Contains("--seed"));
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("Migrations concluídas com sucesso.");
return 0;
