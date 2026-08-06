using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Migrado desde FinancialAccountEndpoints.Update (PATCH-048) -- actualización parcial
/// idéntica a la original, incluido el orden de validación: NotFound primero, luego
/// conflicto de Name (solo si Name viene informado), recién después Type inválido
/// (solo si Type viene informado) -- ver el comentario de
/// <see cref="UpdateFinancialAccountCommand"/>. AccountNumber/Notes en blanco limpian
/// el campo (null) cuando vienen informados; null en ambos significa "no tocar".
/// </summary>
public sealed class UpdateFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateFinancialAccountHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UpdateFinancialAccountResult> Handle(
        UpdateFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var account = await _db.FinancialAccounts.FindAsync([command.FinancialAccountId], cancellationToken);
        if (account is null) return UpdateFinancialAccountResult.NotFound();

        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            var name = command.Name.Trim();
            var taken = await FinancialAccountNameUniqueness.IsTakenByActiveAccountAsync(
                _db, name, excludeId: command.FinancialAccountId, cancellationToken);
            if (taken) return UpdateFinancialAccountResult.NameConflict(name);
            account.Name = name;
        }

        if (!string.IsNullOrWhiteSpace(command.Type))
        {
            if (!Enum.TryParse<FinancialAccountType>(command.Type, ignoreCase: true, out var type))
                return UpdateFinancialAccountResult.InvalidType(command.Type);
            account.Type = type;
        }

        if (command.AccountNumber is not null)
            account.AccountNumber = string.IsNullOrWhiteSpace(command.AccountNumber) ? null : command.AccountNumber.Trim();

        if (!string.IsNullOrWhiteSpace(command.Currency))
            account.Currency = command.Currency.Trim().ToUpperInvariant();

        if (command.Notes is not null)
            account.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();

        account.UpdatedAt = _dateTimeProvider.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return UpdateFinancialAccountResult.Success(new FinancialAccountSummary(
            account.Id, account.Name, account.Type, account.AccountNumber, account.Currency,
            account.Notes, account.IsDeactivated, account.CreatedAt, account.UpdatedAt));
    }
}
