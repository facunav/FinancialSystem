namespace FinancialSystem.Application.Imports;

public interface IFileParser
{
    IReadOnlyCollection<string> SupportedExtensions { get; }

    Task<FileParseResult> ParseAsync(string filePath, CancellationToken cancellationToken = default);
}
