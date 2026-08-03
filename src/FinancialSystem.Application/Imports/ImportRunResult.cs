namespace FinancialSystem.Application.Imports;

/// <summary>
/// Resultado uniforme de una corrida de importación, devuelto por IFileImportHandler.HandleAsync.
///
/// POR QUÉ EXISTE:
///   Los importadores ya calculaban esta misma información con formas ligeramente distintas
///   (BbvaBankStatementImporter.ImportResult, por ejemplo) pero HandleAsync no la exponía — se
///   perdía apenas terminaba el log. FileImportRouter usa este contrato común para persistir un
///   ImportBatch por corrida sin que cada handler/importador tenga que conocer ImportBatch
///   (ver PR I4, docs/Epics/EpicaI-Importacion.md).
///
/// Diagnostics: mismo formato que FileParseResult.Diagnostics (strings ya formados, ver PR I1) —
/// representan líneas que se intentaron parsear y fallaron. No hay detalle por línea de lo
/// simplemente omitido (SkippedRows es solo un contador, ver PdfStatementParserBase).
///
/// Outcome (Patch 0051): distingue un archivo que se intentó procesar (Processed — puede
/// haber insertado 0 o más movimientos) de uno que la validación previa rechazó sin que
/// ningún parser/handler llegara a ejecutarse (RejectedByValidation). Parámetro opcional
/// con default Processed para no romper los call sites existentes, que siguen
/// construyendo este record igual que antes.
/// </summary>
public sealed record ImportRunResult(
    int Inserted,
    int Duplicates,
    int Failed,
    int Skipped,
    IReadOnlyList<string> Diagnostics,
    ImportOutcome Outcome = ImportOutcome.Processed)
{
    public static ImportRunResult Failure(string reason) =>
        new(0, 0, 1, 0, [reason]);

    /// <summary>
    /// El archivo fue rechazado por la validación previa (Patch 0051) — ningún
    /// IFileImportHandler/IFileParser llegó a intentar procesarlo, nada se persistió.
    /// </summary>
    public static ImportRunResult RejectedByValidation(string reason) =>
        new(0, 0, 1, 0, [reason], ImportOutcome.RejectedByValidation);
}

/// <summary>
/// Ver el comentario de Outcome en <see cref="ImportRunResult"/>. Distingue, sin exponer
/// entidades ni romper el contrato existente, si un ImportRunResult representa un
/// archivo procesado (con o sin movimientos) o un rechazo de la validación previa.
/// </summary>
public enum ImportOutcome
{
    Processed,
    RejectedByValidation
}
