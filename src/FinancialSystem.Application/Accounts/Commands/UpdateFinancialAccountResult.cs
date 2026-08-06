namespace FinancialSystem.Application.Accounts.Commands;

public enum UpdateFinancialAccountFailureReason
{
    NotFound,

    /// <summary>Ya existe otra cuenta ACTIVA con ese Name (case-insensitive).</summary>
    NameConflict,

    /// <summary>Type informado no es un valor válido de FinancialAccountType.</summary>
    InvalidType,
}

/// <summary>
/// Resultado de <see cref="UpdateFinancialAccountCommand"/>. ConflictingName solo se
/// completa en NameConflict; InvalidTypeValue solo se completa en InvalidType (el
/// string original, tal cual vino en el request, para el mensaje de error).
/// </summary>
public sealed record UpdateFinancialAccountResult(
    bool IsSuccess,
    FinancialAccountSummary? Account,
    UpdateFinancialAccountFailureReason? FailureReason,
    string? ConflictingName,
    string? InvalidTypeValue)
{
    public static UpdateFinancialAccountResult NotFound() =>
        new(false, null, UpdateFinancialAccountFailureReason.NotFound, null, null);

    public static UpdateFinancialAccountResult NameConflict(string name) =>
        new(false, null, UpdateFinancialAccountFailureReason.NameConflict, name, null);

    public static UpdateFinancialAccountResult InvalidType(string typeValue) =>
        new(false, null, UpdateFinancialAccountFailureReason.InvalidType, null, typeValue);

    public static UpdateFinancialAccountResult Success(FinancialAccountSummary account) =>
        new(true, account, null, null, null);
}
