namespace FinancialSystem.Application.Imports;

public interface ITransactionNormalizer
{
    ParsedTransaction Normalize(ExtractedTransaction extracted);

    IReadOnlyList<ParsedTransaction> NormalizeAll(IEnumerable<ExtractedTransaction> extracted);
}
