namespace FinancialSystem.Application.Imports;

/// <summary>Raw transaction produced by a format-specific parser before normalization.</summary>
public sealed record ExtractedTransaction(
    DateTime Date,
    string RawDescription,
    decimal Amount,
    string? CurrencyHint = null,
    string? CouponNumber = null,
    string? RawLine = null,
    string? SourceLocation = null,
    string? AccountNumber = null);
