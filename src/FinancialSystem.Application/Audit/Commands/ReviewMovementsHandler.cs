using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Audit.Commands;

/// <summary>
/// Idempotente: un movimiento ya revisado no genera una fila duplicada ni falla si se
/// vuelve a pedir su revisión — ver el índice único de MovementAuditDecisionConfiguration.
/// Una sola consulta para todo el lote (mismo criterio que ya usa
/// ClassificationSuggestionService/AuditReportService: nunca una consulta por elemento).
/// </summary>
public sealed class ReviewMovementsHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ReviewMovementsHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(ReviewMovementsCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Movements.Count == 0) return;

        var sourceIds = command.Movements.Select(m => m.SourceId).Distinct().ToList();
        var existing = await _db.MovementAuditDecisions
            .Where(r => sourceIds.Contains(r.SourceId))
            .Select(r => new { r.SourceEntityType, r.SourceId })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(e => (e.SourceEntityType, e.SourceId)).ToHashSet();

        var now = _dateTimeProvider.UtcNow;

        foreach (var movement in command.Movements)
        {
            if (existingKeys.Contains((movement.SourceEntityType, movement.SourceId))) continue;

            _db.MovementAuditDecisions.Add(new MovementAuditDecision
            {
                SourceEntityType = movement.SourceEntityType,
                SourceId = movement.SourceId,
                ReviewedAtUtc = now,
            });

            existingKeys.Add((movement.SourceEntityType, movement.SourceId));
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
