using FinancialSystem.Domain.Planning;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Planning;

/// <summary>
/// Cubre la regla explícita de docs/Epics/Epica-PlanificacionMensual.md, sección
/// 6.5: Available es null cuando ExpectedIncome es null -- nunca se calcula un
/// valor en su lugar.
/// </summary>
public class PlanningQueryServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_SinExpectedIncome_AvailableEsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName, expectedIncome: null, itemAmount: 100m, itemPaid: false);

        var summary = await CreateService(dbName).GetSummaryAsync(monthId);

        Assert.NotNull(summary);
        Assert.Null(summary!.Available);
        Assert.Equal(100m, summary.TotalPlanned);
    }

    [Fact]
    public async Task GetSummaryAsync_ConExpectedIncome_CalculaTotalesYDisponible()
    {
        var dbName = Guid.NewGuid().ToString();
        var monthId = await SeedMonthAsync(dbName, expectedIncome: 1000m, itemAmount: 300m, itemPaid: true);

        var summary = await CreateService(dbName).GetSummaryAsync(monthId);

        Assert.NotNull(summary);
        Assert.Equal(300m, summary!.TotalPlanned);
        Assert.Equal(300m, summary.Paid);
        Assert.Equal(0m, summary.Pending);
        Assert.Equal(700m, summary.Available);
    }

    [Fact]
    public async Task GetSummaryAsync_PlanningMonthInexistente_DevuelveNull()
    {
        var dbName = Guid.NewGuid().ToString();

        var summary = await CreateService(dbName).GetSummaryAsync(Guid.NewGuid());

        Assert.Null(summary);
    }

    private static PlanningQueryService CreateService(string dbName) => new(OpenDb(dbName));

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedMonthAsync(
        string dbName, decimal? expectedIncome, decimal itemAmount, bool itemPaid)
    {
        await using var db = OpenDb(dbName);
        var month = new PlanningMonth
        {
            Period = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpectedIncome = expectedIncome,
        };
        db.PlanningMonths.Add(month);
        db.PlanningItems.Add(new PlanningItem
        {
            PlanningMonthId = month.Id,
            Title = "Internet",
            ExpectedAmount = itemAmount,
            IsPaid = itemPaid,
            PaidAt = itemPaid ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return month.Id;
    }
}
