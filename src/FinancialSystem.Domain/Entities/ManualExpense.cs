namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Gasto cargado manualmente en el Excel personal.
/// Entidad SEPARADA de Transaction:
///   Transaction  = verdad contable confirmada por banco/tarjeta.
///   ManualExpense = intención/registro del usuario, editable.
///
/// IDEMPOTENCIA:
///   ExternalId es la clave de unicidad para re-importaciones.
///   Formato: SHA256("{SourceFile}|{SheetName}|{RowNumber}") — determinístico,
///   permite importar el mismo archivo N veces sin duplicar datos.
/// </summary>
public class ManualExpense
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // ── Campos comunes a ambas hojas ─────────────────────────────

    public DateTime Date { get; set; }

    /// <summary>
    /// Categoría tal como la escribió el usuario.
    /// Ejemplos reales: "Almacen", "Farmacia", "Alquiler", "Medicos", "Nafta".
    /// No se normaliza: se preserva el vocabulario del usuario.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Forma de pago declarada: "Debito", "Credito", "Efectivo".
    /// Nota: el Excel tiene el typo "Creidito" — el parser lo normaliza antes de persistir.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";

    /// <summary>
    /// Notas libres del usuario. Null si la celda estaba vacía.
    /// Ejemplos: "4 patamuslos, tomate", "Abogada", "Gasto hasta el dia 9"
    /// </summary>
    public string? Notes { get; set; }

    // ── Campos exclusivos de Gastos Fijos ────────────────────────

    /// <summary>Mes declarado (ej: "Enero"). Sólo hoja Gastos Fijos.</summary>
    public string? MonthLabel { get; set; }

    /// <summary>"Pagado" | "Pendiente". Sólo hoja Gastos Fijos. Null si no informado.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>Fecha real de pago. Sólo Gastos Fijos cuando Estado = "Pagado".</summary>
    public DateTime? PaidAt { get; set; }

    // ── Clasificación de origen ───────────────────────────────────

    public ManualExpenseSheet Sheet { get; set; }

    // ── Trazabilidad e idempotencia ───────────────────────────────

    /// <summary>
    /// Clave de unicidad para idempotencia. Nunca cambia una vez persistida.
    /// Calculada por el importer, validada por la constraint única en DB.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    public string? SourceFile { get; set; }
    public string? SheetName { get; set; }
    public int? RowNumber { get; set; }

    public DateTime ImportedAtUtc { get; set; }
}

public enum ManualExpenseSheet
{
    Dynamic = 1,  // "Gastos Dinámicos"
    Fixed   = 2,  // "Gastos Fijos"
}
