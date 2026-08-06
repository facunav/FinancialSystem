namespace FinancialSystem.Application.BankStatements.Commands;

public enum AssignBankStatementFinancialAccountFailureReason
{
    NotFound,

    /// <summary>El FinancialAccountId informado no corresponde a ninguna cuenta existente.</summary>
    InvalidFinancialAccountId,
}

/// <summary>
/// InvalidFinancialAccountId solo se completa en el caso de fallo homónimo -- es el Id
/// que el endpoint necesita para el mensaje de error, sin recalcularlo.
/// BankStatementId/AssignedFinancialAccountId solo se completan en éxito.
/// </summary>
public sealed record AssignBankStatementFinancialAccountResult(
    bool IsSuccess,
    AssignBankStatementFinancialAccountFailureReason? FailureReason,
    Guid? InvalidFinancialAccountId,
    Guid? BankStatementId,
    Guid? AssignedFinancialAccountId)
{
    public static AssignBankStatementFinancialAccountResult NotFound() =>
        new(false, AssignBankStatementFinancialAccountFailureReason.NotFound, null, null, null);

    public static AssignBankStatementFinancialAccountResult InvalidAccount(Guid accountId) =>
        new(false, AssignBankStatementFinancialAccountFailureReason.InvalidFinancialAccountId, accountId, null, null);

    public static AssignBankStatementFinancialAccountResult Success(Guid bankStatementId, Guid? financialAccountId) =>
        new(true, null, null, bankStatementId, financialAccountId);
}
