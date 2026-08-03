namespace FinancialSystem.Application.Imports;

/// <summary>
/// Orquesta todos los IImportConsistencyCheck registrados sobre una corrida ya persistida
/// (Patch 0056). FileImportRouter la invoca una única vez, después de persistir el
/// ImportBatch de una corrida que sí llegó a ejecutar un handler -- no conoce ni necesita
/// conocer cuántos ni cuáles checks corren.
/// </summary>
public interface IImportConsistencyVerifier
{
    Task<ImportConsistencyReport> VerifyAsync(ImportConsistencyContext context, CancellationToken ct = default);
}
