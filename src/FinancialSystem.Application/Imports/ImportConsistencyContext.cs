namespace FinancialSystem.Application.Imports;

/// <summary>
/// Datos que necesita cualquier <see cref="IImportConsistencyCheck"/> para verificar una
/// corrida ya persistida (Patch 0056 — verificación de integridad posterior a la
/// importación). Se arma una sola vez en FileImportRouter, con los mismos valores que ya
/// se usaron para persistir el ImportBatch -- ningún check necesita volver a calcular
/// nada por su cuenta, solo confirmar que lo persistido coincide con esto.
/// </summary>
public sealed record ImportConsistencyContext(
    Guid ImportBatchId,
    string SourceFile,
    string HandlerName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int ExpectedInserted,
    int ExpectedDuplicates,
    int ExpectedFailed,
    int ExpectedSkipped,
    string? ExpectedParserUsed);
