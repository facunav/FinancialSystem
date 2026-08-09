using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Movements;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Movements;

/// <summary>
/// Primer test directo de <see cref="MovementLookupService"/> contra un
/// <see cref="AppDbContext"/> real (InMemory) -- no existía ninguno antes de Patch 0107/0108
/// (mismo patrón InMemory que ClassifyMovementHandlerTests/AuditReportServiceTests: una base
/// por test, vía Guid).
///
/// Patch 0108 -- FIX de un bug preexistente descubierto al escribir estos tests durante
/// Patch 0107: LoadClassificationAsync (y su equivalente batched en GetManyBySourceAsync)
/// hacían .AsNoTracking().Include(i => i.ClassifiedMovement).ThenInclude(cm => cm!.Items) --
/// un Include cíclico (Item -> Movement -> Items, que vuelve a incluir el propio tipo Item),
/// que EF Core rechaza en queries sin tracking con "Cycles are not allowed in no-tracking
/// queries". Reproducía siempre, incluso sin ninguna fila en ClassifiedMovementItems -- es un
/// chequeo sobre la FORMA del Include, no sobre los datos, y no es específico del proveedor
/// InMemory (la verificación vive en el ensamblado core de EF). El fix (ver MovementLookupService.cs)
/// separa la resolución del grupo de matching en una segunda consulta explícita sin Include.
///
/// El test que efectivamente reproduce el bug (y prueba el fix) es
/// GetBySourceAsync_MovimientoConGrupoDeMatchingDeVariosItems_NoLanzaYDevuelveLosItemsDelGrupo --
/// con 2+ ClassifiedMovementItems compartiendo el mismo ClassifiedMovementId, exactamente la
/// forma de datos que dispara el Include cíclico.
/// </summary>
public class MovementLookupServiceTests
{
    private static readonly DateTime Day = new(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static MovementLookupService CreateService(string dbName) => new(OpenDb(dbName));

    private static async Task<Guid> SeedTransactionAsync(string dbName, string description = "DLO*PEDIDOSYA MCDONALD")
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

    // Mismo patrón que ClassifyMovementHandlerTests.SeedClassifiedMovementAsync -- siembra
    // directa (sin pasar por el handler) de un ClassifiedMovement con N ClassifiedMovementItems
    // (el "grupo de matching"), para poder controlar exactamente cuántos ítems hermanos tiene.
    private static async Task<Guid> SeedClassifiedMovementAsync(
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
        return classifiedMovement.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Movimiento sin clasificación
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBySourceAsync_MovimientoSinClasificacion_DevuelveClassificationNuloSinExcepcion()
    {
        var dbName = Guid.NewGuid().ToString();
        var transactionId = await SeedTransactionAsync(dbName);
        var service = CreateService(dbName);

        var detail = await service.GetBySourceAsync(SourceEntityType.Transaction, transactionId);

        Assert.NotNull(detail);
        Assert.Null(detail!.Classification);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Movimiento clasificado, 1 solo ClassifiedMovementItem (caso normal 1:1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBySourceAsync_MovimientoClasificadoUnSoloItem_DevuelveLaClasificacionCompleta()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var transactionId = await SeedTransactionAsync(dbName);
        await SeedClassifiedMovementAsync(
            dbName, categoryId, (SourceEntityType.Transaction, transactionId, MovementRole.Reference));
        var service = CreateService(dbName);

        var detail = await service.GetBySourceAsync(SourceEntityType.Transaction, transactionId);

        Assert.NotNull(detail);
        Assert.NotNull(detail!.Classification);
        Assert.Equal(categoryId, detail.Classification!.CategoryId);
        Assert.Equal("Alimentacion", detail.Classification.CategoryName);
        Assert.Single(detail.Classification.GroupItems);
        Assert.Equal(MovementRole.Reference, detail.Classification.GroupItems[0].Role);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Movimiento clasificado CON GRUPO (2+ ClassifiedMovementItems) -- el caso que
    //    reproduce el bug de EF Core reportado durante Patch 0107.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBySourceAsync_MovimientoConGrupoDeMatchingDeVariosItems_NoLanzaYDevuelveLosItemsDelGrupo()
    {
        // Antes del fix (Patch 0108) esto lanzaba InvalidOperationException ("Cycles are not
        // allowed in no-tracking queries") -- ver doc-comment de la clase. El test no solo
        // verifica el resultado: el mero hecho de que GetBySourceAsync complete sin excepción
        // ya es la prueba central del fix.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var referenceId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (banco)");
        var candidateId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (tarjeta)");
        await SeedClassifiedMovementAsync(
            dbName, categoryId,
            (SourceEntityType.Transaction, referenceId, MovementRole.Reference),
            (SourceEntityType.Transaction, candidateId, MovementRole.Candidate));
        var service = CreateService(dbName);

        var detail = await service.GetBySourceAsync(SourceEntityType.Transaction, referenceId);

        Assert.NotNull(detail);
        Assert.NotNull(detail!.Classification);
        Assert.Equal(2, detail.Classification!.GroupItems.Count);
        Assert.Contains(detail.Classification.GroupItems, i => i.SourceId == referenceId && i.Role == MovementRole.Reference);
        Assert.Contains(detail.Classification.GroupItems, i => i.SourceId == candidateId && i.Role == MovementRole.Candidate);
    }

    [Fact]
    public async Task GetManyBySourceAsync_MovimientoConGrupoDeMatchingDeVariosItems_NoLanzaYDevuelveLosItemsDelGrupo()
    {
        // Mismo escenario que el test anterior, pero por el camino batched
        // (GetManyBySourceAsync) -- tiene su propia consulta con el mismo Include
        // problemático (ver MovementLookupService.cs), separada de LoadClassificationAsync.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var referenceId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (banco)");
        var candidateId = await SeedTransactionAsync(dbName, "DLO*PEDIDOSYA MCDONALD (tarjeta)");
        await SeedClassifiedMovementAsync(
            dbName, categoryId,
            (SourceEntityType.Transaction, referenceId, MovementRole.Reference),
            (SourceEntityType.Transaction, candidateId, MovementRole.Candidate));
        var service = CreateService(dbName);

        var results = await service.GetManyBySourceAsync(
            [(SourceEntityType.Transaction, referenceId), (SourceEntityType.Transaction, candidateId)]);

        var referenceDetail = results[(SourceEntityType.Transaction, referenceId)];
        var candidateDetail = results[(SourceEntityType.Transaction, candidateId)];
        Assert.NotNull(referenceDetail.Classification);
        Assert.NotNull(candidateDetail.Classification);
        Assert.Equal(2, referenceDetail.Classification!.GroupItems.Count);
        Assert.Equal(2, candidateDetail.Classification!.GroupItems.Count);
    }

    [Fact]
    public async Task GetManyBySourceAsync_VariosMovimientosClasificadosSinGrupo_CadaUnoTieneUnSoloItem()
    {
        // Regresión del camino batched para el caso normal (1:1, sin grupo) -- confirma que
        // el fix (segunda consulta batched para grupos) no introdujo un item de más ni de
        // menos cuando no hay grupo real.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var transactionA = await SeedTransactionAsync(dbName, "Movimiento A");
        var transactionB = await SeedTransactionAsync(dbName, "Movimiento B");
        await SeedClassifiedMovementAsync(dbName, categoryId, (SourceEntityType.Transaction, transactionA, MovementRole.Reference));
        await SeedClassifiedMovementAsync(dbName, categoryId, (SourceEntityType.Transaction, transactionB, MovementRole.Reference));
        var service = CreateService(dbName);

        var results = await service.GetManyBySourceAsync(
            [(SourceEntityType.Transaction, transactionA), (SourceEntityType.Transaction, transactionB)]);

        Assert.Single(results[(SourceEntityType.Transaction, transactionA)].Classification!.GroupItems);
        Assert.Single(results[(SourceEntityType.Transaction, transactionB)].Classification!.GroupItems);
    }
}
