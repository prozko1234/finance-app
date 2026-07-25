using FinanceApp.Application.Contracts;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Назва категорії обов'язкова.")
            .MaximumLength(60);
        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.Color).MaximumLength(9);
    }
}
