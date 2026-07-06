namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Efecto patrimonial de un movimiento clasificado. Responde "¿cómo afecta mi patrimonio?".
///
/// Es una de las 4 dimensiones de clasificación de ClassifiedMovement.
///
/// REGLA DEL MCP:
///   Solo Expense cuenta para "¿cuánto gasté?" y "¿cuánto necesito para vivir?".
///   InternalMovement y DebtPayment no modifican el patrimonio neto.
///   Income se suma al patrimonio.
/// </summary>
public enum FinancialImpact
{
    /// <summary>
    /// Dinero que salió del patrimonio de forma definitiva.
    /// Ejemplos: supermercado, farmacia, servicios, alquiler, cigarrillos.
    /// Es el ÚNICO tipo que alimenta métricas de gasto del MCP.
    /// </summary>
    Expense = 1,

    /// <summary>
    /// Ingreso de dinero al patrimonio.
    /// Ejemplos: sueldo, cobro de freelance, reintegro, intereses a favor.
    /// </summary>
    Income = 2,

    /// <summary>
    /// Movimiento entre cuentas o personas propias. No modifica el patrimonio neto.
    /// Ejemplos: transferencia a cuenta propia, transferencia a cónyuge para gastos compartidos.
    /// </summary>
    InternalMovement = 3,

    /// <summary>
    /// Pago de una deuda ya registrada como gasto en otro momento.
    /// No debe contabilizarse como gasto adicional para evitar duplicación.
    /// Ejemplos: pago del resumen de tarjeta de crédito, cuota de préstamo.
    /// </summary>
    DebtPayment = 4,
}