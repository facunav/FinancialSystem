using FinancialSystem.Application.Imports;
using Microsoft.Extensions.Logging;

namespace FinancialSystem.Infrastructure.Imports;

/// <summary>
/// Handler catch-all para transacciones bancarias: PDF, CSV, y xlsx bancarios.
///
/// DETECCIÓN:
///   Acepta cualquier archivo con extensión soportada que no fue tomado
///   por un handler anterior (BbvaBankStatementImportHandler).
///   Registrarse ÚLTIMO en DI es parte de su contrato implícito.
///
/// EJECUCIÓN:
///   Delega a IImportFileSink — el pipeline genérico existente.
///   No cambia nada de ese pipeline.
/// </summary>
internal sealed class TransactionImportHandler : IFileImportHandler
{
    private static readonly HashSet<string> SupportedExtensions =
        new(FileIngestionOptions.WatchedExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly IImportFileSink _sink;
    private readonly ILogger<TransactionImportHandler> _logger;

    public TransactionImportHandler(
        IImportFileSink sink,
        ILogger<TransactionImportHandler> logger)
    {
        _sink = sink;
        _logger = logger;
    }

    public string HandlerName => "Transaction";

    public bool CanHandle(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public async Task HandleAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[Transaction] Procesando {File}",
            Path.GetFileName(filePath));

        await _sink.HandleFileAsync(filePath, ct);
    }
}
