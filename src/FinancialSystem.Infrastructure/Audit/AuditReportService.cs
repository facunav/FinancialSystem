using System.Diagnostics;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Review;
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
        var computed = await ComputeFlaggedMovementsAsync(from, to, financialAccountId, ct);
        return FormatMisclassifiedMovementsReport(computed, from, to);
    }

    // Extraído de BuildMisclassifiedMovementsReportAsync (PATCH-041): formateo puro, sin
    // I/O -- separado para que BuildFullAuditReportAsync pueda reutilizar un
    // FlaggedMovementsResult ya calculado en vez de volver a llamar a
    // ComputeFlaggedMovementsAsync (ver comentario en la sobrecarga de ese método que
    // recibe los movimientos ya cargados).
    private static string FormatMisclassifiedMovementsReport(
        FlaggedMovementsResult computed, DateOnly from, DateOnly to)
    {
        if (computed.ClassifiedCount == 0)
            return $"No hay movimientos clasificados entre {from:dd/MM/yyyy} y " +
                   $"{to:dd/MM/yyyy} para analizar.";

        if (computed.Flagged.Count == 0)
            return $"No se encontraron movimientos potencialmente mal clasificados entre " +
                   $"{from:dd/MM/yyyy} y {to:dd/MM/yyyy}.";

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{computed.Flagged.Count} movimiento(s) potencialmente mal clasificado(s) entre " +
            $"{from:dd/MM/yyyy} y {to:dd/MM/yyyy}:");
        sb.AppendLine();

        var index = 0;
        foreach (var (m, motivos) in computed.Flagged)
        {
            index++;
            sb.AppendLine($"Movimiento {index}");
            sb.AppendLine($"- Id: {m.SourceId}");
            sb.AppendLine($"- Fecha: {m.Date:yyyy-MM-dd}");
            sb.AppendLine(
                $"- Cuenta: {(m.FinancialAccountId is { } accId ? computed.AccountNames.GetValueOrDefault(accId, "(desconocida)") : "(sin asignar)")}");
            sb.AppendLine($"- Descripción: {m.Description}");
            sb.AppendLine($"- Importe: {m.Amount:N2}");
            sb.AppendLine($"- Moneda: {m.Currency}");
            sb.AppendLine($"- Categoría actual: {computed.CategoryNames.GetValueOrDefault(m.CategoryId!.Value, "(no resuelve)")}");
            sb.AppendLine(
                $"- Contraparte actual: {(m.CounterpartyId is { } cpId ? computed.CounterpartyNames.GetValueOrDefault(cpId, "(desconocida)") : "-")}");
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

    /// <summary>
    /// Versión estructurada de <see cref="BuildMisclassifiedMovementsReportAsync"/>, para
    /// audit.html (Centro de Auditoría) -- mismo cálculo exacto
    /// (<see cref="ComputeFlaggedMovementsAsync(DateOnly, DateOnly, Guid?, CancellationToken)"/>,
    /// compartido con la versión de texto que sigue usando la tool MCP AuditDatabase), sin
    /// reformatear nada a texto. Agrega el estado de revisión humana (MovementAuditDecision) por
    /// movimiento -- no filtra a los revisados, los sigue devolviendo (ver MovementAuditDecision:
    /// "no oculta el hallazgo").
    /// </summary>
    public async Task<IReadOnlyList<MisclassifiedMovement>> GetMisclassifiedMovementsAsync(
        DateOnly from, DateOnly to, Guid? financialAccountId, CancellationToken ct = default)
    {
        var computed = await ComputeFlaggedMovementsAsync(from, to, financialAccountId, ct);
        return await BuildMisclassifiedMovementsAsync(computed, ct);
    }

    // Extraído de GetMisclassifiedMovementsAsync (PATCH-041) por el mismo motivo que
    // FormatMisclassifiedMovementsReport: permitir que BuildFullAuditReportAsync
    // reutilice un FlaggedMovementsResult ya calculado en vez de recalcularlo.
    private async Task<IReadOnlyList<MisclassifiedMovement>> BuildMisclassifiedMovementsAsync(
        FlaggedMovementsResult computed, CancellationToken ct)
    {
        if (computed.Flagged.Count == 0) return [];

        var sourceIds = computed.Flagged.Select(f => f.Movement.SourceId).ToList();
        var reviews = await _db.MovementAuditDecisions
            .AsNoTracking()
            .Where(r => sourceIds.Contains(r.SourceId))
            .ToDictionaryAsync(r => r.SourceId, r => r.ReviewedAtUtc, ct);

        return computed.Flagged.Select(f =>
        {
            var m = f.Movement;
            var reviewed = reviews.TryGetValue(m.SourceId, out var reviewedAtUtc);

            return new MisclassifiedMovement(
                m.Source.ToSourceEntityType(),
                m.SourceId,
                m.Date,
                m.Description,
                computed.CategoryNames.GetValueOrDefault(m.CategoryId!.Value, "(no resuelve)"),
                m.CounterpartyId is { } cpId ? computed.CounterpartyNames.GetValueOrDefault(cpId, "(desconocida)") : "-",
                m.MovementType?.ToString() ?? "-",
                m.FinancialImpact?.ToString() ?? "-",
                f.Motivos.Select(motivo => new MisclassifiedMotivo(
                    motivo.Dimension, motivo.ValorActual, motivo.ValorSugerido, motivo.MatchCount, motivo.WinnerCount)).ToList(),
                reviewed,
                reviewed ? reviewedAtUtc : null);
        }).ToList();
    }

    private readonly record struct FlaggedMovementsResult(
        int ClassifiedCount,
        IReadOnlyList<(MovementView Movement, List<Motivo> Motivos)> Flagged,
        Dictionary<Guid, string> CategoryNames,
        Dictionary<Guid, string> CounterpartyNames,
        Dictionary<Guid, string> AccountNames);

    private async Task<FlaggedMovementsResult> ComputeFlaggedMovementsAsync(
        DateOnly from, DateOnly to, Guid? financialAccountId, CancellationToken ct)
    {
        var movements = await _movementsQuery.GetAsync(from, to, financialAccountId, search: null, ct);
        return await ComputeFlaggedMovementsAsync(movements, ct);
    }

    // Sobrecarga (PATCH-041) que recibe los movimientos ya cargados, para
    // BuildFullAuditReportAsync: antes, ese método llamaba a
    // BuildMisclassifiedMovementsReportAsync y a GetMisclassifiedMovementsAsync, y cada
    // uno de los dos volvía a pedirle los movimientos a IMovementsQueryService (mismo
    // from/to, financialAccountId siempre null en ese método) y a recorrer todo este
    // cálculo desde cero -- sugerencias vía IClassificationSuggestionService, defaults
    // de contraparte, nombres de categoría/contraparte/cuenta -- dos veces por
    // auditoría completa, sobre exactamente los mismos datos. Ahora
    // BuildFullAuditReportAsync llama a esta sobrecarga una sola vez, con los
    // movimientos que ya cargó para su propio resumen, y reutiliza el resultado tanto
    // para el texto (FormatMisclassifiedMovementsReport) como para la lista
    // estructurada (BuildMisclassifiedMovementsAsync). La sobrecarga de arriba (con
    // from/to/financialAccountId) se mantiene intacta para cuando
    // BuildMisclassifiedMovementsReportAsync/GetMisclassifiedMovementsAsync se llaman
    // de forma independiente (ej. desde AuditTools/AuditDatabaseTools, con su propio
    // financialAccountId) -- ningún llamador externo ni comportamiento público cambia.
    private async Task<FlaggedMovementsResult> ComputeFlaggedMovementsAsync(
        IReadOnlyList<MovementView> movements, CancellationToken ct)
    {
        var classified = movements.Where(m => m.Status is not null).ToList();

        if (classified.Count == 0)
            return new FlaggedMovementsResult(0, [], [], [], []);

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

        return new FlaggedMovementsResult(classified.Count, flagged, categoryNames, counterpartyNames, accountNames);
    }

    // ── Comparación de un único movimiento contra la sugerencia vigente (PATCH-0106) ──
    // Reutiliza exactamente el mismo cálculo que ComputeFlaggedMovementsAsync
    // (ToFinancialMovement, IClassificationSuggestionService.SuggestAsync,
    // Counterparty.Default*, BuildSuggestionMotivos, BuildDefaultMotivos) -- sin
    // copiar ninguno de esos métodos -- pero sin cargar un rango de fechas completo:
    // SuggestAsync se llama con una lista de un único movimiento, y los defaults de
    // contraparte se resuelven con una consulta puntual (una fila) en vez del
    // diccionario por lote que usa el camino de auditoría por rango.
    //
    // Consumido por MovementTools.ExplainClassification (hosts/FinancialSystem.McpServer) --
    // ComputeFlaggedMovementsAsync/BuildMisclassifiedMovementsReportAsync/
    // GetMisclassifiedMovementsAsync/BuildFullAuditReportAsync no se tocan, mismo
    // comportamiento exacto que antes de este método.

    /// <summary>
    /// Compara la clasificación actual de un único movimiento (<paramref name="movement"/>)
    /// contra lo que <see cref="IClassificationSuggestionService"/> sugeriría hoy para esa
    /// misma descripción, más los valores por defecto de su <c>Counterparty</c> si tiene
    /// una asignada -- la misma interpretación que ya usa Auditoría
    /// (<see cref="ComputeFlaggedMovementsAsync(IReadOnlyList{MovementView}, CancellationToken)"/>),
    /// aplicada a un solo movimiento en vez de un lote por rango de fechas.
    ///
    /// Precondición: <paramref name="movement"/> debe representar un movimiento ya
    /// clasificado (<c>Status</c>/<c>CategoryId</c>/<c>MovementType</c>/<c>FinancialImpact</c>
    /// no nulos) -- mismo requisito que <c>ComputeFlaggedMovementsAsync</c> ya exige
    /// internamente antes de tocar las 4 dimensiones (ahí filtrando
    /// <c>movements.Where(m =&gt; m.Status is not null)</c> antes de llegar a este cálculo).
    /// El llamador es responsable de no invocar este método para un movimiento pendiente.
    /// </summary>
    public async Task<ClassificationComparisonResult> ExplainCurrentClassificationAsync(
        MovementView movement, CancellationToken ct = default)
    {
        var financialMovement = ToFinancialMovement(movement);
        var suggestionSets = await _suggestionService.SuggestAsync([financialMovement], ct);
        var suggestions = suggestionSets.Count > 0
            ? suggestionSets[0].Suggestions
            : (IReadOnlyList<ClassificationSuggestion>)[];

        CounterpartyDefaults? defaults = null;
        if (movement.CounterpartyId is { } counterpartyId)
        {
            defaults = await _db.Counterparties
                .AsNoTracking()
                .Where(c => c.Id == counterpartyId)
                .Select(c => new CounterpartyDefaults(
                    c.Id, c.DefaultCategoryId, c.DefaultMovementType, c.DefaultFinancialImpact))
                .FirstOrDefaultAsync(ct);
        }

        var (categoryNames, counterpartyNames) = await ResolveComparisonNamesAsync(movement, suggestions, defaults, ct);

        var motivos = new List<Motivo>();
        if (suggestions.Count > 0)
            motivos.AddRange(BuildSuggestionMotivos(movement, suggestions, categoryNames, counterpartyNames));

        if (defaults is not null)
            motivos.AddRange(BuildDefaultMotivos(movement, defaults, categoryNames));

        var hasSuggestion = suggestions.Count > 0 || HasAnyDefault(defaults);

        return new ClassificationComparisonResult(hasSuggestion, motivos);
    }

    private static bool HasAnyDefault(CounterpartyDefaults? defaults) =>
        defaults is not null &&
        (defaults.DefaultCategoryId is not null
            || defaults.DefaultMovementType is not null
            || defaults.DefaultFinancialImpact is not null);

    // Mismo criterio que ComputeFlaggedMovementsAsync (categoryIds/counterpartyIdsForNames),
    // acotado a los Ids que puede necesitar UN movimiento en vez de un lote completo:
    // valor actual, valor(es) sugerido(s) por historial, valor por defecto de contraparte.
    private async Task<(Dictionary<Guid, string> CategoryNames, Dictionary<Guid, string> CounterpartyNames)> ResolveComparisonNamesAsync(
        MovementView movement,
        IReadOnlyList<ClassificationSuggestion> suggestions,
        CounterpartyDefaults? defaults,
        CancellationToken ct)
    {
        var categoryIds = new List<Guid>();
        if (movement.CategoryId is { } currentCategoryId) categoryIds.Add(currentCategoryId);
        categoryIds.AddRange(suggestions
            .Where(s => s.Dimension == SuggestionDimension.Category)
            .Select(s => (Guid)s.Value));
        if (defaults?.DefaultCategoryId is { } defaultCategoryId) categoryIds.Add(defaultCategoryId);

        var counterpartyIds = new List<Guid>();
        if (movement.CounterpartyId is { } currentCounterpartyId) counterpartyIds.Add(currentCounterpartyId);
        counterpartyIds.AddRange(suggestions
            .Where(s => s.Dimension == SuggestionDimension.Counterparty)
            .Select(s => (Guid)s.Value));

        var categoryNames = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        var counterpartyNames = counterpartyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Counterparties.AsNoTracking()
                .Where(c => counterpartyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return (categoryNames, counterpartyNames);
    }

    // ── Reporte completo (ex AuditDatabaseTools.AuditDatabase) ───────────────────
    // from/to ya vienen resueltos (AuditDatabaseTools/el endpoint de la Api calculan
    // el período por defecto -- mes en curso -- antes de llamar acá; ninguno de los
    // dos tiene parámetros propios de rango).
    //
    // Devuelve FullAuditReport (no un string) desde el rediseño del Centro de
    // Auditoría (tablero de salud): AuditDatabaseTools.AuditDatabase (la tool MCP)
    // solo necesita el texto formateado -- FullAuditReport.ReportText es exactamente
    // ese mismo texto, sin cambios. audit.html necesita además los números
    // individuales (para distinguir "sin datos" de "correcta" sin adivinar
    // parseando texto) y los cuatro bloques de "Problemas encontrados" por separado
    // (para que cada categoría sea expandible) -- estos ya se calculaban acá adentro;
    // ahora se exponen en vez de descartarse una vez concatenados al texto final.

    public async Task<FullAuditReport> BuildFullAuditReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var movements = await _movementsQuery.GetAsync(from, to, financialAccountId: null, search: null, ct);
        var pending = movements.Where(m => m.Status is null).ToList();
        var classifiedCount = movements.Count - pending.Count;

        var suspiciousText = await BuildSuspiciousMovementsReportAsync(from, to, null, ct);
        var suspiciousGroupsCount = ParseLeadingCount(suspiciousText);

        // PATCH-041: antes acá se llamaba a BuildMisclassifiedMovementsReportAsync y a
        // GetMisclassifiedMovementsAsync por separado -- cada uno volvía a pedir los
        // movimientos y a recalcular sugerencias/nombres desde cero (ver el comentario
        // en la sobrecarga de ComputeFlaggedMovementsAsync que recibe `movements`).
        // Ahora se calcula una sola vez, reutilizando los `movements` ya cargados
        // arriba, y se deriva de ahí tanto el texto como la lista estructurada.
        var flaggedMovements = await ComputeFlaggedMovementsAsync(movements, ct);
        var misclassifiedText = FormatMisclassifiedMovementsReport(flaggedMovements, from, to);

        // Misclassified* separa lo detectado por el sistema de lo que sigue pendiente de
        // revisión humana (MovementAuditDecision) -- ver GetMisclassifiedMovementsAsync. El
        // conteo que participa de "Problemas encontrados"/Conclusión/Estado de la
        // auditoría es el pendiente, no el detectado: un movimiento revisado deja de
        // sumar como problema activo, pero sigue existiendo (misclassifiedDetectedCount).
        var misclassifiedMovements = await BuildMisclassifiedMovementsAsync(flaggedMovements, ct);
        var misclassifiedDetectedCount = misclassifiedMovements.Count;
        var misclassifiedReviewedCount = misclassifiedMovements.Count(m => m.Reviewed);
        var misclassifiedCount = misclassifiedDetectedCount - misclassifiedReviewedCount;

        var investigations = await _db.Investigations.AsNoTracking().ToListAsync(ct);
        var openInvestigations = investigations.Where(i => i.Status == InvestigationStatus.Open).ToList();
        var resolvedInvestigationsCount = investigations.Count(i => i.Status == InvestigationStatus.Resolved);

        var misclassifiedBlock = misclassifiedCount > 0 ? misclassifiedText.TrimEnd() : "(ninguna)";
        var suspiciousBlock = suspiciousGroupsCount > 0 ? suspiciousText.TrimEnd() : "(ninguno)";

        var pendingBlock = pending.Count == 0
            ? "(ninguno)"
            : string.Join(
                Environment.NewLine,
                pending.Select(m => $"- {m.SourceId} | {m.Date:yyyy-MM-dd} | {m.Description} | {m.Currency} {m.Amount:N2}"));

        var investigationsBlock = openInvestigations.Count == 0
            ? "(ninguna)"
            : string.Join(
                Environment.NewLine,
                openInvestigations.Select(i => $"- {i.Id} | {i.Question}"));

        var totalProblems = misclassifiedCount + suspiciousGroupsCount + pending.Count + openInvestigations.Count;

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
        sb.AppendLine(misclassifiedBlock);
        sb.AppendLine();

        sb.AppendLine("Grupos sospechosos");
        sb.AppendLine(suspiciousBlock);
        sb.AppendLine();

        sb.AppendLine("Pendientes");
        sb.AppendLine(pendingBlock);
        sb.AppendLine();

        sb.AppendLine("Investigaciones abiertas");
        sb.AppendLine(investigationsBlock);
        sb.AppendLine();

        sb.AppendLine("Conclusión");
        sb.AppendLine(totalProblems == 0
            ? "No se detectaron problemas."
            : $"Se detectaron {totalProblems} posibles problemas que requieren revisión.");

        stopwatch.Stop();

        return new FullAuditReport(
            from, to, movements.Count, pending.Count, classifiedCount, suspiciousGroupsCount,
            misclassifiedCount, openInvestigations.Count, resolvedInvestigationsCount, totalProblems,
            misclassifiedBlock, suspiciousBlock, pendingBlock, investigationsBlock, sb.ToString(),
            generatedAtUtc, stopwatch.ElapsedMilliseconds,
            misclassifiedDetectedCount, misclassifiedReviewedCount, misclassifiedMovements);
    }

    // ── Helpers (idénticos a los que tenía AuditTools.cs) ────────────────────────

    // PATCH-0106: pasa de private a public -- único cambio de visibilidad de este patch
    // -- para que sea el tipo de retorno de ClassificationComparisonResult.Motivos,
    // consumido desde hosts/FinancialSystem.McpServer (MovementTools.ExplainClassification).
    // Se mantiene anidado dentro de AuditReportService (no se mueve a nivel de namespace)
    // para minimizar el diff: ningún otro cambio de forma ni de contenido.
    public sealed record Motivo(
        string Origen,
        string Dimension,
        string ValorActual,
        string ValorSugerido,
        string? Confianza,
        int? MatchCount = null,
        int? WinnerCount = null);

    /// <summary>
    /// Resultado de <see cref="ExplainCurrentClassificationAsync"/> (PATCH-0106).
    /// <c>HasSuggestion</c> distingue "no hay nada con qué comparar" (ni historial ni
    /// default de contraparte -- <c>Motivos</c> vacío no dice por sí solo cuál de los
    /// dos casos es) de "hay sugerencia y coincide" (<c>HasSuggestion=true</c>,
    /// <c>Motivos</c> vacío) de "hay sugerencia y difiere en una o más dimensiones"
    /// (<c>HasSuggestion=true</c>, <c>Motivos</c> con uno o más elementos).
    /// </summary>
    public sealed record ClassificationComparisonResult(
        bool HasSuggestion,
        IReadOnlyList<Motivo> Motivos);

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
            if (s.Confidence == SuggestionConfidence.Low) continue;

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
                            s.Confidence.ToString(),
                            s.MatchCount,
                            s.WinnerCount));
                    break;

                case SuggestionDimension.MovementType:
                    var suggestedType = (MovementType)s.Value;
                    if (suggestedType != m.MovementType)
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Tipo",
                            m.MovementType?.ToString() ?? "-",
                            suggestedType.ToString(),
                            s.Confidence.ToString(),
                            s.MatchCount,
                            s.WinnerCount));
                    break;

                case SuggestionDimension.FinancialImpact:
                    var suggestedImpact = (FinancialImpact)s.Value;
                    if (suggestedImpact != m.FinancialImpact)
                        motivos.Add(new Motivo(
                            SuggestionOrigen,
                            "Impacto",
                            m.FinancialImpact?.ToString() ?? "-",
                            suggestedImpact.ToString(),
                            s.Confidence.ToString(),
                            s.MatchCount,
                            s.WinnerCount));
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
                            s.Confidence.ToString(),
                            s.MatchCount,
                            s.WinnerCount));
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

/// <summary>
/// Resultado de <see cref="AuditReportService.BuildFullAuditReportAsync"/>. Los
/// cuatro *Text son los mismos bloques que ya se concatenaban dentro de ReportText
/// bajo "Problemas encontrados" -- se exponen por separado para que audit.html pueda
/// mostrar cada categoría en su propia sección expandible, sin tener que volver a
/// buscar los encabezados de texto ("Grupos sospechosos", etc.) para partirlos.
/// ReportText es exactamente el texto que ya devolvía este método antes del
/// rediseño del Centro de Auditoría -- AuditDatabaseTools.AuditDatabase (la tool
/// MCP) sigue devolviendo eso mismo, sin cambios. GeneratedAtUtc/DurationMs son
/// puramente informativos para audit.html (cuándo se ejecutó, cuánto tardó) -- no
/// se persisten en ningún lado, se recalculan en cada ejecución.
///
/// Misclassified representa lo pendiente de revisión humana (MovementAuditDecision), no lo
/// detectado en total -- ver GetMisclassifiedMovementsAsync. MisclassifiedDetected es el
/// total (pendiente + revisado); MisclassifiedReviewed es el complemento. Ningún
/// movimiento revisado desaparece: solo deja de contar para TotalProblems/Conclusión.
/// </summary>
public sealed record FullAuditReport(
    DateOnly From,
    DateOnly To,
    int MovementsAnalyzed,
    int Pending,
    int Classified,
    int SuspiciousGroups,
    int Misclassified,
    int OpenInvestigations,
    int ResolvedInvestigations,
    int TotalProblems,
    string MisclassifiedText,
    string SuspiciousText,
    string PendingText,
    string InvestigationsText,
    string ReportText,
    DateTime GeneratedAtUtc,
    long DurationMs,
    int MisclassifiedDetected,
    int MisclassifiedReviewed,
    IReadOnlyList<MisclassifiedMovement> MisclassifiedMovements);

/// <summary>
/// Un movimiento marcado como potencialmente mal clasificado, para audit.html -- ver
/// AuditReportService.GetMisclassifiedMovementsAsync. Reviewed/ReviewedAtUtc reflejan
/// MovementAuditDecision: el movimiento sigue apareciendo aunque haya sido revisado.
/// </summary>
public sealed record MisclassifiedMovement(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    DateTime Date,
    string Description,
    string CurrentCategory,
    string CurrentCounterparty,
    string CurrentMovementType,
    string CurrentFinancialImpact,
    IReadOnlyList<MisclassifiedMotivo> Motivos,
    bool Reviewed,
    DateTime? ReviewedAtUtc);

/// <summary>
/// Un motivo de duda dentro de un <see cref="MisclassifiedMovement"/> -- una dimensión
/// (Categoría/Tipo/Impacto/Contraparte) donde el valor sugerido difiere del actual.
/// MatchCount/WinnerCount vienen de ClassificationSuggestion.MatchCount/WinnerCount
/// (null cuando el motivo salió de un default configurado en la Counterparty, no de un
/// conteo de historial).
/// </summary>
public sealed record MisclassifiedMotivo(
    string Dimension,
    string CurrentValue,
    string SuggestedValue,
    int? MatchCount,
    int? WinnerCount);
