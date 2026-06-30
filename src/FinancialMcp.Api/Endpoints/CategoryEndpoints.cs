using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinancialMcp.Api.Endpoints
{
    public static class CategoryEndpoints
    {
        public static IEndpointRouteBuilder MapCategoryEndpoints(
            this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/categories", GetAll).WithTags("Categories");
            return app;
        }

        private static async Task<IResult> GetAll(
            IApplicationDbContext db,
            CancellationToken ct)
        {
            var categories = await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto(c.Id, c.Name, c.DisplayName, c.SortOrder))
                .ToListAsync(ct);

            return Results.Ok(categories);
        }
    }
}
