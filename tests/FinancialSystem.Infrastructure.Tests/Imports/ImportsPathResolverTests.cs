using FinancialSystem.Application.Imports;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Imports;

/// <summary>
/// Cubre ImportsPathResolver (Patch 0067, PATCH-018) -- la lógica de resolución de
/// FileIngestion:ImportsPath, extraída de ImportsFolderWatcherHostedService para
/// poder probarla de forma aislada sin depender de un BackgroundService/
/// FileSystemWatcher real (que vive en hosts/FinancialSystem.Worker, sin un proyecto
/// de tests propio).
/// </summary>
public class ImportsPathResolverTests
{
    private const string ExecutableBaseDirectory = "/build/output";
    private const string ContentRootPath = "/repo/hosts/FinancialSystem.Worker";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithEmptyConfiguredPath_FallsBackToExecutableImportsFolder(string? configured)
    {
        // Sección "Mantener funcionamiento" del patch: con el default vacío que ahora
        // trae appsettings.json (Patch 0067), el arranque debe seguir siendo válido.
        var resolved = ImportsPathResolver.Resolve(configured, ExecutableBaseDirectory, ContentRootPath);

        Assert.Equal(Path.Combine(ExecutableBaseDirectory, "imports"), resolved);
    }

    [Fact]
    public void Resolve_WithRelativeConfiguredPath_ResolvesAgainstContentRoot()
    {
        var resolved = ImportsPathResolver.Resolve("data/incoming", ExecutableBaseDirectory, ContentRootPath);

        Assert.Equal(Path.Combine(ContentRootPath, "data/incoming"), resolved);
    }

    [Fact]
    public void Resolve_WithAbsoluteConfiguredPath_ReturnsItUnchanged()
    {
        // Ruta absoluta genérica de prueba (no una ruta real de ningún desarrollador
        // en particular) -- prueba que el arranque funciona con una ruta configurada
        // explícitamente por el usuario, sin importar el sistema operativo del CI.
        var absolute = OperatingSystem.IsWindows() ? "C:\\imports-de-prueba" : "/imports-de-prueba";

        var resolved = ImportsPathResolver.Resolve(absolute, ExecutableBaseDirectory, ContentRootPath);

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void DefaultFileIngestionOptions_ImportsPathIsNullOrEmpty_NotAPersonalPath()
    {
        // No existen dependencias de rutas personales (sección 5 del patch): el
        // default de la clase de opciones nunca trae una ruta real embebida.
        var options = new FileIngestionOptions();

        Assert.True(string.IsNullOrEmpty(options.ImportsPath));
    }
}
