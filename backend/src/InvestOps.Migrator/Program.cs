using System.Reflection;
using DbUp;
using DbUp.Engine;

var connectionString =
    args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable("INVESTOPS_DB")
    ?? "Host=localhost;Port=5432;Database=investops;Username=investops;Password=investops";

var runSeed = args.Contains("--seed");
var assembly = Assembly.GetExecutingAssembly();

if (!RunGroup("schema", "schemaversions", ".Scripts.Schema."))
    return 1;

if (runSeed && !RunGroup("seed", "seedversions", ".Scripts.Seed."))
    return 1;

Console.WriteLine("Migrations concluídas com sucesso.");
return 0;

bool RunGroup(string label, string journalTable, string folderMarker)
{
    var engine = DeployChanges.To
        .PostgresqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(assembly, name => name.Contains(folderMarker, StringComparison.Ordinal))
        .JournalToPostgresqlTable("public", journalTable)
        .LogToConsole()
        .Build();

    var result = engine.PerformUpgrade();
    if (!result.Successful)
    {
        Console.Error.WriteLine($"Falha ao aplicar {label}: {result.Error}");
        return false;
    }
    return true;
}
