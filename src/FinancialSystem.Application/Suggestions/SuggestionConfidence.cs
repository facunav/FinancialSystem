namespace FinancialSystem.Application.Suggestions;

/// <summary>
/// Qué tan confiable es un <see cref="ClassificationSuggestion"/>, en escala ordinal.
///
/// Deliberadamente ordinal y no un score numérico continuo: comprometerse a un score
/// (0.0-1.0, por ejemplo) sería empezar a diseñar el algoritmo interno de una
/// implementación concreta (conteo de coincidencias, distancia de embeddings, salida
/// de un modelo, etc.) en el contrato público. Cualquier implementación futura —desde
/// un GROUP BY simple hasta reglas configurables o un proveedor de IA— mapea su propia
/// noción de certeza a esta misma escala de 3 valores.
/// </summary>
public enum SuggestionConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}
