using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports.Consistency;

/// <summary>
/// Implementación de <see cref="IImportConsistencyVerifier"/> (Patch 0056): ejecuta todos
/// los IImportConsistencyCheck registrados en DI y agrega sus resultados. No sabe nada del
/// significado de cada check -- ese conocimiento vive exclusivamente en cada
/// implementación concreta (ver QuantityConsistencyCheck, MovementProvenanceCheck,
/// ImportHistoryConsistencyCheck).
/// </summary>
internal sealed class ImportConsistencyVerifier(
    IEnumerable<IImportConsistencyCheck> checks,
    ILogger<ImportConsistencyVerifier> logger) : IImportConsistencyVerifier
{
    private readonly IReadOnlyList<IImportConsistencyCheck> _checks = checks.ToList().AsReadOnly();

    public async Task<ImportConsistencyReport> VerifyAsync(ImportConsistencyContext context, CancellationToken ct = default)
    {
        var issues = new List<string>();

        foreach (var check in _checks)
        {
            try
            {
                var checkIssues = await check.CheckAsync(context, ct);
                issues.AddRange(checkIssues);
            }
            catch (Exception ex)
            {
                // Un check que falla al ejecutarse es en sí mismo un problema de
                // integridad no verificable -- no debe hacer desaparecer la corrida ni
                // el resto de los checks (ver sección 4 del patch: "no ocultar el
                // problema").
                logger.LogError(ex,
                    "ImportConsistencyVerifier: el check '{Check}' falló al ejecutarse para el batch {BatchId}",
                    check.Name, context.ImportBatchId);
                issues.Add($"El check '{check.Name}' no pudo ejecutarse: {ex.Message}");
            }
        }

        return issues.Count == 0
            ? ImportConsistencyReport.Consistent
            : ImportConsistencyReport.Inconsistent(issues.AsReadOnly());
    }
}
