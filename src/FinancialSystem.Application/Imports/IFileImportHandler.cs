namespace FinancialSystem.Application.Imports;

/// <summary>
/// Contrato para un handler de importación de archivos.
///
/// Cada implementación conoce:
///   1. Qué archivos puede procesar (CanHandle)
///   2. Cómo procesarlos (HandleAsync)
///
/// El router itera los handlers registrados en orden y delega
/// al primero que devuelva CanHandle = true.
///
/// EXTENSIÓN:
///   Para agregar soporte a una nueva fuente (JSON, OFX, etc.):
///   1. Implementar IFileImportHandler
///   2. Registrar en DI antes de TransactionImportHandler
///   Sin tocar el watcher, el router, ni los handlers existentes.
/// </summary>
public interface IFileImportHandler
{
    /// <summary>
    /// Nombre descriptivo para logging. Ejemplo: "ManualExpense", "Transaction".
    /// </summary>
    string HandlerName { get; }

    /// <summary>
    /// Determina si este handler puede procesar el archivo dado.
    /// Debe ser rápido: solo inspecciona nombre/extensión, no abre el archivo.
    /// </summary>
    bool CanHandle(string filePath);

    /// <summary>
    /// Procesa el archivo. Solo se llama si CanHandle devolvió true.
    /// </summary>
    Task HandleAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
/// Enruta un archivo al handler correcto.
/// El watcher solo conoce esta interfaz — no sabe nada de parsers ni importers.
/// </summary>
public interface IFileImportRouter
{
    Task RouteAsync(string filePath, CancellationToken ct = default);
}
