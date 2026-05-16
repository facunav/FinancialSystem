namespace FinancialSystem.Application.Imports;

public sealed record ParsedTransaction(
    DateTime Date,
    string Description,
    decimal Amount,
    string? Currency = null);
