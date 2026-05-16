using FinancialSystem.Application.Imports;

namespace FinancialSystem.Infrastructure.Imports.Parsers;

internal sealed class FileParserFactory(IEnumerable<IFileParser> parsers) : IFileParserFactory
{
    private readonly IReadOnlyList<IFileParser> _parsers = parsers.ToList();

    public bool TryGetParser(string filePath, out IFileParser? parser)
    {
        var extension = Path.GetExtension(filePath);
        parser = _parsers.FirstOrDefault(p =>
            p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        return parser is not null;
    }
}
