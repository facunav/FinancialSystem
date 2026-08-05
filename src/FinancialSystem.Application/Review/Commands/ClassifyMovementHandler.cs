using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Crea un ClassifiedMovement (Status=Reviewed) + ClassifiedMovementItem (Role=Reference)
/// a partir de un movimiento crudo (Transaction o BankStatement), sin depender del
/// motor de revisión.
///
/// RECLASIFICACIÓN (K3): si el origen ya tiene un ClassifiedMovementItem y ese
/// ClassifiedMovement tiene un único item (fue creado por este mismo handler), se
/// actualiza en el lugar en vez de crear uno nuevo. Sin esto, reclasificar duplicaría
/// el movimiento en las métricas — no hay índice único sobre (SourceEntityType,
/// SourceId) que lo evite a nivel de base.
/// Si el ClassifiedMovement tiene más de un item, se rechaza: decidir qué pasa con el
/// resto del grupo excede este caso de uso. PR-L4: esos grupos de más de un item solo
/// podían crearse vía ConfirmMatchCommand (retirado — exigía al menos un Reference y
/// un Candidate, dos items como mínimo), así que ya no se generan grupos nuevos, pero
/// los históricos siguen existiendo y esta protección los sigue cubriendo.
///
/// Patch 0074 (PATCH-021): al reclasificar, ProcessingSource se actualiza a
/// ManualReview igual que el resto de las dimensiones, sin importar cuál haya sido el
/// origen previo (incluyendo orígenes de flujos ya retirados, ver ProcessingSource.cs)
/// -- representa siempre el origen de la clasificación vigente, nunca el inicial.
/// </summary>
public sealed class ClassifyMovementHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<ClassifyMovementHandler> _logger;

    public ClassifyMovementHandler(
        IApplicationDbContext db, IDateTimeProvider dateTimeProvider, ILogger<ClassifyMovementHandler> logger)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<ClassifyMovementResult> Handle(
        ClassifyMovementCommand command, CancellationToken cancellationToken = default)
    {
        // INSTRUMENTACIÓN TEMPORAL (dashboard-income-wrong-month): EffectiveDate tal
        // como llega deserializado del request, antes de cualquier ToUtc/SpecifyKind.
        // Kind=Unspecified acá es normal (ver ToUtc); lo que importa es que Ticks
        // coincida con lo que el usuario tipeó en el date picker.
        _logger.LogInformation(
            "ClassifyMovementHandler.Handle: SourceEntityType={SourceEntityType} SourceId={SourceId} " +
            "commandEffectiveDate={EffectiveDate:O} commandEffectiveDateKind={Kind}",
            command.SourceEntityType, command.SourceId,
            command.EffectiveDate, command.EffectiveDate?.Kind);

        var source = await FindSourceAsync(command.SourceEntityType, command.SourceId, cancellationToken);
        if (source is null)
            return ClassifyMovementResult.Failure(ClassifyMovementFailureReason.SourceNotFound);

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == command.CategoryId, cancellationToken);
        if (!categoryExists)
            return ClassifyMovementResult.Failure(ClassifyMovementFailureReason.CategoryNotFound);

        if (command.CounterpartyId is { } counterpartyId)
        {
            var counterpartyExists = await _db.Counterparties
                .AnyAsync(c => c.Id == counterpartyId, cancellationToken);
            if (!counterpartyExists)
                return ClassifyMovementResult.Failure(ClassifyMovementFailureReason.CounterpartyNotFound);
        }

        var now = _dateTimeProvider.UtcNow;

        var existingItem = await _db.ClassifiedMovementItems
            .Include(i => i.ClassifiedMovement)
            .ThenInclude(cm => cm!.Items)
            .FirstOrDefaultAsync(
                i => i.SourceEntityType == command.SourceEntityType && i.SourceId == command.SourceId,
                cancellationToken);

        if (existingItem is not null)
        {
            var existing = existingItem.ClassifiedMovement!;
            if (existing.Items.Count > 1)
                return ClassifyMovementResult.Failure(ClassifyMovementFailureReason.AlreadyPartOfMatchGroup);

            existing.MovementType = command.MovementType;
            existing.FinancialImpact = command.FinancialImpact;
            existing.CategoryId = command.CategoryId;
            existing.CounterpartyId = command.CounterpartyId;
            existing.Comment = command.Comment;
            existing.ProcessedAt = now;

            // Patch 0074 (PATCH-021): ProcessingSource debe reflejar el origen de la
            // clasificación VIGENTE, no el de la clasificación inicial. Este handler
            // solo se invoca para clasificación manual (ver doc-comment de
            // ClassifyMovementCommand: "Clasifica manualmente un movimiento crudo") --
            // antes de este patch, reclasificar acá un movimiento cuyo ProcessingSource
            // original era, por ejemplo, ConfirmedFromSuggestion (de un flujo de
            // sugerencias ya retirado, PR-L4/PR-L5, pero con filas históricas) dejaba
            // ese origen desactualizado indefinidamente, aunque el usuario acabara de
            // reclasificarlo a mano. Sin versionado ni ProcessingSource múltiple (fuera
            // de alcance): se sobrescribe, igual que el resto de las dimensiones.
            existing.ProcessingSource = ProcessingSource.ManualReview;

            // Sin EffectiveDate en el comando: no tocar el campo. Nunca
            // "command.EffectiveDate ?? OriginalDate" — eso resetearía silenciosamente
            // un período financiero ya ajustado a mano en una reclasificación posterior
            // (cambiar de categoría, por ejemplo) que no tenía intención de tocar la fecha.
            if (command.EffectiveDate is { } newEffectiveDate)
                existing.EffectiveDate = ToUtc(newEffectiveDate);

            await _db.SaveChangesAsync(cancellationToken);

            // Lectura de diagnóstico: AsNoTracking fuerza un SELECT nuevo contra la DB,
            // en vez de devolver el valor cacheado en el change tracker. Si Postgres/Npgsql
            // redondea o trunca el valor al guardarlo, PersistedEffectiveDate va a diferir
            // de InMemoryEffectiveDate acá.
            var persisted = await _db.ClassifiedMovements
                .AsNoTracking()
                .Where(m => m.Id == existing.Id)
                .Select(m => m.EffectiveDate)
                .FirstAsync(cancellationToken);
            _logger.LogInformation(
                "ClassifyMovementHandler.Handle: RECLASSIFY ClassifiedMovementId={Id} " +
                "inMemoryEffectiveDate={InMemory:O} readBackFromDb={Persisted:O}",
                existing.Id, existing.EffectiveDate, persisted);

            return ClassifyMovementResult.Success(existing.Id);
        }

        var classifiedMovement = new ClassifiedMovement
        {
            // Si el comando trae EffectiveDate (usuario ajustó el período financiero
            // ya en la primera clasificación), se usa ese valor; si no, nace igual a
            // la fecha bancaria, como siempre.
            EffectiveDate = command.EffectiveDate is { } initialEffectiveDate
                ? ToUtc(initialEffectiveDate)
                : source.Date,
            TotalAmount = Math.Abs(source.Amount),
            Currency = source.Currency,
            Description = source.Description,
            MovementType = command.MovementType,
            FinancialImpact = command.FinancialImpact,
            CategoryId = command.CategoryId,
            CounterpartyId = command.CounterpartyId,
            Status = ClassificationStatus.Reviewed,
            ProcessingSource = ProcessingSource.ManualReview,
            Comment = command.Comment,
            CreatedAt = now,
            ProcessedAt = now,
        };

        classifiedMovement.Items.Add(new ClassifiedMovementItem
        {
            ClassifiedMovementId = classifiedMovement.Id,
            SourceEntityType = command.SourceEntityType,
            SourceId = command.SourceId,
            Role = MovementRole.Reference,
            OriginalAmount = source.Amount,
            OriginalDate = source.Date,
            OriginalDescription = source.Description,
            OriginalCurrency = source.Currency,
            OriginalSourceFile = source.SourceFile,
        });

        _db.ClassifiedMovements.Add(classifiedMovement);
        await _db.SaveChangesAsync(cancellationToken);

        var persistedNew = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(m => m.Id == classifiedMovement.Id)
            .Select(m => m.EffectiveDate)
            .FirstAsync(cancellationToken);
        _logger.LogInformation(
            "ClassifyMovementHandler.Handle: CREATE ClassifiedMovementId={Id} " +
            "inMemoryEffectiveDate={InMemory:O} readBackFromDb={Persisted:O}",
            classifiedMovement.Id, classifiedMovement.EffectiveDate, persistedNew);

        return ClassifyMovementResult.Success(classifiedMovement.Id);
    }

    private async Task<SourceSnapshot?> FindSourceAsync(
        SourceEntityType sourceEntityType, Guid sourceId, CancellationToken cancellationToken)
    {
        switch (sourceEntityType)
        {
            case SourceEntityType.BankStatement:
                var bankStatement = await _db.BankStatements
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == sourceId, cancellationToken);
                return bankStatement is null ? null : new SourceSnapshot(
                    bankStatement.Date, bankStatement.Concept, bankStatement.Amount,
                    bankStatement.Currency, bankStatement.SourceFile);

            case SourceEntityType.Transaction:
                var transaction = await _db.Transactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == sourceId, cancellationToken);
                return transaction is null ? null : new SourceSnapshot(
                    transaction.Date, transaction.Description, transaction.Amount,
                    transaction.Currency, transaction.SourceFile);

            // PR-L5: LegacyImportedExpense (y el case SourceEntityType.LegacyImport que
            // la resolvía acá) se eliminó — no queda ninguna fuente detrás de ese valor
            // de enum. SourceEntityType.LegacyImport se conserva igual (ver comentario en
            // SourceEntityType.cs): filas históricas de ClassifiedMovementItem siguen
            // usándolo, simplemente ya no hay nada que clasificar con ese origen.
            default:
                return null;
        }
    }

    private sealed record SourceSnapshot(
        DateTime Date, string Description, decimal Amount, string Currency, string? SourceFile);

    // EffectiveDate proveniente de ClassifyMovementCommand llega deserializado del
    // request HTTP y puede tener Kind=Unspecified (ej. "2026-03-01" sin offset).
    // ClassifiedMovement.EffectiveDate mapea a timestamp with time zone en
    // PostgreSQL, que rechaza escribir un DateTime sin Kind=Utc -- mismo patrón que
    // ya usa el resto del proyecto para esta columna (ver TransactionNormalizer,
    // BbvaBankStatementParser, FinancialMetricsService, MovementLoader).
    private static DateTime ToUtc(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
}
