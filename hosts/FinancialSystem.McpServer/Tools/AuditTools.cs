using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de auditoría — Fase 2 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: ninguna regla de detección nueva vive acá.
///
/// FindSuspiciousMovements expone exactamente lo que ya calcula ISuspicionDetector
/// (duplicados y splits), orquestado por IReviewEngine -- el mismo componente que ya
/// usa MovementsQueryService.LoadPendingWithWarningsAsync para la pantalla
/// Movimientos (K6). No dejó de tener sentido tras PR-L4: ese PR retiró únicamente
/// el motor de matching contra movimientos "Candidate" legacy (ver ReviewResult.cs),
/// que era un componente distinto orquestado por el mismo ReviewEngine --
/// ISuspicionDetector nunca dependió de esa segunda fuente y sigue activo y
/// vigente, documentado explícitamente como tal en ese mismo archivo.
///
/// FindMisclassifiedMovements expone dos señales objetivas que ya existen en el
/// dominio, sin agregar ninguna regla propia: (1) IClassificationSuggestionService
/// -- el mismo motor de sugerencias que ya usa la pantalla Movimientos para
/// pendientes, invocado acá sobre movimientos YA clasificados para ver si el
/// historial de descripción idéntica sugeriría algo distinto a lo que tienen; y
/// (2) comparación directa contra Counterparty.Default* (confirmado como señal de
/// negocio válida en ADR-003, hoy sin wiring en ninguna pantalla). Ninguna de las
/// dos es un motor nuevo: la primera es una llamada directa al servicio existente,
/// la segunda es una comparación de igualdad entre dos campos que el dominio ya
/// modela como "deberían coincidir".
///
/// Este archivo solo formatea esos resultados a datos estructurados.
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

    private readonly IReviewEngine _reviewEngine;
    private readonly IMovementsQueryService _movementsQuery;
    private readonly IClassificationSuggestionService _suggestionService;
    private readonly IApplicationDbContext _db;

    public AuditTools(
        IReviewEngine reviewEngine,
        IMovementsQueryService movementsQuery,
        IClassificationSuggestionService suggestionService,
        IApplicationDbContext db)
    {
        _reviewEngine = reviewEngine;
        _movementsQuery = movementsQuery;
        _suggestionService = suggestionService;
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
        string? from,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to,
        [Description("Id de FinancialAccount para filtrar. Mismo parámetro que ya usa SearchMovements.")]
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

        var movements = await _movementsQuery.GetAsync(
            effectiveFrom, effectiveTo, financialAccountId, search: null, ct);
        var classified = movements.Where(m => m.Status is not null).ToList();

        if (classified.Count == 0)
            return $"No hay movimientos clasificados entre {effectiveFrom:dd/MM/yyyy} y " +
                   $"{effectiveTo:dd/MM/yyyy} para analizar.";

        // Señal 1: mismo motor que ya usa la pantalla Movimientos para pendientes,
        // invocado acá sobre movimientos ya clasificados -- una sola llamada en lote,
        // igual que exige el contrato del servicio (ver doc-comment de SuggestAsync).
        var financialMovements = classified.Select(ToFinancialMovement).ToList();
        var suggestionSets = await _suggestionService.SuggestAsync(financialMovements, ct);
        var suggestionsBySourceId = suggestionSets.ToDictionary(s => s.SourceId, s => s.Suggestions);

        // Señal 2: Counterparty.Default* -- comparación directa, no vía SuggestAsync:
        // la heurística 2 del motor (EnrichWithCounterpartyDefaultsAsync) solo se
        // dispara cuando la heurística 1 (historial de descripción) ya sugirió esa
        // misma contraparte -- acá la contraparte ya está asignada de verdad, así que
        // depender de esa heurística encadenada perdería casos reales (descripción
        // sin historial, pero contraparte con defaults configurados que no coinciden).
        var counterpartyIds = classified
            .Where(m => m.CounterpartyId is not null)
            .Select(m => m.CounterpartyId!.Value)
            .Distinct()
            .ToList();
        var defaultsByCounterpartyId = counterpartyIds.Count == 0
            ? new Dictionary<Guid, CounterpartyDefaults>()
            : await _db.Counterparties
                .AsNoTracking()
                .Where(c => counterpartyIds.Contains(c.Id))
                .Select(c => new CounterpartyDefaults(
                    c.Id, c.DefaultCategoryId, c.DefaultMovementType, c.DefaultFinancialImpact))
                .ToDictionaryAsync(d => d.CounterpartyId, ct);

        // Nombres en bloque -- todo Guid de Category/Counterparty que podría aparecer en
        // la salida (actual, sugerido por el historial, o default de la contraparte),
        // resuelto de una vez por catálogo (3 queries fijas, nunca una por fila) --
        // mismo criterio que MovementTools.ResolveNamesAsync.
        var categoryIds = classified.Select(m => m.CategoryId!.Value)
            .Concat(suggestionSets.SelectMany(s => s.Suggestions)
                .Where(s => s.Dimension == SuggestionDimension.Category)
                .Select(s => (Guid)s.Value))
            .Concat(defaultsByCounterpartyId.Values
                .Where(d => d.DefaultCategoryId is not null)
                .Select(d => d.DefaultCategoryId!.Value))
            .Distinct()
            .ToList();
        var counterpartyIdsForNames = classified
            .Where(m => m.CounterpartyId is not null)
            .Select(m => m.CounterpartyId!.Value)
            .Concat(suggestionSets.SelectMany(s => s.Suggestions)
                .Where(s => s.Dimension == SuggestionDimension.Counterparty)
                .Select(s => (Guid)s.Value))
            .Distinct()
            .ToList();
        var accountIds = classified
            .Where(m => m.FinancialAccountId is not null)
            .Select(m => m.FinancialAccountId!.Value)
            .Distinct()
            .ToList();

        var categoryNames = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var counterpartyNames = counterpartyIdsForNames.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Counterparties.AsNoTracking()
                .Where(c => counterpartyIdsForNames.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.FinancialAccounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var flagged = new List<(MovementView Movement, List<Motivo> Motivos)>();
        foreach (var m in classified)
        {
            var motivos = new List<Motivo>();

            if (suggestionsBySourceId.TryGetValue(m.SourceId, out var suggestions))
                motivos.AddRange(BuildSuggestionMotivos(m, suggestions, categoryNames, counterpartyNames));

            if (m.CounterpartyId is { } counterpartyId
                && defaultsByCounterpartyId.TryGetValue(counterpartyId, out var defaults))
                motivos.AddRange(BuildDefaultMotivos(m, defaults, categoryNames));

            if (motivos.Count > 0)
                flagged.Add((m, motivos));
        }

        if (flagged.Count == 0)
            return $"No se encontraron movimientos potencialmente mal clasificados entre " +
                   $"{effectiveFrom:dd/MM/yyyy} y {effectiveTo:dd/MM/yyyy}.";

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{flagged.Count} movimiento(s) potencialmente mal clasificado(s) entre " +
            $"{effectiveFrom:dd/MM/yyyy} y {effectiveTo:dd/MM/yyyy}:");
        sb.AppendLine();

        var index = 0;
        foreach (var (m, motivos) in flagged)
        {
            index++;
            sb.AppendLine($"Movimiento {index}");
            sb.AppendLine($"- Id: {m.SourceId}");
            sb.AppendLine($"- Fecha: {m.Date:yyyy-MM-dd}");
            sb.AppendLine(
                $"- Cuenta: {(m.FinancialAccountId is { } accId ? accountNames.GetValueOrDefault(accId, "(desconocida)") : "(sin asignar)")}");
            sb.AppendLine($"- Descripción: {m.Description}");
            sb.AppendLine($"- Importe: {m.Amount:N2}");
            sb.AppendLine($"- Moneda: {m.Currency}");
            sb.AppendLine($"- Categoría actual: {categoryNames.GetValueOrDefault(m.CategoryId!.Value, "(no resuelve)")}");
            sb.AppendLine(
                $"- Contraparte actual: {(m.CounterpartyId is { } cpId ? counterpartyNames.GetValueOrDefault(cpId, "(desconocida)") : "-")}");
            sb.AppendLine($"- Tipo actual: {m.MovementType?.ToString() ?? "-"}");
            sb.AppendLine($"- Impacto actual: {m.FinancialImpact?.ToString() ?? "-"}");
            sb.AppendLine("- Motivos encontrados:");

            var motivoIndex = 0;
            foreach (var motivo in motivos)
            {
                motivoIndex++;
                sb.AppendLine($"  Motivo {motivoIndex}");
                sb.AppendLine($"  - Origen: {motivo.Origen}");
                sb.AppendLine($"  - Dimensión: {motivo.Dimension}");
                sb.AppendLine($"  - Valor actual: {motivo.ValorActual}");
                sb.AppendLine($"  - Valor sugerido: {motivo.ValorSugerido}");
                // Confianza solo si el motor que produjo esta señal la calcula
                // (IClassificationSuggestionService la expone siempre en sus
                // resultados; la comparación contra Counterparty.Default* no tiene
                // ninguna noción de confianza -- se omite la línea, no se inventa un
                // valor).
                if (motivo.Confianza is not null)
                    sb.AppendLine($"  - Confianza: {motivo.Confianza}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Origen | Dimensión | valor actual | valor sugerido | confianza (null si el
    // origen de la señal no produce ninguna).
    private sealed record Motivo(string Origen, string Dimension, string ValorActual, string ValorSugerido, string? Confianza);

    // ── Señal 1: historial de descripción (IClassificationSuggestionService) ────

    private const string SuggestionOrigen = "Historial de descripción idéntica (IClassificationSuggestionService)";

    private static List<Motivo> BuildSuggestionMotivos(
        MovementView m,
        IReadOnlyList<ClassificationSuggestion> suggestions,
        Dictionary<Guid, string> categoryNames,
        Dictionary<Guid, string> counterpartyNames)
    {
        var motivos = new List<Motivo>();

        foreach (var s in suggestions)
        {
            switch (s.Dimension)
            {
                case SuggestionDimension.Category:
                    var suggestedCategoryId = (Guid)s.Value;
                    if (suggestedCategoryId != m.CategoryId)
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Categoría",
                            categoryNames.GetValueOrDefault(m.CategoryId!.Value, "(no resuelve)"),
                            categoryNames.GetValueOrDefault(suggestedCategoryId, "(desconocida)"),
                            s.Confidence.ToString()));
                    break;

                case SuggestionDimension.MovementType:
                    var suggestedType = (MovementType)s.Value;
                    if (suggestedType != m.MovementType)
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Tipo",
                            m.MovementType?.ToString() ?? "-",
                            suggestedType.ToString(),
                            s.Confidence.ToString()));
                    break;

                case SuggestionDimension.FinancialImpact:
                    var suggestedImpact = (FinancialImpact)s.Value;
                    if (suggestedImpact != m.FinancialImpact)
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Impacto",
                            m.FinancialImpact?.ToString() ?? "-",
                            suggestedImpact.ToString(),
                            s.Confidence.ToString()));
                    break;

                case SuggestionDimension.Counterparty:
                    var suggestedCounterpartyId = (Guid)s.Value;
                    if (suggestedCounterpartyId != m.CounterpartyId)
                    {
                        var actualName = m.CounterpartyId is { } cpId
                            ? counterpartyNames.GetValueOrDefault(cpId, "(desconocida)")
                            : "-";
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Contraparte",
                            actualName,
                            counterpartyNames.GetValueOrDefault(suggestedCounterpartyId, "(desconocida)"),
                            s.Confidence.ToString()));
                    }
                    break;
            }
        }

        return motivos;
    }

    // ── Señal 2: Counterparty.Default* (ADR-003) ─────────────────────────────
    // Sin Confianza: es una comparación de igualdad directa, no un motor con noción
    // de confianza propia -- Motivo.Confianza queda null en los tres casos.

    private const string CounterpartyDefaultOrigen = "Default configurado en la contraparte (Counterparty.Default*, ADR-003)";

    private static List<Motivo> BuildDefaultMotivos(
        MovementView m, CounterpartyDefaults defaults, Dictionary<Guid, string> categoryNames)
    {
        var motivos = new List<Motivo>();

        if (defaults.DefaultCategoryId is { } defaultCategoryId && defaultCategoryId != m.CategoryId)
            motivos.Add(new Motivo(
                CounterpartyDefaultOrigen,
                "Categoría",
                categoryNames.GetValueOrDefault(m.CategoryId!.Value, "(no resuelve)"),
                categoryNames.GetValueOrDefault(defaultCategoryId, "(desconocida)"),
                Confianza: null));

        if (defaults.DefaultMovementType is { } defaultMovementType && defaultMovementType != m.MovementType)
            motivos.Add(new Motivo(
                CounterpartyDefaultOrigen,
                "Tipo",
                m.MovementType?.ToString() ?? "-",
                defaultMovementType.ToString(),
                Confianza: null));

        if (defaults.DefaultFinancialImpact is { } defaultFinancialImpact && defaultFinancialImpact != m.FinancialImpact)
            motivos.Add(new Motivo(
                CounterpartyDefaultOrigen,
                "Impacto",
                m.FinancialImpact?.ToString() ?? "-",
                defaultFinancialImpact.ToString(),
                Confianza: null));

        return motivos;
    }

    private static FinancialMovement ToFinancialMovement(MovementView m) => new()
    {
        SourceId = m.SourceId,
        Date = m.Date,
        Description = m.Description,
        Amount = m.Amount,
        Currency = m.Currency,
        Source = m.Source,
        FinancialAccountId = m.FinancialAccountId,
        Merchant = m.Merchant,
        MerchantAtUtc = m.MerchantAtUtc,
    };

    // Record de clase (no struct) para mantener el mismo estilo que ClassifiedHistoryRow/
    // CounterpartyDefaultsRow en ClassificationSuggestionService.cs -- ambos son el mismo
    // tipo de proyección efímera de EF Core, sin necesidad de ser un value type acá.
    private sealed record CounterpartyDefaults(
        Guid CounterpartyId,
        Guid? DefaultCategoryId,
        MovementType? DefaultMovementType,
        FinancialImpact? DefaultFinancialImpact);

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
