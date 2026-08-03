using FinanceApp.Api.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Import;

namespace FinanceApp.Api.Endpoints;

/// Bringing a bank export in. Two steps on purpose: the file is read and shown first, and
/// nothing is written until the user has looked at what the reader made of it.
public static class ImportEndpoints
{
    /// Big enough for years of statements, small enough that a mistaken upload of a video
    /// does not sit in memory. A bank CSV of a whole year is well under a megabyte.
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import").WithTags("Import");

        group.MapPost("/preview", async (HttpRequest request, IImportService import, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.Problem(title: "Очікується файл", statusCode: StatusCodes.Status400BadRequest);

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.Problem(title: "Файл не додано", statusCode: StatusCodes.Status400BadRequest);

            if (file.Length > MaxUploadBytes)
                return Results.Problem(title: "Файл завеликий", statusCode: StatusCodes.Status413PayloadTooLarge);

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);

            var result = await import.PreviewAsync(memory.ToArray(), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        }).DisableAntiforgery();

        group.MapPost("/commit", async (
            CommitImportRequest req, IImportService import, CancellationToken ct) =>
        {
            var result = await import.CommitAsync(req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        return app;
    }
}
