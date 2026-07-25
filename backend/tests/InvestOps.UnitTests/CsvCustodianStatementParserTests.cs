using System.Text;
using InvestOps.Application.Reconciliation.ImportReconciliation;

namespace InvestOps.UnitTests;

public class CsvCustodianStatementParserTests
{
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid ProductB = Guid.NewGuid();

    private static Stream StreamOf(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Parses_valid_csv_into_rows()
    {
        var csv = $"account_id,product_id,quantity\n{AccountA},{ProductA},100\n{AccountB},{ProductB},250.5\n";

        var rows = CsvCustodianStatementParser.Parse(StreamOf(csv));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new(AccountA, ProductA, 100m), rows[0]);
        Assert.Equal(new(AccountB, ProductB, 250.5m), rows[1]);
    }

    [Fact]
    public void Ignores_blank_and_trailing_lines()
    {
        var csv = $"account_id,product_id,quantity\n\n{AccountA},{ProductA},100\n   \n";

        var rows = CsvCustodianStatementParser.Parse(StreamOf(csv));

        Assert.Single(rows);
    }

    [Fact]
    public void Throws_when_header_missing_or_wrong()
    {
        var csv = $"conta,produto,qtd\n{AccountA},{ProductA},100\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_column_count_wrong()
    {
        var csv = $"account_id,product_id,quantity\n{AccountA},{ProductA}\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_quantity_not_numeric()
    {
        var csv = $"account_id,product_id,quantity\n{AccountA},{ProductA},abc\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_quantity_negative()
    {
        var csv = $"account_id,product_id,quantity\n{AccountA},{ProductA},-5\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_uuid_invalid()
    {
        var csv = $"account_id,product_id,quantity\nnot-a-guid,{ProductA},100\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_pair_duplicated()
    {
        var csv = $"account_id,product_id,quantity\n{AccountA},{ProductA},100\n{AccountA},{ProductA},200\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }

    [Fact]
    public void Throws_when_no_data_rows()
    {
        var csv = "account_id,product_id,quantity\n";

        Assert.Throws<CsvFormatException>(() => CsvCustodianStatementParser.Parse(StreamOf(csv)));
    }
}
