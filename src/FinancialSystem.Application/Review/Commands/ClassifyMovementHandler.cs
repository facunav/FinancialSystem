using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Review.Commands;

/// <summary>
/// Crea un ClassifiedMovement (Status=Reviewed) + ClassifiedMovementItem (Role=Reference)
/// a partir de un movimiento crudo (Transaction, BankStatement o LegacyImportedExpense),
/// sin depender del motor de sugerencias.
/// </summary>
public sealed class ClassifyMovementHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ClassifyMovementHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<ClassifyMovementResult> Handle(
        ClassifyMovementCommand command, CancellationToken cancellationToken = default)
    {
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

        var classifiedMovement = new ClassifiedMovement
        {
            EffectiveDate = source.Date,
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
