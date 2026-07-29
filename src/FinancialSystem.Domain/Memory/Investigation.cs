using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Memory;

/// <summary>
/// Investigación registrada por el MCP — el concepto central de memoria fijado en
/// ADR-007 (Memoria del Financial MCP), §3.
///
/// SOLO PERSISTENCIA (Fase 2 de ADR-007, §8):
///   Esta entidad no tiene todavía tools de lectura/escritura ni lógica de negocio
///   asociada — eso es Fase 3. Este PR únicamente deja preparado el modelo.
///
/// SIN SNAPSHOT FINANCIERO (ADR-007, §2):
///   Ningún campo de esta entidad copia un valor financiero (importe, moneda,
///   categoría actual, etc.). Question y Conclusion son siempre interpretación en
///   lenguaje natural, nunca el dato en sí. Los movimientos u otras entidades reales
///   a los que una investigación se refiere se vinculan solo por referencia, vía
///   References (ver InvestigationReference) — nunca por copia.
/// </summary>
public class Investigation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Pregunta o hipótesis que dio origen a la investigación.</summary>
    public string Question { get; set; } = string.Empty;

    public InvestigationStatus Status { get; set; } = InvestigationStatus.Open;

    /// <summary>Conclusión de la investigación. Solo tiene sentido cuando Status = Resolved.</summary>
    public string? Conclusion { get; set; }

    /// <summary>
    /// Etiquetas libres separadas por coma (ej. "tarjeta-visa,contraparte-desconocida") para
    /// agrupar temáticamente y facilitar la búsqueda — ver ADR-007 §3 ("Etiquetas").
    /// </summary>
    public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<InvestigationReference> References { get; set; } = new List<InvestigationReference>();
}
