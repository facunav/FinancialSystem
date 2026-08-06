namespace FinancialSystem.Application.Transactions.Commands;

public enum AssignTransactionFinancialAccountFailureReason
{
    NotFound,

    /// <summary>El FinancialAccountId informado no corresponde a ninguna cuenta existente.</summary>
    InvalidFinancialAccountId,
}

/// <summary>
/// InvalidFinancialAccountId solo se completa en el caso de fallo homónimo -- es el Id
/// que el endpoint necesita para el mensaje de error, sin recalcularlo. TransactionId/
/// AssignedFinancialAccountId solo se completan en éxito.
/// </summary>
public sealed record AssignTransactionFinancialAccountResult(
    bool IsSuccess,
    AssignTransactionFinancialAccountFailureReason? FailureReason,
    Guid? InvalidFinancialAccountId,
    Guid? TransactionId,
    Guid? AssignedFinancialAccountId)
{
    public static AssignTransactionFinancialAccountResult NotFound() =>
        new(false, AssignTransactionFinancialAccountFailureReason.NotFound, null, null, null);

    public static AssignTransactionFinancialAccountResult InvalidAccount(Guid accountId) =>
        new(false, AssignTransactionFinancialAccountFailureReason.InvalidFinancialAccountId, accountId, null, null);

    public static AssignTransactionFinancialAccountResult Success(Guid transactionId, Guid? financialAccountId) =>
        new(true, null, null, transactionId, financialAccountId);
}
