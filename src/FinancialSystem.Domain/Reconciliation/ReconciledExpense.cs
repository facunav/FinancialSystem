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

        // ── Auditoría ─────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; }   // inmutable: cuándo el motor generó esto
        public DateTime? ConfirmedAt { get; set; }   // null hasta que el usuario actúa
        public string? ConfirmedBy { get; set; }   // userId, email, o "system"

        // ── Navegación ────────────────────────────────────────────────
        public ICollection<ReconciledExpenseItem> Items { get; set; } = [];
    }
}
