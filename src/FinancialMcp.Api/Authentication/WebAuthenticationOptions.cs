namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Configuración del mecanismo de autenticación de la interfaz web (Patch 0067A):
/// una única contraseña compartida, independiente de <see cref="ApiAuthenticationOptions.ApiKey"/>
/// -- misma filosofía single-user que ApiAuthenticationOptions, pero un secreto propio,
/// para que rotar la contraseña de la UI no obligue a rotar la clave de las
/// integraciones externas (y viceversa).
/// </summary>
public sealed class WebAuthenticationOptions
{
    public const string SectionName = "WebAuthentication";

    /// <summary>
    /// Contraseña esperada en POST /api/auth/login. Vacía por defecto -- nunca se
    /// hardcodea acá. Se completa vía appsettings, User Secrets, o la variable de
    /// entorno FINANCIALMCP_WEB_PASSWORD (ver
    /// ApiAuthenticationServiceCollectionExtensions.AddApiAuthentication), mismo
    /// criterio que ApiAuthenticationOptions.ApiKey.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}
