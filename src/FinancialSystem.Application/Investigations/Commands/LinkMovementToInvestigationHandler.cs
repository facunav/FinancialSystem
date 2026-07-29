using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Asocia un movimiento existente a una Investigation ya creada. Sin lógica adicional:
/// no actualiza Investigation.Status ni UpdatedAt, no crea hallazgos ni historial (ver
/// ADR-007 §3) — solo la referencia.
/// </summary>
public sealed class LinkMovementToInvestigationHandler
{
    private readonly IApplicationDbContext _db;

    public LinkMovementToInvestigationHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<LinkMovementToInvestigationResult> Handle(
        LinkMovementToInvestigationCommand command, CancellationToken cancellationToken = default)
    {
        var investigationExists = await _db.Investigations
            .AnyAsync(i => i.Id == command.InvestigationId, cancellationToken);
        if (!investigationExists)
            return LinkMovementToInvestigationResult.InvestigationNotFound();

        var alreadyLinked = await _db.InvestigationReferences.AnyAsync(
            r => r.InvestigationId == command.InvestigationId
                && r.SourceEntityType == command.SourceEntityType
                && r.SourceId == command.SourceId,
            cancellationToken);

        if (alreadyLinked)
            return LinkMovementToInvestigationResult.Success(LinkMovementToInvestigationOutcome.AlreadyLinked);

        _db.InvestigationReferences.Add(new InvestigationReference
        {
            InvestigationId = command.InvestigationId,
            SourceEntityType = command.SourceEntityType,
            SourceId = command.SourceId,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return LinkMovementToInvestigationResult.Success(LinkMovementToInvestigationOutcome.Linked);
    }
}
