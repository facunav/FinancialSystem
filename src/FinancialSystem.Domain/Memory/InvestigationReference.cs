using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Memory;

/// <summary>
/// Referencia de una Investigation a una entidad real del sistema financiero —
/// nunca una copia de su dato (ver Investigation, ADR-007 §2).
///
/// REFERENCIA SIN FK EXPLÍCITA HACIA EL ORIGEN:
///   SourceEntityType + SourceId identifican el registro real referenciado, la misma
///   convención que ya usan ClassifiedMovementItem, GetMovement, ExplainMovement y
///   ExplainClassification (ver ADR-007 §4) — no una nueva. Sin FK explícita hacia esas
///   tablas, por la misma razón que ya documenta ClassifiedMovementItem: evita cascadas
///   indeseadas sobre datos de importación y permite agregar nuevos tipos de fuente sin
///   migrar este schema.
/// </summary>
public class InvestigationReference
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid InvestigationId { get; set; }
    public Investigation Investigation { get; set; } = null!;

    /// <summary>Qué tabla contiene el registro real referenciado.</summary>
    public SourceEntityType SourceEntityType { get; set; }

    /// <summary>Id del registro real en su tabla.</summary>
    public Guid SourceId { get; set; }
}
