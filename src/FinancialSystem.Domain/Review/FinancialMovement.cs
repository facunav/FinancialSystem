namespace FinancialSystem.Domain.Review;

// MODELO UNIFICADO
// FinancialMovement es la vista normalizada de un movimiento de banco/tarjeta.
// El motor de revisión opera exclusivamente sobre este modelo — nunca sobre
// Transaction/BankStatement directamente.
// DECISIÓN DE DISEÑO: record inmutable en lugar de clase mutable.
// El proceso de revisión no modifica movimientos, sólo los agrupa para detectar
// posibles duplicados/splits.
//
// NOTA: el nombre de este tipo no cambia. "FinancialMovement" ya describía
// correctamente su responsabilidad: es la representación neutra de un
// movimiento, previa a su clasificación. Hasta PR-L4 también incluía
// movimientos legacy/manuales (Excel) como candidatos de matching — ver
// ReviewResult.cs para el detalle de por qué ese mecanismo se retiró.
public sealed record FinancialMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Id real de la entidad persistida en su tabla de origen (Transaction.Id,
    /// BankStatement.Id o LegacyImportedExpense.Id según <see cref="Source"/>).
    /// Es el identificador técnico: el que hay que enviar como SourceId en
    /// ClassifyMovementCommand. No usar <see cref="OriginalId"/> para eso — esa es
    /// una referencia de negocio, no necesariamente un Guid.
    /// </summary>
    public required Guid SourceId { get; init; }

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
    /// Referencia de negocio en la fuente (cupón Visa, número de fila Excel, etc.),
    /// solo para mostrar en UI o trazabilidad. No es un identificador técnico
    /// estable ni necesariamente un Guid — para eso usar <see cref="SourceId"/>.
    /// </summary>
    public string? OriginalId { get; init; }

    /// <summary>Archivo de origen para trazabilidad completa.</summary>
    public string? SourceFile { get; init; }

    /// <summary>Línea/fila original sin procesar, para debugging.</summary>
    public string? RawLine { get; init; }

    /// <summary>Nombre de hoja (solo aplica a movimientos importados desde Excel legacy).</summary>
    public string? SheetName { get; init; }

    /// <summary>
    /// Cuenta financiera asignada al movimiento de origen (Transaction/BankStatement).
    /// Pasamano de solo lectura: no participa en ninguna regla de matching — el motor
    /// de sugerencias no lo usa. Null en movimientos legacy/manuales, que no soportan
    /// esta asignación (ver docs/RoadMaps/FinancialMcp-vNext.md, Épica J).
    /// </summary>
    public Guid? FinancialAccountId { get; init; }

    public override string ToString() =>
        $"[{Source}] {Date:dd/MM/yy} | {Description} | {Currency} {Amount:N2}";
}

// ── Enums ────────────────────────────────────────────────────────────────────

// PR-L4: LegacyDynamic/LegacyFixed (Excel "Gastos Dinámicos"/"Gastos Fijos") se
// retiraron — MovementLoader ya no produce movimientos de esa fuente y no es un
// enum persistido en ningún lado (a diferencia de MovementRole/SourceEntityType),
// así que no hay riesgo de datos históricos al sacar los valores.
public enum MovementSource
{
    BankDebit,       // Extracto débito bancario
    CreditCard,      // PDF tarjeta (Visa, Mastercard, etc.)
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