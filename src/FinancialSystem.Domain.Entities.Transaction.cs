namespace FinancialSystem.Domain.Entities;

using System.ComponentModel.DataAnnotations.Schema;

public class Transaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateTime CreatedAtUtc { get; set; }

    // CouponNumber is used only during parsing and need not be persisted
    // if the database schema doesn't contain it. Mark as NotMapped to avoid
    // EF Core selecting a non-existent column.
    [NotMapped]
    public string? CouponNumber { get; set; }

    public string? RawLine { get; set; }       // Para debugging/auditoría
    public string? SourceFile { get; set; }    // Trazabilidad
}
