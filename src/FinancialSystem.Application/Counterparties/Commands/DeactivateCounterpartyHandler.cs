using FinancialSystem.Application.Abstractions;

namespace FinancialSystem.Application.Counterparties.Commands;

/// <summary>
/// Migrado desde CounterpartyEndpoints.Deactivate (PATCH-047) -- desactivación lógica
/// idempotente, con los mismos dos mensajes exactos que el código original ("Ya estaba
/// desactivada" / "Contraparte '...' desactivada").
/// </summary>
public sealed class DeactivateCounterpartyHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeactivateCounterpartyHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<DeactivateCounterpartyResult> Handle(
        DeactivateCounterpartyCommand command, CancellationToken cancellationToken = default)
    {
        var c = await _db.Counterparties.FindAsync([command.CounterpartyId], cancellationToken);
        if (c is null) return DeactivateCounterpartyResult.NotFound();
        if (c.IsDeactivated)
            return DeactivateCounterpartyResult.Success(
                DeactivateCounterpartyOutcome.AlreadyDeactivated, "Ya estaba desactivada");

        c.IsDeactivated = true;
        c.UpdatedAt = _dateTimeProvider.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return DeactivateCounterpartyResult.Success(
            DeactivateCounterpartyOutcome.Deactivated, $"Contraparte '{c.Name}' desactivada");
    }
}
