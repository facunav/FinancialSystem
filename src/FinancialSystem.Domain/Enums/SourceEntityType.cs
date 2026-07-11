namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Qué tabla contiene el movimiento original referenciado en ClassifiedMovementItem.
/// No existe FK explícita hacia esas tablas (diseño intencional — ver ClassifiedMovementItem).
/// </summary>
public enum SourceEntityType
{
    /// <summary>Registro en la tabla Transactions (extracto tarjeta de crédito).</summary>
    Transaction = 0,

    /// <summary>
    /// PR-L5: la tabla LegacyImportedExpenses que este valor identificaba se eliminó —
    /// ya no se genera ningún ClassifiedMovementItem nuevo con este origen. Se conserva
    /// el valor de enum (sin cambiar el número) porque filas históricas de
    /// ClassifiedMovementItem ya persistidas lo usan y deben seguir siendo válidas.
    /// </summary>
    LegacyImport = 1,

    /// <summary>Registro en la tabla BankStatements (extracto débito bancario).</summary>
    BankStatement = 2,
}