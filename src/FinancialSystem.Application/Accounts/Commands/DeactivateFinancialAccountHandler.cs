using FinancialSystem.Application.Abstractions;

namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Migrado desde FinancialAccountEndpoints.Deactivate (PATCH-048) -- desactivación
/// lógica idempotente, con los mismos dos mensajes exactos que el código original ("Ya
/// estaba desactivada" / "Cuenta '...' desactivada").
/// </summary>
public sealed class DeactivateFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeactivateFinancialAccountHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<DeactivateFinancialAccountResult> Handle(
        DeactivateFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var account = await _db.FinancialAccounts.FindAsync([command.FinancialAccountId], cancellationToken);
        if (account is null) return DeactivateFinancialAccountResult.NotFound();
        if (account.IsDeactivated)
            return DeactivateFinancialAccountResult.Success(
                DeactivateFinancialAccountOutcome.AlreadyDeactivated, "Ya estaba desactivada");

        account.IsDeactivated = true;
        account.UpdatedAt = _dateTimeProvider.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return DeactivateFinancialAccountResult.Success(
            DeactivateFinancialAccountOutcome.Deactivated, $"Cuenta '{account.Name}' desactivada");
    }
}
