using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace InvestOps.Migrator;

public static class MigrationRunner
{
    public static DatabaseUpgradeResult Run(string connectionString, bool includeSeed)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var schemaResult = Build(connectionString, assembly, "schemaversions", ".Scripts.Schema.").PerformUpgrade();
        if (!schemaResult.Successful || !includeSeed)
            return schemaResult;

        return Build(connectionString, assembly, "seedversions", ".Scripts.Seed.").PerformUpgrade();
    }

    private static UpgradeEngine Build(string connectionString, Assembly assembly, string journalTable, string folderMarker) =>
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, name => name.Contains(folderMarker, StringComparison.Ordinal))
            .JournalToPostgresqlTable("public", journalTable)
            .LogToConsole()
            .Build();
}
