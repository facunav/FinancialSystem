using FinancialSystem.Application.Abstractions;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Rehidrata MatchedPairs a partir de los IDs de sus movimientos.
    ///
    /// RESPONSABILIDAD:
    ///   El endpoint de confirmación recibe IDs (no el par completo).
    ///   Este servicio consulta las entidades originales y construye el par
    ///   usando MovementAdapter — exactamente igual que el orquestador.
    ///
    /// SCORE EN CONFIRMACIÓN MANUAL:
    ///   Si el cliente no envía score (confirmación manual pura sin sugerencia),
    ///   el par se construye con Score.Total = 1.0 y Confidence = High.
    ///   ConfirmationSource = Manual lo distingue en auditoría del score del motor.
    ///
    /// SCORE PRESERVADO DE SUGERENCIA:
    ///   Si el cliente envía el score original (vino de una sugerencia),
    ///   se construye el par con ese score para que BuildExpense lo persista.
    /// </summary>
    public sealed class MovementHydrationService
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<MovementHydrationService> _logger;

        public MovementHydrationService(
            IApplicationDbContext db,
            ILogger<MovementHydrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════
        // HIDRATACIÓN INDIVIDUAL (se mantiene para compatibilidad)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Rehidrata un par desde IDs. Retorna null si alguna entidad no existe.
        /// </summary>
        public async Task<MatchedPair?> HydrateAsync(
            Guid referenceId, MovementSource referenceSource,
            Guid candidateId, MovementSource candidateSource,
            double? originalScore = null,
            string? originalConfidence = null,
            CancellationToken ct = default)
        {
            var requests = new[]
            {
            new HydrationRequest(0, referenceId, referenceSource, candidateId, candidateSource,
                originalScore, originalConfidence)
        };

            var results = await HydrateBatchAsync(requests, ct);
            return results[0].Pair;
        }

        // ════════════════════════════════════════════════════════════
        // HIDRATACIÓN EN BATCH
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Rehidrata múltiples pares con queries en batch por tipo de entidad.
        ///
        /// ESTRATEGIA:
        ///   1. Recolectar todos los (Id, Source) de referencias y candidatos
        ///   2. Agrupar por tipo de entidad (Transaction, BankStatement, ManualExpense)
        ///   3. Una query "WHERE Id IN (...)" por tipo — máximo 3 queries totales,
        ///      independientemente del tamaño del batch
        ///   4. Construir cada MatchedPair en memoria desde los diccionarios cargados
        ///
        /// Comparado con HydrateAsync llamado N veces (hasta 2N queries individuales),
        /// esto hace como máximo 3 queries sin importar cuántos pares se pidan.
        ///
        /// El resultado preserva el orden y el Index de cada request — el llamador
        /// puede correlacionar 1:1 sin búsquedas adicionales.
        /// </summary>
        public async Task<IReadOnlyList<HydrationResult>> HydrateBatchAsync(
            IReadOnlyList<HydrationRequest> requests,
            CancellationToken ct = default)
        {
            if (requests.Count == 0) return [];

            // ── Paso 1+2: recolectar y agrupar IDs por tipo ───────────
            var transactionIds = new HashSet<Guid>();
            var bankStatementIds = new HashSet<Guid>();
            var manualExpenseIds = new HashSet<Guid>();

            foreach (var req in requests)
            {
                CollectId(req.ReferenceId, req.ReferenceSource, transactionIds, bankStatementIds, manualExpenseIds);
                CollectId(req.CandidateId, req.CandidateSource, transactionIds, bankStatementIds, manualExpenseIds);
            }

            // ── Paso 3: una query por tipo ────────────────────────────
            var transactions = await LoadTransactionsAsync(transactionIds, ct);
            var bankStatements = await LoadBankStatementsAsync(bankStatementIds, ct);
            var manualExpenses = await LoadManualExpensesAsync(manualExpenseIds, ct);

            _logger.LogDebug(
                "HydrateBatchAsync: {Requests} pares | cargados {Tx} transactions, {Bs} bank statements, {Me} manual expenses",
                requests.Count, transactions.Count, bankStatements.Count, manualExpenses.Count);

            // ── Paso 4: construir cada par en memoria ─────────────────
            var results = new List<HydrationResult>(requests.Count);

            foreach (var req in requests)
            {
                var reference = Resolve(req.ReferenceId, req.ReferenceSource, transactions, bankStatements, manualExpenses);
                if (reference is null)
                {
                    _logger.LogWarning(
                        "HydrateBatchAsync[{Index}]: referencia no encontrada — Id={Id} Source={Source}",
                        req.Index, req.ReferenceId, req.ReferenceSource);
                    results.Add(HydrationResult.NotFound(req.Index, "referencia"));
                    continue;
                }

                var candidate = Resolve(req.CandidateId, req.CandidateSource, transactions, bankStatements, manualExpenses);
                if (candidate is null)
                {
                    _logger.LogWarning(
                        "HydrateBatchAsync[{Index}]: candidato no encontrado — Id={Id} Source={Source}",
                        req.Index, req.CandidateId, req.CandidateSource);
                    results.Add(HydrationResult.NotFound(req.Index, "candidato"));
                    continue;
                }

                var pair = new MatchedPair
                {
                    Reference = reference,
                    Candidate = candidate,
                    Score = BuildScore(req.OriginalScore),
                    Confidence = ParseConfidence(req.OriginalConfidence),
                    Contributions = [],
                };

                results.Add(HydrationResult.Found(req.Index, pair));
            }

            return results.AsReadOnly();
        }

        // ── Carga batch por tipo ───────────────────────────────────────

        private async Task<Dictionary<Guid, FinancialMovement>> LoadTransactionsAsync(
            HashSet<Guid> ids, CancellationToken ct)
        {
            if (ids.Count == 0) return [];

            var entities = await _db.Transactions
                .AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .ToListAsync(ct);

            return entities.ToDictionary(e => e.Id, e => MovementAdapter.FromTransaction(e));
        }

        private async Task<Dictionary<Guid, FinancialMovement>> LoadBankStatementsAsync(
            HashSet<Guid> ids, CancellationToken ct)
        {
            if (ids.Count == 0) return [];

            var entities = await _db.BankStatements
                .AsNoTracking()
                .Where(b => ids.Contains(b.Id))
                .ToListAsync(ct);

            return entities.ToDictionary(e => e.Id, e => MovementAdapter.FromBankStatement(e));
        }

        private async Task<Dictionary<Guid, FinancialMovement>> LoadManualExpensesAsync(
            HashSet<Guid> ids, CancellationToken ct)
        {
            if (ids.Count == 0) return [];

            var entities = await _db.ManualExpenses
                .AsNoTracking()
                .Where(m => ids.Contains(m.Id))
                .ToListAsync(ct);

            return entities.ToDictionary(e => e.Id, e => MovementAdapter.FromManualExpense(e));
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static void CollectId(
            Guid id, MovementSource source,
            HashSet<Guid> transactionIds, HashSet<Guid> bankStatementIds, HashSet<Guid> manualExpenseIds)
        {
            switch (source)
            {
                case MovementSource.CreditCard:
                    transactionIds.Add(id);
                    break;
                case MovementSource.BankDebit:
                    bankStatementIds.Add(id);
                    break;
                case MovementSource.ManualDynamic or MovementSource.ManualFixed:
                    manualExpenseIds.Add(id);
                    break;
            }
        }

        private static FinancialMovement? Resolve(
            Guid id, MovementSource source,
            Dictionary<Guid, FinancialMovement> transactions,
            Dictionary<Guid, FinancialMovement> bankStatements,
            Dictionary<Guid, FinancialMovement> manualExpenses) => source switch
            {
                MovementSource.CreditCard => transactions.GetValueOrDefault(id),
                MovementSource.BankDebit => bankStatements.GetValueOrDefault(id),
                MovementSource.ManualDynamic or MovementSource.ManualFixed
                => manualExpenses.GetValueOrDefault(id),
                _ => null,
            };

        private static MatchScore BuildScore(double? originalScore)
        {
            var total = originalScore ?? 1.0;
            return new MatchScore
            {
                Total = total,
                AmountScore = originalScore.HasValue ? 0 : 1.0,
                DateScore = originalScore.HasValue ? 0 : 1.0,
                DescriptionScore = originalScore.HasValue ? 0 : 1.0,
                PaymentMethodScore = originalScore.HasValue ? 0 : 1.0,
            };
        }

        private static MatchConfidence ParseConfidence(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return MatchConfidence.High;

            return Enum.TryParse<MatchConfidence>(raw, ignoreCase: true, out var parsed)
                ? parsed
                : MatchConfidence.High;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // TIPOS DE REQUEST/RESULT PARA BATCH
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Una solicitud de hidratación dentro de un batch.
    /// Index preserva la posición original en la lista de entrada del llamador,
    /// permitiendo correlación 1:1 sin búsquedas adicionales.
    /// </summary>
    public sealed record HydrationRequest(
        int Index,
        Guid ReferenceId, MovementSource ReferenceSource,
        Guid CandidateId, MovementSource CandidateSource,
        double? OriginalScore = null,
        string? OriginalConfidence = null);

    /// <summary>
    /// Resultado de una hidratación individual dentro del batch.
    /// Pair es null si NotFoundReason tiene valor.
    /// </summary>
    public sealed record HydrationResult(
        int Index,
        MatchedPair? Pair,
        string? NotFoundReason)
    {
        public bool Success => Pair is not null;

        public static HydrationResult Found(int index, MatchedPair pair) =>
            new(index, pair, null);

        public static HydrationResult NotFound(int index, string what) =>
            new(index, null, $"No se encontró el movimiento ({what}) en la base de datos");
    }
}
