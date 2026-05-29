namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Qué tabla/entidad contiene el registro original referenciado.
    /// Extensible: agregar nuevos valores sin modificar ReconciledExpenseItem.
    /// </summary>
    public enum SourceEntityType
    {
        /// <summary>Movimiento de tarjeta de crédito. Tabla: Transactions.</summary>
        Transaction = 0,

        /// <summary>Gasto cargado manualmente. Tabla: ManualExpenses.</summary>
        ManualExpense = 1,

        /// <summary>Movimiento bancario de caja de ahorros/cuenta corriente. Tabla: BankStatements.</summary>
        BankStatement = 2,

        // Reservado para renaming futuro si BankStatement pasa a AccountMovement:
        AccountMovement = 3,
    }
}
