using System.Diagnostics;
using ClosedXML.Excel;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

internal sealed class ExcelWorkbookParser(ILogger<ExcelWorkbookParser> logger) : IFileParser
{
    private const int MaxHeaderScanRows = 25;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".xlsx"];

    public Task<FileParseResult> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var transactions = new List<ExtractedTransaction>();
        var diagnostics = new List<string>();
        var skipped = 0;

        using var workbook = new XLWorkbook(filePath);

        foreach (var worksheet in workbook.Worksheets)
        {
            if (worksheet.Visibility != XLWorksheetVisibility.Visible)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                logger.LogDebug("Excel sheet {Sheet} in {FilePath} is empty", worksheet.Name, filePath);
                continue;
            }

            if (!TryFindHeaderRow(usedRange, out var headerRow, out var columnMap))
            {
                diagnostics.Add($"Sheet '{worksheet.Name}': no date/description/amount columns found");
                logger.LogWarning(
                    "Excel {FilePath} sheet {Sheet}: could not detect transaction columns",
                    filePath,
                    worksheet.Name);
                continue;
            }

            logger.LogDebug(
                "Excel {FilePath} sheet {Sheet}: header row {HeaderRow}, columns {Columns}",
                filePath,
                worksheet.Name,
                headerRow,
                string.Join(", ", columnMap.Keys));

            columnMap.TryGetValue("date", out var dateCol);
            columnMap.TryGetValue("description", out var descCol);
            columnMap.TryGetValue("amount", out var amountCol);
            columnMap.TryGetValue("currency", out var currencyCol);

            var lastRow = usedRange.LastRow().RowNumber();
            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dateCell = worksheet.Cell(row, dateCol).GetFormattedString().Trim();
                var descCell = worksheet.Cell(row, descCol).GetFormattedString().Trim();
                var amountCell = worksheet.Cell(row, amountCol).GetFormattedString().Trim();

                if (string.IsNullOrWhiteSpace(dateCell)
                    && string.IsNullOrWhiteSpace(descCell)
                    && string.IsNullOrWhiteSpace(amountCell))
                {
                    continue;
                }

                try
                {
                    if (!ImportValueParser.TryParseDate(dateCell, out var date))
                    {
                        skipped++;
                        diagnostics.Add($"{worksheet.Name} row {row}: invalid date '{dateCell}'");
                        continue;
                    }

                    if (!TryParseExcelAmount(worksheet.Cell(row, amountCol), amountCell, out var amount))
                    {
                        skipped++;
                        diagnostics.Add($"{worksheet.Name} row {row}: invalid amount '{amountCell}'");
                        continue;
                    }

                    string? currency = null;
                    if (currencyCol > 0)
                    {
                        var currencyRaw = worksheet.Cell(row, currencyCol).GetFormattedString().Trim();
                        if (!string.IsNullOrEmpty(currencyRaw))
                        {
                            currency = currencyRaw.Length <= 3
                                ? currencyRaw.ToUpperInvariant()
                                : currencyRaw;
                        }
                    }

                    transactions.Add(new ExtractedTransaction(
                        date,
                        descCell,
                        amount,
                        currency,
                        $"{worksheet.Name}:row:{row}"));
                }
                catch (Exception ex)
                {
                    skipped++;
                    diagnostics.Add($"{worksheet.Name} row {row}: {ex.Message}");
                    logger.LogWarning(ex, "Excel {FilePath} sheet {Sheet} row {Row} skipped", filePath, worksheet.Name, row);
                }
            }
        }

        sw.Stop();
        logger.LogInformation(
            "Excel parser extracted {Count} transactions from {FilePath} in {ElapsedMs}ms ({Skipped} skipped)",
            transactions.Count,
            filePath,
            sw.ElapsedMilliseconds,
            skipped);

        return Task.FromResult(new FileParseResult(transactions, skipped, diagnostics, sw.Elapsed));
    }

    private static bool TryFindHeaderRow(
        IXLRange usedRange,
        out int headerRow,
        out Dictionary<string, int> columnMap)
    {
        headerRow = 0;
        columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var firstRow = usedRange.FirstRow().RowNumber();
        var lastScanRow = Math.Min(usedRange.LastRow().RowNumber(), firstRow + MaxHeaderScanRows - 1);
        var lastCol = usedRange.LastColumn().ColumnNumber();

        for (var row = firstRow; row <= lastScanRow; row++)
        {
            var headers = new List<string>();
            for (var col = 1; col <= lastCol; col++)
            {
                headers.Add(usedRange.Worksheet.Cell(row, col).GetFormattedString());
            }

            var map = ImportValueParser.MapColumns(headers);
            if (map.ContainsKey("date") && map.ContainsKey("description") && map.ContainsKey("amount"))
            {
                headerRow = row;
                columnMap = map;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseExcelAmount(IXLCell cell, string formatted, out decimal amount)
    {
        if (cell.TryGetValue(out decimal decimalValue))
        {
            amount = decimalValue;
            return true;
        }

        if (cell.TryGetValue(out double doubleValue))
        {
            amount = (decimal)doubleValue;
            return true;
        }

        return ImportValueParser.TryParseAmount(formatted, out amount);
    }
}
