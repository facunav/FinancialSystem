using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Planning;

/// <summary>
/// Cubre docs/Epics/Epica-PlanificacionMensual.md, sección 9: sugerencias de
/// coincidencia entre un PlanningItem pendiente y movimientos ya clasificados del
/// mismo mes -- solo lectura, nunca marca nada como pagado por su cuenta.
/// </summary>
public class PlanningMatchSuggestionServiceTests
{
    [Fact]
    public async Task GetSuggestionsAsync_DescripcionContieneElTitulo_SugiereLaCoincidencia()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var monthId = await SeedMonthWithPendingItemAsync(dbName, "Internet");
        await SeedClassifiedMovementAsync(dbName, categoryId, "PAGO INTERNET FIBERTEL",
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 43200m);

        var suggestions = await CreateService(dbName).GetSuggestionsAsync(monthId);

        Assert.NotNull(suggestions);
        var suggestion = Assert.Single(suggestions!);
        Assert.Equal("Internet", suggestion.PlanningItemTitle);
        Assert.Contains("INTERNET", suggestion.MovementDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ItemYaPagado_NuncaSeSugiere()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var monthId = await SeedMonthWithPendingItemAsync(dbName, "Internet", isPaid: true);
        await SeedClassifiedMovementAsync(dbName, categoryId, "PAGO INTERNET FIBERTEL",
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 43200m);

        var suggestions = await CreateService(dbName).GetSuggestionsAsync(monthId);

        Assert.NotNull(suggestions);
        Assert.Empty(suggestions!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_MovimientoFueraDelMes_NoSeSugiere()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var monthId = await SeedMonthWithPendingItemAsync(dbName, "Internet");
        await SeedClassifiedMovementAsync(dbName, categoryId, "PAGO INTERNET FIBERTEL",
            new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), 43200m); // mes anterior al de la planificación

        var suggestions = await CreateService(dbName).GetSuggestionsAsync(monthId);

        Assert.NotNull(suggestions);
        Assert.Empty(suggestions!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_SinCoincidenciaDeTexto_NoSeSugiere()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var monthId = await SeedMonthWithPendingItemAsync(dbName, "Internet");
        await SeedClassifiedMovementAsync(dbName, categoryId, "SUPERMERCADO DIA",
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 15000m);

        var suggestions = await CreateService(dbName).GetSuggestionsAsync(monthId);

        Assert.NotNull(suggestions);
        Assert.Empty(suggestions!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_PlanningMonthInexistente_DevuelveNull()
    {
        var dbName = Guid.NewGuid().ToString();

        var suggestions = await CreateService(dbName).GetSuggestionsAsync(Guid.NewGuid());

        Assert.Null(suggestions);
    }

    private static PlanningMatchSuggestionService CreateService(string dbName) => new(OpenDb(dbName));

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedCategoryAsync(string dbName)
    {
        await using var db = OpenDb(dbName);
        var category = new Category { Name = "Test", DisplayName = "Test" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static async Task<Guid> SeedMonthWithPendingItemAsync(string dbName, string title, bool isPaid = false)
    {
        await using var db = OpenDb(dbName);
        var month = new PlanningMonth { Period = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) };
        db.PlanningMonths.Add(month);
        db.PlanningItems.Add(new PlanningItem
        {
            PlanningMonthId = month.Id,
            Title = title,
            IsPaid = isPaid,
            PaidAt = isPaid ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return month.Id;
    }

    private static async Task SeedClassifiedMovementAsync(
        string dbName, Guid categoryId, string description, DateTime effectiveDate, decimal amount)
    {
        await using var db = OpenDb(dbName);
        db.ClassifiedMovements.Add(new ClassifiedMovement
        {
            EffectiveDate = effectiveDate,
            TotalAmount = amount,
            Currency = "ARS",
            Description = description,
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            CategoryId = categoryId,
            Status = ClassificationStatus.Reviewed,
            ProcessingSource = ProcessingSource.ManualReview,
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
