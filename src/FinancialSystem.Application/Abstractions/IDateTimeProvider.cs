namespace FinancialSystem.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
