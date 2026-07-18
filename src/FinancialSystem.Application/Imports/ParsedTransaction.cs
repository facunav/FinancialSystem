namespace FinancialSystem.Application.Imports;

public sealed record ParsedTransaction(
    DateTime Date,
    string Description,
    decimal Amount,
    string? Currency = null,
    string? CouponNumber = null,
    string? RawLine = null,
    string? sourceFile = null,
    string? AccountNumber = null
);
