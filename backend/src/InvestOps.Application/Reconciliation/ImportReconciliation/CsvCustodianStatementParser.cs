using System.Globalization;
using InvestOps.Domain.Reconciliation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public static class CsvCustodianStatementParser
{
    private static readonly string[] Header = ["account_id", "product_id", "quantity"];

    public static IReadOnlyList<CustodianRow> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Split('\n').Select(l => l.Trim('\r', ' ', '\t')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) throw new CsvFormatException("CSV vazio.");

        var head = lines[0].Split(',', StringSplitOptions.TrimEntries);
        if (!head.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new CsvFormatException("Header esperado: account_id,product_id,quantity.");

        var rows = new List<CustodianRow>();
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',', StringSplitOptions.TrimEntries);
            if (cols.Length != 3) throw new CsvFormatException($"Linha com {cols.Length} colunas (esperado 3): {line}");
            if (!Guid.TryParse(cols[0], out var accountId)) throw new CsvFormatException($"account_id inválido: {cols[0]}");
            if (!Guid.TryParse(cols[1], out var productId)) throw new CsvFormatException($"product_id inválido: {cols[1]}");
            if (!decimal.TryParse(cols[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) || qty < 0)
                throw new CsvFormatException($"quantity inválida: {cols[2]}");
            if (!seen.Add((accountId, productId)))
                throw new CsvFormatException($"Par duplicado: {accountId},{productId}");
            rows.Add(new CustodianRow(accountId, productId, qty));
        }
        if (rows.Count == 0) throw new CsvFormatException("Nenhuma linha de dados.");
        return rows;
    }
}
