using FluentValidation;

namespace InvestOps.Application.Reconciliation.ImportReconciliation;

public sealed class ImportReconciliationValidator : AbstractValidator<ImportReconciliationRequest>
{
    public ImportReconciliationValidator()
    {
        RuleFor(x => x.Source).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReferenceDate).NotEqual(default(DateOnly));
        RuleFor(x => x.Rows).NotEmpty();
        RuleForEach(x => x.Rows).Must(r => r.CustodianQuantity >= 0).WithMessage("quantidade negativa");
    }
}
