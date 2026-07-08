namespace FinancialSystem.Application.Imports;

public interface IImportFileSink
{
    Task<ImportRunResult> HandleFileAsync(string filePath, CancellationToken cancellationToken = default);
}
