namespace FinancialSystem.Application.Imports;

public interface IFileParserFactory
{
    bool TryGetParser(string filePath, out IFileParser? parser);
}
