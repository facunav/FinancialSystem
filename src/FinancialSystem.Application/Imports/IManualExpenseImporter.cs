using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Imports;

/// <summary>
/// Contrato para importadores de gastos manuales.
/// El importer lee una fuente (Excel, CSV futuro, etc.) y persiste
/// ManualExpense con idempotencia garantizada.
///
/// SEPARACIÓN DE RESPONSABILIDADES:
///   IManualExpenseImporter   = orquesta parsing + persistencia
///   IManualExpenseSheetParser = parsea una hoja específica (sin persistir)
///   Ambos son independientes del FileSystemWatcher y del IFileParser general.
///
/// RAZÓN DE SEPARACIÓN vs IFileParser:
///   IFileParser produce ExtractedTransaction (modelo genérico).
///   El Excel manual tiene campos que no existen en Transaction:
///   PaymentMethod, Category, PaymentStatus, PaidAt, Notes, MonthLabel.
///   Forzar eso dentro de ExtractedTransaction sería corromper el modelo.
/// </summary>
public interface IManualExpenseImporter
{
    Task<ManualExpenseImportResult> ImportAsync(
        string filePath,
        CancellationToken ct = default);
}

/// <summary>
/// Parsea una hoja específica del Excel manual.
/// Implementaciones: DynamicSheetParser, FixedSheetParser.
/// </summary>
public interface IManualExpenseSheetParser
{
    /// <summary>Nombre(s) de hoja que este parser maneja (case-insensitive).</summary>
    IReadOnlyList<string> HandledSheetNames { get; }

    /// <summary>
    /// Parsea las filas de la hoja y devuelve gastos extraídos.
    /// Nunca lanza excepciones por datos malformados — los errores van a Diagnostics.
    /// </summary>
    SheetParseResult Parse(ISheetReader sheet, string sourceFile);
}

/// <summary>
/// Abstracción sobre la hoja Excel. Permite testear sin ClosedXML.
/// </summary>
public interface ISheetReader
{
    string SheetName { get; }
    int RowCount { get; }

    /// <summary>Lee el valor de una celda como string. Null si la celda está vacía.</summary>
    string? GetString(int row, int col);

    /// <summary>Lee el valor de una celda como DateTime. Null si no es fecha válida.</summary>
    DateTime? GetDate(int row, int col);

    /// <summary>Lee el valor de una celda como decimal. Null si no es número válido.</summary>
    decimal? GetDecimal(int row, int col);
}

// ── Resultados ────────────────────────────────────────────────────

public sealed record ManualExpenseImportResult
{
    public required string FilePath { get; init; }
    public required int Inserted { get; init; }
    public required int Skipped { get; init; }        // filas sin datos
    public required int Duplicates { get; init; }     // ExternalId ya existente en DB
    public required int ParseErrors { get; init; }    // filas que fallaron el parse
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required TimeSpan Elapsed { get; init; }

    public bool HasErrors => ParseErrors > 0;

    public override string ToString() =>
        $"{Path.GetFileName(FilePath)}: inserted={Inserted} dup={Duplicates} " +
        $"errors={ParseErrors} skipped={Skipped} ({Elapsed.TotalMilliseconds:F0}ms)";
}

public sealed record SheetParseResult(
    IReadOnlyList<ManualExpense> Expenses,
    int SkippedRows,
    IReadOnlyList<string> Diagnostics);
