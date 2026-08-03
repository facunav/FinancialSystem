namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Estado final de una corrida de importación (Patch 0054 — Trazabilidad). Se persiste en
/// ImportBatch para que el resultado de cada corrida quede consultable directamente desde
/// el historial, sin tener que inferirlo combinando HandlerName y los distintos contadores
/// (InsertedCount/FailedCount/SkippedCount) — que es justamente la inconsistencia entre
/// parsers que este patch busca evitar (ver FileImportRouter, único lugar que asigna
/// este valor).
///
/// ImportBatch.Outcome es nullable: las corridas anteriores a este patch quedan en null
/// -- no se infiere retroactivamente un valor que podría ser incorrecto. Null se lee como
/// "resultado no registrado (corrida previa a Patch 0054)", distinto de cualquiera de los
/// 5 estados reales.
/// </summary>
public enum ImportBatchOutcome
{
    /// <summary>Exitosa: procesada sin ninguna fila omitida ni fallida.</summary>
    Processed = 1,

    /// <summary>Exitosa con advertencias: procesada, con al menos una fila omitida o fallida.</summary>
    ProcessedWithWarnings = 2,

    /// <summary>Rechazada: la validación previa (o la ausencia de un handler) impidió procesarla.</summary>
    RejectedByValidation = 3,

    /// <summary>Ya se había importado el mismo contenido antes -- se saltea, no es un error.</summary>
    AlreadyImported = 4,

    /// <summary>Fallida: una excepción no controlada abortó el procesamiento.</summary>
    Failed = 5,
}
