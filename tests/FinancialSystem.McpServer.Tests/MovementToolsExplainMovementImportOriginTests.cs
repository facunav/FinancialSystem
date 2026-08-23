using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Dedupe;
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
/// Cubre la sección "Origen de importación" de ExplainMovement (Patch 0107) -- los 3 casos
/// obligatorios de MovementDetail.ImportBatchId/ImportOrigin (ver doc-comment de
/// MovementDetail.ImportBatchId en IMovementLookupService.cs): sin corrida registrada,
/// corrida resuelta, y referencia blanda huérfana.
///
/// IMPORTANTE (limitación documentada, Patch 0107): este archivo ejercita
/// MovementTools.ExplainMovement con un fake de IMovementLookupService construido a mano
/// (mismo patrón que MovementToolsExplainClassificationComparisonTests.cs) -- NO ejercita
/// la implementación real (MovementLookupService, Infrastructure) contra EF Core, porque
/// MovementLookupService.LoadClassificationAsync (código preexistente, no tocado por
/// Patch 0107) lanza "Cycles are not allowed in no-tracking queries" al materializar su
/// Include(i => i.ClassifiedMovement).ThenInclude(cm => cm!.Items) sin tracking -- un bug
/// preexistente y ajeno a este patch (reproduce incluso con ImportBatchId null, sin
/// ninguna fila en ClassifiedMovementItems) que bloquea CUALQUIER prueba automatizada
/// contra la implementación real de MovementLookupService.GetBySourceAsync/
/// GetManyBySourceAsync hoy. No se modifica ese código para "arreglarlo" -- está fuera del
/// alcance de Patch 0107 (ver ENTREGA FINAL). La resolución de ImportBatchId/ImportOrigin
/// en sí (MovementLookupService.ResolveImportOriginAsync y el bloque batched equivalente en
/// GetManyBySourceAsync) se verificó por revisión de código + build limpio, y debe
/// verificarse manualmente contra Postgres (ver PRUEBAS MANUALES).
/// </summary>
public class MovementToolsExplainMovementImportOriginTests
{
    private static readonly DateTime Day = new(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc);

    private static MovementDetail BaseDetail(Guid sourceId, Guid? importBatchId, MovementImportOrigin? importOrigin) => new(
        SourceEntityType.Transaction,
        sourceId,
        Day,
        "DLO*PEDIDOSYA MCDONALD",
        32725.00m,
        "ARS",
        SourceFile: "Visa_Marzo.pdf",
        FinancialAccountId: null,
        FinancialAccountName: null,
        ExternalId: "003842",
        CouponNumber: "003842",
        RawLine: null,
        BankName: null,
        AccountNumber: null,
        BankDetail: null,
        Balance: null,
        SheetName: null,
        RowNumber: null,
        Merchant: null,
        MerchantAtUtc: null,
        SourceRecordedAtUtc: Day,
        Classification: null,
        ImportBatchId: importBatchId,
        ImportOrigin: importOrigin);

    [Fact]
    public async Task ExplainMovement_MovimientoReciente_MuestraLosSeisDatosDelImportBatchResuelto()
    {
        var sourceId = Guid.NewGuid();
        var importBatchId = Guid.NewGuid();
        var startedAt = new DateTime(2026, 3, 28, 10, 15, 0, DateTimeKind.Utc);
        var origin = new MovementImportOrigin(
            importBatchId, "Visa_Marzo.pdf", "Transaction", "BBVA_VISA_AR", startedAt, ImportBatchOutcome.Processed);
        var detail = BaseDetail(sourceId, importBatchId, origin);

        var tools = BuildTools(detail);
        var result = await tools.ExplainMovement("Transaction", sourceId);

        Assert.Contains("Origen de importación", result);
        Assert.Contains(importBatchId.ToString(), result);
        Assert.Contains("Visa_Marzo.pdf", result);
        Assert.Contains("Transaction", result);
        Assert.Contains("BBVA_VISA_AR", result);
        Assert.Contains(startedAt.ToString("O"), result);
        Assert.Contains("Processed", result);
        Assert.DoesNotContain("no resuelve", result);
        Assert.DoesNotContain("Sin corrida de importación registrada", result);
    }

    [Fact]
    public async Task ExplainMovement_MovimientoHistorico_ImportBatchIdNulo_IndicaQueNoHayCorridaRegistrada()
    {
        var sourceId = Guid.NewGuid();
        var detail = BaseDetail(sourceId, importBatchId: null, importOrigin: null);

        var tools = BuildTools(detail);
        var result = await tools.ExplainMovement("Transaction", sourceId);

        Assert.Contains("Origen de importación", result);
        Assert.Contains("Sin corrida de importación registrada", result);
        Assert.Contains("Patch 0105", result);
    }

    [Fact]
    public async Task ExplainMovement_ImportBatchIdHuerfano_MuestraElIdCrudoYNoResuelve()
    {
        var sourceId = Guid.NewGuid();
        var orphanImportBatchId = Guid.NewGuid();
        var detail = BaseDetail(sourceId, importBatchId: orphanImportBatchId, importOrigin: null);

        var tools = BuildTools(detail);
        var result = await tools.ExplainMovement("Transaction", sourceId);

        Assert.Contains("Origen de importación", result);
        Assert.Contains(orphanImportBatchId.ToString(), result);
        Assert.Contains("no resuelve", result);
        Assert.DoesNotContain("Sin corrida de importación registrada", result);
    }

    // ── Construcción de la tool con fakes locales ────────────────────────────

    private static MovementTools BuildTools(MovementDetail detail) => new(
        new ThrowingMovementsQueryService(),
        new FakeMovementLookupService(detail),
        new ThrowingApplicationDbContext(),
        new AuditReportService(
            new ThrowingReviewEngine(),
            new ThrowingMovementsQueryService(),
            new ThrowingClassificationSuggestionService(),
            new ThrowingApplicationDbContext()));

    private sealed class FakeMovementLookupService(MovementDetail detail) : IMovementLookupService
    {
        public Task<MovementDetail?> GetBySourceAsync(
            SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovementDetail?>(
                detail.SourceEntityType == sourceEntityType && detail.SourceId == sourceId ? detail : null);

        public Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
            IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba una llamada a GetManyBySourceAsync en estos escenarios.");
    }

    private sealed class ThrowingMovementsQueryService : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba ninguna llamada a IMovementsQueryService.");
    }

    private sealed class ThrowingReviewEngine : IReviewEngine
    {
        public Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba ninguna llamada a IReviewEngine.");
    }

    private sealed class ThrowingClassificationSuggestionService : IClassificationSuggestionService
    {
        public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
            IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No se esperaba ninguna llamada a IClassificationSuggestionService.");
    }

    private sealed class ThrowingApplicationDbContext : IApplicationDbContext
    {
        private static InvalidOperationException NotExpected([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
            new($"No se esperaba ningún acceso a IApplicationDbContext.{member} en estos escenarios (ExplainMovement no consulta la base).");

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
        public DbSet<MovementIdentityLink> MovementIdentityLinks => throw NotExpected();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw NotExpected();
    }
}
