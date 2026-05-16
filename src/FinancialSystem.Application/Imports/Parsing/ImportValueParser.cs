using System.Globalization;
using System.Text;

namespace FinancialSystem.Application.Imports.Parsing;

/// <summary>Shared date, amount, and header parsing for CSV, Excel, and PDF line heuristics.</summary>
public static class ImportValueParser
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    public static readonly IReadOnlyDictionary<string, string> ColumnAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fecha"] = "date",
            ["date"] = "date",
            ["fec"] = "date",
            ["descripcion"] = "description",
            ["descripción"] = "description",
            ["description"] = "description",
            ["detalle"] = "description",
            ["concepto"] = "description",
            ["movimiento"] = "description",
            ["monto"] = "amount",
            ["amount"] = "amount",
            ["importe"] = "amount",
            ["debito"] = "amount",
            ["débito"] = "amount",
            ["credito"] = "amount",
            ["crédito"] = "amount",
            ["moneda"] = "currency",
            ["currency"] = "currency"
        };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "MM/dd/yyyy",
        "dd-MM-yyyy",
        "yyyy/MM/dd",
        "dd/MM/yy",
        "d/M/yy"
    ];

    public static string NormalizeHeader(string header)
    {
        var trimmed = header.Trim();
        var lower = trimmed.ToLowerInvariant();
        return lower
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal);
    }

    public static Dictionary<string, int> MapColumns(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            if (ColumnAliases.TryGetValue(normalized, out var canonical) && !map.ContainsKey(canonical))
            {
                map[canonical] = i;
            }
        }

        return map;
    }

    public static char DetectDelimiter(string headerLine)
    {
        var commaFields = ParseCsvLine(headerLine, ',').Count;
        var semicolonFields = ParseCsvLine(headerLine, ';').Count;
        return semicolonFields > commaFields ? ';' : ',';
    }

    public static IReadOnlyList<string> ParseCsvLine(string line, char delimiter = ',') =>
        ParseCsvLineCore(line, delimiter);

    private static IReadOnlyList<string> ParseCsvLineCore(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            if ((c == '\r' || c == '\n') && !inQuotes)
            {
                break;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields;
    }

    public static bool TryParseDate(string value, out DateTime date)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            date = default;
            return false;
        }

        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTime.TryParse(trimmed, EsAr, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0;
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var cleaned = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsDigit(c) || c is ',' or '.' or '-')
            {
                cleaned.Append(c);
            }
        }

        var numeric = cleaned.ToString();
        if (string.IsNullOrEmpty(numeric) || numeric is "-" or "," or ".")
        {
            return false;
        }

        var lastComma = numeric.LastIndexOf(',');
        var lastDot = numeric.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? '.' : ',';
            numeric = numeric.Replace(thousandsSeparator.ToString(), string.Empty)
                .Replace(decimalSeparator, '.');
        }
        else if (lastComma >= 0)
        {
            var commaCount = numeric.Count(ch => ch == ',');
            numeric = commaCount == 1 && numeric.Length - lastComma - 1 <= 2
                ? numeric.Replace(',', '.')
                : numeric.Replace(",", string.Empty);
        }
        else if (lastDot >= 0)
        {
            var dotCount = numeric.Count(ch => ch == '.');
            if (dotCount > 1)
            {
                var last = lastDot;
                numeric = numeric[..last].Replace(".", string.Empty) + numeric[last..];
            }
        }

        return decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    public static string? DetectCurrencyFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var upper = text.ToUpperInvariant();
        if (upper.Contains("USD", StringComparison.Ordinal) || upper.Contains("U$S", StringComparison.Ordinal))
        {
            return "USD";
        }

        if (upper.Contains("EUR", StringComparison.Ordinal))
        {
            return "EUR";
        }

        if (upper.Contains("ARS", StringComparison.Ordinal) || upper.Contains(" $", StringComparison.Ordinal))
        {
            return "ARS";
        }

        return null;
    }
}
