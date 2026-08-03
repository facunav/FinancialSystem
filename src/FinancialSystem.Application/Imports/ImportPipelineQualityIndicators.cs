namespace FinancialSystem.Application.Imports;

/// <summary>
/// Indicadores de calidad de una corrida (Patch 0057, sección 2 del patch). Se calculan
/// siempre a partir de ImportRunResult/ImportOutcome -- el contrato único ya unificado en
/// el Patch 0055 -- para que la estructura sea la misma sin importar qué handler produjo
/// la corrida, sin que ningún importador tenga que calcular ni reportar esto por su
/// cuenta.
/// </summary>
public sealed record ImportPipelineQualityIndicators(
    bool Rejected,
    bool Successful,
    bool Failed,
    bool HasWarnings,
    int MovementsPersisted,
    int MovementsOmitted)
{
    public static ImportPipelineQualityIndicators FromResult(ImportRunResult result) => new(
        Rejected: result.Outcome == ImportOutcome.RejectedByValidation,
        Successful: result.Outcome is ImportOutcome.Processed or ImportOutcome.ProcessedWithWarnings or ImportOutcome.AlreadyImported,
        Failed: result.Outcome == ImportOutcome.Failed,
        HasWarnings: result.Outcome == ImportOutcome.ProcessedWithWarnings,
        MovementsPersisted: result.Inserted,
        MovementsOmitted: result.Duplicates + result.Skipped);
}
