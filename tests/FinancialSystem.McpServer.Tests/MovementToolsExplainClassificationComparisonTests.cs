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
/// Cubre el escenario "Pendiente" de PATCH-0106 (ver AuditReportServiceTests.cs para
/// los otros 5 escenarios, ejercitados directamente contra
/// AuditReportService.ExplainCurrentClassificationAsync): un movimiento sin
/// clasificación no tiene nada que comparar -- la sección "Comparación con la
/// sugerencia actual" de MovementTools.ExplainClassification debe decirlo sin
/// invocar a AuditReportService en absoluto (mismo criterio que ya usan las demás
/// secciones del método para "sin clasificar, no aplica").
///
/// Este es el único archivo de test que ejercita MovementTools directamente -- no
/// existe una base de fakes compartida para IMovementsQueryService/
/// IMovementLookupService/IApplicationDbContext en FinancialSystem.McpServer.Tests
/// (ver ToolRegistrySyncTests.cs, el único test previo del proyecto, que no
/// instancia ninguna tool). Se definen acá fakes mínimos y locales, siguiendo la
/// misma convención de duplicación por archivo que ya usa el resto de la suite
/// (ver AuditReportServiceTests.cs, ImportBatchMovementLinkTests.cs, etc.) -- todas
/// "throwing dummies" salvo IMovementLookupService (la única dependencia que este
/// escenario necesita), para que cualquier acceso inesperado a una dependencia no
/// usada haga fallar el test de forma explícita, en vez de silenciosamente.
/// </summary>
public class MovementToolsExplainClassificationComparisonTests
{
    private static readonly DateTime Day = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExplainClassification_MovimientoPendiente_NoLlamaAAuditReportServiceYLoDiceExplicitamente()
    {
        var sourceId = Guid.NewGuid();
        var pendingDetail = new MovementDetail(
            SourceEntityType.Transaction,
            sourceId,
            Day,
            "Compra pendiente de clasificar",
            100m,
            "ARS",
            SourceFile: "resumen.pdf",
            FinancialAccountId: null,
            FinancialAccountName: null,
            ExternalId: "003842",
            CouponNumber: null,
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
            Classification: null);

        var tools = new MovementTools(
            new ThrowingMovementsQueryService(),
            new FakeMovementLookupService(pendingDetail),
            new ThrowingApplicationDbContext(),
            new AuditReportService(
                new ThrowingReviewEngine(),
                new ThrowingMovementsQueryService(),
                new ThrowingClassificationSuggestionService(),
                new ThrowingApplicationDbContext()));

        var result = await tools.ExplainClassification("Transaction", sourceId);

        Assert.Contains("Comparación con la sugerencia actual", result);
        Assert.Contains("(sin clasificar, no aplica)", result);
        Assert.Contains("Pendiente", result);
    }

    // ── Fakes/dummies locales ─────────────────────────────────────────────────

    private sealed class FakeMovementLookupService(MovementDetail detail) : IMovementLookupService
    {
        public Task<MovementDetail?> GetBySourceAsync(
            SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovementDetail?>(
                detail.SourceEntityType == sourceEntityType && detail.SourceId == sourceId ? detail : null);

        public Task<IReadOnlyDictionary<(SourceEntityType SourceEntityType, Guid SourceId), MovementDetail>> GetManyBySourceAsync(
            IReadOnlyList<(SourceEntityType SourceEntityType, Guid SourceId)> sources,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "No se esperaba una llamada a GetManyBySourceAsync en este escenario (Pendiente).");
    }

    private sealed class ThrowingMovementsQueryService : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "No se esperaba ninguna llamada a IMovementsQueryService: un movimiento pendiente " +
                "no debe disparar la comparación con la sugerencia actual.");
    }

    private sealed class ThrowingReviewEngine : IReviewEngine
    {
        public Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "No se esperaba ninguna llamada a IReviewEngine: AuditReportService no debe " +
                "invocarse en absoluto para un movimiento pendiente.");
    }

    private sealed class ThrowingClassificationSuggestionService : IClassificationSuggestionService
    {
        public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
            IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "No se esperaba ninguna llamada a IClassificationSuggestionService.SuggestAsync: " +
                "para un movimiento pendiente (c is null), MovementTools.ExplainClassification no " +
                "debe invocar AuditReportService.ExplainCurrentClassificationAsync en absoluto.");
    }

    private sealed class ThrowingApplicationDbContext : IApplicationDbContext
    {
        private static InvalidOperationException NotExpected([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
            new($"No se esperaba ningún acceso a IApplicationDbContext.{member} en este escenario (Pendiente).");

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
