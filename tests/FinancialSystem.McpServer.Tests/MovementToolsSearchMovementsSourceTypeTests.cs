using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Memory;
using FinancialSystem.Domain.Planning;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Audit;
using FinancialSystem.McpServer.Tools;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.McpServer.Tests;

/// <summary>
/// Patch 0109 -- cierra la única deuda no bloqueante señalada por la auditoría final del
/// Modo Investigación: SearchMovements no informaba SourceEntityType, obligando a adivinar
/// entre "Transaction" y "BankStatement" para el siguiente paso (GetMovement/ExplainMovement/
/// ExplainClassification, los 3 exigen ese dato). Reutiliza
/// MovementSourceExtensions.ToSourceEntityType() (Domain.Review, PATCH-042) -- sin mapeo
/// nuevo, sin tocar MovementLookupService/AuditReportService/ImportBatch.
///
/// Mismos fakes/dummies locales que MovementToolsExplainClassificationComparisonTests.cs/
/// MovementToolsExplainMovementImportOriginTests.cs -- SearchMovements no toca
/// IMovementLookupService/AuditReportService, así que quedan como dummies que lanzan
/// excepción si se los llama.
/// </summary>
public class MovementToolsSearchMovementsSourceTypeTests
{
    private static readonly DateTime Day = new(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

    private static MovementView BankMovement(Guid? sourceId = null, string description = "Movimiento de banco") => new(
        sourceId ?? Guid.NewGuid(), Day, description, 1000m, "ARS", MovementSource.BankDebit,
        FinancialAccountId: null, Status: null, CategoryId: null, CounterpartyId: null,
        MovementType: null, FinancialImpact: null, Warning: null, Suggestions: [],
        Merchant: null, MerchantAtUtc: null, EffectiveDate: null);

    private static MovementView CardMovement(Guid? sourceId = null, string description = "Movimiento de tarjeta") => new(
        sourceId ?? Guid.NewGuid(), Day, description, 500m, "USD", MovementSource.CreditCard,
        FinancialAccountId: null, Status: null, CategoryId: null, CounterpartyId: null,
        MovementType: null, FinancialImpact: null, Warning: null, Suggestions: [],
        Merchant: null, MerchantAtUtc: null, EffectiveDate: null);

    [Fact]
    public async Task SearchMovements_MovimientoBancario_DevuelveSourceEntityTypeBankStatement()
    {
        var sourceId = Guid.NewGuid();
        var tools = BuildTools([BankMovement(sourceId, "Transferencia recibida")]);

        var result = await tools.SearchMovements(from: null, to: "2026-03-31", text: null,
            categoryId: null, counterpartyId: null, financialImpact: null, movementType: null,
            status: null, currency: null, minAmount: null, maxAmount: null);

        Assert.Contains("SourceEntityType: BankStatement", result);
        Assert.Contains($"SourceId: {sourceId}", result);
    }

    [Fact]
    public async Task SearchMovements_MovimientoDeTarjeta_DevuelveSourceEntityTypeTransaction()
    {
        var sourceId = Guid.NewGuid();
        var tools = BuildTools([CardMovement(sourceId, "Compra con tarjeta")]);

        var result = await tools.SearchMovements(from: null, to: "2026-03-31", text: null,
            categoryId: null, counterpartyId: null, financialImpact: null, movementType: null,
            status: null, currency: null, minAmount: null, maxAmount: null);

        Assert.Contains("SourceEntityType: Transaction", result);
        Assert.Contains($"SourceId: {sourceId}", result);
    }

    [Fact]
    public async Task SearchMovements_MantieneTodosLosCamposPreexistentes()
    {
        // Regresión: ningún campo que ya mostraba SearchMovements debe desaparecer --
        // el cambio es aditivo (2 líneas nuevas), no un reemplazo de formato.
        var tools = BuildTools([BankMovement(description: "Movimiento de prueba")]);

        var result = await tools.SearchMovements(from: null, to: "2026-03-31", text: null,
            categoryId: null, counterpartyId: null, financialImpact: null, movementType: null,
            status: null, currency: null, minAmount: null, maxAmount: null);

        Assert.Contains("Fecha bancaria:", result);
        Assert.Contains("Período financiero:", result);
        Assert.Contains("Descripción:", result);
        Assert.Contains("Movimiento de prueba", result);
        Assert.Contains("Importe:", result);
        Assert.Contains("Categoría:", result);
        Assert.Contains("Contraparte:", result);
        Assert.Contains("Tipo:", result);
        Assert.Contains("Impacto financiero:", result);
        Assert.Contains("Estado:", result);
    }

    [Fact]
    public async Task SearchMovements_SourceEntityTypeDevuelto_PermiteLlamarAGetMovementSinAdivinar()
    {
        // Flujo completo: SearchMovements -> (parsear SourceEntityType/SourceId de la salida,
        // como haría un consumidor real) -> GetMovement, sin probar los dos valores posibles.
        var sourceId = Guid.NewGuid();
        var detail = new MovementDetail(
            SourceEntityType.BankStatement, sourceId, Day, "Transferencia recibida", 1000m, "ARS",
            SourceFile: null, FinancialAccountId: null, FinancialAccountName: null, ExternalId: null,
            CouponNumber: null, RawLine: null, BankName: "BBVA", AccountNumber: null, BankDetail: null,
            Balance: null, SheetName: null, RowNumber: null, Merchant: null, MerchantAtUtc: null,
            SourceRecordedAtUtc: Day, Classification: null);
        var tools = BuildTools([BankMovement(sourceId, "Transferencia recibida")], detail);

        var searchResult = await tools.SearchMovements(from: null, to: "2026-03-31", text: null,
            categoryId: null, counterpartyId: null, financialImpact: null, movementType: null,
            status: null, currency: null, minAmount: null, maxAmount: null);

        // Mismo criterio que usaría un LLM: leer la línea "SourceEntityType: X" del texto.
        var sourceEntityTypeLine = searchResult.Split('\n').Single(l => l.StartsWith("SourceEntityType: "));
        var parsedSourceEntityType = sourceEntityTypeLine["SourceEntityType: ".Length..].Trim();

        var getMovementResult = await tools.GetMovement(parsedSourceEntityType, sourceId);

        Assert.DoesNotContain("Error: sourceEntityType inválido", getMovementResult);
        Assert.DoesNotContain("No se encontró ningún", getMovementResult);
        Assert.Contains("Transferencia recibida", getMovementResult);
    }

    // ── Construcción de la tool con fakes/dummies locales ────────────────────

    private static MovementTools BuildTools(IReadOnlyList<MovementView> movements, MovementDetail? lookupResult = null) => new(
        new FakeMovementsQueryService(movements),
        lookupResult is null ? new ThrowingMovementLookupService() : new FakeMovementLookupService(lookupResult),
        new ThrowingApplicationDbContext(),
        new AuditReportService(
            new ThrowingReviewEngine(), new ThrowingMovementsQueryService(), new ThrowingClassificationSuggestionService(),
            new ThrowingApplicationDbContext()));

    private sealed class FakeMovementsQueryService(IReadOnlyList<MovementView> movements) : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(movements);
    }

    private sealed class FakeMovementLookupService(MovementDetail detail) : IMovementLookupService
    {
        public Task<MovementDetail?> GetBySourceAsync(
            SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovementDetail?>(
                detail.SourceEntityType == sourceEntityType && detail.SourceId == sourceId ? detail : null);

        public Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
            IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba una llamada a GetManyBySourceAsync en este escenario.");
    }

    private sealed class ThrowingMovementLookupService : IMovementLookupService
    {
        public Task<MovementDetail?> GetBySourceAsync(
            SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba una llamada a IMovementLookupService en este escenario (solo SearchMovements).");

        public Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
            IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba una llamada a IMovementLookupService en este escenario (solo SearchMovements).");
    }

    private sealed class ThrowingMovementsQueryService : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba una llamada a IMovementsQueryService desde AuditReportService en este escenario.");
    }

    private sealed class ThrowingReviewEngine : IReviewEngine
    {
        public Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba ninguna llamada a IReviewEngine en este escenario.");
    }

    private sealed class ThrowingClassificationSuggestionService : IClassificationSuggestionService
    {
        public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
            IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba ninguna llamada a IClassificationSuggestionService en este escenario.");
    }

    private sealed class ThrowingApplicationDbContext : IApplicationDbContext
    {
        private static InvalidOperationException NotExpected([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
            new($"No se esperaba ningún acceso a IApplicationDbContext.{member} en este escenario (sin categoría/contraparte asignadas, ResolveNamesAsync no debería consultar la base).");

        public DbSet<Transaction> Transactions => throw NotExpected();
        public DbSet<BankStatement> BankStatements => throw NotExpected();
        public DbSet<Category> Categories => throw NotExpected();
        public DbSet<ClassifiedMovement> ClassifiedMovements => throw NotExpected();
        public DbSet<ClassifiedMovementItem> ClassifiedMovementItems => throw NotExpected();
        public DbSet<Counterparty> Counterparties => throw NotExpected();
        public DbSet<ImportBatch> ImportBatches => throw NotExpected();
        public DbSet<ImportBatchLine> ImportBatchLines => throw NotExpected();
        public DbSet<FinancialAccount> FinancialAccounts => throw NotExpected();
        public DbSet<Investigation> Investigations => throw NotExpected();
        public DbSet<InvestigationReference> InvestigationReferences => throw NotExpected();
        public DbSet<InvestigationFinding> InvestigationFindings => throw NotExpected();
        public DbSet<MovementAuditDecision> MovementAuditDecisions => throw NotExpected();
        public DbSet<PlanningMonth> PlanningMonths => throw NotExpected();
        public DbSet<PlanningItem> PlanningItems => throw NotExpected();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw NotExpected();
    }
}
