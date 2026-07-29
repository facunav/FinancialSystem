namespace FinancialSystem.Domain.Memory;

/// <summary>
/// Hallazgo registrado durante una Investigation — el concepto "Hallazgo" fijado en
/// ADR-007 (Memoria del Financial MCP), §3: "una observación puntual encontrada en el
/// curso de una investigación — una interpretación sobre un dato, nunca el dato en sí".
///
/// ENTIDAD NUEVA, NO UN CAMPO DE Investigation:
///   Investigation.Conclusion es un único string nullable — alcanza para la conclusión
///   final, pero una investigación puede acumular varios hallazgos parciales antes (o
///   incluso sin) llegar a una conclusión (ADR-007 §3 los distingue explícitamente:
///   "Investigación... incluye hipótesis descartadas en el camino, no solo la
///   conclusión final"). Un campo único no puede representar una lista que crece con
///   el tiempo, así que hace falta esta entidad — no una extensión de Investigation.
///
/// MÍNIMA A PROPÓSITO: sin autor, sin IA, sin score, sin embeddings, sin referencias
/// propias — un hallazgo referencia movimientos indirectamente, a través de la
/// Investigation a la que pertenece (ver InvestigationReference).
/// </summary>
public class InvestigationFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid InvestigationId { get; set; }
    public Investigation Investigation { get; set; } = null!;

    /// <summary>Texto del hallazgo, en lenguaje natural.</summary>
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
