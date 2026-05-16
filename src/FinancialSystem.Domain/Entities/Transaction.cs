namespace FinancialSystem.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public DateTime CreatedAtUtc { get; set; }
}
