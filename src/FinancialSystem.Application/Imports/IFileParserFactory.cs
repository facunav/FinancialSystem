namespace FinancialSystem.Application.Imports;

public interface IFileParserFactory
{
    /// <summary>
    /// Resuelve el parser que debe procesar el archivo. Ver <see cref="FileParserResolution"/>
    /// para el contrato completo: Resolved (un único parser), NotFound (ninguno) o Ambiguous
    /// (más de uno — nunca se elige automáticamente; el caller debe abortar la importación).
    /// </summary>
    FileParserResolution ResolveParser(string filePath);
}
