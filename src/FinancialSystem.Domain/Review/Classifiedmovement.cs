using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Review;

/// <summary>
/// Movimiento financiero clasificado. Única fuente de verdad para el MCP y el Dashboard.
///
/// POR QUÉ ESTE NOMBRE (no "ProcessedExpense", no "ReconciledExpense"):
///   Esta entidad representa CUALQUIER movimiento financiero ya clasificado:
///   gastos, ingresos, transferencias internas, pagos de deuda y a futuro inversiones.
///   "Expense" presuponía gasto, lo cual es incorrecto para 3 de los 4 valores
///   posibles de FinancialImpact. "ClassifiedMovement" no presupone naturaleza
///   ni signo — es correcto para todo lo que el dominio puede clasificar, hoy y a futuro.
///
/// CONTRATO:
///   Toda fila en esta tabla representa verdad financiera verificada por el usuario.
///   No existen estados intermedios ni sugerencias aquí.
///   Las sugerencias de matching viven en MatchSuggestion (tabla de staging, no persistida hoy).
///
/// CLASIFICACIÓN OBLIGATORIA:
///   CategoryId y FinancialImpact son requeridos.
///   MovementType es requerido (qué ocurrió: Compra, Transferencia, Pago, etc.).
///   CounterpartyId es opcional pero fuertemente recomendado — habilita sugerencias futuras.
///
/// RELACIÓN CON FUENTES:
///   Las tablas originales (Transactions, BankStatements, LegacyImportedExpenses)
///   son fuentes de importación. Permanecen intactas e inmutables.
///   Este registro no las reemplaza: las referencia vía Items (snapshot).
///
/// QUERIES DEL MCP:
///   ¿Cuánto gasto?        → SUM(TotalAmount) WHERE FinancialImpact = Expense
///   ¿En qué gasto?        → GROUP BY CategoryId WHERE FinancialImpact = Expense
///   ¿Cómo evolucionan?    → GROUP BY DATE_TRUNC('month', EffectiveDate), CategoryId
///   ¿Cuánto le pagué a X? → GROUP BY CounterpartyId
/// </summary>
public class ClassifiedMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // ── Datos financieros canónicos ──────────────────────────────────────────
    // Copiados del movimiento bancario/tarjeta original al clasificar.
    // TotalAmount = Math.Abs(movimiento.Amount): representa magnitud, no signo contable.

    /// <summary>Fecha del movimiento original. Base para todas las queries temporales del MCP.</summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>Monto absoluto del movimiento. Siempre positivo.</summary>
    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = "ARS";

    /// <summary>Descripción del movimiento original (Concept/Description del banco o tarjeta).</summary>
    public string Description { get; set; } = string.Empty;

    // ── Clasificación — 4 dimensiones independientes ─────────────────────────

    /// <summary>
    /// Qué ocurrió (Compra, Transferencia, Pago, Cobro, Comisión, Interés, Reintegro, Ajuste, Otro).
    /// Obligatorio.
    /// </summary>
    public MovementType MovementType { get; set; }

    /// <summary>
    /// Cómo afecta el patrimonio (Gasto, Ingreso, Movimiento interno, Financiación/Pago de deuda).
    /// Obligatorio. El MCP filtra por Expense para calcular gasto neto real.
    /// </summary>
    public FinancialImpact FinancialImpact { get; set; }

    /// <summary>
    /// Para qué se usó el dinero. Obligatoria.
    /// FK a Category. El MCP agrupa por este campo para responder "¿en qué gasto?".
    /// </summary>
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// Con quién o qué se relaciona el movimiento. Opcional pero recomendada.
    /// FK a Counterparty. Habilita sugerencias automáticas de clasificación
    /// en revisiones futuras del mismo emisor/receptor.
    /// </summary>
    public Guid? CounterPartyId { get; set; }
    public CounterParty? CounterParty { get; set; }

    // ── Estado de clasificación ──────────────────────────────────────────────

    /// <summary>Confirmed = clasificado con coincidencia externa. Reviewed = sin coincidencia.</summary>
    public ClassificationStatus Status { get; set; }

    /// <summary>Cómo fue clasificado este movimiento. Para trazabilidad y auditoría.</summary>
    public ProcessingSource ProcessingSource { get; set; }

    /// <summary>
    /// Comentario libre opcional. Reemplaza al antiguo enum cerrado ReviewReason.
    /// Cualquier contexto que las 4 dimensiones estructuradas no cubran bien.
    /// </summary>
    public string? Comment { get; set; }

    // ── Métricas del motor de sugerencias (trazabilidad, no para MCP) ────────
    // Null cuando la clasificación fue completamente manual.

    /// <summary>Score del motor al momento de la sugerencia. Null si fue manual puro.</summary>
    public double? MatchScore { get; set; }

    /// <summary>
    /// Diferencia absoluta entre suma de References y suma de Candidates.
    /// 0 en coincidencias perfectas. Null en revisiones sin contraparte.
    /// </summary>
    public decimal? AmountDelta { get; set; }

    // ── Auditoría ────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }

    // ── Navegación ───────────────────────────────────────────────────────────

    public ICollection<ClassifiedMovementItem> Items { get; set; } = [];
}