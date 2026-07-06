using FinancialSystem.Application.Helpers;
using FinancialSystem.Application.Imports;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Legacy;

// ── PARSER DE GASTOS DINÁMICOS ────────────────────────────────────────────────
// Columnas (base-1): 1=Fecha | 2=Descripcion | 3=FormaPago | 4=Monto | 5=Comentario

public sealed class LegacyDynamicSheetParser : ILegacyExpenseSheetParser
{
    private const int ColFecha = 1;
    private const int ColDescripcion = 2;
    private const int ColPago = 3;
    private const int ColMonto = 4;
    private const int ColComentario = 5;
    private const int HeaderRow = 1;

    private readonly ILogger<LegacyDynamicSheetParser> _logger;
    public LegacyDynamicSheetParser(ILogger<LegacyDynamicSheetParser> logger) => _logger = logger;

    public IReadOnlyList<string> HandledSheetNames { get; } =
        ["Gastos Dinamicos", "Gastos Dinámicos", "GastosDinamicos", "Dinamicos"];

    public LegacySheetParseResult Parse(ISheetReader sheet, string sourceFile)
    {
        var expenses = new List<LegacyImportedExpense>();
        var diagnostics = new List<string>();
        var skipped = 0;

        for (var row = HeaderRow + 1; row <= sheet.RowCount; row++)
        {
            var dateRaw = sheet.GetString(row, ColFecha);
            var descRaw = sheet.GetString(row, ColDescripcion);
            var amount = sheet.GetDecimal(row, ColMonto);

            if (dateRaw is null && descRaw is null && amount is null)
            { skipped++; continue; }

            var date = sheet.GetDate(row, ColFecha);
            if (date is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: fecha inválida '{dateRaw}' → omitida");
                skipped++; continue;
            }
            if (string.IsNullOrWhiteSpace(descRaw))
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: descripción vacía → omitida");
                skipped++; continue;
            }
            if (amount is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: monto inválido → omitida");
                skipped++; continue;
            }

            var paymentRaw = sheet.GetString(row, ColPago) ?? string.Empty;
            var notes = sheet.GetString(row, ColComentario);

            expenses.Add(new LegacyImportedExpense
            {
                Date = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc),
                Description = descRaw.Trim(),
                PaymentMethod = CommonHelper.NormalizePaymentMethod(paymentRaw),
                Amount = amount.Value,
                Currency = "ARS",
                Notes = notes,
                Sheet = LegacyImportSheet.Dynamic,
                ExternalId = CommonHelper.BuildExternalId(sourceFile, sheet.SheetName, row),
                SourceFile = sourceFile,
                SheetName = sheet.SheetName,
                RowNumber = row,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }

        _logger.LogInformation("[{Sheet}] {Count} registros, {Skipped} omitidos",
            sheet.SheetName, expenses.Count, skipped);

        return new LegacySheetParseResult(expenses, skipped, diagnostics);
    }
}

// ── PARSER DE GASTOS FIJOS ────────────────────────────────────────────────────
// Columnas (base-1): 1=Fecha | 2=Mes | 3=Descripcion | 4=FormaPago | 5=Monto
//                    6=Estado | 7=PagadoDia | 8=Comentario

public sealed class LegacyFixedSheetParser : ILegacyExpenseSheetParser
{
    private const int ColFecha = 1;
    private const int ColMes = 2;
    private const int ColDescripcion = 3;
    private const int ColPago = 4;
    private const int ColMonto = 5;
    private const int ColEstado = 6;
    private const int ColPagadoDia = 7;
    private const int ColComentario = 8;
    private const int HeaderRow = 1;

    private readonly ILogger<LegacyFixedSheetParser> _logger;
    public LegacyFixedSheetParser(ILogger<LegacyFixedSheetParser> logger) => _logger = logger;

    public IReadOnlyList<string> HandledSheetNames { get; } =
        ["Gastos Fijos", "GastosFijos", "Fijos"];

    public LegacySheetParseResult Parse(ISheetReader sheet, string sourceFile)
    {
        var expenses = new List<LegacyImportedExpense>();
        var diagnostics = new List<string>();
        var skipped = 0;

        for (var row = HeaderRow + 1; row <= sheet.RowCount; row++)
        {
            var dateRaw = sheet.GetString(row, ColFecha);
            var descRaw = sheet.GetString(row, ColDescripcion);
            var amount = sheet.GetDecimal(row, ColMonto);

            if (dateRaw is null && descRaw is null && amount is null)
            { skipped++; continue; }

            var date = sheet.GetDate(row, ColFecha);
            if (date is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: fecha inválida '{dateRaw}' → omitida");
                skipped++; continue;
            }
            if (string.IsNullOrWhiteSpace(descRaw))
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: descripción vacía → omitida");
                skipped++; continue;
            }
            if (amount is null)
            {
                diagnostics.Add($"[{sheet.SheetName}] fila {row}: monto inválido → omitida");
                skipped++; continue;
            }

            expenses.Add(new LegacyImportedExpense
            {
                Date = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc),
                Description = descRaw.Trim(),
                PaymentMethod = CommonHelper.NormalizePaymentMethod(sheet.GetString(row, ColPago) ?? ""),
                Amount = amount.Value,
                Currency = "ARS",
                Notes = sheet.GetString(row, ColComentario),
                MonthLabel = sheet.GetString(row, ColMes)?.Trim(),
                PaymentStatus = sheet.GetString(row, ColEstado)?.Trim(),
                PaidAt = sheet.GetDate(row, ColPagadoDia) is { } paid
                                    ? DateTime.SpecifyKind(paid.Date, DateTimeKind.Utc)
                                    : null,
                Sheet = LegacyImportSheet.Fixed,
                ExternalId = CommonHelper.BuildExternalId(sourceFile, sheet.SheetName, row),
                SourceFile = sourceFile,
                SheetName = sheet.SheetName,
                RowNumber = row,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }

        _logger.LogInformation("[{Sheet}] {Count} registros, {Skipped} omitidos",
            sheet.SheetName, expenses.Count, skipped);

        return new LegacySheetParseResult(expenses, skipped, diagnostics);
    }
}