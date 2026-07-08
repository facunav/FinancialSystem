namespace FinancialSystem.Domain.Entities;

public class Transaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateTime CreatedAtUtc { get; set; }
    public string? CouponNumber { get; set; }
    public string? RawLine { get; set; }       // Para debugging/auditoría
    public string? SourceFile { get; set; }    // Trazabilidad

    /// <summary>
    /// Identificador determinístico para idempotencia (ver SheetParserHelpers.BuildTransactionExternalId).
    /// Nullable: filas existentes previas a esta columna quedan sin valor — Postgres no las
    /// considera duplicadas entre sí en el índice único (NULL != NULL). Toda fila nueva la recibe.
    /// </summary>
    public string? ExternalId { get; set; }
}

