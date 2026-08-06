using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Accounts.Commands;

/// <summary>
/// Migrado desde FinancialAccountEndpoints.Create (PATCH-048) -- misma lógica exacta:
/// unicidad de Name solo contra cuentas activas, Currency por defecto "ARS" cuando no
/// se informa, AccountNumber/Notes en blanco se guardan como null.
/// </summary>
public sealed class CreateFinancialAccountHandler
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateFinancialAccountHandler(IApplicationDbContext db, IDateTimeProvider dateTimeProvider)
    {
        _db = db;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<CreateFinancialAccountResult> Handle(
        CreateFinancialAccountCommand command, CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();

        var taken = await FinancialAccountNameUniqueness.IsTakenByActiveAccountAsync(
            _db, name, excludeId: null, cancellationToken);
        if (taken) return CreateFinancialAccountResult.NameConflict(name);

        var now = _dateTimeProvider.UtcNow;

        var account = new FinancialAccount
        {
            Name = name,
            Type = command.Type,
            AccountNumber = string.IsNullOrWhiteSpace(command.AccountNumber) ? null : command.AccountNumber.Trim(),
            Currency = string.IsNullOrWhiteSpace(command.Currency) ? "ARS" : command.Currency.Trim().ToUpperInvariant(),
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
            IsDeactivated = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.FinancialAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);

        return CreateFinancialAccountResult.Success(new FinancialAccountSummary(
            account.Id, account.Name, account.Type, account.AccountNumber, account.Currency,
            account.Notes, account.IsDeactivated, account.CreatedAt, account.UpdatedAt));
    }
}
