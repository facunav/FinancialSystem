using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Punto único de registro de la infraestructura de autenticación (Patch 0058, PATCH-009;
/// extendido en el Patch 0067A con Cookie Authentication para la UI web). Program.cs solo
/// llama a AddApiAuthentication -- ni los esquemas, ni las claves, ni el origen de
/// configuración quedan expuestos ahí, para que evolucionar el mecanismo más adelante no
/// requiera tocar Program.cs de nuevo, solo esta clase y/o los grupos de endpoints que
/// agreguen .RequireAuthorization().
/// </summary>
public static class ApiAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Variable de entorno de respaldo cuando ApiAuthentication:ApiKey no está en
    /// appsettings/user secrets -- mismo criterio que OpenAIOptions.ApiKey
    /// (DependencyInjection.AddInfrastructure), para no hardcodear credenciales.
    /// </summary>
    public const string ApiKeyEnvironmentVariableName = "FINANCIALMCP_API_KEY";

    /// <summary>
    /// Variable de entorno de respaldo cuando WebAuthentication:Password no está en
    /// appsettings/user secrets (Patch 0067A) -- mismo criterio que ApiKeyEnvironmentVariableName.
    /// </summary>
    public const string WebPasswordEnvironmentVariableName = "FINANCIALMCP_WEB_PASSWORD";

    /// <summary>
    /// Nombre del esquema "virtual" (Patch 0067A) que decide, por request, si autenticar
    /// vía ApiKey o vía Cookie -- ver AddApiAuthentication. Nunca se usa para SignIn/SignOut
    /// (eso apunta siempre, explícitamente, al esquema Cookie real -- ver AuthEndpoints).
    /// </summary>
    public const string DualSchemeName = "ApiKeyOrCookie";

    /// <summary>
    /// Registro histórico (Patch 0058): un único esquema, ApiKey. Se mantiene sin cambios
    /// -- varios tests de patches anteriores lo llaman directamente para levantar un host
    /// mínimo que solo necesita el esquema ApiKey -- pero Program.cs ya NO lo usa desde el
    /// Patch 0067A: usa AddApiAuthentication (ApiKey + Cookie) en su lugar. Se deja este
    /// método intacto en vez de modificarlo para no romper esos tests.
    /// </summary>
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

    /// <summary>
    /// Patch 0067A: registro real usado por Program.cs. Agrega Cookie Authentication
    /// (destinada exclusivamente a la UI web) SIN reemplazar ApiKey (destinada a
    /// integraciones externas) -- ambos esquemas coexisten. El esquema por defecto es
    /// DualSchemeName, un "policy scheme" (AddPolicyScheme, mecanismo estándar de
    /// ASP.NET Core para elegir esquema según la request, sin middleware propio) que
    /// reenvía a ApiKey si la request trae el header X-Api-Key, o a Cookie en caso
    /// contrario -- así, cada .RequireAuthorization() ya existente (Patches 0059-0061)
    /// acepta cualquiera de los dos mecanismos sin que ningún grupo de endpoints
    /// necesite tocarse.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiAuthenticationOptions>(configuration.GetSection(ApiAuthenticationOptions.SectionName));
        services.PostConfigure<ApiAuthenticationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = configuration[ApiKeyEnvironmentVariableName]
                    ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName)
                    ?? string.Empty;
        });

        services.Configure<WebAuthenticationOptions>(configuration.GetSection(WebAuthenticationOptions.SectionName));
        services.PostConfigure<WebAuthenticationOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.Password))
                options.Password = configuration[WebPasswordEnvironmentVariableName]
                    ?? Environment.GetEnvironmentVariable(WebPasswordEnvironmentVariableName)
                    ?? string.Empty;
        });

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = DualSchemeName;
                options.DefaultChallengeScheme = DualSchemeName;
            })
            .AddPolicyScheme(DualSchemeName, "ApiKey o Cookie (según la request)", policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                        ? ApiKeyAuthenticationHandler.SchemeName
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, _ => { })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookieOptions =>
            {
                cookieOptions.Cookie.Name = "FinancialMcp.Auth";
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
                // SameAsRequest (no "Always" hardcodeado): esta app puede correr detrás
                // de HTTPS o, en un entorno local sin TLS, por HTTP puro -- exigir
                // "Always" rompería el login en ese segundo caso.
                cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookieOptions.ExpireTimeSpan = TimeSpan.FromDays(14);
                cookieOptions.SlidingExpiration = true;
                // Sin esto, una request de API (fetch desde el frontend, o cualquier
                // caller) que no está autenticada recibiría un 302 hacia LoginPath en vez
                // de un 401 -- rompería el contrato JSON que ya usa el resto de la API
                // (incluido el manejo de errores del Patch 0064). El frontend decide
                // cuándo redirigir a login.html por su cuenta (ver auth-guard.js), nunca
                // el servidor.
                cookieOptions.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookieOptions.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();

        return services;
    }
}
