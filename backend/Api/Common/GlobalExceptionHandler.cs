using Microsoft.AspNetCore.Diagnostics;

namespace FinanceApp.Api.Common;

/// Catches any UNEXPECTED exception (bug, DB failure), logs it and returns 500 to the
/// client as ProblemDetails — without leaking a stack trace.
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Unhandled exception at {Path}", ctx.Request.Path);
        await Results.Problem(
            title: "Внутрішня помилка сервера",
            statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(ctx);
        return true;
    }
}
