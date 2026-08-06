namespace FinancialSystem.Application.Accounts.Commands;

public enum CreateFinancialAccountFailureReason
{
    /// <summary>Ya existe una cuenta ACTIVA con ese Name (case-insensitive) -- cuentas desactivadas no cuentan.</summary>
    NameAlreadyExists,
}

/// <summary>
/// Resultado de <see cref="CreateFinancialAccountCommand"/>: éxito con la cuenta creada
/// (reutiliza <see cref="FinancialAccountSummary"/>, el mismo modelo de solo lectura ya
/// usado por IFinancialAccountQueryService), o motivo de fallo. ConflictingName solo se
/// completa en el caso de conflicto -- es el Name ya recortado (Trim) que el endpoint
/// necesita para el mensaje de error, sin recalcularlo.
/// </summary>
public sealed record CreateFinancialAccountResult(
    bool IsSuccess,
    FinancialAccountSummary? Account,
    CreateFinancialAccountFailureReason? FailureReason,
    string? ConflictingName)
{
    public static CreateFinancialAccountResult Success(FinancialAccountSummary account) => new(true, account, null, null);

    public static CreateFinancialAccountResult NameConflict(string name) =>
        new(false, null, CreateFinancialAccountFailureReason.NameAlreadyExists, name);
}
