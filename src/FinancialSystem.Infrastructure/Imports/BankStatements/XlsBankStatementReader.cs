using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FinancialSystem.Domain.Entities;
using Microsoft.Extensions.Logging;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace FinancialSystem.Infrastructure.Imports.BankStatements;

// ════════════════════════════════════════════════════════════════
// XLS READER
//
// Responsabilidad: abrir el extracto con NPOI y convertir cada celda
// a string normalizado. No sabe nada de qué significa cada celda.
//
// POR QUÉ NPOI Y NO CLOSEDXML:
//   ClosedXML soporta .xlsx (OOXML). Los extractos bancarios llegan como
//   .xls (BIFF8/OLE2 — Excel 97-2003) o, pese a la extensión .xls, como
//   OOXML real (algunos exports de homebanking generan XLSX y conservan
//   la extensión histórica). NPOI soporta ambos formatos — por eso se
//   abre con WorkbookFactory.Create, que detecta el formato real del
//   archivo (sniffing de la cabecera) en vez de asumir uno fijo.
//   ClosedXML puede abrir .xls básicos pero con pérdida de fidelidad.
//
// POR QUÉ NO OLEDB:
//   Requiere ACE/JET driver (solo Windows x86). NPOI es cross-platform.
// ════════════════════════════════════════════════════════════════

public sealed class XlsBankStatementReader
{
    private readonly ILogger<XlsBankStatementReader> _logger;

    public XlsBankStatementReader(ILogger<XlsBankStatementReader> logger)
        => _logger = logger;

    /// <summary>
    /// Lee todas las celdas de la primera hoja visible como string[][].
    /// Las celdas numéricas y de fecha se convierten a su representación
    /// formateada tal como Excel la mostraría al usuario.
    /// </summary>
    public (string?[][] Rows, string SheetName) ReadFirstSheet(string filePath)
    {
        using var stream   = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        // WorkbookFactory detecta el formato real del archivo (BIFF8/OLE2 vs. OOXML)
        // en vez de asumir BIFF8 — necesario porque no todo archivo .xls es binario
        // legacy: algunos extractos traen contenido XLSX con extensión .xls.
        using var workbook = WorkbookFactory.Create(stream);

        var sheet = workbook.GetSheetAt(0)
            ?? throw new InvalidOperationException($"El archivo XLS no tiene hojas: {filePath}");

        var sheetName = sheet.SheetName;
        var rows      = new List<string?[]>();

        for (var r = 0; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                rows.Add([]);
                continue;
            }

            var cells = new string?[row.LastCellNum];
            for (var c = 0; c < row.LastCellNum; c++)
            {
                cells[c] = ReadCellAsString(row.GetCell(c), workbook);
            }
            rows.Add(cells);
        }

        _logger.LogDebug(
            "XlsBankStatementReader: {Rows} filas leídas de hoja '{Sheet}' en {File}",
            rows.Count, sheetName, Path.GetFileName(filePath));

        return (rows.ToArray(), sheetName);
    }

    private static string? ReadCellAsString(ICell? cell, IWorkbook workbook)
    {
        if (cell is null || cell.CellType == CellType.Blank)
            return null;

        return cell.CellType switch
        {
            CellType.String  => cell.StringCellValue?.Trim(),
            CellType.Numeric => ReadNumericCell(cell, workbook),
            CellType.Boolean => cell.BooleanCellValue.ToString(),
            CellType.Formula => ReadFormulaCellAsString(cell, workbook),
            _                => null,
        };
    }

    private static string? ReadNumericCell(ICell cell, IWorkbook workbook)
    {
        // NPOI no distingue automáticamente fecha de número —
        // hay que chequear el formato de celda.
        if (DateUtil.IsCellDateFormatted(cell))
        {
            var date = cell.DateCellValue;
            return string.Format(CultureInfo.InvariantCulture, "{0:dd/MM/yyyy}", date);
        }

        // Intentar leer el valor formateado (como lo muestra Excel)
        var formatter = new DataFormatter();
        var formatted = formatter.FormatCellValue(cell);
        return string.IsNullOrWhiteSpace(formatted)
            ? Convert.ToString(cell.NumericCellValue, CultureInfo.InvariantCulture)
            : formatted;
    }

    private static string? ReadFormulaCellAsString(ICell cell, IWorkbook workbook)
    {
        // Para celdas con fórmula, usar el valor cacheado
        return cell.CachedFormulaResultType switch
        {
            CellType.String  => cell.StringCellValue?.Trim(),
            CellType.Numeric => ReadNumericCell(cell, workbook),
            _                => null,
        };
    }
}
