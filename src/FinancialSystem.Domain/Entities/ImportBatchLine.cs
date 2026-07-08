namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Una línea descartada o fallida dentro de un ImportBatch. Registra línea, texto
/// crudo y motivo — la misma información que hoy los parsers ya calculan (ver
/// PdfStatementParserBase, CsvFileParser, ExcelWorkbookParser) pero que se pierde
/// al terminar el proceso, salvo por lo que llega al log.
/// </summary>
public class ImportBatchLine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>Número de línea dentro del archivo de origen.</summary>
    public int LineNumber { get; set; }

    /// <summary>Texto original de la línea, sin procesar.</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>Motivo por el que la línea fue descartada o falló el parseo.</summary>
    public string Reason { get; set; } = string.Empty;
}
