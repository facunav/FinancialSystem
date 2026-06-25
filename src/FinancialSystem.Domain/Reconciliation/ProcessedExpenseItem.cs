using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Reconciliation
{
    /// <summary>
    /// Referencia inmutable a un movimiento original que participó en un gasto procesado.
    ///
    /// SNAPSHOT DELIBERADO:
    ///   Los campos Original* son copias inmutables tomadas al momento del procesamiento.
    ///   Garantizan trazabilidad histórica aunque cambien los importadores,
    ///   se re-importen archivos, o se modifiquen los parsers en el futuro.
    ///
    /// REFERENCIA SIN FK EXPLÍCITA:
    ///   SourceEntityType + SourceId referencian el registro original
    ///   sin FK hacia las tablas fuente. Intencional:
    ///   - Evita cascadas indeseadas sobre datos de importación
    ///   - Permite agregar nuevos tipos de fuente sin migrar este schema
    ///   La integridad referencial se mantiene por convención de negocio.
    ///
    /// USO POR EL MCP:
    ///   El MCP no consulta esta tabla para métricas.
    ///   Solo se usa para la UI de "ver detalle" y auditoría.
    /// </summary>
    public class ProcessedExpenseItem
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        // ── Relación con cabecera ────────────────────────────────────────────────

        public Guid ProcessedExpenseId { get; set; }
        public ProcessedExpense ProcessedExpense { get; set; } = null!;

        // ── Referencia al registro original ─────────────────────────────────────

        /// <summary>Qué tabla contiene el registro original.</summary>
        public SourceEntityType SourceEntityType { get; set; }

        /// <summary>Id del registro original en su tabla.</summary>
        public Guid SourceId { get; set; }

        /// <summary>
        /// Rol semántico en este gasto procesado.
        /// Reference = movimiento de verdad contable (banco, tarjeta).
        /// Candidate  = movimiento auxiliar (manual Excel).
        /// </summary>
        public ReconciliationItemRole Role { get; set; }

        // ── Snapshot inmutable del momento del procesamiento ─────────────────────

        public decimal OriginalAmount { get; set; }
        public DateTime OriginalDate { get; set; }
        public string OriginalDescription { get; set; } = string.Empty;
        public string OriginalCurrency { get; set; } = "ARS";
        public string? OriginalSourceFile { get; set; }
    }
}
