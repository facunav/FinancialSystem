namespace FinancialSystem.Application.BankStatements.Commands;

/// <summary>FinancialAccountId null desasigna la cuenta -- misma semántica que el endpoint original.</summary>
public sealed record AssignBankStatementFinancialAccountCommand(Guid BankStatementId, Guid? FinancialAccountId);
