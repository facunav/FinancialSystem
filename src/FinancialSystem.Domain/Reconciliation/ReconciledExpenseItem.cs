using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Reconciliation
{
    /// <summary>
    /// Referencia a un registro original que participa en una reconciliación.
    ///
    /// SNAPSHOT DELIBERADO:
    ///   OriginalAmount, OriginalDate, OriginalDescription, OriginalCurrency
    ///   son copias inmutables del momento de la reconciliación.
    ///   Garantizan trazabilidad histórica aunque cambien los importadores,
    ///   se re-importen archivos, o se modifiquen descripciones futuras.
    ///
    /// REFERENCIA SIN FK DIRECTA:
    ///   SourceEntityType + SourceId referencian el registro original
    ///   sin FK explícita hacia las tablas fuente. Esto es intencional:
    ///   - Evita cascadas indeseadas
    ///   - Permite que las fuentes sean inmutables e independientes
    ///   - Facilita agregar nuevos tipos de fuente sin migrar este schema
    ///   La integridad referencial se mantiene por convención de negocio,
    ///   no por constraint de DB.
    /// </summary>
    public class ReconciledExpenseItem
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        // ── Relación con cabecera ─────────────────────────────────────
        public Guid ReconciledExpenseId { get; set; }
        public ReconciledExpense ReconciledExpense { get; set; } = null!;

        // ── Referencia al registro original ──────────────────────────
        public SourceEntityType SourceEntityType { get; set; }
        public Guid SourceId { get; set; }

        /// <summary>
        /// Rol semántico en esta reconciliación.
        /// Reference = movimiento de verdad contable (banco, tarjeta).
        /// Candidate  = movimiento a reconciliar (manual, otra fuente).
        /// </summary>
        public ReconciliationItemRole Role { get; set; }

        // ── Snapshot inmutable de datos originales ────────────────────
        public decimal OriginalAmount { get; set; }
        public DateTime OriginalDate { get; set; }
        public string OriginalDescription { get; set; } = string.Empty;
        public string OriginalCurrency { get; set; } = "ARS";
        public string? OriginalSourceFile { get; set; }

        // ── Contribución al score ─────────────────────────────────────
        // Null para ítems sin contraparte (reconciliaciones manuales de un solo ítem).
        public double? ContributionScore { get; set; }
    }
}
