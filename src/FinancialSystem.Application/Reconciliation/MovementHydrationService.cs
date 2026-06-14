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
    /// Rehidrata un MatchedPair a partir de los IDs de sus movimientos.
    ///
    /// RESPONSABILIDAD:
    ///   El endpoint de confirmación recibe IDs (no el par completo).
    ///   Este servicio consulta las entidades originales y construye el par
    ///   usando MovementAdapter — exactamente igual que el orquestador.
    ///
    /// SCORE EN CONFIRMACIÓN MANUAL:
    ///   Si el cliente no envía score (confirmación manual pura sin sugerencia),
    ///   el par se construye con Score.Total = 1.0 y Confidence = High.
    ///   Esto es correcto: si el usuario lo confirmó a mano, es por definición
    ///   de máxima confianza desde su perspectiva.
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

        /// <summary>
        /// Rehidrata un par desde IDs. Retorna null si alguna entidad no existe.
        /// El score opcional permite preservar el score original del motor.
        /// </summary>
        public async Task<MatchedPair?> HydrateAsync(
            Guid referenceId, MovementSource referenceSource,
            Guid candidateId, MovementSource candidateSource,
            double? originalScore = null,
            string? originalConfidence = null,
            CancellationToken ct = default)
        {
            var reference = await LoadMovementAsync(referenceId, referenceSource, ct);
            if (reference is null)
            {
                _logger.LogWarning(
                    "MovementHydrationService: referencia no encontrada — Id={Id} Source={Source}",
                    referenceId, referenceSource);
                return null;
            }

            var candidate = await LoadMovementAsync(candidateId, candidateSource, ct);
            if (candidate is null)
            {
                _logger.LogWarning(
                    "MovementHydrationService: candidato no encontrado — Id={Id} Source={Source}",
                    candidateId, candidateSource);
                return null;
            }

            // Construir el score: usar el original si viene del cliente,
            // o 1.0/High si es confirmación manual sin sugerencia previa.
            var score = BuildScore(originalScore);
            var confidence = ParseConfidence(originalConfidence);

            return new MatchedPair
            {
                Reference = reference,
                Candidate = candidate,
                Score = score,
                Confidence = confidence,
                Contributions = [],
            };
        }

        // ── Carga por tipo de entidad ─────────────────────────────────

        private async Task<FinancialMovement?> LoadMovementAsync(
            Guid id, MovementSource source, CancellationToken ct) =>
            source switch
            {
                MovementSource.CreditCard => await LoadTransactionAsync(id, ct),
                MovementSource.BankDebit => await LoadBankStatementAsync(id, ct),
                MovementSource.ManualDynamic or
                MovementSource.ManualFixed => await LoadManualExpenseAsync(id, source, ct),
                _ => null,
            };

        private async Task<FinancialMovement?> LoadTransactionAsync(Guid id, CancellationToken ct)
        {
            var entity = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            return entity is null ? null : MovementAdapter.FromTransaction(entity);
        }

        private async Task<FinancialMovement?> LoadBankStatementAsync(Guid id, CancellationToken ct)
        {
            var entity = await _db.BankStatements
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, ct);
            return entity is null ? null : MovementAdapter.FromBankStatement(entity);
        }

        private async Task<FinancialMovement?> LoadManualExpenseAsync(
            Guid id, MovementSource source, CancellationToken ct)
        {
            var entity = await _db.ManualExpenses
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id, ct);
            return entity is null ? null : MovementAdapter.FromManualExpense(entity);
        }

        // ── Score helpers ─────────────────────────────────────────────

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
}
