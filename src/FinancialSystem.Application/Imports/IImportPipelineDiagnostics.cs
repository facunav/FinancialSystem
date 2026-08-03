namespace FinancialSystem.Application.Imports;

/// <summary>
/// Punto único de observabilidad del pipeline de importación (Patch 0057, Epic P, P008,
/// sección 4 del patch). Solo FileImportRouter la invoca -- ningún handler/parser conoce
/// esta interfaz ni genera sus propias métricas, para que la estructura sea uniforme sin
/// importar el origen del archivo (sección 2 del patch).
///
/// EXTENSIBILIDAD (sección 5 del patch): agregar una métrica nueva es agregar un campo a
/// ImportPipelineRunMetrics/ImportPipelineQualityIndicators (o una nueva implementación de
/// esta interfaz) sin tocar cada IFileImportHandler.
/// </summary>
public interface IImportPipelineDiagnostics
{
    /// <summary>Se invoca exactamente una vez por cada RouteAsync, sin importar el desenlace.</summary>
    void RecordRun(ImportPipelineRunMetrics metrics);

    /// <summary>
    /// Se invoca solo cuando una excepción no controlada escapó de una etapa del pipeline
    /// (sección 3 del patch). No expone el mensaje de la excepción al usuario final -- eso
    /// sigue resuelto por ImportRunResult.Failure/ConsistencyIssues, que ya sanitizan lo
    /// que se persiste; esta llamada es exclusivamente para diagnóstico interno (logs).
    /// </summary>
    void RecordFailure(string sourceFile, Guid? importBatchId, ImportPipelineStage stage, Exception exception);
}
