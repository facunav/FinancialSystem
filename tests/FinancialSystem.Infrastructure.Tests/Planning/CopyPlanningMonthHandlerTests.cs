using FinancialSystem.Application.Planning.Commands;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Planning;

/// <summary>
/// Cubre las reglas de negocio explícitas de docs/Epics/Epica-PlanificacionMensual.md,
/// sección 6.2: los PlanningItem copiados vuelven a Pending (IsPaid=false,
/// PaidAt=null), ExpectedAmount/DueDate se preservan como referencia, el mes de
/// origen queda intacto (independencia total tras la copia), y ExpectedIncome
/// nunca se copia. Alcance mínimo de este patch (0043): no repite acá lo que ya
/// cubriría un test de cada handler restante del Patch 0042.
/// </summary>
public class CopyPlanningMonthHandlerTests
{
    [Fact]
    public async Task Handle_CopiaItems_QuedanPendientesConMontoYFechaPreservados()
    {
        var dbName = Guid.NewGuid().ToString();
        var sourceId = await SeedMonthWithPaidItemAsync(dbName);

        var targetPeriod = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await CreateHandler(dbName).Handle(new CopyPlanningMonthCommand(sourceId, targetPeriod));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ItemsCopied);

        await using var db = OpenDb(dbName);
        var copiedItem = await db.PlanningItems.SingleAsync(i => i.PlanningMonthId == result.PlanningMonthId);

        Assert.False(copiedItem.IsPaid);
        Assert.Null(copiedItem.PaidAt);
        Assert.Equal(150m, copiedItem.ExpectedAmount);
        Assert.Equal(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), copiedItem.DueDate);
    }

    [Fact]
    public async Task Handle_NoCopiaExpectedIncome()
    {
        var dbName = Guid.NewGuid().ToString();
        var sourceId = await SeedMonthWithPaidItemAsync(dbName, expectedIncome: 500000m);

        var result = await CreateHandler(dbName).Handle(new CopyPlanningMonthCommand(
            sourceId, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var target = await db.PlanningMonths.SingleAsync(m => m.Id == result.PlanningMonthId);
        Assert.Null(target.ExpectedIncome);
    }

    [Fact]
    public async Task Handle_MesOrigenQuedaIntactoDespuesDeCopiar()
    {
        var dbName = Guid.NewGuid().ToString();
        var sourceId = await SeedMonthWithPaidItemAsync(dbName);

        await CreateHandler(dbName).Handle(new CopyPlanningMonthCommand(
            sourceId, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        await using var db = OpenDb(dbName);
        var sourceItem = await db.PlanningItems.SingleAsync(i => i.PlanningMonthId == sourceId);
        Assert.True(sourceItem.IsPaid);
        Assert.NotNull(sourceItem.PaidAt);
    }

    [Fact]
    public async Task Handle_YaExisteUnPlanningMonthParaElPeriodoDestino_Falla()
    {
        var dbName = Guid.NewGuid().ToString();
        var sourceId = await SeedMonthWithPaidItemAsync(dbName);
        var targetPeriod = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedEmptyMonthAsync(dbName, targetPeriod);

        var result = await CreateHandler(dbName).Handle(new CopyPlanningMonthCommand(sourceId, targetPeriod));

        Assert.False(result.IsSuccess);
        Assert.Equal(CopyPlanningMonthFailureReason.TargetPeriodAlreadyExists, result.FailureReason);
    }

    [Fact]
    public async Task Handle_NoExisteElPlanningMonthDeOrigen_Falla()
    {
        var dbName = Guid.NewGuid().ToString();

        var result = await CreateHandler(dbName).Handle(new CopyPlanningMonthCommand(
            Guid.NewGuid(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.False(result.IsSuccess);
        Assert.Equal(CopyPlanningMonthFailureReason.SourceNotFound, result.FailureReason);
    }

    private static CopyPlanningMonthHandler CreateHandler(string dbName) => new(OpenDb(dbName));

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedMonthWithPaidItemAsync(string dbName, decimal? expectedIncome = null)
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
            ExpectedAmount = 150m,
            DueDate = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            IsPaid = true,
            PaidAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        return month.Id;
    }

    private static async Task SeedEmptyMonthAsync(string dbName, DateTime period)
    {
        await using var db = OpenDb(dbName);
        db.PlanningMonths.Add(new PlanningMonth { Period = period });
        await db.SaveChangesAsync();
    }
}
