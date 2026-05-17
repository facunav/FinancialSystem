namespace FinancialSystem.Application.Imports.Parsing
{
    public sealed class ParseResult<T>
    {
        public bool Success { get; }
        public T? Value { get; }
        public string? Error { get; }

        private ParseResult(bool success, T? value, string? error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static ParseResult<T> Ok(T value) => new(true, value, null);
        public static ParseResult<T> Fail(string reason) => new(false, default, reason);

        public ParseResult<TOut> Map<TOut>(Func<T, TOut> mapper) =>
            Success && Value is not null
                ? ParseResult<TOut>.Ok(mapper(Value))
                : ParseResult<TOut>.Fail(Error ?? "Unknown error");
    }
}
