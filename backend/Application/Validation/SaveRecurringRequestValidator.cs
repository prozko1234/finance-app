using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
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
        RuleFor(x => x.StartsOn)
            .Must(d => d != default).WithMessage("Вкажи дату першого платежу.");
        RuleFor(x => x.Interval)
            .InclusiveBetween(1, RecurringExpense.MaxInterval)
            .WithMessage($"Періодичність має бути від 1 до {RecurringExpense.MaxInterval}.");
        RuleFor(x => x.Unit)
            .Must(u => u is null || Enum.TryParse<RecurrenceUnit>(u, ignoreCase: true, out _))
            .WithMessage("Періодичність має бути Week, Month або Year.");
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Kind)
            .Must(k => k is null || k.Equals("Expense", StringComparison.OrdinalIgnoreCase)
                                 || k.Equals("Income", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Тип має бути Expense або Income.");
    }
}
