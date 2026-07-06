namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Movimiento de cuenta bancaria (caja de ahorros / cuenta corriente).
/// Entidad separada de Transaction (tarjetas) y LegacyImportedExpense (Excel manual).
///
/// SEMÁNTICA DE AMOUNT:
///   Positivo = crédito (ingreso: transferencias recibidas, intereses)
///   Negativo = débito  (egreso: pagos, extracciones, transferencias enviadas)
///   El signo se preserva tal como viene del banco — sin inversión.
///
/// IDEMPOTENCIA:
///   No existe número de operación único en el XLS del BBVA.
///   ExternalId = SHA256("{FileName}|{SheetName}|row|{RowNumber}")
///   Riesgo documentado: si el banco re-exporta con filas insertadas en el
///   medio, los RowNumbers cambian y esas filas se re-insertan.
///   Es el mejor compromiso posible dado el formato del archivo.
/// </summary>
public class BankStatement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // ── Datos del movimiento ──────────────────────────────────────

    public DateTime Date { get; set; }

    /// <summary>Descripción principal. Ej: "PAGO CON VISA DEBITO 96477108 OP2511"</summary>
    public string Concept { get; set; } = string.Empty;

    /// <summary>
    /// Canal/detalle secundario del banco. Ej: "100 - BANCA ONLINE".
    /// Null si la celda estaba vacía o contenía solo el código sin descripción.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Importe. Positivo = crédito. Negativo = débito.
    /// </summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "ARS";

    /// <summary>Saldo de la cuenta después de este movimiento.</summary>
    public decimal? Balance { get; set; }

    // ── Origen ───────────────────────────────────────────────────

    /// <summary>Nombre del banco. Ej: "BBVA". Permite filtrar por banco.</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>Número de cuenta extraído del título. Ej: "214-45099/4"</summary>
    public string? AccountNumber { get; set; }

    // ── Trazabilidad e idempotencia ───────────────────────────────

    public string ExternalId { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
    public string? SheetName { get; set; }
    public int? RowNumber { get; set; }
    public DateTime ImportedAtUtc { get; set; }
}
