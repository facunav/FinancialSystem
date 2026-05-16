namespace FinancialSystem.Application.Imports;

public sealed record FileParseResult(
    IReadOnlyList<ExtractedTransaction> Transactions,
    int SkippedRows,
    IReadOnlyList<string> Diagnostics,
    TimeSpan Elapsed);
