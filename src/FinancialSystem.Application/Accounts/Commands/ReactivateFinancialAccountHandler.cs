using FinancialSystem.Application.Abstractions;

namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Migrado desde FinancialAccountEndpoints.Reactivate (PATCH-048) -- misma lógica
/// exacta: idempotente si ya estaba activa, y no permite reactivar si el Name ya está
/// en uso por otra cuenta activa (misma invariante que Create/Update, ver comentario
/// original: "Reactivar no debe romper la invariante de nombre único entre cuentas
/// activas").
/// </summary>
public sealed class ReactivateFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ReactivateFinancialAccountHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<ReactivateFinancialAccountResult> Handle(
        ReactivateFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var account = await _db.FinancialAccounts.FindAsync([command.FinancialAccountId], cancellationToken);
        if (account is null) return ReactivateFinancialAccountResult.NotFound();
        if (!account.IsDeactivated)
            return ReactivateFinancialAccountResult.Success(
                ReactivateFinancialAccountOutcome.AlreadyActive, "Ya estaba activa");

        var taken = await FinancialAccountNameUniqueness.IsTakenByActiveAccountAsync(
            _db, account.Name, excludeId: command.FinancialAccountId, cancellationToken);
        if (taken)
            return ReactivateFinancialAccountResult.NameConflict(
                $"No se puede reactivar: ya existe una cuenta activa con Name='{account.Name}'");

        account.IsDeactivated = false;
        account.UpdatedAt = _dateTimeProvider.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return ReactivateFinancialAccountResult.Success(
            ReactivateFinancialAccountOutcome.Reactivated, $"Cuenta '{account.Name}' reactivada");
    }
}
