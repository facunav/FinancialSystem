using FinancialSystem.Application.Helpers;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Imports.Parsing
{
    // ════════════════════════════════════════════════════════════════
    // PARSER DE GASTOS FIJOS
    //
    // Columnas del Excel (índices base-1):
    //   1: Fecha | 2: Mes | 3: Categoria | 4: Forma de Pago | 5: Monto
    //   6: Estado | 7: Pagado el dia | 8: Comentario
    //
    // Particularidades conocidas:
    //   - Estado puede ser null (2 filas de 462)
    //   - "Pagado el dia" puede ser null (21 de 462)
    //   - Categorías incluyen nombres de tarjetas: "Visa BBVA", "Mastercard Galicia"
    // ════════════════════════════════════════════════════════════════

    public sealed class FixedSheetParser : IManualExpenseSheetParser
    {
        private const int ColFecha = 1;
        private const int ColMes = 2;
        private const int ColCategoria = 3;
        private const int ColPago = 4;
        private const int ColMonto = 5;
        private const int ColEstado = 6;
        private const int ColPagadoDia = 7;
        private const int ColComentario = 8;
        private const int HeaderRow = 1;

        private readonly ILogger<FixedSheetParser> _logger;

        public FixedSheetParser(ILogger<FixedSheetParser> logger) => _logger = logger;

        public IReadOnlyList<string> HandledSheetNames { get; } =
            ["Gastos Fijos", "GastosFijos", "Fijos"];

        public SheetParseResult Parse(ISheetReader sheet, string sourceFile)
        {
            var expenses = new List<ManualExpense>();
            var diagnostics = new List<string>();
            var skipped = 0;

            for (var row = HeaderRow + 1; row <= sheet.RowCount; row++)
            {
                var dateRaw = sheet.GetString(row, ColFecha);
                var catRaw = sheet.GetString(row, ColCategoria);
                var amount = sheet.GetDecimal(row, ColMonto);

                if (dateRaw is null && catRaw is null && amount is null)
                {
                    skipped++;
                    continue;
                }

                // ── Fecha ────────────────────────────────────────────
                var date = sheet.GetDate(row, ColFecha);
                if (date is null)
                {
                    diagnostics.Add($"[{sheet.SheetName}] fila {row}: fecha inválida '{dateRaw}' — omitida");
                    skipped++;
                    continue;
                }

                // ── Categoría ────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(catRaw))
                {
                    diagnostics.Add($"[{sheet.SheetName}] fila {row}: categoría vacía — omitida");
                    skipped++;
                    continue;
                }

                // ── Monto ─────────────────────────────────────────────
                if (amount is null)
                {
                    diagnostics.Add($"[{sheet.SheetName}] fila {row}: monto inválido — omitida");
                    skipped++;
                    continue;
                }

                // ── Campos opcionales ─────────────────────────────────
                var mesRaw = sheet.GetString(row, ColMes);
                var paymentRaw = sheet.GetString(row, ColPago) ?? string.Empty;
                var estadoRaw = sheet.GetString(row, ColEstado);
                var paidAtDate = sheet.GetDate(row, ColPagadoDia);
                var notes = sheet.GetString(row, ColComentario);

                expenses.Add(new ManualExpense
                {
                    Date = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc),
                    Description = catRaw.Trim(),
                    PaymentMethod = CommonHelper.NormalizePaymentMethod(paymentRaw),
                    Amount = amount.Value,
                    Currency = "ARS",
                    Notes = notes,
                    MonthLabel = mesRaw?.Trim(),
                    PaymentStatus = estadoRaw?.Trim(),
                    PaidAt = paidAtDate.HasValue
                                        ? DateTime.SpecifyKind(paidAtDate.Value.Date, DateTimeKind.Utc)
                                        : null,
                    Sheet = ManualExpenseSheet.Fixed,
                    ExternalId = CommonHelper.BuildExternalId(sourceFile, sheet.SheetName, row),
                    SourceFile = sourceFile,
                    SheetName = sheet.SheetName,
                    RowNumber = row,
                    ImportedAtUtc = DateTime.UtcNow,
                });
            }

            _logger.LogInformation(
                "[{Sheet}] {Count} gastos parseados, {Skipped} filas omitidas",
                sheet.SheetName, expenses.Count, skipped);

            return new SheetParseResult(expenses, skipped, diagnostics);
        }
    }
}
