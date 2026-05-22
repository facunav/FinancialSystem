using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Reconciliation;

/// <summary>
/// Punto de entrada para ejecutar una conciliación completa de un período.
///
/// RESPONSABILIDAD ÚNICA:
///   Carga los movimientos de referencia (Transactions) y los candidatos
///   (ManualExpenses) desde la DB, los convierte a FinancialMovement,
///   y construye el ReconciliationRequest para el motor.
///
/// NO hace matching. Eso es responsabilidad de IReconciliationEngine.
/// NO persiste el resultado. Eso será responsabilidad de una capa futura.
///
/// SEPARACIÓN DELIBERADA:
///   ReconciliationEngine recibe FinancialMovement[] ya construidos.
///   Este servicio es el único que sabe cómo obtenerlos desde la DB.
///   Así el motor permanece sin dependencias de infraestructura.
/// </summary>
public sealed class ReconciliationOrchestrator
{
    private readonly IApplicationDbContext _appDbContext;
    private readonly IManualExpenseRepository _manualExpenses;
    private readonly IReconciliationEngine _engine;
    private readonly ILogger<ReconciliationOrchestrator> _logger;

    public ReconciliationOrchestrator(
        IApplicationDbContext appDbContext,
        IManualExpenseRepository manualExpenses,
        IReconciliationEngine engine,
        ILogger<ReconciliationOrchestrator> logger)
    {
        _appDbContext = appDbContext;
        _manualExpenses = manualExpenses;
        _engine = engine;
        _logger = logger;
    }

    public async Task<ReconciliationResult> RunAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        ReconciliationOptions? options = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Cargando movimientos para conciliación: {Start} → {End}",
            periodStart, periodEnd);

        // ── Cargar referencias: Transactions del período ──────────
        var references = await LoadReferenceMovementsAsync(periodStart, periodEnd, ct);

        // ── Cargar candidatos: ManualExpenses del período ─────────
        var candidates = await LoadCandidateMovementsAsync(periodStart, periodEnd, ct);

        _logger.LogInformation(
            "Movimientos cargados: {RefCount} referencias (banco/tarjeta), {CandCount} candidatos (manuales)",
            references.Count, candidates.Count);

        // ── Construir request y delegar al motor ──────────────────
        var request = new ReconciliationRequest
        {
            ReferenceMovements = references,
            CandidateMovements = candidates,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Options = options,
        };

        return await _engine.ReconcileAsync(request, ct);
    }

    // ── Carga de referencias (banco + tarjeta) ────────────────────

    private async Task<IReadOnlyList<FinancialMovement>> LoadReferenceMovementsAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var transactions = await _appDbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);

        _logger.LogDebug(
            "Transactions cargadas del período: {Count}", transactions.Count);

        return transactions
            .Select(t => MovementAdapter.FromTransaction(t))
            .ToList()
            .AsReadOnly();
    }

    // ── Carga de candidatos (gastos manuales) ─────────────────────

    private async Task<IReadOnlyList<FinancialMovement>> LoadCandidateMovementsAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Cargar ambas hojas: Dynamic y Fixed
        var expenses = await _manualExpenses.GetByPeriodAsync(from, to, sheet: null, ct);

        _logger.LogDebug(
            "ManualExpenses cargados del período: {Count} ({Dynamic} dinámicos, {Fixed} fijos)",
            expenses.Count,
            expenses.Count(e => e.Sheet == ManualExpenseSheet.Dynamic),
            expenses.Count(e => e.Sheet == ManualExpenseSheet.Fixed));

        return expenses
            .Select(MovementAdapter.FromManualExpense)
            .ToList()
            .AsReadOnly();
    }
}
