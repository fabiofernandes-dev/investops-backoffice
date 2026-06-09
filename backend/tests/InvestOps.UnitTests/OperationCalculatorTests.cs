using InvestOps.Domain.Operations;

namespace InvestOps.UnitTests;

public class OperationCalculatorTests
{
    [Fact]
    public void GrossValue_is_quantity_times_unit_price()
        => Assert.Equal(2540.00m, OperationCalculator.GrossValue(100m, 25.40m));

    [Fact]
    public void SettlementDate_is_operation_date_plus_liquidity_days()
        => Assert.Equal(new DateOnly(2026, 6, 10),
            OperationCalculator.SettlementDate(new DateOnly(2026, 6, 7), liquidityDays: 3));

    [Fact]
    public void SettlementDate_with_zero_liquidity_is_same_day()
        => Assert.Equal(new DateOnly(2026, 6, 7),
            OperationCalculator.SettlementDate(new DateOnly(2026, 6, 7), liquidityDays: 0));
}
