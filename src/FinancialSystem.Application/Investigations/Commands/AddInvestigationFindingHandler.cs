using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Crea un InvestigationFinding en una Investigation ya existente. Sin lógica
/// adicional: no toca Investigation.Status, no toca Investigation.UpdatedAt (mismo
/// criterio que LinkMovementToInvestigationHandler — el proyecto no tiene la
/// convención de propagar UpdatedAt al padre cuando se agrega un hijo) y no toca
/// References.
/// </summary>
public sealed class AddInvestigationFindingHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddInvestigationFindingHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AddInvestigationFindingResult> Handle(
        AddInvestigationFindingCommand command, CancellationToken cancellationToken = default)
    {
        var investigationExists = await _db.Investigations
            .AnyAsync(i => i.Id == command.InvestigationId, cancellationToken);
        if (!investigationExists)
            return AddInvestigationFindingResult.InvestigationNotFound();

        var now = _dateTimeProvider.UtcNow;

        var finding = new InvestigationFinding
        {
            InvestigationId = command.InvestigationId,
            Text = command.Text,
            CreatedAt = now,
        };

        _db.InvestigationFindings.Add(finding);
        await _db.SaveChangesAsync(cancellationToken);

        return AddInvestigationFindingResult.Success(finding.Id, finding.CreatedAt);
    }
}
