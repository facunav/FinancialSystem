namespace FinancialSystem.Domain.Review;

// MODELO UNIFICADO
// FinancialMovement es la vista normalizada de cualquier movimiento,
// independientemente de su fuente (banco, tarjeta, manual/legacy).
// El motor de sugerencias opera exclusivamente sobre este modelo — nunca
// sobre Transaction directamente.
// DECISIÓN DE DISEÑO: record inmutable en lugar de clase mutable.
// El proceso de revisión no modifica movimientos, sólo los cruza para sugerir.
//
// NOTA: el nombre de este tipo no cambia en la refactorización v2.0.
// "FinancialMovement" ya describía correctamente su responsabilidad:
// es la representación neutra de un movimiento, previa a su clasificación.
public sealed record FinancialMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Fecha del movimiento según la fuente original.</summary>
    public required DateTime Date { get; init; }

    /// <summary>Descripción normalizada. Puede ser "FARMACITY SA" o "Farmacia".</summary>
    public required string Description { get; init; }

    /// <summary>Monto positivo = gasto/débito. Negativo = ingreso/crédito.</summary>
    public required decimal Amount { get; init; }

    public string Currency { get; init; } = "ARS";

    /// <summary>De dónde viene este movimiento.</summary>
    public required MovementSource Source { get; init; }

    /// <summary>Categoría semántica heurística del movimiento (puede ser Unknown si no se pudo inferir).
    /// Es una pre-clasificación en memoria para ayudar al matching; no reemplaza la
    /// clasificación definitiva por CategoryId que vive en ClassifiedMovement.</summary>
    public MovementCategory Category { get; init; } = MovementCategory.Unknown;

    /// <summary>
    /// Método de pago declarado. Sólo disponible en movimientos manuales/legacy.
    /// Null = no informado o no aplicable (movimientos bancarios/tarjeta).
    /// </summary>
    public PaymentMethod? PaymentMethod { get; init; }

    /// <summary>Identificador original en la fuente (cupón Visa, número de fila Excel, etc.).</summary>
    public string? OriginalId { get; init; }

    /// <summary>Archivo de origen para trazabilidad completa.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Línea/fila original sin procesar, para debugging.</summary>
    public string? RawLine { get; init; }

    /// <summary>Nombre de hoja (solo aplica a movimientos importados desde Excel legacy).</summary>
    public string? SheetName { get; init; }

    public override string ToString() =>
        $"[{Source}] {Date:dd/MM/yy} | {Description} | {Currency} {Amount:N2}";
}

// ── Enums ────────────────────────────────────────────────────────────────────

public enum MovementSource
{
    BankDebit,       // Extracto débito bancario
    CreditCard,      // PDF tarjeta (Visa, Mastercard, etc.)
    LegacyDynamic,   // Excel legacy hoja "Gastos dinámicos" (solo migración histórica)
    LegacyFixed,     // Excel legacy hoja "Gastos fijos" (solo migración histórica)
}

public enum MovementCategory
{
    Unknown,
    Food,
    Transport,
    Health,
    Entertainment,
    Services,        // electricidad, gas, internet
    Insurance,
    Education,
    Subscription,    // Netflix, Spotify, gym, etc.
    Transfer,
    Income,
    Other,
}

public enum PaymentMethod
{
    Cash,
    Debit,
    Credit,
    Transfer,
}