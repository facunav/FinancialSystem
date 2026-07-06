namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Registro importado desde el Excel personal histórico del usuario.
///
/// NOMBRE INTENCIONAL:
///   "Legacy" porque este tipo de dato es exclusivamente para migración histórica.
///   El flujo principal del sistema ya no depende de Excel.
///   Toda la información financiera futura vive en entidades propias del sistema.
///
/// ROL EN EL SISTEMA:
///   No es fuente de verdad financiera. Su único propósito actual es:
///   1. Proveer candidatos al motor de sugerencias de matching para datos históricos.
///   2. Detectar posibles movimientos olvidados durante la transición.
///   3. Aportar descripción más legible cuando el banco reporta solo códigos.
///
/// CUANDO DESAPAREZCA:
///   Una vez que el usuario haya migrado y clasificado todo su historial,
///   esta tabla deja de recibir datos. No se elimina (preserva el historial
///   del snapshot en ClassifiedMovementItem) pero se vuelve inerte.
///
/// IDEMPOTENCIA:
///   ExternalId es la clave de unicidad para re-importaciones.
///   Formato: SHA256("{SourceFile}|{SheetName}|{RowNumber}") — determinístico.
/// </summary>
public class LegacyImportedExpense
{
    public Guid Id { get; init; } = Guid.NewGuid();

    // ── Datos del registro ────────────────────────────────────────────────────

    public DateTime Date { get; set; }

    /// <summary>
    /// Descripción del gasto tal como la escribió el usuario en el Excel.
    /// Ejemplos: "Almacen", "Farmacia", "Alquiler", "Medicos", "Nafta".
    /// No se normaliza: preserva el vocabulario del usuario para el matching de texto.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Forma de pago declarada: "Debito", "Credito", "Efectivo".
    /// Usado por la regla de matching de método de pago.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";

    /// <summary>Notas libres del usuario. Null si la celda estaba vacía.</summary>
    public string? Notes { get; set; }

    // ── Metadatos de origen (solo para gastos fijos legacy) ──────────────────
    // Estos campos solo se populan cuando Sheet = LegacyFixed.
    // Una vez que el módulo nativo de Gastos Fijos esté implementado,
    // esta información migra allá y estos campos quedan obsoletos.

    public LegacyImportSheet Sheet { get; set; }
    public string? MonthLabel { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? PaidAt { get; set; }

    // ── Estado ────────────────────────────────────────────────────────────────

    /// <summary>
    /// True = el usuario descartó explícitamente este registro.
    /// No se elimina físicamente: permite restaurarlo si fue un error.
    /// Un registro descartado no aparece como candidato de sugerencias.
    /// </summary>
    public bool IsDiscarded { get; set; }
    public DateTime? DiscardedAt { get; set; }

    // ── Trazabilidad e idempotencia ────────────────────────────────────────────

    public string ExternalId { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
    public string? SheetName { get; set; }
    public int? RowNumber { get; set; }
    public DateTime ImportedAtUtc { get; set; }
}

public enum LegacyImportSheet
{
    Dynamic = 1,  // Hoja "Gastos Dinámicos" del Excel
    Fixed = 2,  // Hoja "Gastos Fijos" del Excel
}