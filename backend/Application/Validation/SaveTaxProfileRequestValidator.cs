using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SaveTaxProfileRequestValidator : AbstractValidator<SaveTaxProfileRequest>
{
    public SaveTaxProfileRequestValidator()
    {
        RuleFor(x => x.Regime).NotEmpty();
        RuleFor(x => x.ZusType).NotEmpty();
        RuleFor(x => x.RyczaltRate)
            .InclusiveBetween(0m, 1m).WithMessage("Ставка ryczałt має бути часткою від 0 до 1 (напр. 0.12).");
        RuleFor(x => x.VatRate)
            .InclusiveBetween(0m, 1m).WithMessage("Ставка VAT має бути часткою від 0 до 1 (напр. 0.23).");
        RuleFor(x => x.ZusSocial)
            .GreaterThanOrEqualTo(0m).WithMessage("Соцвнески не можуть бути від'ємними.");
        RuleFor(x => x.HealthContribution)
            .GreaterThanOrEqualTo(0m).WithMessage("Складка здоровотна не може бути від'ємною.");
    }
}
