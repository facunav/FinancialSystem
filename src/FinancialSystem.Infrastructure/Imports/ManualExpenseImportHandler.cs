using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Handler para archivos Excel de gastos manuales personales.
///
/// DETECCIÓN:
///   Acepta archivos .xlsx cuyo nombre matchea al menos uno de los
///   patrones configurados en FileIngestionOptions.ManualExpenseFilePatterns.
///   La comparación es case-insensitive y usa glob simple (* = cualquier secuencia).
///
///   Ejemplos con pattern "Cuentas*.xlsx":
///     "Cuentas nuevo.xlsx"     → acepta ✓
///     "Cuentas_2026.xlsx"      → acepta ✓
///     "MovimientosBBVA.xlsx"   → rechaza ✗ (va al TransactionImportHandler)
///
/// EJECUCIÓN:
///   Delega completamente a IManualExpenseImporter, que maneja
///   parsing multi-hoja, idempotencia y persistencia en ManualExpenses.
/// </summary>
internal sealed class ManualExpenseImportHandler : IFileImportHandler
{
    private readonly IManualExpenseImporter _importer;
    private readonly FileIngestionOptions _options;
    private readonly ILogger<ManualExpenseImportHandler> _logger;

    public ManualExpenseImportHandler(
        IManualExpenseImporter importer,
        IOptions<FileIngestionOptions> options,
        ILogger<ManualExpenseImportHandler> logger)
    {
        _importer = importer;
        _options = options.Value;
        _logger = logger;
    }

    public string HandlerName => "ManualExpense";

    public bool CanHandle(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileName(filePath);
        return _options.ManualExpenseFilePatterns
            .Any(pattern => MatchesGlob(fileName, pattern));
    }

    public async Task HandleAsync(string filePath, CancellationToken ct = default)
    {
        var result = await _importer.ImportAsync(filePath, ct);

        if (result.HasErrors)
        {
            _logger.LogWarning(
                "[ManualExpense] {File}: {Errors} errores de parseo",
                Path.GetFileName(filePath),
                result.ParseErrors);

            foreach (var diag in result.Diagnostics.Take(10))
                _logger.LogDebug("[ManualExpense] {File}: {Diag}",
                    Path.GetFileName(filePath), diag);
        }

        _logger.LogInformation(
            "[ManualExpense] {Result}",
            result.ToString());
    }

    /// <summary>
    /// Glob simple: solo soporta '*' como wildcard de múltiples caracteres.
    /// Suficiente para patrones de nombre de archivo. Case-insensitive.
    /// </summary>
    private static bool MatchesGlob(string fileName, string pattern)
    {
        // Convertir glob a regex equivalente
        // "Cuentas*.xlsx" → "^Cuentas.*\.xlsx$"
        var regexPattern = "^" +
            System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace(@"\*", ".*") +
            "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
