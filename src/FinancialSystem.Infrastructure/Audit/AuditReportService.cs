using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Infrastructure.Audit;

/// <summary>
/// Orquestación de auditoría compartida entre FinancialSystem.McpServer (tools
/// AuditTools/AuditDatabaseTools) y FinancialMcp.Api (endpoints /api/audit/*, Centro
/// de Auditoría). Reubicada acá (0027) porque ningún proyecto host podía llamar al
/// otro directamente -- hosts/FinancialSystem.McpServer no lo referencia
/// FinancialMcp.Api ni viceversa, ambos sí referencian FinancialSystem.Infrastructure
/// -- y la tarea pedía explícitamente que la UI reutilizara "exactamente la misma
/// lógica" que ya usa AuditDatabase, no una reimplementación de sus reglas.
///
/// PRINCIPIO: cero reglas nuevas -- este archivo es el mismo código que ya vivía en
/// AuditTools.cs/AuditDatabaseTools.cs, movido tal cual (mismos servicios, misma
/// comparación, mismo texto de salida). AuditTools ahora solo valida los parámetros
/// string de la tool (from/to/rango máximo) y delega acá; AuditDatabaseTools ahora
/// solo calcula el período por defecto (sin parámetros propios) y delega acá.
/// </summary>
public sealed class AuditReportService
{
    private readonly IReviewEngine _reviewEngine;
    private readonly IMovementsQueryService _movementsQuery;
    private readonly IClassificationSuggestionService _suggestionService;
    private readonly IApplicationDbContext _db;

    public AuditReportService(
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

    // ── Grupos sospechosos (ex AuditTools.FindSuspiciousMovements) ──────────────
    // Idéntico al cuerpo que tenía la tool luego de validar from/to -- acá ya llegan
    // validados (from <= to, rango <= MaxDateRangeDays), esa validación de parámetros
    // de tool queda en AuditTools.cs, no es una regla de auditoría.

    public async Task<string> BuildSuspiciousMovementsReportAsync(
        DateOnly from, DateOnly to, Guid? financialAccountId, CancellationToken ct = default)
    {
        var result = await _reviewEngine.GenerateAsync(from, to, ct);

        var groups = result.Suspicious;
        if (financialAccountId is { } accountId)
            groups = groups.Where(g => g.Movements.Any(m => m.FinancialAccountId == accountId)).ToList();

        if (groups.Count == 0)
            return $"No se detectaron movimientos sospechosos entre {from:dd/MM/yyyy} " +
                   $"y {to:dd/MM/yyyy}.";

        var accountNames = await ResolveAccountNamesAsync(groups, ct);

        var sb = new StringBuilder();
        var totalMovements = groups.Sum(g => g.Movements.Count);
        sb.AppendLine(
            $"{groups.Count} grupo(s) sospechoso(s), {totalMovements} movimiento(s) involucrado(s), " +
            $"entre {from:dd/MM/yyyy} y {to:dd/MM/yyyy}:");
        sb.AppendLine();

        var groupIndex = 0;
        foreach (var group in groups)
        {
            groupIndex++;
            sb.AppendLine($"Grupo {groupIndex}");
            sb.AppendLine($"- Tipo de sospecha: {group.Reason}");
            sb.AppendLine($"- Motivo de sospecha: {group.Description}");
            sb.AppendLine($"- Tamaño del grupo: {group.Movements.Count}");
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

    // ── Clasificaciones dudosas (ex AuditTools.FindMisclassifiedMovements) ──────

    public async Task<string> BuildMisclassifiedMovementsReportAsync(
        DateOnly from, DateOnly to, Guid? financialAccountId, CancellationToken ct = default)
    {
        var movements = await _movementsQuery.GetAsync(from, to, financialAccountId, search: null, ct);
        var classified = movements.Where(m => m.Status is not null).ToList();

        if (classified.Count == 0)
            return $"No hay movimientos clasificados entre {from:dd/MM/yyyy} y " +
                   $"{to:dd/MM/yyyy} para analizar.";

        var financialMovements = classified.Select(ToFinancialMovement).ToList();
        var suggestionSets = await _suggestionService.SuggestAsync(financialMovements, ct);
        var suggestionsBySourceId = suggestionSets.ToDictionary(s => s.SourceId, s => s.Suggestions);

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
                   $"{from:dd/MM/yyyy} y {to:dd/MM/yyyy}.";

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{flagged.Count} movimiento(s) potencialmente mal clasificado(s) entre " +
            $"{from:dd/MM/yyyy} y {to:dd/MM/yyyy}:");
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
                if (motivo.Confianza is not null)
                    sb.AppendLine($"  - Confianza: {motivo.Confianza}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ── Reporte completo (ex AuditDatabaseTools.AuditDatabase) ───────────────────
    // from/to ya vienen resueltos (AuditDatabaseTools/el endpoint de la Api calculan
    // el período por defecto -- mes en curso -- antes de llamar acá; ninguno de los
    // dos tiene parámetros propios de rango).

    public async Task<string> BuildFullAuditReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var movements = await _movementsQuery.GetAsync(from, to, financialAccountId: null, search: null, ct);
        var pending = movements.Where(m => m.Status is null).ToList();
        var classifiedCount = movements.Count - pending.Count;

        var suspiciousText = await BuildSuspiciousMovementsReportAsync(from, to, null, ct);
        var misclassifiedText = await BuildMisclassifiedMovementsReportAsync(from, to, null, ct);
        var suspiciousGroupsCount = ParseLeadingCount(suspiciousText);
        var misclassifiedCount = ParseLeadingCount(misclassifiedText);

        var investigations = await _db.Investigations.AsNoTracking().ToListAsync(ct);
        var openInvestigations = investigations.Where(i => i.Status == InvestigationStatus.Open).ToList();
        var resolvedInvestigationsCount = investigations.Count(i => i.Status == InvestigationStatus.Resolved);

        var sb = new StringBuilder();

        sb.AppendLine("Resumen");
        sb.AppendLine($"Movimientos analizados: {movements.Count}");
        sb.AppendLine($"Pendientes: {pending.Count}");
        sb.AppendLine($"Clasificados: {classifiedCount}");
        sb.AppendLine($"Grupos sospechosos: {suspiciousGroupsCount}");
        sb.AppendLine($"Movimientos posiblemente mal clasificados: {misclassifiedCount}");
        sb.AppendLine($"Investigaciones abiertas: {openInvestigations.Count}");
        sb.AppendLine($"Investigaciones resueltas: {resolvedInvestigationsCount}");
        sb.AppendLine();

        sb.AppendLine("Problemas encontrados");
        sb.AppendLine();

        sb.AppendLine("Clasificaciones dudosas");
        sb.AppendLine(misclassifiedCount > 0 ? misclassifiedText.TrimEnd() : "(ninguna)");
        sb.AppendLine();

        sb.AppendLine("Grupos sospechosos");
        sb.AppendLine(suspiciousGroupsCount > 0 ? suspiciousText.TrimEnd() : "(ninguno)");
        sb.AppendLine();

        sb.AppendLine("Pendientes");
        if (pending.Count == 0)
        {
            sb.AppendLine("(ninguno)");
        }
        else
        {
            foreach (var m in pending)
                sb.AppendLine($"- {m.SourceId} | {m.Date:yyyy-MM-dd} | {m.Description} | {m.Currency} {m.Amount:N2}");
        }
        sb.AppendLine();

        sb.AppendLine("Investigaciones abiertas");
        if (openInvestigations.Count == 0)
        {
            sb.AppendLine("(ninguna)");
        }
        else
        {
            foreach (var investigation in openInvestigations)
                sb.AppendLine($"- {investigation.Id} | {investigation.Question}");
        }
        sb.AppendLine();

        var totalProblems = misclassifiedCount + suspiciousGroupsCount + pending.Count + openInvestigations.Count;
        sb.AppendLine("Conclusión");
        sb.AppendLine(totalProblems == 0
            ? "No se detectaron problemas."
            : $"Se detectaron {totalProblems} posibles problemas que requieren revisión.");

        return sb.ToString();
    }

    // ── Helpers (idénticos a los que tenía AuditTools.cs) ────────────────────────

    private sealed record Motivo(string Origen, string Dimension, string ValorActual, string ValorSugerido, string? Confianza);

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

    private sealed record CounterpartyDefaults(
        Guid CounterpartyId,
        Guid? DefaultCategoryId,
        MovementType? DefaultMovementType,
        FinancialImpact? DefaultFinancialImpact);

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

    // Lee el número al inicio de la primera línea que ya reportan
    // BuildSuspiciousMovementsReportAsync/BuildMisclassifiedMovementsReportAsync (ej.
    // "5 grupo(s)..."), 0 si el mensaje es el de "no se encontraron" (empieza con una
    // letra, no un dígito).
    private static int ParseLeadingCount(string text)
    {
        var span = text.AsSpan();
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
            i++;

        return i == 0 ? 0 : int.Parse(span[..i]);
    }
}
