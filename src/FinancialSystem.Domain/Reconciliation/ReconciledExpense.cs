using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Reconciliation
{
    /// <summary>
    /// Gasto financiero consolidado. Única fuente oficial de cálculo.
    ///
    /// CICLO DE VIDA:
    ///   El motor genera sugerencias (Suggested / AutoConfirmed).
    ///   El usuario confirma o rechaza. Solo los Confirmed representan
    ///   la verdad financiera del sistema.
    ///
    /// RELACIÓN CON FUENTES:
    ///   Las tablas originales (Transactions, ManualExpenses, BankStatements)
    ///   son fuentes de importación/auditoría — permanecen intactas.
    ///   Este registro no las reemplaza: las referencia via Items.
    ///
    /// INMUTABILIDAD PARCIAL:
    ///   MatchScore, MatchConfidence y CreatedAt son inmutables post-creación.
    ///   Status, ConfirmedAt, ConfirmedBy solo avanzan — no retroceden.
    ///   EffectiveDate, TotalAmount, Description pueden ajustarse
    ///   mientras el estado sea Suggested/AutoConfirmed.
    /// </summary>
    public class ReconciledExpense
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        // ── Período ───────────────────────────────────────────────────
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }

        // ── Datos consolidados ────────────────────────────────────────
        // Vista unificada del gasto. Se calculan al crear la sugerencia
        // y pueden ajustarse antes de confirmar.
        public DateTime EffectiveDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "ARS";
        public string Description { get; set; } = string.Empty;

        // ── Estado y confianza del motor ──────────────────────────────
        public ReconciledExpenseStatus Status { get; set; }
        public double MatchScore { get; set; }   // inmutable post-creación
        public string MatchConfidence { get; set; } = string.Empty; // inmutable
        /// <summary>
        /// Origen de la confirmación. Permite distinguir si el score
        /// fue calculado por el motor o es una confirmación manual pura.
        /// </summary>
        public ConfirmationSource ConfirmationSource { get; set; }

        // ── Auditoría ─────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; }   // inmutable: cuándo el motor generó esto
        public DateTime? ConfirmedAt { get; set; }   // null hasta que el usuario actúa
        public string? ConfirmedBy { get; set; }   // userId, email, o "system"

        public ReviewReason? ReviewReason { get; set; }

        public string? ReviewNotes { get; set; }

        /// <summary>
        /// NUEVO: diferencia entre suma(Reference) y suma(Candidate) en valor absoluto.
        /// 0 para conciliaciones balanceadas. Persistido para auditoría —
        /// permite reportes de "conciliaciones con diferencia" sin recalcular.
        /// </summary>
        public decimal AmountDelta { get; set; }

        /// <summary>
        /// NUEVO: true si AmountDelta excede la tolerancia configurada
        /// al momento de confirmar. No bloquea, pero queda trazado.
        /// </summary>
        public bool HasAmountMismatch { get; set; }

        public ReconciliationGroupingMode GroupingMode { get; set; }

        // ── Navegación ────────────────────────────────────────────────
        public ICollection<ReconciledExpenseItem> Items { get; set; } = [];
    }
}
