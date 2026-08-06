using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Domain.Review;

/// <summary>
/// Conversión entre las dos formas en que el dominio identifica el origen de un
/// movimiento: <see cref="MovementSource"/> (banco/tarjeta, la vista neutra que usa el
/// motor de revisión, ver FinancialMovement) y <see cref="SourceEntityType"/> (la tabla
/// real de origen -- Transaction/BankStatement/LegacyImport -- que usan
/// ClassifiedMovementItem, InvestigationReference, MovementAuditDecision y
/// ClassificationSuggestionSet para referenciar un movimiento sin FK explícita).
///
/// Extraído a un único lugar (PATCH-042, Epic V) -- antes existían dos copias
/// idénticas, privadas, en ClassificationSuggestionService y AuditReportService
/// (ambas en FinancialSystem.Infrastructure). Vive en Domain porque es una conversión
/// pura entre dos enums de dominio, sin dependencias de infraestructura ni de
/// aplicación -- cualquier capa superior puede usarla sin crear una dependencia nueva
/// hacia Infrastructure, y sin duplicarla otra vez.
/// </summary>
public static class MovementSourceExtensions
{
    public static SourceEntityType ToSourceEntityType(this MovementSource source) => source switch
    {
        MovementSource.BankDebit => SourceEntityType.BankStatement,
        MovementSource.CreditCard => SourceEntityType.Transaction,
        _ => throw new ArgumentOutOfRangeException(
            nameof(source), source, "MovementSource sin mapeo a SourceEntityType conocido."),
    };
}
