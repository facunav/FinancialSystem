using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Application.Reconciliation;

/// <summary>
/// Punto de entrada para ejecutar una conciliación de un período.
///
/// RESPONSABILIDAD ÚNICA:
///   Cargar referencias (Transactions + BankStatements) y candidatos
///   (ManualExpenses), ejecutar el motor, devolver sugerencias en memoria.
///
/// NO persiste nada. NO confirma nada.
/// El llamador decide qué hacer con las sugerencias:
///   - mostrarlas en UI para revisión
///   - pasarlas a ReconciliationConfirmationService para confirmar
///   - descartarlas si solo quería una vista previa
/// </summary>
public sealed class ReconciliationOrchestrator
{
    private readonly IApplicationDbContext _db;
    private readonly IManualExpenseRepository _manualExpenses;
    private readonly IReconciliationEngine _engine;
    private readonly ILogger<ReconciliationOrchestrator> _logger;

    public ReconciliationOrchestrator(
        IApplicationDbContext db,
        IManualExpenseRepository manualExpenses,
        IReconciliationEngine engine,
        ILogger<ReconciliationOrchestrator> logger)
    {
        _db = db;
        _manualExpenses = manualExpenses;
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta la conciliación del período y devuelve sugerencias clasificadas.
    /// Nada se persiste en este paso.
    /// </summary>
    public async Task<ReconciliationSuggestions> RunAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        ReconciliationOptions? options = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Iniciando conciliación: {Start} → {End}",
            periodStart, periodEnd);

        var references = await LoadReferenceMovementsAsync(periodStart, periodEnd, ct);
        var candidates = await LoadCandidateMovementsAsync(periodStart, periodEnd, ct);

        _logger.LogInformation(
            "Fuentes cargadas: {RefCount} referencias ({TxCount} tarjeta, {BsCount} banco) | {CandCount} candidatos manuales",
            references.Count,
            references.Count(r => r.Source == MovementSource.CreditCard),
            references.Count(r => r.Source == MovementSource.BankDebit),
            candidates.Count);

        var request = new ReconciliationRequest
        {
            ReferenceMovements = references,
            CandidateMovements = candidates,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Options = options,
        };

        var result = await _engine.ReconcileAsync(request, ct);
        var suggestions = ReconciliationSuggestions.FromResult(result, periodStart, periodEnd);

        _logger.LogInformation(
            "Sugerencias generadas: {AutoConfirmed} auto-confirmables, {Suggested} para revisión, {Ignored} ignoradas, {Unmatched} sin match",
            suggestions.AutoConfirmable.Count,
            suggestions.NeedsReview.Count,
            suggestions.Ignored.Count,
            suggestions.Unmatched.Count);

        return suggestions;
    }

    // ── Carga de referencias ──────────────────────────────────────

    private async Task<IReadOnlyList<FinancialMovement>> LoadReferenceMovementsAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // ── Transactions (tarjetas de crédito) ────────────────────
        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);

        // ── BankStatements (débito bancario) ──────────────────────
        // Se incluyen solo débitos (Amount < 0) para la conciliación
        // contra gastos manuales. Los créditos (transferencias entrantes,
        // intereses) se cargan pero el motor los clasificará correctamente
        // por signo de monto.
        var bankStatements = await _db.BankStatements
            .AsNoTracking()
            .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
            .OrderBy(b => b.Date)
            .ToListAsync(ct);

        _logger.LogDebug(
            "Referencias cargadas: {TxCount} transactions, {BsCount} bank statements",
            transactions.Count, bankStatements.Count);

        var movements = new List<FinancialMovement>(transactions.Count + bankStatements.Count);
        movements.AddRange(transactions.Select(x => MovementAdapter.FromTransaction(x)));
        movements.AddRange(bankStatements.Select(x => MovementAdapter.FromBankStatement(x)));

        return movements.AsReadOnly();
    }

    // ── Carga de candidatos ───────────────────────────────────────

    private async Task<IReadOnlyList<FinancialMovement>> LoadCandidateMovementsAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var expenses = await _manualExpenses.GetByPeriodAsync(from, to, sheet: null, ct);

        _logger.LogDebug(
            "Candidatos cargados: {Count} gastos manuales ({Dynamic} dinámicos, {Fixed} fijos)",
            expenses.Count,
            expenses.Count(e => e.Sheet == ManualExpenseSheet.Dynamic),
            expenses.Count(e => e.Sheet == ManualExpenseSheet.Fixed));

        return expenses
            .Select(MovementAdapter.FromManualExpense)
            .ToList()
            .AsReadOnly();
    }
}