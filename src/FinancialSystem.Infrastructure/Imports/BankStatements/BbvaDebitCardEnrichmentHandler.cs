using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Imports.BankStatements
{
    /// <summary>
    /// Handler del router de importación para el XLSX "Últimos Movimientos" de Tarjeta de
    /// Débito BBVA.
    ///
    /// La Tarjeta de Débito nunca crea movimientos — solo enriquece BankStatement
    /// existentes (Merchant/MerchantAtUtc) cuando hay un único candidato por importe y
    /// fecha. Ante ambigüedad o ausencia de match, la operación se descarta sin persistir
    /// nada (ver docs/patch/enriquecimiento-tarjeta-debito.md).
    ///
    /// MATCH: candidato = BankStatement sin enriquecer todavía, con "VISA DEBITO" en el
    /// concepto, mismo importe (en valor absoluto) y fecha entre 0 y 3 días posteriores a
    /// la fecha de la compra (ventana validada contra extractos reales — ver documento).
    ///
    /// DETECCIÓN: acepta .xlsx cuyo nombre matchea patrones configurados.
    ///   Default: ["*ltimos_movimientos*.xlsx", "*Movimientos*Debito*.xlsx"]
    /// </summary>
    internal sealed class BbvaDebitCardEnrichmentHandler : IFileImportHandler
    {
        private const int MatchWindowDays = 3;

        private readonly XlsBankStatementReader _reader;
        private readonly BbvaDebitCardParser _parser;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly FileIngestionOptions _options;
        private readonly ILogger<BbvaDebitCardEnrichmentHandler> _logger;

        public BbvaDebitCardEnrichmentHandler(
            XlsBankStatementReader reader,
            BbvaDebitCardParser parser,
            IServiceScopeFactory scopeFactory,
            IOptions<FileIngestionOptions> options,
            ILogger<BbvaDebitCardEnrichmentHandler> logger)
        {
            _reader = reader;
            _parser = parser;
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        public string HandlerName => "BbvaDebitCardEnrichment";

        public bool CanHandle(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                return false;

            var fileName = Path.GetFileName(filePath);
            return _options.BbvaDebitCardFilePatterns
                .Any(pattern => MatchesGlob(fileName, pattern));
        }

        public async Task<ImportRunResult> HandleAsync(string filePath, CancellationToken ct = default)
        {
            string?[][] rawRows;
            try
            {
                (rawRows, _) = _reader.ReadFirstSheet(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BbvaDebitCardEnrichment] Error leyendo XLSX: {File}", filePath);
                return ImportRunResult.Failure($"No se pudo abrir el archivo: {ex.Message}");
            }

            var parseResult = _parser.Parse(rawRows, filePath);

            if (parseResult.Operations.Count == 0)
            {
                _logger.LogWarning(
                    "[BbvaDebitCardEnrichment] {File}: sin operaciones parseadas",
                    Path.GetFileName(filePath));

                return new ImportRunResult(
                    Inserted: 0,
                    Duplicates: 0,
                    Failed: parseResult.Diagnostics.Count,
                    Skipped: parseResult.SkippedRows,
                    Diagnostics: parseResult.Diagnostics);
            }

            var (enriched, ambiguous, noMatch) = await EnrichAsync(parseResult.Operations, ct);

            _logger.LogInformation(
                "[BbvaDebitCardEnrichment] {File}: {Enriched} enriquecidos, {Ambiguous} ambiguos, {NoMatch} sin match",
                Path.GetFileName(filePath), enriched, ambiguous, noMatch);

            return new ImportRunResult(
                Inserted: 0,
                Duplicates: 0,
                Failed: parseResult.Diagnostics.Count,
                Skipped: parseResult.SkippedRows + ambiguous + noMatch,
                Diagnostics: parseResult.Diagnostics);
        }

        private async Task<(int Enriched, int Ambiguous, int NoMatch)> EnrichAsync(
            IReadOnlyList<BbvaDebitCardParser.DebitCardOperation> operations,
            CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var minDate = DateTime.SpecifyKind(operations.Min(o => o.PurchaseAt.Date), DateTimeKind.Utc);
            var maxDate = DateTime.SpecifyKind(operations.Max(o => o.PurchaseAt.Date), DateTimeKind.Utc)
                .AddDays(MatchWindowDays);

            // Candidatos: compras con débito de Caja de Ahorro sin enriquecer todavía,
            // dentro del rango de fechas del archivo. Se cargan una sola vez y se
            // matchea en memoria — volumen mensual típico, no requiere N consultas.
            var candidates = await db.BankStatements
                .Where(b => b.Merchant == null)
                .Where(b => b.Concept.Contains("VISA DEBITO"))
                .Where(b => b.Date >= minDate && b.Date <= maxDate)
                .ToListAsync(ct);

            var enriched = 0;
            var ambiguous = 0;
            var noMatch = 0;

            foreach (var op in operations)
            {
                var matches = candidates
                    .Where(b => b.Amount == -op.Amount)
                    .Where(b => b.Date.Date >= op.PurchaseAt.Date
                             && b.Date.Date <= op.PurchaseAt.Date.AddDays(MatchWindowDays))
                    .ToList();

                if (matches.Count == 1)
                {
                    var bankStatement = matches[0];
                    bankStatement.Merchant = op.Merchant;
                    bankStatement.MerchantAtUtc = op.PurchaseAt.UtcDateTime;
                    candidates.Remove(bankStatement); // ya usado: no vuelve a ofrecerse como candidato
                    enriched++;
                }
                else if (matches.Count == 0)
                {
                    noMatch++;
                }
                else
                {
                    ambiguous++;
                }
            }

            if (enriched > 0)
                await db.SaveChangesAsync(ct);

            return (enriched, ambiguous, noMatch);
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
