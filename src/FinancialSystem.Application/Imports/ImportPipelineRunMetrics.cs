namespace FinancialSystem.Application.Imports;

/// <summary>Duración medida de una etapa concreta (Patch 0057). TimeSpan.Zero si esa corrida no llegó a esa etapa.</summary>
public sealed record ImportPipelineStageTiming(ImportPipelineStage Stage, TimeSpan Duration);

/// <summary>
/// Métricas de una única corrida de RouteAsync (Patch 0057 — observabilidad y
/// diagnóstico). ImportBatchId es null cuando la corrida no llegó a persistir un
/// ImportBatch (no debería ocurrir hoy, pero el tipo lo permite en vez de forzar un Guid
/// artificial). No es necesaria alta precisión (sección 1 del patch): las duraciones se
/// miden con Stopwatch, no con IDateTimeProvider, para no interferir con StartedAtUtc/
/// CompletedAtUtc (Patch 0054) ni con ningún test que use un reloj fijo para esos campos.
/// </summary>
public sealed record ImportPipelineRunMetrics(
    string SourceFile,
    Guid? ImportBatchId,
    TimeSpan TotalDuration,
    IReadOnlyList<ImportPipelineStageTiming> StageDurations,
    ImportPipelineQualityIndicators Indicators);
