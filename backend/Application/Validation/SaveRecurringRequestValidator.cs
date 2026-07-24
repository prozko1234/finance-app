using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SaveRecurringRequestValidator : AbstractValidator<SaveRecurringRequest>
{
    public SaveRecurringRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Сума має бути більшою за 0.");
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Matches("^[A-Za-z]{3}$").WithMessage("Валюта має бути 3-літерним ISO-кодом.");
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.DayOfMonth)
            .InclusiveBetween(1, 31).WithMessage("День місяця має бути від 1 до 31.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
