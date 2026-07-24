using FinanceApp.Api.Contracts;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", async (AppDbContext db) =>
        {
            var cats = await db.Categories
                .OrderBy(c => c.Id)
                .Select(c => new CategoryResponse(c.Id, c.Name, c.Icon))
                .ToListAsync();
            return Results.Ok(cats);
        }).WithTags("Categories");

        return app;
    }
}
