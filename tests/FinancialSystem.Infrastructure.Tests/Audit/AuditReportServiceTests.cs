using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Memory;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Audit;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Audit;

/// <summary>
/// Cobertura de <see cref="AuditReportService"/> -- documenta el comportamiento actual
/// de sus 4 métodos públicos (<see cref="AuditReportService.BuildSuspiciousMovementsReportAsync"/>,
/// <see cref="AuditReportService.BuildMisclassifiedMovementsReportAsync"/>,
/// <see cref="AuditReportService.GetMisclassifiedMovementsAsync"/>,
/// <see cref="AuditReportService.BuildFullAuditReportAsync"/>), pensada como red de
/// seguridad para el futuro PATCH-041 (ver PATCH-038, Epic W).
///
/// AuditReportService es <c>public sealed</c> -- no necesita InternalsVisibleTo. Sus 3
/// dependencias de interfaz (<see cref="IReviewEngine"/>, <see cref="IMovementsQueryService"/>,
/// <see cref="IClassificationSuggestionService"/>) se reemplazan acá por fakes propios de
/// este archivo, para controlar exactamente qué movimientos/grupos sospechosos/sugerencias
/// ve el servicio en cada escenario; la cuarta dependencia (<see cref="IApplicationDbContext"/>,
/// usada directamente con LINQ sobre Categories/Counterparties/FinancialAccounts/
/// Investigations/MovementAuditDecisions) usa el mismo patrón InMemory ya establecido en
/// el resto de la suite (ClassifyMovementHandlerTests, MovementLoaderTests, ReviewEngineTests).
///
/// Ningún test acá modifica AuditReportService ni corrige comportamiento -- ver PATCH-038.
/// </summary>
public class AuditReportServiceTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);
    private static readonly DateTime Day = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    // ── Fakes de las 3 dependencias de interfaz ──────────────────────────────

    private sealed class FakeReviewEngine(ReviewResult result) : IReviewEngine
    {
        public Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakeMovementsQueryService(IReadOnlyList<MovementView> movements) : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default)
            => Task.FromResult(movements);
    }

    private sealed class FakeClassificationSuggestionService(IReadOnlyList<ClassificationSuggestionSet> sets)
        : IClassificationSuggestionService
    {
        public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
            IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default)
            => Task.FromResult(sets);
    }

    // ── Helpers de construcción ──────────────────────────────────────────────

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ReviewResult EmptyReviewResult() => new()
    {
        PeriodStart = From,
        PeriodEnd = To,
        Movements = [],
        Suspicious = [],
        Elapsed = TimeSpan.Zero,
    };

    private static ReviewResult ReviewResultWith(params SuspiciousGroup[] groups) => new()
    {
        PeriodStart = From,
        PeriodEnd = To,
        Movements = [],
        Suspicious = groups,
        Elapsed = TimeSpan.Zero,
    };

    private static FinancialMovement SuspiciousMovement(
        decimal amount = 100m, Guid? financialAccountId = null, string description = "Movimiento sospechoso") => new()
    {
        SourceId = Guid.NewGuid(),
        Date = Day,
        Description = description,
        Amount = amount,
        Source = MovementSource.BankDebit,
        FinancialAccountId = financialAccountId,
    };

    private static SuspiciousGroup DuplicateGroup(params FinancialMovement[] movements) => new()
    {
        Movements = movements,
        Reason = SuspicionReason.PossibleDuplicate,
        Description = "Grupo de prueba (duplicado)",
    };

    private static AuditReportService CreateService(
        string dbName,
        ReviewResult? reviewResult = null,
        IReadOnlyList<MovementView>? movements = null,
        IReadOnlyList<ClassificationSuggestionSet>? suggestions = null) =>
        new(
            new FakeReviewEngine(reviewResult ?? EmptyReviewResult()),
            new FakeMovementsQueryService(movements ?? []),
            new FakeClassificationSuggestionService(suggestions ?? []),
            OpenDb(dbName));

    private static MovementView PendingMovement(
        Guid? sourceId = null, decimal amount = 100m, string description = "Pendiente") => new(
        sourceId ?? Guid.NewGuid(), Day, description, amount, "ARS", MovementSource.BankDebit,
        FinancialAccountId: null, Status: null, CategoryId: null, CounterpartyId: null,
        MovementType: null, FinancialImpact: null, Warning: null, Suggestions: [],
        Merchant: null, MerchantAtUtc: null, EffectiveDate: null);

    private static MovementView ClassifiedMovementView(
        Guid categoryId,
        Guid? sourceId = null,
        decimal amount = 100m,
        string description = "Clasificado",
        Guid? counterpartyId = null,
        MovementType movementType = MovementType.Purchase,
        FinancialImpact financialImpact = FinancialImpact.Expense,
        ClassificationStatus status = ClassificationStatus.Confirmed) => new(
        sourceId ?? Guid.NewGuid(), Day, description, amount, "ARS", MovementSource.CreditCard,
        FinancialAccountId: null, Status: status, CategoryId: categoryId, CounterpartyId: counterpartyId,
        MovementType: movementType, FinancialImpact: financialImpact, Warning: null, Suggestions: [],
        Merchant: null, MerchantAtUtc: null, EffectiveDate: null);

    private static ClassificationSuggestionSet SuggestionSet(Guid sourceId, params ClassificationSuggestion[] suggestions) =>
        new(SourceEntityType.Transaction, sourceId, suggestions);

    private static async Task SeedCategoryAsync(string dbName, Guid id, string displayName)
    {
        await using var db = OpenDb(dbName);
        db.Categories.Add(new Category { Id = id, Name = displayName, DisplayName = displayName });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCounterpartyAsync(
        string dbName, Guid id, string name, Guid? defaultCategoryId = null,
        MovementType? defaultMovementType = null, FinancialImpact? defaultFinancialImpact = null)
    {
        await using var db = OpenDb(dbName);
        db.Counterparties.Add(new Counterparty
        {
            Id = id,
            Name = name,
            Type = CounterpartyType.Business,
            DefaultCategoryId = defaultCategoryId,
            DefaultMovementType = defaultMovementType,
            DefaultFinancialImpact = defaultFinancialImpact,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedFinancialAccountAsync(string dbName, Guid id, string name)
    {
        await using var db = OpenDb(dbName);
        db.FinancialAccounts.Add(new FinancialAccount { Id = id, Name = name, Type = FinancialAccountType.Bank });
        await db.SaveChangesAsync();
    }

    private static async Task SeedInvestigationAsync(string dbName, InvestigationStatus status, string question = "Pregunta de prueba")
    {
        await using var db = OpenDb(dbName);
        db.Investigations.Add(new Investigation { Question = question, Status = status, CreatedAt = Day, UpdatedAt = Day });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAuditDecisionAsync(string dbName, SourceEntityType sourceEntityType, Guid sourceId, DateTime reviewedAtUtc)
    {
        await using var db = OpenDb(dbName);
        db.MovementAuditDecisions.Add(new MovementAuditDecision
        {
            SourceEntityType = sourceEntityType,
            SourceId = sourceId,
            ReviewedAtUtc = reviewedAtUtc,
        });
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildSuspiciousMovementsReportAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_SinGruposSospechosos_DevuelveMensajeSinHallazgos()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.Contains("No se detectaron movimientos sospechosos", report);
        Assert.Contains(From.ToString("dd/MM/yyyy"), report);
        Assert.Contains(To.ToString("dd/MM/yyyy"), report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_ConUnGrupo_IncluyeConteoYDetalle()
    {
        var dbName = Guid.NewGuid().ToString();
        var group = DuplicateGroup(SuspiciousMovement(), SuspiciousMovement());
        var service = CreateService(dbName, reviewResult: ReviewResultWith(group));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.Contains("1 grupo(s) sospechoso(s), 2 movimiento(s) involucrado(s)", report);
        Assert.Contains("PossibleDuplicate", report);
        Assert.Contains(group.Description, report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_ConMultiplesGrupos_LosIncluyeATodos()
    {
        var dbName = Guid.NewGuid().ToString();
        var groupA = new SuspiciousGroup
        {
            Movements = [SuspiciousMovement(description: "Grupo A")],
            Reason = SuspicionReason.PossibleDuplicate,
            Description = "Descripcion grupo A",
        };
        var groupB = new SuspiciousGroup
        {
            Movements = [SuspiciousMovement(description: "Grupo B")],
            Reason = SuspicionReason.SplitTransaction,
            Description = "Descripcion grupo B",
        };
        var service = CreateService(dbName, reviewResult: ReviewResultWith(groupA, groupB));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.Contains("2 grupo(s) sospechoso(s)", report);
        Assert.Contains("Descripcion grupo A", report);
        Assert.Contains("Descripcion grupo B", report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_FiltraPorCuentaFinanciera_ExcluyeGruposDeOtrasCuentas()
    {
        var dbName = Guid.NewGuid().ToString();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var groupA = DuplicateGroup(SuspiciousMovement(financialAccountId: accountA, description: "De cuenta A"));
        var groupB = DuplicateGroup(SuspiciousMovement(financialAccountId: accountB, description: "De cuenta B"));
        var service = CreateService(dbName, reviewResult: ReviewResultWith(groupA, groupB));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, accountA);

        Assert.Contains("1 grupo(s) sospechoso(s)", report);
        Assert.Contains("De cuenta A", report);
        Assert.DoesNotContain("De cuenta B", report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_MovimientoSinCuentaAsignada_MuestraSinAsignar()
    {
        var dbName = Guid.NewGuid().ToString();
        var group = DuplicateGroup(SuspiciousMovement(financialAccountId: null));
        var service = CreateService(dbName, reviewResult: ReviewResultWith(group));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.Contains("(sin asignar)", report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_CuentaAsignadaSinResolverEnLaBase_MuestraDesconocida()
    {
        // La cuenta financiera referenciada por el movimiento no existe en la base --
        // comportamiento actual: se documenta como "(desconocida)", no falla.
        var dbName = Guid.NewGuid().ToString();
        var group = DuplicateGroup(SuspiciousMovement(financialAccountId: Guid.NewGuid()));
        var service = CreateService(dbName, reviewResult: ReviewResultWith(group));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.Contains("(desconocida)", report);
    }

    [Fact]
    public async Task BuildSuspiciousMovementsReportAsync_ElOrdenDeLosGruposSigueElOrdenDeEntrada()
    {
        // BuildSuspiciousMovementsReportAsync no reordena los grupos -- el orden de
        // salida es exactamente el de ReviewResult.Suspicious.
        var dbName = Guid.NewGuid().ToString();
        var first = DuplicateGroup(SuspiciousMovement(description: "Primero"));
        var second = DuplicateGroup(SuspiciousMovement(description: "Segundo"));
        var service = CreateService(dbName, reviewResult: ReviewResultWith(first, second));

        var report = await service.BuildSuspiciousMovementsReportAsync(From, To, null);

        Assert.True(report.IndexOf("Grupo 1", StringComparison.Ordinal) < report.IndexOf("Grupo 2", StringComparison.Ordinal));
        Assert.True(report.IndexOf("Primero", StringComparison.Ordinal) < report.IndexOf("Segundo", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildMisclassifiedMovementsReportAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_SinMovimientosClasificados_DevuelveMensajeNoHayClasificados()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName, movements: [PendingMovement()]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("No hay movimientos clasificados", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_ClasificadosSinMotivos_DevuelveMensajeNoSeEncontraron()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var movement = ClassifiedMovementView(categoryId);
        var service = CreateService(dbName, movements: [movement]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("No se encontraron movimientos potencialmente mal clasificados", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_SugerenciaDeCategoriaDistinta_IncluyeElMotivo()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "Motivo de prueba");
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("1 movimiento(s) potencialmente mal clasificado(s)", report);
        Assert.Contains("Alimentacion", report);
        Assert.Contains("Salud", report);
        Assert.Contains("Historial de descripción idéntica", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_TieneMatchCountWinnerCountYReason_LosIncluyeEnElTexto()
    {
        // Tier 0 (AGENT-002/003/004): MatchCount/WinnerCount ya se calculaban y ya
        // viajaban en Motivo antes de este cambio -- lo único nuevo acá es que el texto
        // que recibe el cliente MCP (FormatMisclassifiedMovementsReport) ahora los
        // imprime, junto con el mismo Reason que ya arma
        // ClassificationSuggestionService.BuildReason (sin alterar ese método).
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Otros");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Ingresos");
        var movement = ClassifiedMovementView(currentCategoryId, description: "INTERESES GANADOS");
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.Medium,
            "5 clasificaciones históricas con la misma descripción; mayoría amplia (4 de 5) coincide en este valor.",
            MatchCount: 5, WinnerCount: 4);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("- Evidencia: 4 de 5", report);
        Assert.Contains("- Reason: 5 clasificaciones históricas con la misma descripción; mayoría amplia (4 de 5) coincide en este valor.", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_DefaultDeContraparteDistinto_IncluyeElMotivo()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var defaultCategoryId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, defaultCategoryId, "Salud");
        await SeedCounterpartyAsync(dbName, counterpartyId, "Farmacia Amancay", defaultCategoryId: defaultCategoryId);
        var movement = ClassifiedMovementView(currentCategoryId, counterpartyId: counterpartyId);
        var service = CreateService(dbName, movements: [movement]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("Default configurado en la contraparte", report);
        Assert.Contains("Salud", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_SugerenciaDeBajaConfianza_NoGeneraMotivo()
    {
        // Comportamiento actual documentado tal cual: BuildSuggestionMotivos descarta
        // explícitamente las sugerencias con Confidence == Low.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.Low, "Motivo de baja confianza");
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("No se encontraron movimientos potencialmente mal clasificados", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_SugerenciaIgualAlValorActual_NoGeneraMotivo()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var movement = ClassifiedMovementView(categoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, categoryId, SuggestionConfidence.High, "Misma categoria");
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("No se encontraron movimientos potencialmente mal clasificados", report);
    }

    [Fact]
    public async Task BuildMisclassifiedMovementsReportAsync_MultiplesMovimientosConMotivos_LosIncluyeATodos()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryA = Guid.NewGuid();
        var categoryB = Guid.NewGuid();
        var suggestedCategory = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryA, "Alimentacion");
        await SeedCategoryAsync(dbName, categoryB, "Transporte");
        await SeedCategoryAsync(dbName, suggestedCategory, "Salud");
        var movementA = ClassifiedMovementView(categoryA, description: "Movimiento A");
        var movementB = ClassifiedMovementView(categoryB, description: "Movimiento B");
        var suggestionA = new ClassificationSuggestion(SuggestionDimension.Category, suggestedCategory, SuggestionConfidence.High, "m");
        var suggestionB = new ClassificationSuggestion(SuggestionDimension.Category, suggestedCategory, SuggestionConfidence.Medium, "m");
        var service = CreateService(
            dbName,
            movements: [movementA, movementB],
            suggestions: [SuggestionSet(movementA.SourceId, suggestionA), SuggestionSet(movementB.SourceId, suggestionB)]);

        var report = await service.BuildMisclassifiedMovementsReportAsync(From, To, null);

        Assert.Contains("2 movimiento(s) potencialmente mal clasificado(s)", report);
        Assert.Contains("Movimiento A", report);
        Assert.Contains("Movimiento B", report);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetMisclassifiedMovementsAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_SinHallazgos_DevuelveListaVacia()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var service = CreateService(dbName, movements: [ClassifiedMovementView(categoryId)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_ConHallazgoSinRevisar_ReviewedEsFalse()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        var item = Assert.Single(result);
        Assert.Equal(movement.SourceId, item.SourceId);
        Assert.False(item.Reviewed);
        Assert.Null(item.ReviewedAtUtc);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_ConHallazgoYaRevisado_ReviewedEsTrueConFecha()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        var reviewedAt = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
        await SeedAuditDecisionAsync(dbName, SourceEntityType.Transaction, movement.SourceId, reviewedAt);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        var item = Assert.Single(result);
        Assert.True(item.Reviewed);
        Assert.Equal(reviewedAt, item.ReviewedAtUtc);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_NoOcultaHallazgosRevisados_SigueApareciendoEnLaLista()
    {
        // Ver doc-comment de GetMisclassifiedMovementsAsync: "no filtra a los
        // revisados, los sigue devolviendo". No es una omisión -- es la regla.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        await SeedAuditDecisionAsync(dbName, SourceEntityType.Transaction, movement.SourceId, Day);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_LaBusquedaDeRevisionSoloComparaSourceId_NoSourceEntityType()
    {
        // Comportamiento actual documentado tal cual, sin corregir: la consulta a
        // MovementAuditDecisions en este método filtra únicamente por SourceId
        // (Where(r => sourceIds.Contains(r.SourceId))), sin comparar SourceEntityType
        // -- a pesar de que el propio doc-comment de MovementAuditDecision advierte
        // que SourceId solo "no alcanza" porque BankStatement y Transaction son
        // tablas distintas sin garantía de unicidad entre ambas. Se registra acá la
        // decisión con SourceEntityType.BankStatement, distinto al real del
        // movimiento (Transaction, vía MovementSource.CreditCard) y mismo SourceId:
        // el resultado actual es que igual queda marcado como revisado.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        await SeedAuditDecisionAsync(dbName, SourceEntityType.BankStatement, movement.SourceId, Day);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        var item = Assert.Single(result);
        Assert.True(item.Reviewed);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_MapeaDimensionYValoresDelMotivo()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m",
            MatchCount: 8, WinnerCount: 6);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        var motivo = Assert.Single(Assert.Single(result).Motivos);
        Assert.Equal("Categoría", motivo.Dimension);
        Assert.Equal("Alimentacion", motivo.CurrentValue);
        Assert.Equal("Salud", motivo.SuggestedValue);
        Assert.Equal(8, motivo.MatchCount);
        Assert.Equal(6, motivo.WinnerCount);
    }

    [Fact]
    public async Task GetMisclassifiedMovementsAsync_DefaultDeContraparte_MatchCountYWinnerCountQuedanNulos()
    {
        // BuildDefaultMotivos siempre pasa Confianza/MatchCount/WinnerCount en null --
        // no hay conteo de historial detrás de un default de Counterparty.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var defaultCategoryId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, defaultCategoryId, "Salud");
        await SeedCounterpartyAsync(dbName, counterpartyId, "Farmacia Amancay", defaultCategoryId: defaultCategoryId);
        var movement = ClassifiedMovementView(currentCategoryId, counterpartyId: counterpartyId);
        var service = CreateService(dbName, movements: [movement]);

        var result = await service.GetMisclassifiedMovementsAsync(From, To, null);

        var motivo = Assert.Single(Assert.Single(result).Motivos);
        Assert.Null(motivo.MatchCount);
        Assert.Null(motivo.WinnerCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildFullAuditReportAsync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildFullAuditReportAsync_BaseVacia_TodosLosContadoresEnCeroYSinProblemas()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(From, report.From);
        Assert.Equal(To, report.To);
        Assert.Equal(0, report.MovementsAnalyzed);
        Assert.Equal(0, report.Pending);
        Assert.Equal(0, report.Classified);
        Assert.Equal(0, report.SuspiciousGroups);
        Assert.Equal(0, report.Misclassified);
        Assert.Equal(0, report.OpenInvestigations);
        Assert.Equal(0, report.ResolvedInvestigations);
        Assert.Equal(0, report.TotalProblems);
        Assert.Contains("No se detectaron problemas.", report.ReportText);
        Assert.Equal("(ninguno)", report.SuspiciousText);
        Assert.Equal("(ninguno)", report.PendingText);
        Assert.Equal("(ninguna)", report.MisclassifiedText);
        Assert.Equal("(ninguna)", report.InvestigationsText);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_ConMovimientosPendientes_LosCuentaYLosListaEnElBloque()
    {
        var dbName = Guid.NewGuid().ToString();
        var pending1 = PendingMovement(description: "Pendiente 1");
        var pending2 = PendingMovement(description: "Pendiente 2");
        var service = CreateService(dbName, movements: [pending1, pending2]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(2, report.MovementsAnalyzed);
        Assert.Equal(2, report.Pending);
        Assert.Equal(0, report.Classified);
        Assert.Contains("Pendiente 1", report.PendingText);
        Assert.Contains("Pendiente 2", report.PendingText);
        Assert.Equal(2, report.TotalProblems);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_ConMovimientosClasificadosSinProblemas_ClassifiedCuentaYNoSumaProblemas()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var service = CreateService(dbName, movements: [ClassifiedMovementView(categoryId)]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.MovementsAnalyzed);
        Assert.Equal(0, report.Pending);
        Assert.Equal(1, report.Classified);
        Assert.Equal(0, report.TotalProblems);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_ConGruposSospechosos_SumaAlTotalDeProblemas()
    {
        var dbName = Guid.NewGuid().ToString();
        var group = DuplicateGroup(SuspiciousMovement(), SuspiciousMovement());
        var service = CreateService(dbName, reviewResult: ReviewResultWith(group));

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.SuspiciousGroups);
        Assert.Equal(1, report.TotalProblems);
        Assert.NotEqual("(ninguno)", report.SuspiciousText);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_ConInvestigacionesAbiertasYResueltas_LasCuentaPorSeparado()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedInvestigationAsync(dbName, InvestigationStatus.Open, "Pregunta abierta");
        await SeedInvestigationAsync(dbName, InvestigationStatus.Resolved, "Pregunta resuelta");
        var service = CreateService(dbName);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.OpenInvestigations);
        Assert.Equal(1, report.ResolvedInvestigations);
        Assert.Equal(1, report.TotalProblems); // solo las abiertas suman a TotalProblems
        Assert.Contains("Pregunta abierta", report.InvestigationsText);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_InvestigacionesEnProgresoYDescartadas_NoSumanAOpenNiAResolved()
    {
        // Comportamiento actual documentado tal cual: solo Open y Resolved tienen
        // contador propio. InProgress y Discarded existen en la tabla pero no se
        // reflejan en ningún contador de FullAuditReport ni en TotalProblems.
        var dbName = Guid.NewGuid().ToString();
        await SeedInvestigationAsync(dbName, InvestigationStatus.InProgress);
        await SeedInvestigationAsync(dbName, InvestigationStatus.Discarded);
        var service = CreateService(dbName);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(0, report.OpenInvestigations);
        Assert.Equal(0, report.ResolvedInvestigations);
        Assert.Equal(0, report.TotalProblems);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_MisclassifiedRevisado_NoSumaAlTotalPeroQuedaEnDetected()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        await SeedAuditDecisionAsync(dbName, SourceEntityType.Transaction, movement.SourceId, Day);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.MisclassifiedDetected);
        Assert.Equal(1, report.MisclassifiedReviewed);
        Assert.Equal(0, report.Misclassified);
        Assert.Equal(0, report.TotalProblems);
        Assert.Single(report.MisclassifiedMovements);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_MisclassifiedSinRevisar_SumaAlTotalDeProblemas()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.MisclassifiedDetected);
        Assert.Equal(0, report.MisclassifiedReviewed);
        Assert.Equal(1, report.Misclassified);
        Assert.Equal(1, report.TotalProblems);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_TotalProblemsEsLaSumaDeLasCuatroCategorias()
    {
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        await SeedInvestigationAsync(dbName, InvestigationStatus.Open);
        var misclassified = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        var pending = PendingMovement();
        var group = DuplicateGroup(SuspiciousMovement());
        var service = CreateService(
            dbName,
            reviewResult: ReviewResultWith(group),
            movements: [misclassified, pending],
            suggestions: [SuggestionSet(misclassified.SourceId, suggestion)]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.Misclassified);
        Assert.Equal(1, report.SuspiciousGroups);
        Assert.Equal(1, report.Pending);
        Assert.Equal(1, report.OpenInvestigations);
        Assert.Equal(4, report.TotalProblems);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_DistintosPeriodos_ReflejaFromYToEnElResultado()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);
        var otherFrom = new DateOnly(2025, 6, 1);
        var otherTo = new DateOnly(2025, 6, 30);

        var report = await service.BuildFullAuditReportAsync(otherFrom, otherTo);

        Assert.Equal(otherFrom, report.From);
        Assert.Equal(otherTo, report.To);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_DurationMsNoEsNegativoYGeneratedAtEsReciente()
    {
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);
        var before = DateTime.UtcNow;

        var report = await service.BuildFullAuditReportAsync(From, To);

        var after = DateTime.UtcNow;
        Assert.True(report.DurationMs >= 0);
        Assert.InRange(report.GeneratedAtUtc, before, after);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_EsDeterministico_MismaEntradaMismosContadoresEnDosEjecuciones()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        await SeedInvestigationAsync(dbName, InvestigationStatus.Open);
        var group = DuplicateGroup(SuspiciousMovement());
        var pending = PendingMovement();
        var service = CreateService(
            dbName, reviewResult: ReviewResultWith(group), movements: [pending, ClassifiedMovementView(categoryId)]);

        var first = await service.BuildFullAuditReportAsync(From, To);
        var second = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(first.MovementsAnalyzed, second.MovementsAnalyzed);
        Assert.Equal(first.Pending, second.Pending);
        Assert.Equal(first.Classified, second.Classified);
        Assert.Equal(first.SuspiciousGroups, second.SuspiciousGroups);
        Assert.Equal(first.Misclassified, second.Misclassified);
        Assert.Equal(first.OpenInvestigations, second.OpenInvestigations);
        Assert.Equal(first.ResolvedInvestigations, second.ResolvedInvestigations);
        Assert.Equal(first.TotalProblems, second.TotalProblems);
        Assert.Equal(first.PendingText, second.PendingText);
        Assert.Equal(first.SuspiciousText, second.SuspiciousText);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_ReportTextContieneLasSeccionesEnElOrdenEstablecido()
    {
        // "Pendientes", "Grupos sospechosos" e "Investigaciones abiertas" aparecen dos
        // veces en ReportText: primero como línea de conteo dentro de "Resumen" (ej.
        // "Pendientes: 0") y después como encabezado de su propia sección dentro de
        // "Problemas encontrados". Se usa LastIndexOf a propósito para ubicar el
        // encabezado de sección (la segunda aparición), no la línea de Resumen.
        var dbName = Guid.NewGuid().ToString();
        var service = CreateService(dbName);

        var report = await service.BuildFullAuditReportAsync(From, To);

        var text = report.ReportText;
        var resumenIdx = text.LastIndexOf("Resumen", StringComparison.Ordinal);
        var problemasIdx = text.LastIndexOf("Problemas encontrados", StringComparison.Ordinal);
        var dudosasIdx = text.LastIndexOf("Clasificaciones dudosas", StringComparison.Ordinal);
        var sospechososIdx = text.LastIndexOf("Grupos sospechosos", StringComparison.Ordinal);
        var pendientesIdx = text.LastIndexOf("Pendientes", StringComparison.Ordinal);
        var investigacionesIdx = text.LastIndexOf("Investigaciones abiertas", StringComparison.Ordinal);
        var conclusionIdx = text.LastIndexOf("Conclusión", StringComparison.Ordinal);

        Assert.True(resumenIdx < problemasIdx);
        Assert.True(problemasIdx < dudosasIdx);
        Assert.True(dudosasIdx < sospechososIdx);
        Assert.True(sospechososIdx < pendientesIdx);
        Assert.True(pendientesIdx < investigacionesIdx);
        Assert.True(investigacionesIdx < conclusionIdx);
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_UnUnicoMovimientoClasificadoSinProblemas_ReflejaMovimientosAnalizadosUno()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var service = CreateService(dbName, movements: [ClassifiedMovementView(categoryId)]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(1, report.MovementsAnalyzed);
        Assert.Equal("No se detectaron problemas.", report.ReportText.TrimEnd().Split(Environment.NewLine).Last());
    }

    [Fact]
    public async Task BuildFullAuditReportAsync_MultiplesCuentasFinancieras_CuentaTodosLosMovimientosSinDistinguirCuenta()
    {
        // BuildFullAuditReportAsync llama a IMovementsQueryService.GetAsync con
        // financialAccountId: null -- no filtra por cuenta, sea cual sea la cuenta de
        // cada movimiento individual.
        var dbName = Guid.NewGuid().ToString();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        await SeedFinancialAccountAsync(dbName, accountA, "Cuenta A");
        await SeedFinancialAccountAsync(dbName, accountB, "Cuenta B");
        var pendingA = PendingMovement(description: "De cuenta A") with { FinancialAccountId = accountA };
        var pendingB = PendingMovement(description: "De cuenta B") with { FinancialAccountId = accountB };
        var service = CreateService(dbName, movements: [pendingA, pendingB]);

        var report = await service.BuildFullAuditReportAsync(From, To);

        Assert.Equal(2, report.MovementsAnalyzed);
        Assert.Equal(2, report.Pending);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExplainCurrentClassificationAsync (Patch 0106)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExplainCurrentClassificationAsync_SinHistorialNiDefaultDeContraparte_HasSuggestionFalse()
    {
        // Escenario "Sin sugerencia": ni IClassificationSuggestionService devuelve nada
        // para este movimiento, ni tiene una Counterparty con Default* configurado --
        // no hay nada con qué comparar. Esto NO implica que la clasificación actual sea
        // correcta ni incorrecta, solo que no hay base de comparación.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var movement = ClassifiedMovementView(categoryId);
        var service = CreateService(dbName);

        var result = await service.ExplainCurrentClassificationAsync(movement);

        Assert.False(result.HasSuggestion);
        Assert.Empty(result.Motivos);
    }

    [Fact]
    public async Task ExplainCurrentClassificationAsync_LaSugerenciaCoincideConElValorActual_HasSuggestionTrueYMotivosVacio()
    {
        // Escenario "Coincidencia": hay sugerencia (HasSuggestion=true) pero no difiere
        // de la clasificación actual -- Motivos queda vacío, distinto del caso "sin
        // sugerencia" (que también tiene Motivos vacío pero HasSuggestion=false).
        var dbName = Guid.NewGuid().ToString();
        var categoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, categoryId, "Alimentacion");
        var movement = ClassifiedMovementView(categoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, categoryId, SuggestionConfidence.High, "Misma categoria");
        var service = CreateService(dbName, suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.ExplainCurrentClassificationAsync(movement);

        Assert.True(result.HasSuggestion);
        Assert.Empty(result.Motivos);
    }

    [Fact]
    public async Task ExplainCurrentClassificationAsync_SugerenciaDeCategoriaDistinta_DevuelveUnMotivoConDatosCompletos()
    {
        // Escenario "Diferencia": una única dimensión diverge -- se verifican los 5
        // campos que MovementTools.ExplainClassification expone por motivo (dimensión,
        // valor actual, valor sugerido, origen, confianza).
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m", MatchCount: 5, WinnerCount: 4);
        var service = CreateService(dbName, suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var result = await service.ExplainCurrentClassificationAsync(movement);

        Assert.True(result.HasSuggestion);
        var motivo = Assert.Single(result.Motivos);
        Assert.Equal("Categoría", motivo.Dimension);
        Assert.Equal("Alimentacion", motivo.ValorActual);
        Assert.Equal("Salud", motivo.ValorSugerido);
        Assert.Equal("High", motivo.Confianza);
        Assert.Contains("Historial de descripción idéntica", motivo.Origen);
        // Tier 0 (AGENT-004): Reason ahora se propaga hasta Motivo -- mismo texto que
        // ClassificationSuggestion.Reason, sin alterar BuildReason.
        Assert.Equal("m", motivo.Reason);
    }

    [Fact]
    public async Task ExplainCurrentClassificationAsync_MultiplesDimensionesDivergentes_DevuelveUnMotivoPorDimension()
    {
        // Escenario "Varias diferencias": categoría y tipo divergen a la vez -- 2 motivos,
        // uno por dimensión, cada uno con su propio origen/confianza.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId, movementType: MovementType.Purchase);
        var categorySuggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m");
        var typeSuggestion = new ClassificationSuggestion(
            SuggestionDimension.MovementType, MovementType.Transfer, SuggestionConfidence.Medium, "m");
        var service = CreateService(
            dbName, suggestions: [SuggestionSet(movement.SourceId, categorySuggestion, typeSuggestion)]);

        var result = await service.ExplainCurrentClassificationAsync(movement);

        Assert.True(result.HasSuggestion);
        Assert.Equal(2, result.Motivos.Count);
        Assert.Contains(result.Motivos, m => m.Dimension == "Categoría");
        Assert.Contains(result.Motivos, m => m.Dimension == "Tipo");
    }

    [Fact]
    public async Task ExplainCurrentClassificationAsync_MismoMotivoQueGetMisclassifiedMovementsAsync_ParaElMismoMovimiento()
    {
        // Escenario "Comparación MCP vs Auditoría": ExplainCurrentClassificationAsync
        // reutiliza BuildSuggestionMotivos/BuildDefaultMotivos -- el mismo cálculo que
        // ya usa Auditoría vía GetMisclassifiedMovementsAsync. Este test comprueba que,
        // para el mismo movimiento, ambos caminos producen el mismo motivo (Dimension/
        // valor actual/valor sugerido), sin duplicar la lógica de interpretación.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var suggestedCategoryId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, suggestedCategoryId, "Salud");
        var movement = ClassifiedMovementView(currentCategoryId);
        var suggestion = new ClassificationSuggestion(
            SuggestionDimension.Category, suggestedCategoryId, SuggestionConfidence.High, "m", MatchCount: 8, WinnerCount: 6);
        var service = CreateService(
            dbName, movements: [movement], suggestions: [SuggestionSet(movement.SourceId, suggestion)]);

        var auditoriaResult = await service.GetMisclassifiedMovementsAsync(From, To, null);
        var mcpResult = await service.ExplainCurrentClassificationAsync(movement);

        var auditoriaMotivo = Assert.Single(Assert.Single(auditoriaResult).Motivos);
        var mcpMotivo = Assert.Single(mcpResult.Motivos);
        Assert.Equal(auditoriaMotivo.Dimension, mcpMotivo.Dimension);
        Assert.Equal(auditoriaMotivo.CurrentValue, mcpMotivo.ValorActual);
        Assert.Equal(auditoriaMotivo.SuggestedValue, mcpMotivo.ValorSugerido);
        Assert.Equal(auditoriaMotivo.MatchCount, mcpMotivo.MatchCount);
        Assert.Equal(auditoriaMotivo.WinnerCount, mcpMotivo.WinnerCount);
    }

    [Fact]
    public async Task ExplainCurrentClassificationAsync_DefaultDeContraparteDistinto_HasSuggestionTrueConMotivoSinConfianza()
    {
        // Igual que BuildMisclassifiedMovementsReportAsync_DefaultDeContraparteDistinto_IncluyeElMotivo,
        // pero por el camino de un único movimiento: BuildDefaultMotivos nunca informa
        // Confianza/MatchCount/WinnerCount (no hay conteo de historial detrás de un
        // default de Counterparty) -- se preserva ese comportamiento sin cambios.
        var dbName = Guid.NewGuid().ToString();
        var currentCategoryId = Guid.NewGuid();
        var defaultCategoryId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        await SeedCategoryAsync(dbName, currentCategoryId, "Alimentacion");
        await SeedCategoryAsync(dbName, defaultCategoryId, "Salud");
        await SeedCounterpartyAsync(dbName, counterpartyId, "Farmacia Amancay", defaultCategoryId: defaultCategoryId);
        var movement = ClassifiedMovementView(currentCategoryId, counterpartyId: counterpartyId);
        var service = CreateService(dbName);

        var result = await service.ExplainCurrentClassificationAsync(movement);

        Assert.True(result.HasSuggestion);
        var motivo = Assert.Single(result.Motivos);
        Assert.Equal("Categoría", motivo.Dimension);
        Assert.Equal("Salud", motivo.ValorSugerido);
        Assert.Null(motivo.Confianza);
        Assert.Contains("Default configurado en la contraparte", motivo.Origen);
        // Tier 0 (AGENT-004): BuildDefaultMotivos no tiene ningún texto de Reason que
        // propagar (CounterpartyDefaults no lo tiene) -- Reason queda null, sin cambios
        // de comportamiento respecto de antes de este patch.
        Assert.Null(motivo.Reason);
    }
}
