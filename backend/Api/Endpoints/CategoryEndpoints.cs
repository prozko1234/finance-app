using FinanceApp.Api.Common;
using FinanceApp.Application.Categories;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;

namespace FinanceApp.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/categories").WithTags("Categories");

        // ?kind=Income narrows it to one side of the ledger. Absent — both, which is what the
        // settings screen lists.
        g.MapGet("/", async (string? kind, ICategoryService svc, CancellationToken ct) =>
        {
            if (kind is not null && !Enum.TryParse<CategoryKind>(kind, ignoreCase: true, out var parsed))
                return Results.BadRequest();

            return Results.Ok(await svc.GetAllAsync(
                kind is null ? null : Enum.Parse<CategoryKind>(kind, ignoreCase: true), ct));
        });

        g.MapGet("/frequent", async (ICategoryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetFrequentAsync(ct: ct)));

        g.MapPost("/", async (SaveCategoryRequest req, ICategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/categories/{r.Value!.Id}", r.Value)
                : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveCategoryRequest>>();

        g.MapPut("/{id:int}", async (int id, SaveCategoryRequest req, ICategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(id, req, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : r.Error.ToProblem();
        }).AddEndpointFilter<ValidationFilter<SaveCategoryRequest>>();

        g.MapDelete("/{id:int}", async (int id, ICategoryService svc, CancellationToken ct) =>
        {
            var r = await svc.DeleteAsync(id, ct);
            return r.IsSuccess ? Results.NoContent() : r.Error.ToProblem();
        });

        return app;
    }
}
