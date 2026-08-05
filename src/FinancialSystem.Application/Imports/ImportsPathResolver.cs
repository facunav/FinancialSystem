namespace FinancialSystem.Application.Imports;

/// <summary>
/// Resuelve FileIngestionOptions.ImportsPath a una ruta absoluta concreta (Patch 0067,
/// PATCH-018) -- extraído de ImportsFolderWatcherHostedService.ResolveImportsPath para
/// que la resolución sea testeable de forma aislada, sin depender de un
/// BackgroundService/FileSystemWatcher real. Mismo algoritmo que ya existía antes de
/// este patch, sin ningún cambio de comportamiento -- lo que cambió es que la ruta
/// personal que traía appsettings.json/appsettings.Development.json como valor de
/// ImportsPath quedó vacía, y este es exactamente el caso que ya sabía resolver
/// (fallback a "{executableBaseDirectory}/imports").
/// </summary>
public static class ImportsPathResolver
{
    /// <summary>
    /// Si <paramref name="configured"/> está vacío/ausente, devuelve
    /// "{executableBaseDirectory}/imports". Si es una ruta relativa, se resuelve contra
    /// <paramref name="contentRootPath"/>. Si es una ruta absoluta, se devuelve tal cual.
    /// </summary>
    public static string Resolve(string? configured, string executableBaseDirectory, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(executableBaseDirectory, "imports");

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRootPath, configured);
    }
}
