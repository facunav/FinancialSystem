using FinancialSystem.Domain.Entities;

namespace FinancialSystem.Application.Imports
{
    public interface IStatementParser
    {
        /// <summary>
        /// Identificador único del parser (ej: "BBVA_VISA_AR")
        /// </summary>
        string ParserId { get; }

        /// <summary>
        /// Determina si este parser puede manejar el documento.
        /// Se llama antes de CanParse() para routing automático.
        /// </summary>
        bool CanHandle(IReadOnlyList<string> documentLines);

        /// <summary>
        /// Extrae transacciones del texto completo del PDF.
        /// </summary>
        Task<IReadOnlyList<Transaction>> ParseAsync(
            IReadOnlyList<string> documentLines,
            string sourceFile,
            CancellationToken ct = default);
    }
}
