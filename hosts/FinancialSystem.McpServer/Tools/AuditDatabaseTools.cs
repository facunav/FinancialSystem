using System.ComponentModel;
using FinancialSystem.Infrastructure.Audit;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Auditoría integral del sistema -- un resumen único combinando lo que ya calculan
/// las tools/servicios de auditoría existentes, para el mes en curso (mismo período
/// por defecto que ya usan FindSuspiciousMovements/FindMisclassifiedMovements/
/// SearchMovements cuando no se les pasa from/to).
///
/// PRINCIPIO: cero reglas nuevas. Desde 0027, toda la orquestación (qué se cuenta,
/// cómo se arma el resumen y la conclusión) vive en
/// AuditReportService.BuildFullAuditReportAsync (Infrastructure) -- se movió ahí
/// porque FinancialMcp.Api (Centro de Auditoría) necesitaba poder producir
/// "exactamente la misma lógica" que esta tool sin poder referenciar este proyecto
/// host (AuditTools/AuditDatabaseTools viven en hosts/FinancialSystem.McpServer, que
/// FinancialMcp.Api no referencia). Esta clase solo calcula el período por defecto
/// (mes en curso -- AuditDatabase no recibe parámetros) y delega. Texto de salida sin
/// cambios respecto de antes de 0027.
/// </summary>
[McpServerToolType]
public sealed class AuditDatabaseTools
{
    private readonly AuditReportService _auditReportService;

    public AuditDatabaseTools(AuditReportService auditReportService)
    {
        _auditReportService = auditReportService;
    }

    [McpServerTool]
    [Description(
        "Ejecuta todas las auditorías existentes (FindSuspiciousMovements, " +
        "FindMisclassifiedMovements, pendientes vía IMovementsQueryService, " +
        "investigaciones abiertas/resueltas) para el mes en curso y devuelve un único " +
        "resumen: totales, problemas encontrados por categoría, y una conclusión. No " +
        "detecta nada nuevo -- solo combina lo que esas tools ya calculan.")]
    public async Task<string> AuditDatabase(CancellationToken ct = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(to.Year, to.Month, 1);

        return await _auditReportService.BuildFullAuditReportAsync(from, to, ct);
    }
}
