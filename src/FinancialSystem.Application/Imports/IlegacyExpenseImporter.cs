using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Imports;

/// <summary>
/// Contrato para importar registros legacy desde Excel histórico.
/// Solo para migración/compatibilidad. No parte del flujo principal futuro.
/// </summary>
public interface ILegacyExpenseImporter
{
    Task<LegacyExpenseImportResult> ImportAsync(string filePath, CancellationToken ct = default);
}

/// <summary>Parsea una hoja específica del Excel legacy.</summary>
public interface ILegacyExpenseSheetParser
{
    IReadOnlyList<string> HandledSheetNames { get; }
    LegacySheetParseResult Parse(ISheetReader sheet, string sourceFile);
}

public interface ISheetReader
{
    string SheetName { get; }
    int RowCount { get; }
    string? GetString(int row, int col);
    DateTime? GetDate(int row, int col);
    decimal? GetDecimal(int row, int col);
}

// ── Resultados ────────────────────────────────────────────────────────────────

public sealed record LegacyExpenseImportResult
{
    public required string FilePath { get; init; }
    public required int Inserted { get; init; }
    public required int Skipped { get; init; }
    public required int Duplicates { get; init; }
    public required int ParseErrors { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public bool HasErrors => ParseErrors > 0;
    public override string ToString() =>
        $"{Path.GetFileName(FilePath)}: inserted={Inserted} dup={Duplicates} " +
        $"errors={ParseErrors} skipped={Skipped} ({Elapsed.TotalMilliseconds:F0}ms)";
}

public sealed record LegacySheetParseResult(
    IReadOnlyList<LegacyImportedExpense> Expenses,
    int SkippedRows,
    IReadOnlyList<string> Diagnostics);