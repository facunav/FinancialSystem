using FinancialSystem.Domain.Review;

namespace FinancialSystem.Application.Review;

/// <summary>
/// Dimensión de comparación que evalúa una <see cref="IMatchingRule"/> individual.
/// Corresponde 1:1 a los componentes de <see cref="MatchScore"/>.
/// </summary>
public enum MatchRuleKind
{
    Amount,
    Date,
    Description,
    PaymentMethod,
}

/// <summary>
/// Una regla individual de comparación entre un movimiento de referencia y uno candidato.
/// <see cref="IMatchScorer"/> compone el resultado de todas las reglas registradas.
/// </summary>
public interface IMatchingRule
{
    /// <summary>Dimensión del score que esta regla aporta.</summary>
    MatchRuleKind Kind { get; }

    /// <summary>Score en [0.0, 1.0]. 1.0 = coincidencia perfecta en esta dimensión.</summary>
    double Evaluate(FinancialMovement reference, FinancialMovement candidate);
}
