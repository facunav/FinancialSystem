namespace FinancialSystem.Application.Imports;

public interface IImportFileSink
{
    Task HandleFileAsync(string filePath, CancellationToken cancellationToken = default);
}
