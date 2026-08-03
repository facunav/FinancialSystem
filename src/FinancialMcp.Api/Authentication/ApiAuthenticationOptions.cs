namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Configuración del mecanismo de autenticación por API Key (Patch 0058, PATCH-009 —
/// infraestructura de autenticación). Pensado para una aplicación single-user: una única
/// clave estática, sin usuarios, roles ni sesiones.
///
/// Este patch SOLO agrega la infraestructura -- ningún endpoint exige todavía esta
/// autenticación (eso queda para los Patches 0059-0061, que agregarán
/// .RequireAuthorization() a los grupos correspondientes).
/// </summary>
public sealed class ApiAuthenticationOptions
{
    public const string SectionName = "ApiAuthentication";

    /// <summary>
    /// Clave esperada en el encabezado <see cref="ApiKeyAuthenticationHandler.HeaderName"/>.
    /// Vacía por defecto -- nunca se hardcodea acá. Se completa vía appsettings, user
    /// secrets (ya configurados en FinancialMcp.Api.csproj) o la variable de entorno
    /// FINANCIALMCP_API_KEY (ver ApiAuthenticationServiceCollectionExtensions), mismo
    /// criterio que ya usa OpenAIOptions.ApiKey.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
