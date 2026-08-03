namespace FinancialSystem.Application.Imports;

/// <summary>
/// Valida que un archivo tenga forma válida para ser procesado, ANTES de que cualquier
/// IFileImportHandler/IFileParser lo toque (Patch 0051 — Integridad de Importación).
///
/// Responsabilidad acotada a nivel de archivo (existe, es legible, no está vacío) — no
/// conoce reglas de negocio ni el formato específico de ningún banco/parser. La
/// validación estructural propia de cada formato (columnas de CSV/Excel, contenido
/// reconocible de PDF) sigue siendo responsabilidad de cada parser y de
/// FileParserFactory (ver FileParserResolution, Patch 0050) — este validador no la
/// duplica, solo cubre lo que es común a cualquier archivo sin importar su tipo.
/// </summary>
public interface IImportFileValidator
{
    ImportFileValidationResult Validate(string filePath);
}
