using FinancialSystem.Application.Metrics;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Metrics;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Metrics;

/// <summary>
/// Cubre FinancialMetricsService.GetClassificationCoverageAsync (Patch 0068,
/// PATCH-019) de forma aislada, con un IMovementsQueryService fake -- la corrección de
/// MovementsQueryService en sí (unión pendientes/clasificados, IReviewEngine,
/// IClassificationSuggestionService) es código preexistente sin cambios en este
/// patch, ya asumido correcto por el resto del sistema (pantalla Movimientos,
/// AuditEndpoints); acá se prueba exclusivamente la lógica nueva: contar total/
/// clasificados y calcular el porcentaje a partir de lo que esa fuente devuelve.
///
/// FinancialMetricsService es internal -- accesible acá vía
/// [assembly: InternalsVisibleTo("FinancialSystem.Infrastructure.Tests")] en
/// AssemblyInfo.cs (ya existente, no agregado por este patch).
/// </summary>
public class FinancialMetricsServiceClassificationCoverageTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public async Task GetClassificationCoverageAsync_WithNoMovements_ReturnsZeroTotalAndZeroPercentage()
    {
        var service = CreateService([]);

        var result = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(From, result.From);
        Assert.Equal(To, result.To);
        Assert.Equal(0, result.TotalMovements);
        Assert.Equal(0, result.ClassifiedMovements);
        Assert.Equal(0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithNoneClassified_ReturnsZeroPercentCoverage()
    {
        var service = CreateService([Pending(), Pending(), Pending()]);

        var result = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(3, result.TotalMovements);
        Assert.Equal(0, result.ClassifiedMovements);
        Assert.Equal(0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithPartialClassification_ReturnsRoundedPercentage()
    {
        var service = CreateService([Classified(), Classified(), Classified(), Pending()]);

        var result = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(4, result.TotalMovements);
        Assert.Equal(3, result.ClassifiedMovements);
        Assert.Equal(75.0m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_WithAllClassified_Returns100PercentCoverage()
    {
        var service = CreateService([Classified(), Classified()]);

        var result = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(2, result.TotalMovements);
        Assert.Equal(2, result.ClassifiedMovements);
        Assert.Equal(100m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_ConsidersBothConfirmedAndReviewed_AsClassified()
    {
        // Confirmed y Reviewed son los dos únicos estados de "clasificado" -- sin
        // jerarquía entre ellos (ver ClassifiedMovement.cs) -- ambos deben contar.
        var service = CreateService(
            [Classified(ClassificationStatus.Confirmed), Classified(ClassificationStatus.Reviewed)]);

        var result = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(2, result.ClassifiedMovements);
        Assert.Equal(100m, result.CoveragePercentage);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_PassesTheExactRequestedDateRange_ToTheMovementsQuery()
    {
        // Límites de fechas: el período efectivamente consultado es exactamente el
        // pedido por el caller, sin ajustes ni corrimientos.
        (DateOnly From, DateOnly To)? captured = null;
        var fake = new FakeMovementsQueryService([], (from, to) => captured = (from, to));
        var service = new FinancialMetricsService(OpenDb(), fake, NullLogger<FinancialMetricsService>.Instance);

        var from = new DateOnly(2026, 3, 1);
        var to = new DateOnly(2026, 3, 31);
        await service.GetClassificationCoverageAsync(from, to);

        Assert.Equal((from, to), captured);
    }

    [Fact]
    public async Task GetClassificationCoverageAsync_IsDeterministic_ForTheSameInput()
    {
        var service = CreateService([Classified(), Pending()]);

        var first = await service.GetClassificationCoverageAsync(From, To);
        var second = await service.GetClassificationCoverageAsync(From, To);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetPeriodSummaryAsync_StillWorks_NoRegressionFromTheNewConstructorParameter()
    {
        // Ausencia de regresiones sobre métricas existentes (sección de tests del
        // patch): agregar IMovementsQueryService al constructor no debe afectar
        // ningún método preexistente -- GetPeriodSummaryAsync sigue leyendo
        // ClassifiedMovements directamente, sin pasar por IMovementsQueryService.
        await using var db = OpenDb();
        var service = new FinancialMetricsService(db, new FakeMovementsQueryService([]), NullLogger<FinancialMetricsService>.Instance);

        var summary = await service.GetPeriodSummaryAsync(From, To);

        Assert.Equal(From, summary.From);
        Assert.Equal(To, summary.To);
        Assert.Equal(0, summary.ClassifiedCount);
    }

    private static FinancialMetricsService CreateService(IReadOnlyList<MovementView> movements) =>
        new(OpenDb(), new FakeMovementsQueryService(movements), NullLogger<FinancialMetricsService>.Instance);

    private static AppDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MovementView Pending() => new(
        SourceId: Guid.NewGuid(),
        Date: From.ToDateTime(TimeOnly.MinValue),
        Description: "Movimiento pendiente de prueba",
        Amount: 100m,
        Currency: "ARS",
        Source: MovementSource.BankDebit,
        FinancialAccountId: null,
        Status: null,
        CategoryId: null,
        CounterpartyId: null,
        MovementType: null,
        FinancialImpact: null,
        Warning: null,
        Suggestions: [],
        Merchant: null,
        MerchantAtUtc: null,
        EffectiveDate: null);

    private static MovementView Classified(ClassificationStatus status = ClassificationStatus.Confirmed) => new(
        SourceId: Guid.NewGuid(),
        Date: From.ToDateTime(TimeOnly.MinValue),
        Description: "Movimiento clasificado de prueba",
        Amount: 100m,
        Currency: "ARS",
        Source: MovementSource.BankDebit,
        FinancialAccountId: null,
        Status: status,
        CategoryId: Guid.NewGuid(),
        CounterpartyId: null,
        MovementType: MovementType.Purchase,
        FinancialImpact: FinancialImpact.Expense,
        Warning: null,
        Suggestions: [],
        Merchant: null,
        MerchantAtUtc: null,
        EffectiveDate: From.ToDateTime(TimeOnly.MinValue));

    private sealed class FakeMovementsQueryService(
        IReadOnlyList<MovementView> movements,
        Action<DateOnly, DateOnly>? onGetAsync = null) : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default)
        {
            onGetAsync?.Invoke(from, to);
            return Task.FromResult(movements);
        }
    }
}
