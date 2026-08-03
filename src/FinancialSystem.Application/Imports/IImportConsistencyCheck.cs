namespace FinancialSystem.Application.Imports;

/// <summary>
/// Una verificación individual de integridad posterior a la importación (Patch 0056,
/// Epic P, P007). Cada implementación cubre un único aspecto (cantidades, identidad,
/// historial) y devuelve la lista de problemas que encontró -- vacía si no encontró
/// ninguno.
///
/// EXTENSIBILIDAD (sección 5 del patch): agregar una verificación nueva es agregar una
/// nueva implementación de esta interfaz y registrarla en DI -- IImportConsistencyVerifier
/// las ejecuta todas por igual, sin que FileImportRouter ni las implementaciones
/// existentes necesiten cambiar.
/// </summary>
public interface IImportConsistencyCheck
{
    /// <summary>Nombre corto para logging/diagnóstico. Ej.: "Quantities", "Identity", "History".</summary>
    string Name { get; }

    Task<IReadOnlyList<string>> CheckAsync(ImportConsistencyContext context, CancellationToken ct = default);
}
