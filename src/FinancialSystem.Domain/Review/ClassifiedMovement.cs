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
///   Las tablas originales (Transactions, BankStatements) son fuentes de importación.
///   Permanecen intactas e inmutables. Este registro no las reemplaza: las referencia
///   vía Items (snapshot). PR-L5: LegacyImportedExpenses se eliminó — filas históricas
///   de Items con SourceEntityType.LegacyImport ya no tienen tabla de origen que
///   consultar, pero su snapshot (OriginalAmount/OriginalDate/OriginalDescription, etc.)
///   sigue siendo válido y completo por sí solo.
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
    public Guid? CounterpartyId { get; set; }
    public Counterparty? Counterparty { get; set; }

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

    // ── Remanentes del motor de matching retirado (PR-L4) ────────────────────
    // Patch 0076 (PATCH-023): MatchScore y AmountDelta pertenecían a
    // IMatchScorer/ConfirmMatchCommand, el motor que comparaba banco/tarjeta
    // contra movimientos "Candidate" (Excel legacy) -- retirado por completo en
    // PR-L4/PR-L5 (ver ReviewResult.cs y docs/UX/ClassificationUX.md).
    // ClassifyMovementHandler, el único productor de ClassifiedMovement hoy,
    // nunca los escribe: para toda fila creada o reclasificada después del
    // retiro, ambos quedan en null. Solo filas históricas previas a PR-L4 pueden
    // conservar un valor no nulo.
    // Se conservan (no se eliminan) porque sí tienen un consumidor de solo
    // lectura vigente: las herramientas MCP de investigación
    // (MovementTools.GetMovement/ExplainMovement/ExplainClassification,
    // InvestigationTools -- ver
    // docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md) los
    // muestran como parte de la trazabilidad completa de un movimiento, y
    // AmountDelta != 0 dispara una observación en
    // MovementTools.BuildObservations. No usar como precedente de diseño para
    // ningún motor nuevo (ver docs/Architecture/PRS1analisismotorsugerencias.md).

    /// <summary>
    /// Score de coincidencia asignado por el motor de matching retirado en
    /// PR-L4 (<c>IMatchScorer</c>). Sin productor actual -- siempre null para
    /// cualquier clasificación hecha después del retiro (ver comentario de la
    /// sección arriba). Se conserva por trazabilidad histórica y porque las
    /// herramientas MCP de investigación todavía lo exponen cuando existe.
    /// </summary>
    public double? MatchScore { get; set; }

    /// <summary>
    /// Diferencia de importe entre References y Candidates de un grupo de
    /// matching, calculada por el motor retirado en PR-L4. Sin productor actual
    /// -- mismo caso que <see cref="MatchScore"/>: siempre null para cualquier
    /// clasificación hecha después del retiro. Se conserva por trazabilidad
    /// histórica y porque las herramientas MCP de investigación todavía lo
    /// exponen y lo usan para una observación (<c>AmountDelta != 0</c>).
    /// </summary>
    public decimal? AmountDelta { get; set; }

    // ── Auditoría ────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }

    // ── Navegación ───────────────────────────────────────────────────────────

    public ICollection<ClassifiedMovementItem> Items { get; set; } = [];
}