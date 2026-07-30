using FinanceApp.Application.Contracts;
using FinanceApp.Domain.Budgeting;
using FluentValidation;

namespace FinanceApp.Application.Validation;

public sealed class SaveEnvelopeRequestValidator : AbstractValidator<SaveEnvelopeRequest>
{
    public SaveEnvelopeRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Назва банки обов'язкова.")
            // 60 to match the column; the index on the name is what keeps two pots apart.
            .MaximumLength(60);

        RuleFor(x => x.Kind)
            .Must(k => Enum.TryParse<BucketKind>(k, out _))
            .WithMessage("Невідомий вид банки.");
    }
}
