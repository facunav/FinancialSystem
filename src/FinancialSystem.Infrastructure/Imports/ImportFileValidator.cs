using FinancialSystem.Application.Imports;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Implementación de <see cref="IImportFileValidator"/> (Patch 0051). Solo verifica que
/// el archivo exista, sea legible y no esté vacío — validación de "forma", no de
/// contenido ni de reglas de negocio. Se ejecuta en FileImportRouter.RouteAsync antes de
/// ofrecer el archivo a cualquier IFileImportHandler.
/// </summary>
internal sealed class ImportFileValidator : IImportFileValidator
{
    public ImportFileValidationResult Validate(string filePath)
    {
        if (!File.Exists(filePath))
            return ImportFileValidationResult.Invalid(
                $"El archivo no existe o no es un archivo regular: '{filePath}'.");

        FileInfo info;
        try
        {
            info = new FileInfo(filePath);
        }
        catch (Exception ex)
        {
            return ImportFileValidationResult.Invalid($"No se pudo acceder al archivo: {ex.Message}");
        }

        if (info.Length == 0)
            return ImportFileValidationResult.Invalid("El archivo está vacío (0 bytes).");

        // Confirma que el archivo se puede abrir para lectura -- no lee ni interpreta
        // contenido, eso sigue siendo responsabilidad exclusiva del parser.
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            return ImportFileValidationResult.Invalid($"No se pudo leer el archivo: {ex.Message}");
        }

        return ImportFileValidationResult.Valid;
    }
}
