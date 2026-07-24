using FinanceApp.Domain.Common;

namespace FinanceApp.Api.Common;

/// Single place where a domain error type is mapped to an HTTP status + ProblemDetails.
public static class ResultExtensions
{
    public static IResult ToProblem(this Error e) => e.Type switch
    {
        ErrorType.NotFound => Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound),
        ErrorType.Validation => Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest),
        ErrorType.Unsupported => Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        ErrorType.Conflict => Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest),
    };
}
