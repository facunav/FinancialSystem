using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de investigación de movimientos financieros — Fase 1 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: igual que FinancialTools/SystemTools, este archivo no recalcula nada
/// por su cuenta. SearchMovements delega la carga de movimientos (pendientes +
/// clasificados, banco + tarjeta, con IReviewEngine/IClassificationSuggestionService
/// ya resueltos) a IMovementsQueryService — exactamente el mismo servicio que usa
/// GET /api/movements, que a su vez es lo que consume movements.html. Los filtros
/// que ese servicio no expone como parámetro (categoría, contraparte, tipo, impacto,
/// estado, moneda, importe) se aplican en memoria sobre el resultado ya calculado,
/// sobre campos que MovementView ya expone — no es una segunda implementación del
/// query, es un recorte del mismo resultado.
/// </summary>
[McpServerToolType]
public sealed class MovementTools
{
    // Mismo límite y misma razón que MovementsEndpoints.GetAll
    // (src/FinancialMcp.Api/Endpoints/MovementsEndpoints.cs): IMovementsQueryService
    // ejecuta IReviewEngine/ISuspicionDetector internamente, que compara movimientos
    // par a par dentro del período (O(N²) acotado) — acotar el rango evita que N
    // crezca sin límite. Esta tool usa el mismo servicio, así que hereda la misma
    // razón para el mismo límite.
    private const int MaxDateRangeDays = 90;

    private readonly IMovementsQueryService _movementsQuery;
    private readonly IApplicationDbContext _db;

    public MovementTools(IMovementsQueryService movementsQuery, IApplicationDbContext db)
    {
        _movementsQuery = movementsQuery;
        _db = db;
    }

    [McpServerTool]
    [Description(
        "Busca movimientos financieros (banco y tarjeta, pendientes y ya clasificados) dentro " +
        "de un período, con filtros opcionales. Reutiliza el mismo servicio que la pantalla " +
        "Movimientos -- no recalcula nada por su cuenta. Usar para investigar qué movimientos " +
        "componen un total, por qué uno no aparece, o para buscar por texto, categoría, " +
        "contraparte, tipo, impacto financiero, estado, moneda o rango de importe.")]
    public async Task<string> SearchMovements(
        [Description("Fecha de inicio (yyyy-MM-dd). Por defecto, el primer día del mes de 'to'.")]
        string? from,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to,
        [Description("Texto a buscar en la descripción del movimiento (contiene, sin distinguir mayúsculas).")]
        string? text,
        [Description("Id de Category (Category.Id) para filtrar. Solo aplica a movimientos ya clasificados.")]
        Guid? categoryId,
        [Description("Id de Counterparty (Counterparty.Id) para filtrar. Solo aplica a movimientos ya clasificados.")]
        Guid? counterpartyId,
        [Description("FinancialImpact: Expense, Income, InternalMovement o DebtPayment. Solo aplica a clasificados.")]
        string? financialImpact,
        [Description(
            "MovementType: Purchase, Transfer, Payment, Receipt, Fee, Interest, Refund, Adjustment " +
            "u Other. Solo aplica a movimientos ya clasificados.")]
        string? movementType,
        [Description("Estado: Pending (sin clasificar todavía), Reviewed o Confirmed.")]
        string? status,
        [Description("Moneda exacta, ej. ARS o USD.")]
        string? currency,
        [Description(
            "Importe mínimo. Convención de signo igual que el resto del sistema: positivo = " +
            "débito/gasto, negativo = crédito/ingreso.")]
        decimal? minAmount,
        [Description("Importe máximo, misma convención de signo que minAmount.")]
        decimal? maxAmount,
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

        FinancialImpact? parsedImpact = null;
        if (!string.IsNullOrWhiteSpace(financialImpact))
        {
            if (!Enum.TryParse(financialImpact, ignoreCase: true, out FinancialImpact impactValue))
                return $"Error: financialImpact inválido ('{financialImpact}'). " +
                       $"Valores válidos: {string.Join(", ", Enum.GetNames<FinancialImpact>())}.";
            parsedImpact = impactValue;
        }

        MovementType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(movementType))
        {
            if (!Enum.TryParse(movementType, ignoreCase: true, out MovementType typeValue))
                return $"Error: movementType inválido ('{movementType}'). " +
                       $"Valores válidos: {string.Join(", ", Enum.GetNames<MovementType>())}.";
            parsedType = typeValue;
        }

        var filterPending = false;
        ClassificationStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                filterPending = true;
            }
            else if (Enum.TryParse(status, ignoreCase: true, out ClassificationStatus statusValue))
            {
                parsedStatus = statusValue;
            }
            else
            {
                return $"Error: status inválido ('{status}'). " +
                       $"Valores válidos: Pending, {string.Join(", ", Enum.GetNames<ClassificationStatus>())}.";
            }
        }

        var movements = await _movementsQuery.GetAsync(
            effectiveFrom, effectiveTo, financialAccountId: null, search: text, ct);

        IEnumerable<MovementView> filtered = movements;
        if (categoryId is { } cid) filtered = filtered.Where(m => m.CategoryId == cid);
        if (counterpartyId is { } coid) filtered = filtered.Where(m => m.CounterpartyId == coid);
        if (parsedImpact is { } impact) filtered = filtered.Where(m => m.FinancialImpact == impact);
        if (parsedType is { } type) filtered = filtered.Where(m => m.MovementType == type);
        if (filterPending) filtered = filtered.Where(m => m.Status is null);
        if (parsedStatus is { } st) filtered = filtered.Where(m => m.Status == st);
        if (!string.IsNullOrWhiteSpace(currency))
            filtered = filtered.Where(m => string.Equals(m.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (minAmount is { } min) filtered = filtered.Where(m => m.Amount >= min);
        if (maxAmount is { } max) filtered = filtered.Where(m => m.Amount <= max);

        var results = filtered.OrderByDescending(m => m.Date).ToList();

        if (results.Count == 0)
            return $"No se encontraron movimientos entre {effectiveFrom:dd/MM/yyyy} y " +
                   $"{effectiveTo:dd/MM/yyyy} con esos filtros.";

        var (categoryNames, counterpartyNames) = await ResolveNamesAsync(results, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"{results.Count} movimiento(s) entre {effectiveFrom:dd/MM/yyyy} y {effectiveTo:dd/MM/yyyy}:");
        sb.AppendLine();

        foreach (var m in results)
        {
            sb.AppendLine($"Id: {m.SourceId}");
            sb.AppendLine($"  Fecha bancaria:      {m.Date:dd/MM/yyyy}");
            sb.AppendLine($"  Período financiero:  {(m.EffectiveDate is { } eff ? eff.ToString("dd/MM/yyyy") : "(sin clasificar)")}");
            sb.AppendLine($"  Descripción:         {m.Description}");
            sb.AppendLine($"  Importe:             {m.Currency} {m.Amount:N2}");
            sb.AppendLine($"  Categoría:           {(m.CategoryId is { } catId ? categoryNames.GetValueOrDefault(catId, "(desconocida)") : "(sin clasificar)")}");
            sb.AppendLine($"  Contraparte:         {(m.CounterpartyId is { } cpId ? counterpartyNames.GetValueOrDefault(cpId, "(desconocida)") : "-")}");
            sb.AppendLine($"  Tipo:                {(m.MovementType?.ToString() ?? "(sin clasificar)")}");
            sb.AppendLine($"  Impacto financiero:  {(m.FinancialImpact?.ToString() ?? "(sin clasificar)")}");
            sb.AppendLine($"  Estado:              {(m.Status?.ToString() ?? "Pendiente")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Resolución en bloque (2 queries, nunca una por fila) -- mismo criterio que ya
    // usa MovementsQueryService.LoadClassifiedAsync para resolver cuentas asignadas.
    // MovementView solo trae CategoryId/CounterpartyId (sin hidratar la entidad, ver
    // doc-comment de MovementView) -- acá se resuelve el nombre porque, a diferencia
    // de la pantalla Movimientos (que ya tiene los catálogos cargados en el cliente),
    // el resultado de esta tool tiene que ser legible por sí solo.
    private async Task<(Dictionary<Guid, string> Categories, Dictionary<Guid, string> Counterparties)> ResolveNamesAsync(
        List<MovementView> results, CancellationToken ct)
    {
        var categoryIds = results
            .Where(m => m.CategoryId is not null)
            .Select(m => m.CategoryId!.Value)
            .Distinct()
            .ToList();
        var counterpartyIds = results
            .Where(m => m.CounterpartyId is not null)
            .Select(m => m.CounterpartyId!.Value)
            .Distinct()
            .ToList();

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Categories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        var counterparties = counterpartyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Counterparties
                .AsNoTracking()
                .Where(c => counterpartyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return (categories, counterparties);
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
