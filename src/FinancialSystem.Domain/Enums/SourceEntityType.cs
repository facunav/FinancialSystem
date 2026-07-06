namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Qué tabla contiene el movimiento original referenciado en ClassifiedMovementItem.
/// No existe FK explícita hacia esas tablas (diseño intencional — ver ClassifiedMovementItem).
/// </summary>
public enum SourceEntityType
{
    /// <summary>Registro en la tabla Transactions (extracto tarjeta de crédito).</summary>
    Transaction = 0,

    /// <summary>Registro en la tabla LegacyImportedExpenses (Excel legacy, solo migración histórica).</summary>
    LegacyImport = 1,

    /// <summary>Registro en la tabla BankStatements (extracto débito bancario).</summary>
    BankStatement = 2,
}