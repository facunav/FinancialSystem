namespace FinancialSystem.Application.Transactions.Commands;

/// <summary>FinancialAccountId null desasigna la cuenta -- misma semántica que el endpoint original.</summary>
public sealed record AssignTransactionFinancialAccountCommand(Guid TransactionId, Guid? FinancialAccountId);
