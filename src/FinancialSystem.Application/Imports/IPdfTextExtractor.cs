namespace FinancialSystem.Application.Imports
{
    public interface IPdfTextExtractor
    {
        /// <summary>
        /// Extrae líneas de texto del PDF preservando el orden de lectura.
        /// </summary>
        Task<IReadOnlyList<string>> ExtractLinesAsync(string filePath, CancellationToken ct = default);
    }
}
