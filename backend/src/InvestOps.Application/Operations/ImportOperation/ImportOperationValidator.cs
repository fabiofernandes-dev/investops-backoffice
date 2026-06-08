using FluentValidation;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Operations.ImportOperation;

public sealed class ImportOperationValidator : AbstractValidator<ImportOperationRequest>
{
    public ImportOperationValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OperationType)
            .Must(v => Enum.TryParse<OperationType>(v, ignoreCase: false, out _) && !int.TryParse(v, out _))
            .WithMessage("operationType inválido");
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OperationDate).NotEqual(default(DateOnly));
    }
}
