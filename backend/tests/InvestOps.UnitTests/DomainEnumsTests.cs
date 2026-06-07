using InvestOps.Domain.Enums;

namespace InvestOps.UnitTests;

public class DomainEnumsTests
{
    [Theory]
    [InlineData(nameof(InvestorProfile.Conservative))]
    [InlineData(nameof(InvestorProfile.Moderate))]
    [InlineData(nameof(InvestorProfile.Aggressive))]
    public void InvestorProfile_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(InvestorProfile), name));

    [Theory]
    [InlineData("Imported")]
    [InlineData("Validated")]
    [InlineData("Rejected")]
    [InlineData("Processed")]
    [InlineData("Failed")]
    public void OperationStatus_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(OperationStatus), name));

    [Theory]
    [InlineData("Buy")]
    [InlineData("Sell")]
    [InlineData("Deposit")]
    [InlineData("Withdraw")]
    [InlineData("Income")]
    [InlineData("Fee")]
    public void OperationType_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(OperationType), name));

    [Theory]
    [InlineData("FixedIncome")]
    [InlineData("Fund")]
    [InlineData("Stock")]
    [InlineData("Derivative")]
    public void ProductType_has_spec_values(string name)
        => Assert.True(Enum.IsDefined(typeof(ProductType), name));
}
