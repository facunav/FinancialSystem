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
///
/// AlreadyImported (Patch 0052): el archivo ya se había importado con éxito antes (mismo
/// contenido, ver FileImportRouter) — no es un error ni una validación rechazada, es una
/// corrida que se saltea a propósito para no duplicar movimientos. Failed queda en 0
/// porque, a diferencia de RejectedByValidation, no hay ningún problema con el archivo.
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

    /// <summary>
    /// El archivo ya había sido importado antes con el mismo contenido (Patch 0052) — se
    /// saltea deliberadamente, sin insertar nada y sin lanzar ninguna excepción.
    /// </summary>
    public static ImportRunResult AlreadyImported(string reason) =>
        new(0, 0, 0, 0, [reason], ImportOutcome.AlreadyImported);
}

/// <summary>
/// Ver el comentario de Outcome en <see cref="ImportRunResult"/>. Distingue, sin exponer
/// entidades ni romper el contrato existente, si un ImportRunResult representa un
/// archivo procesado (con o sin movimientos), un rechazo de la validación previa, o una
/// reimportación del mismo contenido que se saltea deliberadamente.
/// </summary>
public enum ImportOutcome
{
    Processed,
    RejectedByValidation,
    AlreadyImported
}
