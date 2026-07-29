using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SaveTransactionRequestValidator : AbstractValidator<SaveTransactionRequest>
{
    public SaveTransactionRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сума має бути більшою за 0.");
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Matches("^[A-Za-z]{3}$").WithMessage("Валюта має бути 3-літерним ISO-кодом.");
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Merchant).MaximumLength(200);
    }
}
