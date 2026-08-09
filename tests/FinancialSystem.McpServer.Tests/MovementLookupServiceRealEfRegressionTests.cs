using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Audit;
using FinancialSystem.Infrastructure.Movements;
using FinancialSystem.Infrastructure.Persistence;
using FinancialSystem.McpServer.Tools;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.McpServer.Tests;

/// <summary>
/// Patch 0108 -- regresión de GetMovement/ExplainMovement/ExplainClassification (los 3
/// consumidores de MovementTools que dependen de MovementLookupService) contra la
/// implementación REAL de MovementLookupService (AppDbContext InMemory real, no un fake de
/// IMovementLookupService) -- exactamente el escenario que reveló el bug de EF Core durante
/// Patch 0107 ("Cycles are not allowed in no-tracking queries" en
/// LoadClassificationAsync, ver MovementLookupServiceTests.cs en Infrastructure.Tests para
/// el test que aísla el bug/fix a nivel de MovementLookupService).
///
/// Construye MovementLookupService directamente (tipo internal de
/// FinancialSystem.Infrastructure, visible acá vía InternalsVisibleTo -- ver AssemblyInfo.cs)
/// en vez de a través de DependencyInjection.AddInfrastructure (que fija UseNpgsql y
/// requiere una conexión real). IReviewEngine/IMovementsQueryService de AuditReportService y
/// IMovementsQueryService de MovementTools quedan sin usar por estos 3 métodos -- dummies
/// mínimos, no fakes elaborados; IClassificationSuggestionService sí se ejercita (devuelve
/// vacío) porque ExplainClassification (Patch 0106) lo invoca siempre para un movimiento
/// clasificado.
/// </summary>
public class MovementLookupServiceRealEfRegressionTests
{
    private static readonly DateTime Day = new(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static MovementTools CreateTools(string dbName)
    {
        var db = OpenDb(dbName);
        var movementLookup = new MovementLookupService(db);
        var auditReportService = new AuditReportService(
            new NoOpReviewEngine(), new NoOpMovementsQueryService(), new EmptyClassificationSuggestionService(), db);
        return new MovementTools(new NoOpMovementsQueryService(), movementLookup, db, auditReportService);
    }

    private static async Task<Guid> SeedTransactionAsync(string dbName, string description)
    {
        await using var db = OpenDb(dbName);
        var transaction = new Transaction
        {
            Date = Day,
            Description = description,
            Amount = 32725.00m,
            Currency = "ARS",
            CreatedAtUtc = Day,
            ExternalId = Guid.NewGuid().ToString("N"),
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private static async Task<Guid> SeedCategoryAsync(string dbName, string name = "Alimentacion")
    {
        await using var db = OpenDb(dbName);
        var category = new Category { Name = name, DisplayName = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    // Mismo patrón que ClassifyMovementHandlerTests/MovementLookupServiceTests -- siembra
    // directa de un ClassifiedMovement con N ClassifiedMovementItems (grupo de matching).
    private static async Task SeedClassifiedMovementAsync(
        string dbName, Guid categoryId, params (SourceEntityType SourceEntityType, Guid SourceId, MovementRole Role)[] items)
    {
        await using var db = OpenDb(dbName);
        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = Day,
            TotalAmount = 32725.00m,
            Currency = "ARS",
            Description = "DLO*PEDIDOSYA MCDONALD",
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            CategoryId = categoryId,
            Status = ClassificationStatus.Confirmed,
            ProcessingSource = ProcessingSource.ManualReview,
            CreatedAt = Day,
            ProcessedAt = Day,
        };
        db.ClassifiedMovements.Add(classifiedMovement);

        foreach (var (sourceEntityType, sourceId, role) in items)
        {
            db.ClassifiedMovementItems.Add(new ClassifiedMovementItem
            {
                ClassifiedMovementId = classifiedMovement.Id,
                ClassifiedMovement = classifiedMovement,
                SourceEntityType = sourceEntityType,
                SourceId = sourceId,
                Role = role,
                OriginalAmount = 32725.00m,
                OriginalDate = Day,
                OriginalDescription = "DLO*PEDIDOSYA MCDONALD",
                OriginalCurrency = "ARS",
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMovement_MovimientoClasificadoConGrupoDeDosItems_NoLanzaYMuestraLaClasificacion()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var referenceId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (banco)");
        var candidateId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (tarjeta)");
        await SeedClassifiedMovementAsync(
            dbName, categoryId,
            (SourceEntityType.Transaction, referenceId, MovementRole.Reference),
            (SourceEntityType.Transaction, candidateId, MovementRole.Candidate));
        var tools = CreateTools(dbName);

        var result = await tools.GetMovement("Transaction", referenceId);

        Assert.Contains("Alimentacion", result);
        Assert.Contains("Grupo de matching (2 ítem(s)", result);
        Assert.DoesNotContain("Cycles are not allowed", result);
    }

    [Fact]
    public async Task ExplainMovement_MovimientoClasificadoConGrupoDeDosItems_NoLanzaYMantieneTodasLasSecciones()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var referenceId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (banco)");
        var candidateId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (tarjeta)");
        await SeedClassifiedMovementAsync(
            dbName, categoryId,
            (SourceEntityType.Transaction, referenceId, MovementRole.Reference),
            (SourceEntityType.Transaction, candidateId, MovementRole.Candidate));
        var tools = CreateTools(dbName);

        var result = await tools.ExplainMovement("Transaction", referenceId);

        // Todas las secciones preexistentes (Movimiento/Clasificación/Procesamiento/
        // Matching/Observaciones) más "Origen de importación" (Patch 0107) siguen presentes.
        Assert.Contains("Movimiento", result);
        Assert.Contains("Origen de importación", result);
        Assert.Contains("Sin corrida de importación registrada", result); // no se sembró ImportBatch
        Assert.Contains("Clasificación", result);
        Assert.Contains("Alimentacion", result);
        Assert.Contains("Procesamiento", result);
        Assert.Contains("Matching", result);
        Assert.Contains("Cantidad de Items: 2", result);
        Assert.Contains("Observaciones", result);
    }

    [Fact]
    public async Task ExplainClassification_MovimientoClasificado_NoLanzaYMantieneLaComparacionDePatch0106()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var transactionId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD");
        await SeedClassifiedMovementAsync(
            dbName, categoryId, (SourceEntityType.Transaction, transactionId, MovementRole.Reference));
        var tools = CreateTools(dbName);

        var result = await tools.ExplainClassification("Transaction", transactionId);

        Assert.Contains("Cómo se obtuvo esa clasificación", result);
        Assert.Contains("Comparación con la sugerencia actual", result);
        Assert.Contains("Sin sugerencia disponible", result); // IClassificationSuggestionService vacío, sin contraparte
        Assert.Contains("Alimentacion", result);
    }

    // ── Dummies mínimos (dependencias no ejercitadas por estos 3 métodos) ───────

    private sealed class NoOpMovementsQueryService : IMovementsQueryService
    {
        public Task<IReadOnlyList<MovementView>> GetAsync(
            DateOnly from, DateOnly to, Guid? financialAccountId, string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovementView>>([]);
    }

    private sealed class NoOpReviewEngine : IReviewEngine
    {
        public Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewResult
            {
                PeriodStart = from,
                PeriodEnd = to,
                Movements = [],
                Suspicious = [],
                Elapsed = TimeSpan.Zero,
            });
    }

    private sealed class EmptyClassificationSuggestionService : IClassificationSuggestionService
    {
        public Task<IReadOnlyList<ClassificationSuggestionSet>> SuggestAsync(
            IReadOnlyList<FinancialMovement> movements, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClassificationSuggestionSet>>([]);
    }
}
