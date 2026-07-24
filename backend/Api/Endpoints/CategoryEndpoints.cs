using FinanceApp.Application.Categories;

namespace FinanceApp.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", async (ICategoryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllAsync(ct))).WithTags("Categories");

        return app;
    }
}
