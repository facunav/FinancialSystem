namespace FinancialSystem.Application.Imports
{
    public interface IStatementParserRegistry
    {
        void Register(IStatementParser parser);
        IStatementParser? Resolve(IReadOnlyList<string> documentLines);
    }
}
