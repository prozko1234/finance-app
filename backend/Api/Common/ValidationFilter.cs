using FluentValidation;

namespace FinanceApp.Api.Common;

/// Endpoint filter: validates the incoming DTO of type T via the registered IValidator<T>
/// and returns 400 ValidationProblem before the request reaches the handler.
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is not null)
        {
            var result = await validator.ValidateAsync(arg);
            if (!result.IsValid)
                return Results.ValidationProblem(result.ToDictionary());
        }
        return await next(context);
    }
}
