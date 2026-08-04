namespace FinancialSystem.Api.Imports;

/// <summary>
/// Límites de la recepción de archivos en POST /api/imports (Patch 0063, PATCH-014).
/// Explícito y configurable -- reemplaza la dependencia de los valores implícitos por
/// default del framework (Kestrel ~28.6 MB, FormOptions.MultipartBodyLengthLimit 128 MB)
/// por un único valor propio de la aplicación, sin tocar la configuración global del
/// servidor (no afecta a ningún otro endpoint -- ver Program.cs).
/// </summary>
public sealed class ImportUploadOptions
{
    public const string SectionName = "ImportUpload";

    /// <summary>
    /// Tamaño máximo aceptado para un archivo de importación, en bytes. Default: 20 MB
    /// -- generoso para extractos bancarios PDF/XLS/CSV reales (unos pocos MB como
    /// mucho) y por debajo del límite implícito histórico de Kestrel que reemplaza.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}
