namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed class CsvFormatException(string message) : Exception(message);
