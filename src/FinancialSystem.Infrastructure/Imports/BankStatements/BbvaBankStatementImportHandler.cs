using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Imports.BankStatements
{
    /// <summary>
    /// Handler del router de importación para XLS BBVA Caja de Ahorros.
    ///
    /// DETECCIÓN: acepta .xls cuyo nombre matchea patrones configurados.
    ///   Default: ["Caja*.xls", "*ahorros*.xls", "*corriente*.xls"]
    ///
    /// El handler es específico del banco — en el futuro un "GaliciaBankStatementImportHandler"
    /// usaría el mismo patrón pero con su propio importer y patrones de nombre.
    /// </summary>
    internal sealed class BbvaBankStatementImportHandler : IFileImportHandler
    {
        private readonly BbvaBankStatementImporter _importer;
        private readonly FileIngestionOptions _options;
        private readonly ILogger<BbvaBankStatementImportHandler> _logger;

        public BbvaBankStatementImportHandler(
            BbvaBankStatementImporter importer,
            IOptions<FileIngestionOptions> options,
            ILogger<BbvaBankStatementImportHandler> logger)
        {
            _importer = importer;
            _options = options.Value;
            _logger = logger;
        }

        public string HandlerName => "BbvaBankStatement";

        public bool CanHandle(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (!ext.Equals(".xls", StringComparison.OrdinalIgnoreCase))
                return false;

            var fileName = Path.GetFileName(filePath);
            return _options.BbvaBankStatementFilePatterns
                .Any(pattern => MatchesGlob(fileName, pattern));
        }

        public async Task<ImportRunResult> HandleAsync(string filePath, Guid importBatchId, CancellationToken ct = default)
        {
            BbvaBankStatementImporter.ImportResult result;
            try
            {
                result = await _importer.ImportAsync(filePath, importBatchId, ct);
            }
            catch (Exception ex)
            {
                // Patch 0055 (Epic P, unificación de contrato): mismo criterio que
                // BbvaDebitCardEnrichmentHandler e ImportFileProcessingSink ante una
                // falla al abrir/leer el archivo -- se reporta como Failure explícito
                // (Outcome=Failed), no como una corrida "exitosa con advertencias".
                _logger.LogError(ex, "[BbvaBankStatement] Error leyendo el archivo: {File}", filePath);
                return ImportRunResult.Failure($"No se pudo abrir el archivo: {ex.Message}")
                    with { ParserUsed = nameof(BbvaBankStatementParser) };
            }

            if (result.HasErrors)
            {
                _logger.LogWarning(
                    "[BbvaBankStatement] {File}: {Errors} errores de parseo",
                    Path.GetFileName(filePath), result.ParseErrors);

                foreach (var diag in result.Diagnostics.Take(10))
                    _logger.LogDebug("[BbvaBankStatement] {Diag}", diag);
            }

            _logger.LogInformation("[BbvaBankStatement] {Result}", result.ToString());

            // ParserUsed (Patch 0054): este handler solo tiene un parser posible -- a
            // diferencia del catch-all "Transaction", no hay ambigüedad que resolver, pero
            // se registra igual para que "cada importación" tenga el dato.
            return new ImportRunResult(
                result.Inserted,
                result.Duplicates,
                result.ParseErrors,
                result.SkippedRows,
                result.Diagnostics,
                ParserUsed: nameof(BbvaBankStatementParser));
        }

        private static bool MatchesGlob(string fileName, string pattern)
        {
            var regex = "^" +
                System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace(@"\*", ".*") +
                "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                fileName, regex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
