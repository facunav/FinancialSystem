using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Metrics;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Metrics;

/// <summary>
/// Cubre FinancialMetricsService.GetClassificationCoverageAsync (Patch 0072,
/// PATCH-019 -- reemplaza la versión del Patch 0071). A diferencia de la versión
/// original (que delegaba en un IMovementsQueryService fake, en memoria), este patch
/// cuenta directamente contra la base con COUNT -- así que estos tests siembran
/// BankStatement/Transaction/ClassifiedMovement/ClassifiedMovementItem reales en un
/// AppDbContext InMemory (mismo patrón de siembra directa que
/// ImportConsistencyVerificationTests) y verifican el resultado de consultas EF Core
/// reales, no de un fake.
///
/// FinancialMetricsService es internal -- accesible acá vía
/// [assembly: InternalsVisibleTo("FinancialSystem.Infrastructure.Tests")] en
/// AssemblyInfo.cs (ya existente, no agregado por este patch).
/// </summary>
public class FinancialMetricsServiceClassificationCoverageTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);
    private static readonly DateTime InPeriod = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OutsidePeriod = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetClassificationCoverageAsync_WithNoMovements_ReturnsZeroTotalAndZeroPercentage()
    {
        var result = await CreateService(Guid.NewGuid().ToString()).GetClassificationCoverageAsync(From, To);

        Assert.Equal(From, result.From);
        Assert.Equal(To, result.To);
        Assert.Equal(0, result.TotalMovements);
        Assert.Equal(0, result.ClassifiedMovements);
        Assert.Equal(0, result.PendingMovements);
        Assert.Equal(0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithNoneClassified_ReturnsZeroPercentCoverage()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedPendingBankStatementAsync(dbName);
        await SeedPendingBankStatementAsync(dbName);
        await SeedPendingTransactionAsync(dbName);

        var result = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(3, result.TotalMovements);
        Assert.Equal(0, result.ClassifiedMovements);
        Assert.Equal(3, result.PendingMovements);
        Assert.Equal(0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithPartialClassification_ReturnsRoundedPercentage()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedClassifiedTransactionAsync(dbName);
        await SeedClassifiedTransactionAsync(dbName);
        await SeedClassifiedTransactionAsync(dbName);
        await SeedPendingBankStatementAsync(dbName);

        var result = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(4, result.TotalMovements);
        Assert.Equal(3, result.ClassifiedMovements);
        Assert.Equal(1, result.PendingMovements);
        Assert.Equal(75.0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithAllClassified_Returns100PercentCoverage()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedClassifiedTransactionAsync(dbName);
        await SeedClassifiedBankStatementAsync(dbName);

        var result = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(2, result.TotalMovements);
        Assert.Equal(2, result.ClassifiedMovements);
        Assert.Equal(0, result.PendingMovements);
        Assert.Equal(100m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_ConsidersBothConfirmedAndReviewed_AsClassified()
    {
        // Confirmed y Reviewed son los dos únicos estados de "clasificado" -- sin
        // jerarquía entre ellos (ver ClassifiedMovement.cs) -- ambos deben contar.
        var dbName = Guid.NewGuid().ToString();
        await SeedClassifiedTransactionAsync(dbName, ClassificationStatus.Confirmed);
        await SeedClassifiedTransactionAsync(dbName, ClassificationStatus.Reviewed);

        var result = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(2, result.ClassifiedMovements);
        Assert.Equal(100m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_OnlyCountsMovementsWithinTheRequestedRange()
    {
        // Límites de fecha: un movimiento clasificado y uno pendiente fuera del
        // período pedido no deben contarse -- mismo criterio de rango que
        // MovementLoader.LoadAsync (Date/OriginalDate del propio movimiento, sin
        // ajustes).
        var dbName = Guid.NewGuid().ToString();
        await SeedClassifiedTransactionAsync(dbName, date: InPeriod);
        await SeedClassifiedTransactionAsync(dbName, date: OutsidePeriod);
        await SeedPendingBankStatementAsync(dbName, date: InPeriod);
        await SeedPendingBankStatementAsync(dbName, date: OutsidePeriod);

        var result = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(2, result.TotalMovements);
        Assert.Equal(1, result.ClassifiedMovements);
        Assert.Equal(1, result.PendingMovements);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_IsDeterministic_ForTheSameInput()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedClassifiedTransactionAsync(dbName);
        await SeedPendingBankStatementAsync(dbName);

        var first = await CreateService(dbName).GetClassificationCoverageAsync(From, To);
        var second = await CreateService(dbName).GetClassificationCoverageAsync(From, To);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetPeriodSummaryAsync_StillWorks_NoRegressionFromRemovingIMovementsQueryService()
    {
        // Ausencia de regresiones sobre métricas existentes (sección de tests del
        // patch): quitar IMovementsQueryService del constructor (ya no hace falta --
        // GetClassificationCoverageAsync pasó a consultar la base directamente) no debe
        // afectar ningún método preexistente.
        var summary = await CreateService(Guid.NewGuid().ToString()).GetPeriodSummaryAsync(From, To);

        Assert.Equal(From, summary.From);
        Assert.Equal(To, summary.To);
        Assert.Equal(0, summary.ClassifiedCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FinancialMetricsService CreateService(string dbName) =>
        new(OpenDb(dbName), NullLogger<FinancialMetricsService>.Instance);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task SeedPendingBankStatementAsync(string dbName, DateTime? date = null)
    {
        await using var db = OpenDb(dbName);
        db.BankStatements.Add(new BankStatement
        {
            Date = date ?? InPeriod,
            Concept = "Movimiento bancario pendiente de prueba",
            Amount = -100m,
            Currency = "ARS",
            BankName = "Banco de prueba",
            ExternalId = Guid.NewGuid().ToString(),
            ImportedAtUtc = InPeriod,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPendingTransactionAsync(string dbName, DateTime? date = null)
    {
        await using var db = OpenDb(dbName);
        db.Transactions.Add(new Transaction
        {
            Date = date ?? InPeriod,
            Description = "Movimiento de tarjeta pendiente de prueba",
            Amount = 100m,
            Currency = "ARS",
            CreatedAtUtc = InPeriod,
            ExternalId = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedClassifiedTransactionAsync(
        string dbName, ClassificationStatus status = ClassificationStatus.Confirmed, DateTime? date = null)
    {
        var effectiveDate = date ?? InPeriod;
        await using var db = OpenDb(dbName);
        var transaction = new Transaction
        {
            Date = effectiveDate,
            Description = "Movimiento de tarjeta clasificado de prueba",
            Amount = 100m,
            Currency = "ARS",
            CreatedAtUtc = InPeriod,
            ExternalId = Guid.NewGuid().ToString(),
        };
        db.Transactions.Add(transaction);
        AddClassifiedMovement(db, SourceEntityType.Transaction, transaction.Id, effectiveDate, status);
        await db.SaveChangesAsync();
    }

    private static async Task SeedClassifiedBankStatementAsync(
        string dbName, ClassificationStatus status = ClassificationStatus.Confirmed, DateTime? date = null)
    {
        var effectiveDate = date ?? InPeriod;
        await using var db = OpenDb(dbName);
        var statement = new BankStatement
        {
            Date = effectiveDate,
            Concept = "Movimiento bancario clasificado de prueba",
            Amount = -100m,
            Currency = "ARS",
            BankName = "Banco de prueba",
            ExternalId = Guid.NewGuid().ToString(),
            ImportedAtUtc = InPeriod,
        };
        db.BankStatements.Add(statement);
        AddClassifiedMovement(db, SourceEntityType.BankStatement, statement.Id, effectiveDate, status);
        await db.SaveChangesAsync();
    }

    private static void AddClassifiedMovement(
        AppDbContext db, SourceEntityType sourceEntityType, Guid sourceId, DateTime effectiveDate, ClassificationStatus status)
    {
        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = effectiveDate,
            TotalAmount = 100m,
            Currency = "ARS",
            Description = "Movimiento clasificado de prueba",
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            CategoryId = Guid.NewGuid(),
            Status = status,
            ProcessingSource = ProcessingSource.ManualReview,
            CreatedAt = InPeriod,
            ProcessedAt = InPeriod,
        };
        db.ClassifiedMovements.Add(classifiedMovement);
        db.ClassifiedMovementItems.Add(new ClassifiedMovementItem
        {
            ClassifiedMovementId = classifiedMovement.Id,
            ClassifiedMovement = classifiedMovement,
            SourceEntityType = sourceEntityType,
            SourceId = sourceId,
            Role = MovementRole.Reference,
            OriginalAmount = 100m,
            OriginalDate = effectiveDate,
            OriginalDescription = "Movimiento clasificado de prueba",
            OriginalCurrency = "ARS",
        });
    }
}
