namespace FinancialSystem.Application.Imports;

/// <summary>
/// Resultado de la verificación de integridad posterior a una importación (Patch 0056).
/// Issues está vacío cuando IsConsistent es true. Nunca contiene contenido del archivo ni
/// datos sensibles -- solo texto descriptivo de qué conteo/campo no coincidió con qué se
/// esperaba (ver los IImportConsistencyCheck concretos).
/// </summary>
public sealed record ImportConsistencyReport(bool IsConsistent, IReadOnlyList<string> Issues)
{
    public static ImportConsistencyReport Consistent { get; } = new(true, []);

    public static ImportConsistencyReport Inconsistent(IReadOnlyList<string> issues) =>
        new(false, issues);
}
