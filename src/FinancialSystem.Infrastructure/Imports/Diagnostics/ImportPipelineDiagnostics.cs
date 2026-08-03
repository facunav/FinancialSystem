using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Diagnostics;

/// <summary>
/// Implementación de <see cref="IImportPipelineDiagnostics"/> (Patch 0057): vuelca las
/// métricas y fallas del pipeline como logs estructurados -- mismo mecanismo de
/// diagnóstico que ya usa el resto del pipeline (ILogger), sin agregar una dependencia de
/// telemetría nueva al proyecto. No almacena nada en base de datos: esta clase es
/// exclusivamente de diagnóstico operativo, no de auditoría funcional (eso sigue siendo
/// ImportBatch, sin cambios).
/// </summary>
internal sealed class ImportPipelineDiagnostics(ILogger<ImportPipelineDiagnostics> logger) : IImportPipelineDiagnostics
{
    public void RecordRun(ImportPipelineRunMetrics metrics)
    {
        logger.LogInformation(
            "ImportPipeline: '{File}' (batch {BatchId}) -- total={TotalMs}ms, etapas=[{Stages}], " +
            "rechazado={Rejected} exitoso={Successful} fallido={Failed} advertencias={HasWarnings} " +
            "persistidos={Persisted} omitidos={Omitted}",
            metrics.SourceFile,
            metrics.ImportBatchId,
            metrics.TotalDuration.TotalMilliseconds,
            string.Join(", ", metrics.StageDurations.Select(s => $"{s.Stage}={s.Duration.TotalMilliseconds:F0}ms")),
            metrics.Indicators.Rejected,
            metrics.Indicators.Successful,
            metrics.Indicators.Failed,
            metrics.Indicators.HasWarnings,
            metrics.Indicators.MovementsPersisted,
            metrics.Indicators.MovementsOmitted);
    }

    public void RecordFailure(string sourceFile, Guid? importBatchId, ImportPipelineStage stage, Exception exception)
    {
        // El mensaje de la excepción queda solo en el log (diagnóstico interno) -- nunca
        // se persiste ni se devuelve al usuario final desde acá (ver comentario de
        // RecordFailure en IImportPipelineDiagnostics).
        logger.LogError(
            exception,
            "ImportPipeline: falla no controlada en la etapa {Stage} procesando '{File}' (batch {BatchId})",
            stage, sourceFile, importBatchId);
    }
}
