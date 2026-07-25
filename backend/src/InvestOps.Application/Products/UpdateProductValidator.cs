using FluentValidation;
using InvestOps.Domain.Enums;

namespace InvestOps.Application.Products;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ProductType)
            .Must(v => Enum.TryParse<ProductType>(v, ignoreCase: false, out _) && !int.TryParse(v, out _))
            .WithMessage("productType inválido");
        RuleFor(x => x.RiskLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.LiquidityDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Ticker).MaximumLength(20);
    }
}
