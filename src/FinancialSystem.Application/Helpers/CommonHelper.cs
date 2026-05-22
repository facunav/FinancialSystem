namespace FinancialSystem.Application.Helpers
{
    public static class CommonHelper
    {
        public static string NormalizePaymentMethod(string raw) =>
    SheetParserHelpers.NormalizePaymentMethod(raw);

        public static string BuildExternalId(string sourceFile, string sheetName, int rowNumber) =>
            SheetParserHelpers.BuildExternalId(sourceFile, sheetName, rowNumber);
    }
}
