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
    /// Ejemplos reales: "Almacen", "Farmacia", "Alquiler", "Medicos", "Nafta".
    /// No se normaliza: se preserva el vocabulario del usuario.
    /// Usado por MovementAdapter para matching de descripción.
    /// Antes llamado "Category" — renombrado para reflejar su semántica real.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Forma de pago declarada: "Debito", "Credito", "Efectivo".
    /// El parser normaliza el typo "Creidito" → "Credito" antes de persistir.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";

    /// <summary>
    /// Notas libres del usuario. Null si la celda estaba vacía.
    /// Ejemplos: "4 patamuslos, tomate", "Abogada", "Gasto hasta el dia 9".
    /// </summary>
    public string? Notes { get; set; }

    // ── Campos exclusivos de Gastos Fijos ────────────────────────────────────

    /// <summary>Mes declarado (ej: "Enero"). Solo hoja Gastos Fijos.</summary>
    public string? MonthLabel { get; set; }

    /// <summary>"Pagado" | "Pendiente". Solo hoja Gastos Fijos. Null si no informado.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>Fecha real de pago. Solo Gastos Fijos cuando Estado = "Pagado".</summary>
    public DateTime? PaidAt { get; set; }

    // ── Clasificación de origen ───────────────────────────────────────────────

    public ManualExpenseSheet Sheet { get; set; }

    // ── Trazabilidad e idempotencia ───────────────────────────────────────────

    /// <summary>
    /// Clave de unicidad para idempotencia. Nunca cambia una vez persistida.
    /// Calculada por el importer, validada por constraint única en DB.
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
    Fixed = 2,  // "Gastos Fijos"
}
