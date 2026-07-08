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

    public int InsertedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }

    /// <summary>Detalle de líneas descartadas o fallidas durante esta corrida.</summary>
    public ICollection<ImportBatchLine> Lines { get; set; } = [];
}
