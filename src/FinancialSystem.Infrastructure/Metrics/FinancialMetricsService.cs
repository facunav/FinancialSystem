using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Metrics;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Metrics;

internal sealed class FinancialMetricsService : IFinancialMetricsService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<FinancialMetricsService> _logger;

    public FinancialMetricsService(IApplicationDbContext db, ILogger<FinancialMetricsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── GetPeriodSummaryAsync ─────────────────────────────────────────────────

    public async Task<PeriodSummary> GetPeriodSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        // INSTRUMENTACIÓN TEMPORAL (dashboard-income-wrong-month): rango exacto
        // (con precisión de tick, formato "O") que se manda al filtro EF Core.
        _logger.LogInformation(
            "GetPeriodSummaryAsync: from={From} to={To} fromUtc={FromUtc:O} toUtc={ToUtc:O}",
            from, to, fromUtc, toUtc);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc && e.EffectiveDate <= toUtc)
            .Select(e => new RawRow(e.TotalAmount, e.FinancialImpact, e.Status, e.Currency))
            .ToListAsync(ct);

        // Diagnóstico de borde: trae (sin filtrar por el rango del summary) todo lo
        // que esté a +/-2 días de cada límite, para ver el EffectiveDate exacto que
        // Postgres devuelve y si cae de un lado u otro del corte -- incluye tanto lo
        // que matcheó como lo que quedó justo afuera, para comparar contra fromUtc/toUtc.
        var boundaryRows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e =>
                (e.EffectiveDate >= fromUtc.AddDays(-2) && e.EffectiveDate <= fromUtc.AddDays(2)) ||
                (e.EffectiveDate >= toUtc.AddDays(-2) && e.EffectiveDate <= toUtc.AddDays(2)))
            .Select(e => new { e.Id, e.EffectiveDate, e.FinancialImpact, e.TotalAmount })
            .ToListAsync(ct);

        foreach (var r in boundaryRows)
        {
            var includedInRange = r.EffectiveDate >= fromUtc && r.EffectiveDate <= toUtc;
            _logger.LogInformation(
                "GetPeriodSummaryAsync: boundary row Id={Id} EffectiveDate={EffectiveDate:O} " +
                "Impact={Impact} Amount={Amount} includedInRange={Included}",
                r.Id, r.EffectiveDate, r.FinancialImpact, r.TotalAmount, includedInRange);
        }

        var summary = BuildSummary(from, to, rows);
        _logger.LogInformation(
            "GetPeriodSummaryAsync: result rowCount={RowCount} totalIncome={TotalIncome} totalExpenses={TotalExpenses}",
            rows.Count, summary.TotalIncome, summary.TotalExpenses);

        return summary;
    }

    // ── GetExpensesByCategoryAsync ────────────────────────────────────────────

    public async Task<IReadOnlyList<CategoryExpense>> GetExpensesByCategoryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        var grouped = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e =>
                e.EffectiveDate >= fromUtc &&
                e.EffectiveDate <= toUtc &&
                e.FinancialImpact == FinancialImpact.Expense)
            .GroupBy(e => new
            {
                e.CategoryId,
                Name = e.Category!.Name,
                DisplayName = e.Category!.DisplayName,
            })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Name,
                g.Key.DisplayName,
                Total = g.Sum(e => e.TotalAmount),
                Count = g.Count(),
            })
            .OrderByDescending(g => g.Total)
            .ToListAsync(ct);

        if (grouped.Count == 0) return [];

        var grandTotal = grouped.Sum(g => g.Total);

        return grouped
            .Select(g => new CategoryExpense(
                g.CategoryId, g.Name, g.DisplayName, g.Total, g.Count,
                grandTotal > 0 ? Math.Round(g.Total / grandTotal * 100, 1) : 0m))
            .ToList()
            .AsReadOnly();
    }

    // ── GetMonthlyTrendAsync ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<MonthlyTrendPoint>> GetMonthlyTrendAsync(
        int months, CancellationToken ct = default)
    {
        if (months <= 0 || months > 36) months = 6;

        var cutoff = DateTime.UtcNow.AddMonths(-months + 1);
        var fromUtc = new DateTime(cutoff.Year, cutoff.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc)
            .Select(e => new { e.EffectiveDate.Year, e.EffectiveDate.Month, e.TotalAmount, e.FinancialImpact })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.Year, r.Month })
            .Select(g =>
            {
                var expenses = g.Where(r => r.FinancialImpact == FinancialImpact.Expense).Sum(r => r.TotalAmount);
                var income = g.Where(r => r.FinancialImpact == FinancialImpact.Income).Sum(r => r.TotalAmount);
                var net = income - expenses;
                var savings = income > 0 ? Math.Round((double)(net / income) * 100, 1) : 0.0;
                return new MonthlyTrendPoint(
                    g.Key.Year, g.Key.Month,
                    MonthLabel(g.Key.Year, g.Key.Month),
                    expenses, income, net, (decimal)savings);
            })
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList()
            .AsReadOnly();
    }

    // ── CompareWithPreviousMonthAsync ─────────────────────────────────────────

    public async Task<MonthComparison> CompareWithPreviousMonthAsync(
        int year, int month, CancellationToken ct = default)
    {
        var currentFrom = new DateOnly(year, month, 1);
        var currentTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var prevDate = currentFrom.AddMonths(-1);
        var prevFrom = new DateOnly(prevDate.Year, prevDate.Month, 1);
        var prevTo = new DateOnly(prevDate.Year, prevDate.Month,
                              DateTime.DaysInMonth(prevDate.Year, prevDate.Month));

        var (fromUtc, toUtc) = ToUtcRange(prevFrom, currentTo);

        var rows = await _db.ClassifiedMovements
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= fromUtc && e.EffectiveDate <= toUtc)
            .Select(e => new CompareRow(
                e.EffectiveDate, e.TotalAmount, e.FinancialImpact, e.Status, e.Currency,
                e.Category!.DisplayName, e.CategoryId))
            .ToListAsync(ct);

        var currentFromUtc = currentFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var currentRows = rows.Where(r => r.Date >= currentFromUtc).ToList();
        var prevRows = rows.Where(r => r.Date < currentFromUtc).ToList();

        var currentSummary = BuildSummary(currentFrom, currentTo,
            currentRows.Select(r => new RawRow(r.Amount, r.Impact, r.Status, r.Currency)).ToList());
        var previousSummary = prevRows.Count > 0
            ? BuildSummary(prevFrom, prevTo,
                prevRows.Select(r => new RawRow(r.Amount, r.Impact, r.Status, r.Currency)).ToList())
            : (PeriodSummary?)null;

        var expVariation = currentSummary.TotalExpenses - (previousSummary?.TotalExpenses ?? 0m);
        var prevExp = previousSummary?.TotalExpenses ?? 0m;
        var expVariationPct = prevExp > 0 ? Math.Round((double)(expVariation / prevExp) * 100, 1) : 0.0;

        var currByCat = currentRows
            .Where(r => r.Impact == FinancialImpact.Expense)
            .GroupBy(r => new { r.CategoryId, r.CategoryDisplay })
            .ToDictionary(g => g.Key.CategoryId, g => (g.Key.CategoryDisplay, g.Sum(r => r.Amount)));

        var prevByCat = prevRows
            .Where(r => r.Impact == FinancialImpact.Expense)
            .GroupBy(r => new { r.CategoryId, r.CategoryDisplay })
            .ToDictionary(g => g.Key.CategoryId, g => (g.Key.CategoryDisplay, g.Sum(r => r.Amount)));

        var allCats = currByCat.Keys.Union(prevByCat.Keys).ToList();
        var variations = allCats.Select(id =>
        {
            var name = currByCat.TryGetValue(id, out var c)
                ? c.Item1
                : prevByCat.TryGetValue(id, out var p) ? p.Item1 : "?";
            var curr = currByCat.TryGetValue(id, out var cv) ? cv.Item2 : 0m;
            var prev = prevByCat.TryGetValue(id, out var pv) ? pv.Item2 : 0m;
            var variation = curr - prev;
            var pct = prev > 0 ? Math.Round((double)(variation / prev) * 100, 1) : 0.0;
            return new CategoryVariation(name, curr, prev, variation, pct);
        })
        .OrderByDescending(v => Math.Abs(v.Variation))
        .ToList();

        return new MonthComparison(
            currentSummary, previousSummary, expVariation, expVariationPct,
            variations.AsReadOnly());
    }

    // ── GetClassificationCoverageAsync ────────────────────────────────────────
    // Patch 0072 (PATCH-019), Épica L: resuelve la cobertura con COUNT directos sobre
    // la base -- sin traer un solo movimiento a memoria -- reemplazando la versión
    // original del Patch 0071, que reutilizaba IMovementsQueryService.GetAsync
    // (materializaba el período completo, motor de sospechosos y sugerencias de
    // clasificación incluidos, solo para contar).
    //
    // Mismo criterio de "clasificado"/"pendiente" que ya usa el resto del sistema, sin
    // introducir una definición alternativa (idéntico al que aplican MovementLoader y
    // MovementsQueryService, sin cambios en ninguna de las dos):
    //   - Pendiente: BankStatement/Transaction con Date en el período que NO tiene
    //     ningún ClassifiedMovementItem que lo referencie (ver MovementLoader.LoadAsync)
    //     Y que tampoco es miembro de un MovementIdentityLink cuyo IdentityGroupId ya
    //     tiene otro miembro clasificado -- ver excepción DEDUPE-017 más abajo.
    //   - Clasificado: ClassifiedMovementItem (de BankStatement o Transaction) con
    //     OriginalDate en el período (ver MovementsQueryService.LoadClassifiedAsync).
    //
    // EXCEPCIÓN DEDUPE-017 (ver claude/AUDITORIA-DEDUPE017-METRICS.md, conclusión B):
    // desde DEDUPE-017, ClassifyMovementHandler impide clasificar un miembro de un
    // IdentityGroupId (MovementIdentityLink) cuando otro miembro del mismo grupo ya
    // tiene su propio ClassifiedMovementItem -- ambas filas físicas son la misma
    // identidad económica real. Sin este ajuste, esos hermanos quedan sin
    // ClassifiedMovementItem para siempre (no es que falte clasificarlos: está
    // prohibido clasificarlos) y el criterio de "pendiente" de arriba los seguiría
    // contando como pendientes indefinidamente, sin que la cobertura pueda llegar
    // nunca a 100% para un período con grupos deduplicados ya resueltos.
    // La unidad de cobertura pasa a ser la IDENTIDAD ECONÓMICA cuando existe un
    // MovementIdentityLink: un grupo se considera cubierto (no pendiente) en cuanto
    // CUALQUIERA de sus miembros físicos tiene un ClassifiedMovementItem propio, sin
    // importar cuántos otros miembros del grupo sigan sin el suyo. Una fila física sin
    // MovementIdentityLink se comporta exactamente igual que antes de este cambio.
    // No se crea ni se borra ningún ClassifiedMovementItem ni MovementIdentityLink acá
    // -- esto es puramente una corrección de conteo de lectura.
    //
    // Consultas: las 3 de siempre (pendientes de banco, pendientes de tarjeta,
    // clasificados) más, ÚNICAMENTE cuando alguna fila pendiente tiene
    // MovementIdentityLink (caso raro -- la mayoría de los períodos no tiene grupos
    // deduplicados), 2 consultas adicionales acotadas a esos grupos puntuales para
    // determinar cuáles ya tienen un miembro clasificado -- ver
    // CountPendingCoveredByClassifiedSiblingAsync. Todas secuenciales, mismo motivo ya
    // documentado arriba (un solo IApplicationDbContext).

    public async Task<ClassificationCoverage> GetClassificationCoverageAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        var classifiedBankStatementIds = _db.ClassifiedMovementItems
            .Where(i => i.SourceEntityType == SourceEntityType.BankStatement)
            .Select(i => i.SourceId);
        var classifiedTransactionIds = _db.ClassifiedMovementItems
            .Where(i => i.SourceEntityType == SourceEntityType.Transaction)
            .Select(i => i.SourceId);

        var pendingBankStatementIds = await _db.BankStatements
            .AsNoTracking()
            .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
            .Where(b => !classifiedBankStatementIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(ct);

        var pendingTransactionIds = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
            .Where(t => !classifiedTransactionIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);

        var classified = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Where(i => i.SourceEntityType == SourceEntityType.BankStatement
                     || i.SourceEntityType == SourceEntityType.Transaction)
            .Where(i => i.OriginalDate >= fromUtc && i.OriginalDate <= toUtc)
            .CountAsync(ct);

        var coveredBySiblingCount = await CountPendingCoveredByClassifiedSiblingAsync(
            pendingBankStatementIds, pendingTransactionIds, ct);

        var pending = pendingBankStatementIds.Count + pendingTransactionIds.Count - coveredBySiblingCount;
        var total = classified + pending;
        var coveragePercentage = total > 0
            ? Math.Round((decimal)classified / total * 100, 1)
            : 0m;

        return new ClassificationCoverage(from, to, total, classified, pending, coveragePercentage);
    }

    // DEDUPE-017 (ver comentario de GetClassificationCoverageAsync arriba): de las filas
    // pendientes recibidas, cuenta cuántas pertenecen a un IdentityGroupId
    // (MovementIdentityLink) que ya tiene, en CUALQUIER otro miembro del grupo (dentro o
    // fuera del período consultado -- el ClassifiedMovementItem del hermano puede tener
    // una OriginalDate distinta), un ClassifiedMovementItem propio. Esas filas dejan de
    // contarse como pendientes: su identidad económica ya está resuelta a través del
    // hermano. Salida temprana sin consultas adicionales cuando ninguna fila pendiente
    // tiene MovementIdentityLink -- el caso normal, sin grupos deduplicados en el período.
    private async Task<int> CountPendingCoveredByClassifiedSiblingAsync(
        List<Guid> pendingBankStatementIds,
        List<Guid> pendingTransactionIds,
        CancellationToken ct)
    {
        if (pendingBankStatementIds.Count == 0 && pendingTransactionIds.Count == 0)
            return 0;

        var pendingLinks = await _db.MovementIdentityLinks
            .AsNoTracking()
            .Where(l =>
                (l.SourceEntityType == SourceEntityType.BankStatement && pendingBankStatementIds.Contains(l.SourceId)) ||
                (l.SourceEntityType == SourceEntityType.Transaction && pendingTransactionIds.Contains(l.SourceId)))
            .Select(l => new { l.SourceEntityType, l.SourceId, l.IdentityGroupId })
            .ToListAsync(ct);

        if (pendingLinks.Count == 0)
            return 0;

        var groupIds = pendingLinks.Select(l => l.IdentityGroupId).Distinct().ToList();

        var groupMembers = await _db.MovementIdentityLinks
            .AsNoTracking()
            .Where(l => groupIds.Contains(l.IdentityGroupId))
            .Select(l => new { l.SourceEntityType, l.SourceId, l.IdentityGroupId })
            .ToListAsync(ct);

        var memberBankStatementIds = groupMembers
            .Where(m => m.SourceEntityType == SourceEntityType.BankStatement)
            .Select(m => m.SourceId)
            .ToList();
        var memberTransactionIds = groupMembers
            .Where(m => m.SourceEntityType == SourceEntityType.Transaction)
            .Select(m => m.SourceId)
            .ToList();

        var classifiedMemberKeys = await _db.ClassifiedMovementItems
            .AsNoTracking()
            .Where(i =>
                (i.SourceEntityType == SourceEntityType.BankStatement && memberBankStatementIds.Contains(i.SourceId)) ||
                (i.SourceEntityType == SourceEntityType.Transaction && memberTransactionIds.Contains(i.SourceId)))
            .Select(i => new { i.SourceEntityType, i.SourceId })
            .ToListAsync(ct);

        // Un grupo está cubierto en cuanto CUALQUIERA de sus miembros tiene un
        // ClassifiedMovementItem -- nunca puede ser la fila pendiente en cuestión
        // misma, porque por definición una fila pendiente no tiene su propio item.
        var classifiedKeySet = classifiedMemberKeys
            .Select(k => (k.SourceEntityType, k.SourceId))
            .ToHashSet();

        var coveredGroupIds = groupMembers
            .Where(m => classifiedKeySet.Contains((m.SourceEntityType, m.SourceId)))
            .Select(m => m.IdentityGroupId)
            .ToHashSet();

        return pendingLinks.Count(l => coveredGroupIds.Contains(l.IdentityGroupId));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DateTime fromUtc, DateTime toUtc) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    private static PeriodSummary BuildSummary(DateOnly from, DateOnly to, IReadOnlyList<RawRow> rows)
    {
        var expenses = rows.Where(r => r.Impact == FinancialImpact.Expense).Sum(r => r.Amount);
        var income = rows.Where(r => r.Impact == FinancialImpact.Income).Sum(r => r.Amount);
        var net = income - expenses;
        var savings = income > 0 ? Math.Round((double)(net / income) * 100, 1) : 0.0;
        var currency = rows.Select(r => r.Currency).FirstOrDefault() ?? "ARS";
        return new PeriodSummary(from, to, income, expenses, net, (decimal)savings,
            rows.Count,
            rows.Count(r => r.Status == ClassificationStatus.Confirmed),
            rows.Count(r => r.Status == ClassificationStatus.Reviewed),
            currency);
    }

    private static string MonthLabel(int year, int month)
    {
        var months = new[] { "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic" };
        return $"{months[month - 1]} {year}";
    }

    private sealed record RawRow(decimal Amount, FinancialImpact Impact, ClassificationStatus Status, string Currency);
    private sealed record CompareRow(DateTime Date, decimal Amount, FinancialImpact Impact,
        ClassificationStatus Status, string Currency, string CategoryDisplay, Guid CategoryId);
}