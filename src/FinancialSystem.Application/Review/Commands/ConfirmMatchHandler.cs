using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Crea un ClassifiedMovement (Status=Confirmed) con un ClassifiedMovementItem por
/// cada item del grupo confirmado, soportando relaciones 1↔1, N↔1, 1↔N y N↔M entre
/// movimientos Reference (banco/tarjeta) y Candidate (legacy).
/// </summary>
public sealed class ConfirmMatchHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ConfirmMatchHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<ConfirmMatchResult> Handle(
        ConfirmMatchCommand command, CancellationToken cancellationToken = default)
    {
        var validationFailure = ValidateItems(command.Items);
        if (validationFailure is not null) return validationFailure;

        // K5: sin este chequeo, confirmar una sugerencia sobre un movimiento que ya
        // fue clasificado (manualmente, por otro match, o por una carrera entre dos
        // pestañas) crearía un segundo ClassifiedMovement/ClassifiedMovementItem para
        // el mismo origen — misma clase de bug que ClassifyMovementHandler ya evita
        // para clasificación manual (K3). Se rechaza todo el grupo en vez de intentar
        // "actualizar en el lugar": acá siempre hay ≥2 items, y decidir qué hacer con
        // el resto del grupo si uno solo choca no tiene una respuesta correcta única.
        foreach (var item in command.Items)
        {
            var alreadyClassified = await _db.ClassifiedMovementItems.AnyAsync(
                i => i.SourceEntityType == item.SourceEntityType && i.SourceId == item.SourceId,
                cancellationToken);
            if (alreadyClassified)
                return ConfirmMatchResult.Failure(
                    ConfirmMatchFailureReason.SourceAlreadyClassified, $"{item.SourceEntityType}/{item.SourceId}");
        }

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == command.CategoryId, cancellationToken);
        if (!categoryExists)
            return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.CategoryNotFound);

        if (command.CounterpartyId is { } counterpartyId)
        {
            var counterpartyExists = await _db.Counterparties
                .AnyAsync(c => c.Id == counterpartyId, cancellationToken);
            if (!counterpartyExists)
                return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.CounterpartyNotFound);
        }

        var snapshots = new List<(ConfirmMatchItem Item, SourceSnapshot Source)>();
        foreach (var item in command.Items)
        {
            var source = await FindSourceAsync(item.SourceEntityType, item.SourceId, cancellationToken);
            if (source is null)
                return ConfirmMatchResult.Failure(
                    ConfirmMatchFailureReason.SourceNotFound, $"{item.SourceEntityType}/{item.SourceId}");

            snapshots.Add((item, source));
        }

        var referenceSnapshots = snapshots.Where(s => s.Item.Role == MovementRole.Reference).ToList();
        var candidateSnapshots = snapshots.Where(s => s.Item.Role == MovementRole.Candidate).ToList();

        var referenceTotal = referenceSnapshots.Sum(s => Math.Abs(s.Source.Amount));
        var candidateTotal = candidateSnapshots.Sum(s => Math.Abs(s.Source.Amount));
        var canonicalReference = referenceSnapshots.OrderBy(s => s.Source.Date).First().Source;

        var now = _dateTimeProvider.UtcNow;

        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = canonicalReference.Date,
            TotalAmount = referenceTotal,
            Currency = canonicalReference.Currency,
            Description = canonicalReference.Description,
            MovementType = command.MovementType,
            FinancialImpact = command.FinancialImpact,
            CategoryId = command.CategoryId,
            CounterpartyId = command.CounterpartyId,
            Status = ClassificationStatus.Confirmed,
            ProcessingSource = ProcessingSource.ConfirmedFromSuggestion,
            AmountDelta = Math.Abs(referenceTotal - candidateTotal),
            CreatedAt = now,
            ProcessedAt = now,
        };

        foreach (var (item, source) in snapshots)
        {
            classifiedMovement.Items.Add(new ClassifiedMovementItem
            {
                ClassifiedMovementId = classifiedMovement.Id,
                SourceEntityType = item.SourceEntityType,
                SourceId = item.SourceId,
                Role = item.Role,
                OriginalAmount = source.Amount,
                OriginalDate = source.Date,
                OriginalDescription = source.Description,
                OriginalCurrency = source.Currency,
                OriginalSourceFile = source.SourceFile,
            });
        }

        _db.ClassifiedMovements.Add(classifiedMovement);
        await _db.SaveChangesAsync(cancellationToken);

        return ConfirmMatchResult.Success(classifiedMovement.Id);
    }

    private static ConfirmMatchResult? ValidateItems(IReadOnlyList<ConfirmMatchItem> items)
    {
        if (items.Count == 0)
            return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.EmptyItems);

        var distinctCount = items.Select(i => (i.SourceEntityType, i.SourceId)).Distinct().Count();
        if (distinctCount != items.Count)
            return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.DuplicateItem);

        if (items.All(i => i.Role != MovementRole.Reference))
            return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.MissingReference);

        if (items.All(i => i.Role != MovementRole.Candidate))
            return ConfirmMatchResult.Failure(ConfirmMatchFailureReason.MissingCandidate);

        foreach (var item in items)
        {
            var expectedReference = IsReferenceSource(item.SourceEntityType);
            var actualIsReference = item.Role == MovementRole.Reference;
            if (expectedReference != actualIsReference)
                return ConfirmMatchResult.Failure(
                    ConfirmMatchFailureReason.RoleSourceMismatch, $"{item.SourceEntityType}/{item.SourceId}");
        }

        return null;
    }

    private static bool IsReferenceSource(SourceEntityType sourceEntityType) =>
        sourceEntityType is SourceEntityType.BankStatement or SourceEntityType.Transaction;

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

            case SourceEntityType.LegacyImport:
                var legacyExpense = await _db.LegacyImportedExpenses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == sourceId, cancellationToken);
                return legacyExpense is null ? null : new SourceSnapshot(
                    legacyExpense.Date, legacyExpense.Description, legacyExpense.Amount,
                    legacyExpense.Currency, legacyExpense.SourceFile);

            default:
                return null;
        }
    }

    private sealed record SourceSnapshot(
        DateTime Date, string Description, decimal Amount, string Currency, string? SourceFile);
}
