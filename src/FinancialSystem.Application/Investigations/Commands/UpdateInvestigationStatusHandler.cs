using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Application.Investigations.Commands;

/// <summary>
/// Cambia Investigation.Status y UpdatedAt. Conclusion solo se guarda cuando
/// Status = Resolved (y en ese caso es obligatoria) — en cualquier otro estado no se
/// toca. Sin lógica adicional: no toca References ni Findings.
/// </summary>
public sealed class UpdateInvestigationStatusHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateInvestigationStatusHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UpdateInvestigationStatusResult> Handle(
        UpdateInvestigationStatusCommand command, CancellationToken cancellationToken = default)
    {
        var investigation = await _db.Investigations
            .FirstOrDefaultAsync(i => i.Id == command.InvestigationId, cancellationToken);

        if (investigation is null)
            return UpdateInvestigationStatusResult.Failure(UpdateInvestigationStatusFailureReason.InvestigationNotFound);

        if (command.Status == InvestigationStatus.Resolved && string.IsNullOrWhiteSpace(command.Conclusion))
            return UpdateInvestigationStatusResult.Failure(UpdateInvestigationStatusFailureReason.ConclusionRequired);

        investigation.Status = command.Status;

        if (command.Status == InvestigationStatus.Resolved)
            investigation.Conclusion = command.Conclusion;

        var now = _dateTimeProvider.UtcNow;
        investigation.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return UpdateInvestigationStatusResult.Success(now);
    }
}
