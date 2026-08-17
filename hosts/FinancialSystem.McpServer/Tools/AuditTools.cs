using System.ComponentModel;
using FinancialSystem.Infrastructure.Audit;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de auditoría — Fase 2 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: ninguna regla de detección nueva vive acá. Desde 0027, toda la
/// orquestación real (IReviewEngine, IClassificationSuggestionService, comparación
/// contra Counterparty.Default*) vive en AuditReportService (Infrastructure) — se
/// movió ahí porque FinancialMcp.Api (Centro de Auditoría) necesitaba reutilizar
/// "exactamente la misma lógica" sin poder referenciar este proyecto host. Este
/// archivo solo valida los parámetros string de la tool (from/to/rango máximo,
/// concern específico de la interfaz MCP, no una regla de auditoría) y delega. Texto
/// de salida sin cambios respecto de antes de 0027.
/// </summary>
[McpServerToolType]
public sealed class AuditTools
{
    // Mismo límite y misma razón que MovementTools.MaxDateRangeDays y
    // MovementsEndpoints.GetAll: tanto ISuspicionDetector como el cómputo de
    // pendientes que IMovementsQueryService siempre hace internamente comparan
    // movimientos dentro del período con costo no lineal -- acotar el rango protege
    // a ambas tools de este archivo por igual, aunque FindMisclassifiedMovements
    // solo analice movimientos ya clasificados.
    private const int MaxDateRangeDays = 90;

    private readonly AuditReportService _auditReportService;

    public AuditTools(AuditReportService auditReportService)
    {
        _auditReportService = auditReportService;
    }

    [McpServerTool]
    [Description(
        "Devuelve, en formato estructurado (sin lenguaje natural), los grupos de movimientos " +
        "que ISuspicionDetector marcó como sospechosos (posibles duplicados o transacciones " +
        "divididas) dentro de un período -- el mismo motor que ya usa la pantalla Movimientos, " +
        "sin ninguna regla nueva. Usar para auditar un período antes de confiar en sus totales.")]
    public async Task<string> FindSuspiciousMovements(
        [Description("Fecha de inicio (yyyy-MM-dd). Por defecto, el primer día del mes de 'to'.")]
        string? from = null,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to = null,
        [Description(
            "Id de FinancialAccount para filtrar. Un grupo se incluye si al menos un " +
            "movimiento del grupo pertenece a esta cuenta.")]
        Guid? financialAccountId = null,
        CancellationToken ct = default)
    {
        if (!TryParseDate(to, DateOnly.FromDateTime(DateTime.UtcNow), out var effectiveTo))
            return $"Error: 'to' inválido ('{to}'). Usar formato yyyy-MM-dd.";
        if (!TryParseDate(from, new DateOnly(effectiveTo.Year, effectiveTo.Month, 1), out var effectiveFrom))
            return $"Error: 'from' inválido ('{from}'). Usar formato yyyy-MM-dd.";

        if (effectiveFrom > effectiveTo)
            return "Error: 'from' debe ser anterior o igual a 'to'.";

        var rangeDays = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return $"Error: el rango máximo permitido es de {MaxDateRangeDays} días.";

        return await _auditReportService.BuildSuspiciousMovementsReportAsync(
            effectiveFrom, effectiveTo, financialAccountId, ct);
    }

    [McpServerTool]
    [Description(
        "Devuelve, en formato estructurado (sin lenguaje natural), movimientos YA clasificados " +
        "cuya clasificación actual no coincide con lo que dos señales objetivas del dominio " +
        "indicarían: el historial de movimientos con la misma descripción exacta (el mismo " +
        "motor que sugiere valores para pendientes) y los valores por defecto configurados en " +
        "su Counterparty. No aplica ninguna regla nueva ni IA -- solo compara valores ya " +
        "existentes. Usar para encontrar reclasificaciones candidatas antes de confiar en las " +
        "métricas de un período.")]
    public async Task<string> FindMisclassifiedMovements(
        [Description("Fecha de inicio (yyyy-MM-dd). Por defecto, el primer día del mes de 'to'.")]
        string? from = null,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to = null,
        [Description("Id de FinancialAccount para filtrar. Mismo parámetro que ya usa SearchMovements.")]
        Guid? financialAccountId = null,
        CancellationToken ct = default)
    {
        if (!TryParseDate(to, DateOnly.FromDateTime(DateTime.UtcNow), out var effectiveTo))
            return $"Error: 'to' inválido ('{to}'). Usar formato yyyy-MM-dd.";
        if (!TryParseDate(from, new DateOnly(effectiveTo.Year, effectiveTo.Month, 1), out var effectiveFrom))
            return $"Error: 'from' inválido ('{from}'). Usar formato yyyy-MM-dd.";

        if (effectiveFrom > effectiveTo)
            return "Error: 'from' debe ser anterior o igual a 'to'.";

        var rangeDays = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return $"Error: el rango máximo permitido es de {MaxDateRangeDays} días.";

        return await _auditReportService.BuildMisclassifiedMovementsReportAsync(
            effectiveFrom, effectiveTo, financialAccountId, ct);
    }

    private static bool TryParseDate(string? value, DateOnly fallback, out DateOnly result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = fallback;
            return true;
        }

        return DateOnly.TryParse(value, out result);
    }
}
