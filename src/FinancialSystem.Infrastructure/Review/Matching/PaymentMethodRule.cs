using FinancialSystem.Application.Review;
using FinancialSystem.Domain.Review;

namespace FinancialSystem.Infrastructure.Review.Matching;

/// <summary>
/// Compara el método de pago de dos movimientos. Cuando no puede determinarse
/// para alguno de los dos (dato no informado en la fuente), el score es neutral
/// (0.5) en vez de penalizar el match por falta de información.
/// </summary>
internal sealed class PaymentMethodRule : IMatchingRule
{
    public MatchRuleKind Kind => MatchRuleKind.PaymentMethod;

    public double Evaluate(FinancialMovement reference, FinancialMovement candidate)
    {
        var referenceMethod = Effective(reference);
        var candidateMethod = Effective(candidate);

        if (referenceMethod is null || candidateMethod is null) return 0.5;

        return referenceMethod == candidateMethod ? 1.0 : 0.0;
    }

    /// <summary>
    /// PaymentMethod solo viene informado explícitamente en movimientos legacy/manuales.
    /// Para movimientos bancarios/tarjeta se infiere de la fuente.
    /// </summary>
    private static PaymentMethod? Effective(FinancialMovement movement) =>
        movement.PaymentMethod ?? movement.Source switch
        {
            MovementSource.BankDebit => PaymentMethod.Debit,
            MovementSource.CreditCard => PaymentMethod.Credit,
            _ => null,
        };
}
