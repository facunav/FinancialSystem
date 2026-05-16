using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Insights;

public sealed record TransactionSummary(
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency)
{
    public static TransactionSummary FromTransaction(Transaction transaction) =>
        new(transaction.Date, transaction.Description, transaction.Amount, transaction.Currency);
}
