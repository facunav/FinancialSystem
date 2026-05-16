using System.Diagnostics;
using System.Text;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Imports.Parsing;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

internal sealed class CsvFileParser(ILogger<CsvFileParser> logger) : IFileParser
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".csv"];

    public async Task<FileParseResult> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var transactions = new List<ExtractedTransaction>();
        var diagnostics = new List<string>();
        var skipped = 0;
        var lineNumber = 0;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? headerLine;
        while ((headerLine = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            lineNumber++;
            if (!string.IsNullOrWhiteSpace(headerLine))
            {
                break;
            }
        }

        if (headerLine is null)
        {
            logger.LogWarning("CSV {FilePath} is empty", filePath);
            sw.Stop();
            return new FileParseResult(transactions, skipped, diagnostics, sw.Elapsed);
        }

        var delimiter = ImportValueParser.DetectDelimiter(headerLine);
        var headers = ImportValueParser.ParseCsvLine(headerLine, delimiter);
        var columnMap = ImportValueParser.MapColumns(headers);
        if (!columnMap.TryGetValue("date", out var dateIndex)
            || !columnMap.TryGetValue("description", out var descriptionIndex)
            || !columnMap.TryGetValue("amount", out var amountIndex))
        {
            throw new InvalidOperationException(
                $"CSV {filePath} is missing required columns (fecha/date, descripción/description, monto/amount).");
        }

        columnMap.TryGetValue("currency", out var currencyIndex);
        logger.LogDebug("CSV {FilePath} delimiter={Delimiter} columns mapped: {Columns}", filePath, delimiter, string.Join(", ", columnMap.Keys));

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ImportValueParser.ParseCsvLine(line, delimiter);
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            try
            {
                if (dateIndex >= fields.Count || descriptionIndex >= fields.Count || amountIndex >= fields.Count)
                {
                    skipped++;
                    var msg = $"Line {lineNumber}: not enough columns";
                    diagnostics.Add(msg);
                    logger.LogWarning("CSV {FilePath} {Message}", filePath, msg);
                    continue;
                }

                var dateRaw = fields[dateIndex].Trim();
                var descriptionRaw = fields[descriptionIndex].Trim();
                var amountRaw = fields[amountIndex].Trim();

                if (string.IsNullOrEmpty(dateRaw) && string.IsNullOrEmpty(descriptionRaw) && string.IsNullOrEmpty(amountRaw))
                {
                    continue;
                }

                if (!ImportValueParser.TryParseDate(dateRaw, out var date))
                {
                    skipped++;
                    var msg = $"Line {lineNumber}: invalid date '{dateRaw}'";
                    diagnostics.Add(msg);
                    logger.LogWarning("CSV {FilePath} {Message}", filePath, msg);
                    continue;
                }

                if (!ImportValueParser.TryParseAmount(amountRaw, out var amount))
                {
                    skipped++;
                    var msg = $"Line {lineNumber}: invalid amount '{amountRaw}'";
                    diagnostics.Add(msg);
                    logger.LogWarning("CSV {FilePath} {Message}", filePath, msg);
                    continue;
                }

                string? currency = null;
                if (currencyIndex >= 0 && currencyIndex < fields.Count)
                {
                    var currencyRaw = fields[currencyIndex].Trim();
                    if (!string.IsNullOrEmpty(currencyRaw))
                    {
                        currency = currencyRaw.Length <= 3
                            ? currencyRaw.ToUpperInvariant()
                            : currencyRaw;
                    }
                }

                transactions.Add(new ExtractedTransaction(
                    date,
                    descriptionRaw,
                    amount,
                    currency,
                    $"line:{lineNumber}"));
            }
            catch (Exception ex)
            {
                skipped++;
                diagnostics.Add($"Line {lineNumber}: {ex.Message}");
                logger.LogWarning(ex, "CSV {FilePath} line {LineNumber} skipped", filePath, lineNumber);
            }
        }

        sw.Stop();
        logger.LogInformation(
            "CSV parser extracted {Count} transactions from {FilePath} in {ElapsedMs}ms ({Skipped} skipped)",
            transactions.Count,
            filePath,
            sw.ElapsedMilliseconds,
            skipped);

        return new FileParseResult(transactions, skipped, diagnostics, sw.Elapsed);
    }
}
