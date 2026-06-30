namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Gasto cargado manualmente en el Excel personal.
/// Entidad SEPARADA de Transaction:
///   Transaction   = verdad contable confirmada por banco/tarjeta.
///   ManualExpense = registro auxiliar del usuario, enriquece clasificación.
///
/// ROL EN EL SISTEMA:
///   No es fuente de verdad financiera. Su propósito es:
///   1. Ayudar al motor de matching a encontrar contrapartes bancarias.
///   2. Detectar posibles gastos olvidados (sin contraparte bancaria).
///   3. Aportar descripción legible cuando el banco solo informa códigos.
///
/// IDEMPOTENCIA:
///   ExternalId es la clave de unicidad para re-importaciones.
///   Formato: SHA256("{SourceFile}|{SheetName}|{RowNumber}") — determinístico.
///   Permite importar el mismo archivo N veces sin duplicar datos.
/// </summary>
public class ManualExpense
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // ── Campos comunes a ambas hojas ─────────────────────────────────────────
    public DateTime Date { get; set; }

    /// <summary>
    /// Descripción del gasto tal como la escribió el usuario en el Excel.
    /// Antes llamado "Category" — renombrado para reflejar su semántica real.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Forma de pago: "Debito", "Credito", "Efectivo".</summary>
    public string PaymentMethod { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";

    public string? Notes { get; set; }

    // ── Campos exclusivos de Gastos Fijos ────────────────────────────────────
    public string? MonthLabel { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? PaidAt { get; set; }

    // ── Clasificación de origen ──────────────────────────────────────────────
    public ManualExpenseSheet Sheet { get; set; }

    // ── Estado ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Indica que el usuario descartó este movimiento explícitamente.
    /// No se elimina físicamente: permite restaurarlo en el futuro.
    /// Un movimiento descartado no aparece en el flujo de conciliación.
    /// </summary>
    public bool IsDiscarded { get; set; }

    /// <summary>Cuándo fue descartado. Null si no está descartado.</summary>
    public DateTime? DiscardedAt { get; set; }

    // ── Trazabilidad e idempotencia ──────────────────────────────────────────
    public string ExternalId { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
    public string? SheetName { get; set; }
    public int? RowNumber { get; set; }
    public DateTime ImportedAtUtc { get; set; }
}

public enum ManualExpenseSheet
{
    Dynamic = 1,  // "Gastos Dinámicos"
    Fixed = 2,  // "Gastos Fijos"
}