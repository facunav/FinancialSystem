namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Efecto patrimonial de un gasto procesado.
    /// Es la clasificación semántica más importante para el MCP:
    /// permite calcular gasto neto real excluyendo movimientos internos.
    ///
    /// Regla de uso:
    ///   Solo RealExpense cuenta para "¿cuánto gasté?" y "¿cuánto necesito para vivir?".
    ///   InternalTransfer y DebtPayment no modifican el patrimonio neto.
    ///   Income se suma al patrimonio.
    /// </summary>
    public enum FinancialImpact
    {
        /// <summary>
        /// Dinero que salió del patrimonio de forma definitiva.
        /// Ejemplos: supermercado, farmacia, servicios, alquiler, cigarrillos.
        /// Es el único tipo que alimenta métricas de gasto del MCP.
        /// </summary>
        RealExpense = 1,

        /// <summary>
        /// Movimiento entre cuentas o personas propias. No modifica el patrimonio neto.
        /// Ejemplos: transferencia a cuenta propia, transferencia a cónyuge para gastos compartidos.
        /// </summary>
        InternalTransfer = 2,

        /// <summary>
        /// Pago de una deuda ya registrada como gasto en otro momento.
        /// Ejemplos: pago del resumen de tarjeta de crédito, cuota de préstamo.
        /// No debe contabilizarse como gasto adicional para evitar duplicación.
        /// </summary>
        DebtPayment = 3,

        /// <summary>
        /// Ingreso de dinero al patrimonio.
        /// Ejemplos: sueldo, cobro de freelance, reintegro, intereses a favor.
        /// </summary>
        Income = 4,
    }
}
