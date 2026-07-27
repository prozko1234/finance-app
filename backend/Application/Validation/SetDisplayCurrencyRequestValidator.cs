using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SetDisplayCurrencyRequestValidator : AbstractValidator<SetDisplayCurrencyRequest>
{
    public SetDisplayCurrencyRequestValidator()
    {
        // Shape only — whether a rate actually exists for it is the service's call.
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Matches("^[A-Za-z]{3}$").WithMessage("Валюта має бути 3-літерним ISO-кодом.");
    }
}
