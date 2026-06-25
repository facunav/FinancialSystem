using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Reconciliation
{
    /// <summary>
    /// Gasto financiero procesado. Única fuente de verdad para el MCP.
    ///
    /// CONTRATO:
    ///   Toda fila en esta tabla representa verdad financiera verificada por el usuario.
    ///   No existen estados intermedios ni sugerencias aquí.
    ///   Las sugerencias del motor viven en ReconciliationSuggestion (tabla de staging).
    ///
    /// CLASIFICACIÓN OBLIGATORIA:
    ///   CategoryId y FinancialImpact son requeridos.
    ///   Un ProcessedExpense sin categoría no puede existir.
    ///
    /// RELACIÓN CON FUENTES:
    ///   Las tablas originales (Transactions, BankStatements, ManualExpenses)
    ///   son fuentes de importación. Permanecen intactas e inmutables.
    ///   Este registro no las reemplaza: las referencia vía Items (snapshot).
    ///
    /// QUERIES DEL MCP:
    ///   ¿Cuánto gasto?        → SUM(TotalAmount) WHERE FinancialImpact = RealExpense
    ///   ¿En qué gasto?        → GROUP BY CategoryId WHERE FinancialImpact = RealExpense
    ///   ¿Cómo evolucionan?    → GROUP BY DATE_TRUNC('month', EffectiveDate), CategoryId
    ///   ¿Cuánto necesito?     → AVG mensual WHERE FinancialImpact = RealExpense
    ///   ¿Cuánto puedo ahorrar?→ Income - RealExpense por período
    /// </summary>
    public class ProcessedExpense
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        // ── Datos financieros canónicos ──────────────────────────────────────────
        // Copiados del movimiento bancario/tarjeta original al procesar.
        // TotalAmount = Math.Abs(movimiento.Amount): representa magnitud, no signo contable.

        /// <summary>Fecha del movimiento original. Base para todas las queries temporales del MCP.</summary>
        public DateTime EffectiveDate { get; set; }

        /// <summary>Monto absoluto del gasto. Siempre positivo.</summary>
        public decimal TotalAmount { get; set; }

        public string Currency { get; set; } = "ARS";

        /// <summary>Descripción del movimiento original (Concept/Description del banco o tarjeta).</summary>
        public string Description { get; set; } = string.Empty;

        // ── Clasificación financiera (núcleo del MCP) ────────────────────────────

        /// <summary>
        /// Categoría del gasto. Obligatoria.
        /// FK a Category. El MCP agrupa por este campo para responder "¿en qué gasto?".
        /// </summary>
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }

        /// <summary>
        /// Efecto patrimonial. Obligatorio.
        /// El MCP filtra por RealExpense para calcular gasto neto real.
        /// </summary>
        public FinancialImpact FinancialImpact { get; set; }

        // ── Estado de procesamiento ──────────────────────────────────────────────

        /// <summary>Confirmed = match con contraparte. Reviewed = sin contraparte, marcado manualmente.</summary>
        public ExpenseStatus Status { get; set; }

        /// <summary>Cómo fue procesado este gasto. Para trazabilidad y auditoría.</summary>
        public ProcessingSource ProcessingSource { get; set; }

        // ── Datos de revisión manual (solo si Status = Reviewed) ─────────────────

        public ReviewReason? ReviewReason { get; set; }
        public string? ReviewNotes { get; set; }

        // ── Métricas del motor (trazabilidad, no para MCP) ───────────────────────
        // Null cuando el procesamiento fue completamente manual (ManualMatch o ManualReview).

        /// <summary>Score del motor al momento de la sugerencia. Null si fue match manual puro.</summary>
        public double? MatchScore { get; set; }

        /// <summary>
        /// Diferencia absoluta entre suma de References y suma de Candidates.
        /// 0 en matches perfectos. Null en revisiones sin contraparte.
        /// </summary>
        public decimal? AmountDelta { get; set; }

        // ── Auditoría ────────────────────────────────────────────────────────────

        public DateTime CreatedAt { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? ProcessedBy { get; set; }

        // ── Navegación ───────────────────────────────────────────────────────────

        public ICollection<ProcessedExpenseItem> Items { get; set; } = [];
    }
}
