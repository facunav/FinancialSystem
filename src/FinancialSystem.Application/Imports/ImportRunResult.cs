namespace FinancialSystem.Application.Imports;

/// <summary>
/// Resultado uniforme de una corrida de importación, devuelto por IFileImportHandler.HandleAsync.
///
/// POR QUÉ EXISTE:
///   Los 3 importadores ya calculaban esta misma información con formas ligeramente distintas
///   (BbvaBankStatementImporter.ImportResult, ExcelLegacyExpenseImporter.LegacyExpenseImportResult)
///   pero HandleAsync no la exponía — se perdía apenas terminaba el log. FileImportRouter usa este
///   contrato común para persistir un ImportBatch por corrida sin que cada handler/importador
///   tenga que conocer ImportBatch (ver PR I4, docs/Epics/EpicaI-Importacion.md).
///
/// Diagnostics: mismo formato que FileParseResult.Diagnostics (strings ya formados, ver PR I1) —
/// representan líneas que se intentaron parsear y fallaron. No hay detalle por línea de lo
/// simplemente omitido (SkippedRows es solo un contador, ver PdfStatementParserBase).
/// </summary>
public sealed record ImportRunResult(
    int Inserted,
    int Duplicates,
    int Failed,
    int Skipped,
    IReadOnlyList<string> Diagnostics)
{
    public static ImportRunResult Failure(string reason) =>
        new(0, 0, 1, 0, [reason]);
}
