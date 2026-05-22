using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Enruta cada archivo al primer handler que lo acepta.
///
/// ALGORITMO:
///   Itera los handlers en el orden en que fueron registrados en DI.
///   El primer CanHandle() = true gana. Si ninguno acepta, loguea warning.
///
/// El orden de registro en DI es la única forma de controlar prioridad.
/// Los handlers más específicos deben registrarse primero.
/// </summary>
public class FileImportRouter : IFileImportRouter
{
    private readonly IReadOnlyList<IFileImportHandler> _handlers;
    private readonly ILogger<FileImportRouter> _logger;

    public FileImportRouter(
        IEnumerable<IFileImportHandler> handlers,
        ILogger<FileImportRouter> logger)
    {
        _handlers = handlers.ToList().AsReadOnly();
        _logger = logger;

        _logger.LogInformation(
            "FileImportRouter: {Count} handlers registrados en orden: [{Handlers}]",
            _handlers.Count,
            string.Join(" → ", _handlers.Select(h => h.HandlerName)));
    }

    public async Task RouteAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        foreach (var handler in _handlers)
        {
            if (!handler.CanHandle(filePath))
                continue;

            _logger.LogInformation(
                "Router: '{File}' → [{Handler}]",
                fileName,
                handler.HandlerName);

            await handler.HandleAsync(filePath, ct);
            return;
        }

        _logger.LogWarning(
            "Router: ningún handler aceptó '{File}'. " +
            "Handlers disponibles: [{Handlers}]",
            fileName,
            string.Join(", ", _handlers.Select(h => h.HandlerName)));
    }
}
