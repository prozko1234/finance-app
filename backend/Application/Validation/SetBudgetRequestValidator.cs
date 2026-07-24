using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SetBudgetRequestValidator : AbstractValidator<SetBudgetRequest>
{
    public SetBudgetRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Бюджет не може бути від'ємним.");
    }
}
