namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Punto único de registro de la infraestructura de autenticación (Patch 0058, PATCH-009).
/// Program.cs solo llama a AddApiKeyAuthentication -- ni el esquema, ni la clave, ni el
/// origen de configuración quedan expuestos ahí, para que evolucionar el mecanismo más
/// adelante (sección 5 del patch: preparar para proteger Importaciones/Movimientos/
/// Categorías/Planificación/Auditoría) no requiera tocar Program.cs de nuevo, solo esta
/// clase y/o los grupos de endpoints que agreguen .RequireAuthorization().
/// </summary>
public static class ApiAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Variable de entorno de respaldo cuando ApiAuthentication:ApiKey no está en
    /// appsettings/user secrets -- mismo criterio que OpenAIOptions.ApiKey
    /// (DependencyInjection.AddInfrastructure), para no hardcodear credenciales.
    /// </summary>
    public const string ApiKeyEnvironmentVariableName = "FINANCIALMCP_API_KEY";

    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiAuthenticationOptions>(configuration.GetSection(ApiAuthenticationOptions.SectionName));
        services.PostConfigure<ApiAuthenticationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration[ApiKeyEnvironmentVariableName]
                    ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName)
                    ?? string.Empty;
        });

        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, _ => { });

        // AddAuthorization sin políticas propias: ningún endpoint las exige todavía (ver
        // objetivo del patch). Los próximos patches agregan .RequireAuthorization() a los
        // grupos de endpoints correspondientes, reutilizando este mismo registro.
        services.AddAuthorization();

        return services;
    }
}
