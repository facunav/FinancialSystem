using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de auditoría — Fase 2 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: ninguna regla de detección nueva vive acá. FindSuspiciousMovements
/// expone exactamente lo que ya calcula ISuspicionDetector (duplicados y splits),
/// orquestado por IReviewEngine -- el mismo componente que ya usa
/// MovementsQueryService.LoadPendingWithWarningsAsync para la pantalla Movimientos
/// (K6). No dejó de tener sentido tras PR-L4: ese PR retiró únicamente el motor de
/// matching contra movimientos "Candidate" legacy (ver ReviewResult.cs), que era un
/// componente distinto orquestado por el mismo ReviewEngine -- ISuspicionDetector
/// nunca dependió de esa segunda fuente y sigue activo y vigente, documentado
/// explícitamente como tal en ese mismo archivo. Este archivo solo formatea su
/// resultado a datos estructurados.
/// </summary>
[McpServerToolType]
public sealed class AuditTools
{
    // Mismo límite y misma razón que MovementTools.MaxDateRangeDays y
    // MovementsEndpoints.GetAll: ISuspicionDetector compara movimientos par a par
    // dentro del período (O(N²) acotado). Acá se llama a IReviewEngine directo, así
    // que la razón para el mismo límite aplica todavía más directamente que en
    // SearchMovements.
    private const int MaxDateRangeDays = 90;

    private readonly IReviewEngine _reviewEngine;
    private readonly IApplicationDbContext _db;

    public AuditTools(IReviewEngine reviewEngine, IApplicationDbContext db)
    {
        _reviewEngine = reviewEngine;
        _db = db;
    }

    [McpServerTool]
    [Description(
        "Devuelve, en formato estructurado (sin lenguaje natural), los grupos de movimientos " +
        "que ISuspicionDetector marcó como sospechosos (posibles duplicados o transacciones " +
        "divididas) dentro de un período -- el mismo motor que ya usa la pantalla Movimientos, " +
        "sin ninguna regla nueva. Usar para auditar un período antes de confiar en sus totales.")]
    public async Task<string> FindSuspiciousMovements(
        [Description("Fecha de inicio (yyyy-MM-dd). Por defecto, el primer día del mes de 'to'.")]
        string? from,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to,
        [Description(
            "Id de FinancialAccount para filtrar. Un grupo se incluye si al menos un " +
            "movimiento del grupo pertenece a esta cuenta.")]
        Guid? financialAccountId,
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

        var result = await _reviewEngine.GenerateAsync(effectiveFrom, effectiveTo, ct);

        var groups = result.Suspicious;
        if (financialAccountId is { } accountId)
            groups = groups.Where(g => g.Movements.Any(m => m.FinancialAccountId == accountId)).ToList();

        if (groups.Count == 0)
            return $"No se detectaron movimientos sospechosos entre {effectiveFrom:dd/MM/yyyy} " +
                   $"y {effectiveTo:dd/MM/yyyy}.";

        var accountNames = await ResolveAccountNamesAsync(groups, ct);

        var sb = new StringBuilder();
        var totalMovements = groups.Sum(g => g.Movements.Count);
        sb.AppendLine(
            $"{groups.Count} grupo(s) sospechoso(s), {totalMovements} movimiento(s) involucrado(s), " +
            $"entre {effectiveFrom:dd/MM/yyyy} y {effectiveTo:dd/MM/yyyy}:");
        sb.AppendLine();

        var groupIndex = 0;
        foreach (var group in groups)
        {
            groupIndex++;
            sb.AppendLine($"Grupo {groupIndex}");
            sb.AppendLine($"- Tipo de sospecha: {group.Reason}");
            sb.AppendLine($"- Motivo de sospecha: {group.Description}");
            sb.AppendLine($"- Tamaño del grupo: {group.Movements.Count}");
            // ISuspicionDetector no produce un score numérico -- solo pertenencia a un
            // grupo por Reason. No se inventa uno acá; "-" es el mismo valor que el
            // resto de las tools usa para "campo sin dato".
            sb.AppendLine("- Score o severidad: -");
            sb.AppendLine();

            var movementIndex = 0;
            foreach (var m in group.Movements)
            {
                movementIndex++;
                sb.AppendLine($"  Movimiento {movementIndex}");
                sb.AppendLine($"  - Id: {m.SourceId}");
                sb.AppendLine(
                    $"  - Cuenta: {(m.FinancialAccountId is { } accId ? accountNames.GetValueOrDefault(accId, "(desconocida)") : "(sin asignar)")}");
                sb.AppendLine($"  - Fecha: {m.Date:yyyy-MM-dd}");
                sb.AppendLine($"  - Importe: {m.Amount:N2}");
                sb.AppendLine($"  - Moneda: {m.Currency}");
                sb.AppendLine($"  - Descripción: {m.Description}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Resolución en bloque (1 query, nunca una por fila) -- mismo criterio que ya usa
    // MovementTools.ResolveNamesAsync para Category/Counterparty.
    private async Task<Dictionary<Guid, string>> ResolveAccountNamesAsync(
        IReadOnlyList<SuspiciousGroup> groups, CancellationToken ct)
    {
        var accountIds = groups
            .SelectMany(g => g.Movements)
            .Where(m => m.FinancialAccountId is not null)
            .Select(m => m.FinancialAccountId!.Value)
            .Distinct()
            .ToList();

        if (accountIds.Count == 0) return new Dictionary<Guid, string>();

        return await _db.FinancialAccounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
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
