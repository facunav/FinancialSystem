using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Registro de una corrida de importación. Común a las 3 fuentes (banco, tarjeta, Excel legacy).
///
/// POR QUÉ EXISTE:
///   Hoy esa información se calcula en memoria durante cada corrida (por ejemplo,
///   BbvaBankStatementImporter.ImportResult) y se pierde apenas termina el proceso —
///   solo queda en el log. ImportBatch la persiste para que quede consultable después
///   (ver docs/Epics/EpicaI-Importacion.md).
///
/// ALCANCE DE ESTA ENTIDAD (PR I2):
///   Solo la entidad, su configuración EF y la migración. Ningún importador persiste
///   todavía un ImportBatch — eso es PR I4.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Ruta o nombre del archivo procesado.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>Hash del contenido del archivo, para trazabilidad. No es una clave de idempotencia (ver ADR-005).</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Nombre del handler/importador que procesó el archivo. Ej: "Transaction", "BbvaBankStatement".</summary>
    public string HandlerName { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }

    /// <summary>
    /// Duración de la corrida (Patch 0054 — Trazabilidad). No es una columna propia: se
    /// deriva de StartedAtUtc/CompletedAtUtc, que ya existían, para no duplicar
    /// información persistida.
    /// </summary>
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    /// <summary>
    /// Tamaño del archivo procesado, en bytes (Patch 0054). Se calcula una sola vez en
    /// FileImportRouter.RouteAsync, antes de que el archivo pueda dejar de existir (ver
    /// ImportBatchEndpoints, que borra el temporal de una subida manual apenas termina).
    /// Null en corridas previas a este patch, o si no se pudo leer el tamaño del archivo.
    /// </summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Identificador del parser específico que interpretó el archivo (Patch 0054). Ej.:
    /// "BBVA_VISA_AR", "MASTERCARD_AR", "CsvFileParser", "ExcelWorkbookParser",
    /// "BbvaBankStatementParser", "BbvaDebitCardParser". Distinto de HandlerName: el
    /// handler catch-all "Transaction" puede resolver a varios parsers posibles, y este
    /// campo permite saber cuál se usó realmente en cada corrida. Null cuando no aplica
    /// (rechazos, fallas antes de resolver un parser, o corridas previas a este patch).
    /// </summary>
    public string? ParserUsed { get; set; }

    /// <summary>
    /// Estado final de esta corrida (Patch 0054): Exitosa / Exitosa con advertencias /
    /// Rechazada / Ya importada / Fallida. Lo asigna únicamente FileImportRouter, a partir
    /// de ImportRunResult.Outcome, para que ningún handler/parser decida este valor por su
    /// cuenta. Null en corridas previas a este patch (ver ImportBatchOutcome).
    /// </summary>
    public ImportBatchOutcome? Outcome { get; set; }

    public int InsertedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }

    /// <summary>
    /// Movimientos detectados en esta corrida (Patch 0054 — Trazabilidad): todo lo que se
    /// identificó, haya terminado persistido, omitido o fallado. Computada acá, en un
    /// único lugar, para que ningún parser/handler tenga que calcularla por su cuenta de
    /// forma potencialmente inconsistente con los demás (ver FileImportRouter, sección 4
    /// del patch).
    /// </summary>
    public int DetectedCount => InsertedCount + DuplicateCount + SkippedCount + FailedCount;

    /// <summary>Movimientos efectivamente persistidos en esta corrida. Alias de InsertedCount.</summary>
    public int PersistedCount => InsertedCount;

    /// <summary>
    /// Movimientos detectados pero no persistidos sin que sea un error: ya existían
    /// (DuplicateCount) o el parser los salteó deliberadamente (SkippedCount).
    /// </summary>
    public int OmittedCount => DuplicateCount + SkippedCount;

    /// <summary>Errores registrados en esta corrida. Alias de FailedCount.</summary>
    public int ErrorCount => FailedCount;

    /// <summary>Detalle de líneas descartadas o fallidas durante esta corrida.</summary>
    public ICollection<ImportBatchLine> Lines { get; set; } = [];
}
