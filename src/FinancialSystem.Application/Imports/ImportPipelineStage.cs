namespace FinancialSystem.Application.Imports;

/// <summary>
/// Etapas del pipeline de importación que FileImportRouter puede medir y diagnosticar sin
/// conocer nada de la implementación interna de cada handler/parser (Patch 0057 —
/// observabilidad y diagnóstico, Epic P, P008).
///
/// Extracción agrupa la ejecución completa de IFileImportHandler.HandleAsync -- ese es el
/// límite real de lo que el router puede observar desde afuera sin tocar código de
/// parsers (ver Patch 0055: el pipeline no necesita conocer detalles internos de cada
/// parser). Persistencia, acá, es específicamente la escritura del propio ImportBatch
/// (el registro de auditoría), no la de los movimientos financieros -- esa persistencia
/// ya ocurre dentro de Extracción, dentro del handler.
/// </summary>
public enum ImportPipelineStage
{
    /// <summary>IImportFileValidator.Validate (Patch 0051).</summary>
    Validation,

    /// <summary>El recorrido de handlers registrados hasta encontrar uno con CanHandle=true.</summary>
    ParserSelection,

    /// <summary>Ejecución completa de IFileImportHandler.HandleAsync.</summary>
    Extraction,

    /// <summary>Persistencia del ImportBatch (registro de auditoría), no de los movimientos.</summary>
    Persistence,

    /// <summary>IImportConsistencyVerifier.VerifyAsync (Patch 0056).</summary>
    FinalVerification
}
