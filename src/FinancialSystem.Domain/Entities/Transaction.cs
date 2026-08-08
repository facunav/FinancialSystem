namespace FinancialSystem.Domain.Entities;

public class Transaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateTime CreatedAtUtc { get; set; }
    public string? CouponNumber { get; set; }
    public string? RawLine { get; set; }       // Para debugging/auditoría
    public string? SourceFile { get; set; }    // Trazabilidad

    /// <summary>
    /// Identificador determinístico para idempotencia (ver SheetParserHelpers.BuildTransactionExternalId).
    /// Nullable: filas existentes previas a esta columna quedan sin valor — Postgres no las
    /// considera duplicadas entre sí en el índice único (NULL != NULL). Toda fila nueva la recibe.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Cuenta financiera asociada a este movimiento. Nullable: la asignación es manual
    /// por ahora — no hay wiring automático desde el pipeline de importación (ver
    /// docs/RoadMaps/FinancialMcp-vNext.md, Épica J).
    /// </summary>
    public Guid? FinancialAccountId { get; set; }
    public FinancialAccount? FinancialAccount { get; set; }

    // ── Trazabilidad de importación (referencia blanda, Patch 0105) ────

    /// <summary>
    /// Id del ImportBatch de la corrida que insertó este movimiento. Referencia blanda,
    /// deliberadamente sin FK ni navegación (mismo criterio ya usado por SourceEntityType+
    /// SourceId en ClassifiedMovementItem/MovementAuditDecision/InvestigationReference: la
    /// integridad se sostiene por convención de negocio, no por constraint de base -- ver
    /// ADR-005 y el análisis de trazabilidad de importación previo a este patch). Nullable:
    /// filas insertadas antes de este patch quedan sin valor.
    /// </summary>
    public Guid? ImportBatchId { get; set; }
}

