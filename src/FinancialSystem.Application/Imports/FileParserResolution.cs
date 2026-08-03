namespace FinancialSystem.Application.Imports;

/// <summary>
/// Resultado de intentar resolver qué IFileParser debe procesar un archivo.
///
/// POR QUÉ EXISTE (Patch 0050 — Integridad de Importación):
///   El contrato anterior (TryGetParser(string, out IFileParser?) -> bool) no distinguía
///   "ningún parser reconoce este archivo" de "más de un parser lo reconoce y no hay forma
///   segura de elegir uno" — el segundo caso se resolvía en silencio por orden de registro
///   en DI (ver el historial de FileParserFactory.ResolvePdfParser), con riesgo real de que
///   un archivo terminara procesado por el parser equivocado sin ningún error visible (caso
///   conocido: un PDF BBVA Mastercard reconocido por el fingerprint de BBVA Visa). Ahora la
///   ambigüedad es un resultado explícito — el caller debe abortar la importación, nunca
///   elegir automáticamente.
/// </summary>
public enum FileParserResolutionStatus
{
    /// <summary>Un único parser reconoció el archivo. <see cref="FileParserResolution.Parser"/> nunca es null.</summary>
    Resolved,

    /// <summary>Ningún parser registrado reconoció el archivo.</summary>
    NotFound,

    /// <summary>
    /// Más de un parser reconoció el mismo archivo. No se elige ninguno — el caller debe
    /// abortar la importación e informar el conflicto usando <see cref="FileParserResolution.ConflictingParserIds"/>.
    /// </summary>
    Ambiguous
}

public sealed record FileParserResolution(
    FileParserResolutionStatus Status,
    IFileParser? Parser,
    IReadOnlyList<string> ConflictingParserIds)
{
    public static FileParserResolution Resolved(IFileParser parser) =>
        new(FileParserResolutionStatus.Resolved, parser, []);

    public static FileParserResolution NotFound { get; } =
        new(FileParserResolutionStatus.NotFound, null, []);

    public static FileParserResolution Ambiguous(IReadOnlyList<string> conflictingParserIds) =>
        new(FileParserResolutionStatus.Ambiguous, null, conflictingParserIds);
}
