namespace FinancialSystem.Domain.Enums;

/// <summary>
/// Tipo de movimiento financiero. Responde "¿qué ocurrió?".
///
/// Es una de las 4 dimensiones de clasificación de ClassifiedMovement.
/// Reemplaza el antiguo enum cerrado ReviewReason, que mezclaba el "motivo"
/// de no tener contraparte con el tipo real del movimiento.
///
/// DIFERENCIA CON FinancialImpact:
///   MovementType describe la NATURALEZA de la operación (qué fue).
///   FinancialImpact describe el EFECTO en el patrimonio (cómo afecta).
///   Ejemplo: una "Comisión bancaria" es MovementType=Fee + FinancialImpact=Expense.
///   Una "Transferencia a cuenta propia" es MovementType=Transfer + FinancialImpact=InternalMovement.
/// </summary>
public enum MovementType
{
    /// <summary>Compra de bien o servicio. Caso más frecuente en débito y tarjeta.</summary>
    Purchase = 1,

    /// <summary>Transferencia de fondos entre cuentas o personas.</summary>
    Transfer = 2,

    /// <summary>Pago de deuda, factura o servicio.</summary>
    Payment = 3,

    /// <summary>Cobro: entrada de dinero por venta, servicio prestado o similar.</summary>
    Receipt = 4,

    /// <summary>Comisión cobrada por el banco u otro proveedor financiero.</summary>
    Fee = 5,

    /// <summary>Interés acreditado o debitado.</summary>
    Interest = 6,

    /// <summary>Reintegro o devolución de un gasto anterior.</summary>
    Refund = 7,

    /// <summary>Ajuste contable o corrección.</summary>
    Adjustment = 8,

    /// <summary>No encaja en ninguna categoría anterior.</summary>
    Other = 99,
}