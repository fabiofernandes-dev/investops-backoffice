namespace InvestOps.Domain.Operations;

public static class OperationCalculator
{
    public static decimal GrossValue(decimal quantity, decimal unitPrice) => quantity * unitPrice;

    public static DateOnly SettlementDate(DateOnly operationDate, int liquidityDays)
        => operationDate.AddDays(liquidityDays);
}
