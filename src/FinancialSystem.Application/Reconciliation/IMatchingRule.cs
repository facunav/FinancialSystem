using FinancialSystem.Domain.Reconciliation;

namespace FinancialSystem.Application.Reconciliation
{
    /// <summary>
    /// Una regla de matching individual. Cada regla evalúa una dimensión
    /// (monto, fecha, descripción) y devuelve un score parcial en [0.0, 1.0].
    ///
    /// EXTENSIÓN: implementar esta interfaz para agregar nuevas heurísticas
    /// sin tocar el motor.
    /// </summary>
    public interface IMatchingRule
    {
        string RuleName { get; }

        /// <summary>
        /// Peso relativo de esta regla en el score total.
        /// El motor normaliza los pesos para que sumen 1.0.
        /// </summary>
        double Weight { get; }

        /// <summary>
        /// Calcula el score de similaridad entre dos movimientos.
        /// Retorna (score, detalle_opcional).
        /// Score en [0.0, 1.0]. Nunca lanza excepciones por datos inválidos.
        /// </summary>
        (double Score, string? Detail) Evaluate(FinancialMovement reference, FinancialMovement candidate);

        /// <summary>
        /// Si retorna true, un score de 0.0 en esta regla descalifica el par
        /// directamente (cortocircuito). Útil para moneda, por ejemplo.
        /// </summary>
        bool IsHardConstraint { get; }
    }
}
