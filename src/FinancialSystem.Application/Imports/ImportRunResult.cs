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
///
/// Failed (Patch 0053 — Consistencia transaccional): antes, Failure(reason) dejaba
/// Outcome en el default Processed — una excepción no controlada durante el
/// procesamiento quedaba indistinguible de una corrida realmente completada. Ahora
/// Failure() marca Outcome=Failed explícitamente, para que "completada / rechazada /
/// fallida" sean estados siempre distinguibles, nunca ambiguos.
///
/// ParserUsed / ProcessedWithWarnings (Patch 0054 — Trazabilidad): ParserUsed identifica
/// el parser específico que interpretó el archivo (ej. "BBVA_VISA_AR", "CsvFileParser")
/// -- distinto de HandlerName, que solo dice qué IFileImportHandler corrió (el catch-all
/// "Transaction" puede resolver a varios parsers). Null cuando no aplica (rechazos,
/// fallas antes de resolver un parser). FileImportRouter reclasifica centralizadamente
/// Processed a ProcessedWithWarnings cuando Failed/Skipped &gt; 0, para distinguir
/// "Exitosa" de "Exitosa con advertencias" sin que cada handler lo decida por su cuenta.
/// </summary>
public sealed record ImportRunResult(
    int Inserted,
    int Duplicates,
    int Failed,
    int Skipped,
    IReadOnlyList<string> Diagnostics,
    ImportOutcome Outcome = ImportOutcome.Processed,
    string? ParserUsed = null)
{
    /// <summary>
    /// Una excepción no controlada abortó el procesamiento (Patch 0053) — a diferencia de
    /// RejectedByValidation, acá sí se intentó procesar el archivo. FileImportRouter no
    /// persiste el ContentHash real de una corrida Failed (ver PersistImportBatchAsync),
    /// para que un intento fallido nunca bloquee un reintento futuro del mismo contenido.
    /// </summary>
    public static ImportRunResult Failure(string reason) =>
        new(0, 0, 1, 0, [reason], ImportOutcome.Failed);

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
/// entidades ni romper el contrato existente, entre archivo procesado (con o sin
/// advertencias), rechazo de la validación previa, reimportación salteada y falla no
/// controlada — Exitosa / Exitosa con advertencias / Rechazada / Fallida (Patch 0054)
/// quedan siempre diferenciadas, nunca ambiguas.
/// </summary>
public enum ImportOutcome
{
    /// <summary>Exitosa: procesada sin ningún dato omitido ni fallido a nivel de fila.</summary>
    Processed,

    /// <summary>
    /// Exitosa con advertencias (Patch 0054): procesada, pero con al menos una fila
    /// omitida o fallida (Failed/Skipped &gt; 0). FileImportRouter la asigna
    /// centralizadamente -- ningún handler la construye directamente.
    /// </summary>
    ProcessedWithWarnings,

    RejectedByValidation,
    AlreadyImported,
    Failed
}
