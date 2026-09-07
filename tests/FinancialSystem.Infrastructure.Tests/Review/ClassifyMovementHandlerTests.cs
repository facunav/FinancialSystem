using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Application.Review.Commands;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Review;

/// <summary>
/// Cubre el soporte de EffectiveDate editable en ClassifiedMovement (período financiero
/// distinto de la fecha bancaria). Cada Handle() usa un ClassifyMovementHandler nuevo
/// (mismo InMemory database name) a propósito: reutilizar la misma instancia de
/// AppDbContext entre llamadas arrastraría entidades trackeadas en memoria y podría
/// esconder que una mutación hecha por fuera (o por una llamada anterior) no se
/// refleja realmente en lo persistido.
/// </summary>
public class ClassifyMovementHandlerTests
{
    [Fact]
    public async Task Handle_MovimientoNuevo_SinEffectiveDate_NaceIgualAOriginalDate()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var bankDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(bankDate, classified.EffectiveDate);
    }

    [Fact]
    public async Task Handle_MovimientoNuevo_ConEffectiveDateDesdeApi_UsaEseValorNormalizadoAUtc()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var bankDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);

        // Simula lo que llega realmente desde el request HTTP: un <input type="date">
        // manda "2026-02-01" sin offset, que System.Text.Json deserializa con
        // Kind=Unspecified -- no Utc.
        var requestEffectiveDate = DateTime.SpecifyKind(new DateTime(2026, 2, 1), DateTimeKind.Unspecified);

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null, requestEffectiveDate));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(new DateTime(2026, 2, 1), classified.EffectiveDate);
        Assert.Equal(DateTimeKind.Utc, classified.EffectiveDate.Kind);
    }

    [Fact]
    public async Task Handle_ReclasificarSinEffectiveDate_ConservaElValorAjustadoAnteriormente()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName, "Original");
        var otherCategoryId = await SeedCategoryAsync(dbName, "Otra");
        var bankDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        // Ajuste manual previo -- lo que la UI ya habría hecho en una reclasificación
        // anterior, enviando un EffectiveDate distinto de la fecha bancaria.
        var adjustedEffectiveDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null, adjustedEffectiveDate));

        // Reclasificación posterior que solo cambia la categoría -- sin EffectiveDate
        // en el comando. No debe recalcularse desde OriginalDate ni resetearse.
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, otherCategoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using var db = OpenDb(dbName);
        var updated = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(otherCategoryId, updated.CategoryId);
        Assert.Equal(adjustedEffectiveDate, updated.EffectiveDate);
    }

    [Fact]
    public async Task Handle_ReclasificarConEffectiveDate_ActualizaSoloEseCampoNormalizadoAUtc()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var bankDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        // Mismo caso real que en la creación: el request HTTP manda una fecha sin
        // offset, deserializada con Kind=Unspecified.
        var requestEffectiveDate = DateTime.SpecifyKind(new DateTime(2026, 3, 1), DateTimeKind.Unspecified);
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null, requestEffectiveDate));

        await using var db = OpenDb(dbName);
        var updated = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(new DateTime(2026, 3, 1), updated.EffectiveDate);
        Assert.Equal(DateTimeKind.Utc, updated.EffectiveDate.Kind);

        // La fecha bancaria del snapshot nunca cambia, sin importar cuántas veces se
        // reclasifique ni qué EffectiveDate se haya pedido.
        var item = await db.ClassifiedMovementItems.SingleAsync();
        Assert.Equal(bankDate, item.OriginalDate);
    }

    [Fact]
    public async Task ImportarUnaTransaccionNueva_NoCreaClassifiedMovementAutomaticamente()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTransactionAsync(dbName, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        await using var db = OpenDb(dbName);
        Assert.Equal(0, await db.ClassifiedMovements.CountAsync());
    }

    [Fact]
    public async Task MovimientoYaClasificado_NoCambiaAnteUnaClasificacionNoRelacionada()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);

        // Simula uno de los ~150 movimientos ya clasificados antes de este cambio:
        // EffectiveDate == OriginalDate, tal como los creó siempre ClassifyMovementHandler.
        var preexistingBankDate = new DateTime(2025, 11, 30, 0, 0, 0, DateTimeKind.Utc);
        var preexistingTransactionId = await SeedTransactionAsync(dbName, preexistingBankDate);
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, preexistingTransactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        Guid preexistingId;
        await using (var db = OpenDb(dbName))
        {
            preexistingId = (await db.ClassifiedMovements.SingleAsync()).Id;
        }

        // Clasificar un movimiento completamente distinto no debe alterar el anterior.
        var otherTransactionId = await SeedTransactionAsync(dbName, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, otherTransactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using var finalDb = OpenDb(dbName);
        var untouched = await finalDb.ClassifiedMovements.SingleAsync(cm => cm.Id == preexistingId);
        Assert.Equal(preexistingBankDate, untouched.EffectiveDate);
        Assert.Equal(2, await finalDb.ClassifiedMovements.CountAsync());
    }

    // ── Patch 0074 (PATCH-021): ProcessingSource debe reflejar siempre el origen de
    // la clasificación vigente, no el de la clasificación inicial ──────────────────

    [Fact]
    public async Task Handle_ClasificacionInicial_EstableceProcessingSourceManualReview()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var transactionId = await SeedTransactionAsync(dbName, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(ProcessingSource.ManualReview, classified.ProcessingSource);
    }

    [Fact]
    public async Task Handle_ReclasificarManualmente_MantieneProcessingSourceManualReview()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName, "Original");
        var otherCategoryId = await SeedCategoryAsync(dbName, "Otra");
        var transactionId = await SeedTransactionAsync(dbName, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        // Reclasificación manual posterior (mismo handler, mismo origen: no hay
        // ningún otro flujo que llegue acá) -- debe seguir siendo ManualReview.
        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, otherCategoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(otherCategoryId, classified.CategoryId);
        Assert.Equal(ProcessingSource.ManualReview, classified.ProcessingSource);
    }

    [Fact]
    public async Task Handle_ReclasificarUnMovimientoConfirmadoPorSugerencia_ActualizaProcessingSourceAManualReview()
    {
        // Simula un ClassifiedMovement histórico clasificado por un origen distinto
        // de ManualReview: ConfirmedFromSuggestion, del flujo de sugerencias que
        // PR-L4/PR-L5 retiró como productor pero cuyas filas históricas siguen
        // existiendo (ver ProcessingSource.cs). Antes de este patch, reclasificarlo
        // acá dejaba ese origen desactualizado indefinidamente aunque el usuario
        // acabara de reclasificarlo a mano.
        var dbName = Guid.NewGuid().ToString();
        var originalCategoryId = await SeedCategoryAsync(dbName, "Original");
        var newCategoryId = await SeedCategoryAsync(dbName, "Nueva");
        var bankDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);
        await SeedClassifiedMovementAsync(
            dbName, SourceEntityType.Transaction, transactionId, originalCategoryId,
            ProcessingSource.ConfirmedFromSuggestion, bankDate);

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, newCategoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(newCategoryId, classified.CategoryId);
        Assert.Equal(ProcessingSource.ManualReview, classified.ProcessingSource);
    }

    [Fact]
    public async Task Handle_ReclasificarUnMovimientoConfirmadoPorMatchManual_ActualizaProcessingSourceAManualReview()
    {
        // Mismo caso que el anterior, con otro origen histórico soportado por el enum
        // (ManualMatch) -- confirma que la actualización no depende del valor previo.
        var dbName = Guid.NewGuid().ToString();
        var originalCategoryId = await SeedCategoryAsync(dbName, "Original");
        var newCategoryId = await SeedCategoryAsync(dbName, "Nueva");
        var bankDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var transactionId = await SeedTransactionAsync(dbName, bankDate);
        await SeedClassifiedMovementAsync(
            dbName, SourceEntityType.Transaction, transactionId, originalCategoryId,
            ProcessingSource.ManualMatch, bankDate);

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, newCategoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(ProcessingSource.ManualReview, classified.ProcessingSource);
    }

    [Fact]
    public async Task Handle_ClasificacionInicial_NoTieneRegresionesEnLasDemasDimensiones()
    {
        // Ausencia de regresiones (sección de tests del patch): agregar la
        // actualización de ProcessingSource en la rama de reclasificación no debe
        // afectar en nada la rama de creación inicial -- mismas dimensiones, mismo
        // Status, mismo comportamiento que antes de este patch.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var transactionId = await SeedTransactionAsync(dbName, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, "comentario"));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(MovementType.Purchase, classified.MovementType);
        Assert.Equal(FinancialImpact.Expense, classified.FinancialImpact);
        Assert.Equal(categoryId, classified.CategoryId);
        Assert.Equal("comentario", classified.Comment);
        Assert.Equal(ClassificationStatus.Reviewed, classified.Status);
        Assert.Equal(ProcessingSource.ManualReview, classified.ProcessingSource);
    }

    [Fact]
    public async Task Handle_ClasificacionInicialYReclasificacion_NuncaEscribenMatchScoreNiAmountDelta()
    {
        // Patch 0076 (PATCH-023): confirma en código lo que documenta
        // ClassifiedMovement.cs -- ClassifyMovementHandler, el único productor de
        // ClassifiedMovement hoy, no escribe MatchScore ni AmountDelta en ninguna de
        // sus dos ramas (creación ni reclasificación). Ambos son remanentes del
        // motor de matching retirado en PR-L4 y quedan siempre en null para
        // cualquier fila creada o actualizada después de ese retiro.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName, "Original");
        var otherCategoryId = await SeedCategoryAsync(dbName, "Otra");
        var transactionId = await SeedTransactionAsync(dbName, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using (var db = OpenDb(dbName))
        {
            var created = await db.ClassifiedMovements.SingleAsync();
            Assert.Null(created.MatchScore);
            Assert.Null(created.AmountDelta);
        }

        await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.Transaction, transactionId, otherCategoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        await using var finalDb = OpenDb(dbName);
        var reclassified = await finalDb.ClassifiedMovements.SingleAsync();
        Assert.Null(reclassified.MatchScore);
        Assert.Null(reclassified.AmountDelta);
    }

    // ── DEDUPE-017: no duplicar ClassifiedMovement entre miembros del mismo
    // IdentityGroupId (MovementIdentityLink) ────────────────────────────────────────

    [Fact]
    public async Task Handle_SourceIdConMovementIdentityLinkYMiembroYaClasificado_FallaSinCrearSegundoClassifiedMovement()
    {
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var bankDate = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

        // BankStatement A (pendiente) y B (liquidado/reexportación) -- mismo evento
        // económico real, vinculados por DedupeEngine.ApplyAsync vía MovementIdentityLink.
        var bankStatementAId = await SeedBankStatementAsync(dbName, bankDate, "TRANSFERENCIA");
        var bankStatementBId = await SeedBankStatementAsync(dbName, bankDate, "TRANSFERENCIA");
        var identityGroupId = Guid.NewGuid();
        await SeedMovementIdentityLinkAsync(dbName, identityGroupId, bankStatementAId, IdentityRole.Pendiente);
        await SeedMovementIdentityLinkAsync(dbName, identityGroupId, bankStatementBId, IdentityRole.Liquidado);

        // A ya fue clasificado -- tiene su propio ClassifiedMovement + ClassifiedMovementItem.
        await SeedClassifiedMovementAsync(
            dbName, SourceEntityType.BankStatement, bankStatementAId, categoryId,
            ProcessingSource.ManualReview, bankDate);

        // Intentar clasificar B (nunca clasificado individualmente todavía) debe fallar,
        // no crear un segundo ClassifiedMovement para el mismo evento económico.
        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.BankStatement, bankStatementBId, categoryId,
            MovementType.Transfer, FinancialImpact.InternalMovement, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ClassifyMovementFailureReason.PartOfAlreadyClassifiedIdentityGroup, result.FailureReason);

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.ClassifiedMovements.CountAsync());
        Assert.Equal(1, await db.ClassifiedMovementItems.CountAsync());
    }

    [Fact]
    public async Task Handle_BankStatementSinMovementIdentityLink_FuncionaComoAntes()
    {
        // Regresión: un BankStatement sin ningún MovementIdentityLink (el caso normal,
        // la inmensa mayoría de los movimientos) debe seguir clasificándose exactamente
        // igual que antes de este patch -- la nueva consulta a MovementIdentityLinks no
        // encuentra nada y no cambia el camino existente.
        var dbName = Guid.NewGuid().ToString();
        var categoryId = await SeedCategoryAsync(dbName);
        var bankDate = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        var bankStatementId = await SeedBankStatementAsync(dbName, bankDate, "PAGO CON VISA DEBITO OP1234");

        var result = await CreateHandler(dbName).Handle(new ClassifyMovementCommand(
            SourceEntityType.BankStatement, bankStatementId, categoryId,
            MovementType.Purchase, FinancialImpact.Expense, null, null));

        Assert.True(result.IsSuccess);

        await using var db = OpenDb(dbName);
        Assert.Equal(1, await db.ClassifiedMovements.CountAsync());
        var classified = await db.ClassifiedMovements.SingleAsync();
        Assert.Equal(bankDate, classified.EffectiveDate);
    }

    private static ClassifyMovementHandler CreateHandler(string dbName) =>
        new(OpenDb(dbName), new FakeDateTimeProvider(), NullLogger<ClassifyMovementHandler>.Instance);

    private static AppDbContext OpenDb(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedCategoryAsync(string dbName, string name = "Test")
    {
        await using var db = OpenDb(dbName);
        var category = new Category { Name = name, DisplayName = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static async Task<Guid> SeedTransactionAsync(string dbName, DateTime date)
    {
        await using var db = OpenDb(dbName);
        var transaction = new Transaction
        {
            Date = date,
            Description = "Test transaction",
            Amount = 100m,
            Currency = "ARS",
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private static async Task<Guid> SeedBankStatementAsync(string dbName, DateTime date, string concept)
    {
        await using var db = OpenDb(dbName);
        var bankStatement = new BankStatement
        {
            Date = date,
            Concept = concept,
            Amount = 100m,
            Currency = "ARS",
            BankName = "BBVA",
            ExternalId = Guid.NewGuid().ToString(),
            ImportedAtUtc = date,
        };
        db.BankStatements.Add(bankStatement);
        await db.SaveChangesAsync();
        return bankStatement.Id;
    }

    // Siembra directa de un MovementIdentityLink -- simula lo que ya dejó persistido
    // DedupeEngine.ApplyAsync (no se ejecuta ApplyAsync real acá: este test cubre
    // exclusivamente ClassifyMovementHandler, con el link ya asumido como dado).
    private static async Task SeedMovementIdentityLinkAsync(
        string dbName, Guid identityGroupId, Guid sourceId, IdentityRole role)
    {
        await using var db = OpenDb(dbName);
        db.MovementIdentityLinks.Add(new MovementIdentityLink
        {
            IdentityGroupId = identityGroupId,
            SourceEntityType = SourceEntityType.BankStatement,
            SourceId = sourceId,
            Role = role,
            Classification = IdentityClassification.Fuerte,
            Evidence = "Test: F+K+L, frecuencia=1",
            CreatedAtUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "DedupeEngine",
        });
        await db.SaveChangesAsync();
    }

    // Siembra directa (sin pasar por el handler, que siempre escribe ManualReview) de
    // un ClassifiedMovement con un ProcessingSource arbitrario -- simula el estado de
    // un movimiento histórico clasificado por otro origen antes de ser reclasificado
    // en el test.
    private static async Task SeedClassifiedMovementAsync(
        string dbName, SourceEntityType sourceEntityType, Guid sourceId, Guid categoryId,
        ProcessingSource processingSource, DateTime effectiveDate)
    {
        await using var db = OpenDb(dbName);
        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = effectiveDate,
            TotalAmount = 100m,
            Currency = "ARS",
            Description = "Movimiento clasificado de prueba",
            MovementType = MovementType.Purchase,
            FinancialImpact = FinancialImpact.Expense,
            CategoryId = categoryId,
            Status = ClassificationStatus.Confirmed,
            ProcessingSource = processingSource,
            CreatedAt = effectiveDate,
            ProcessedAt = effectiveDate,
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
        await db.SaveChangesAsync();
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);
    }
}
