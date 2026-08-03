using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Handler de autenticación por API Key (Patch 0058, PATCH-009). Infraestructura
/// preparada para que los próximos patches (0059-0061) protejan endpoints agregando
/// .RequireAuthorization() a los grupos correspondientes -- este patch no protege ningún
/// endpoint todavía, así que mientras ningún endpoint la exija, este handler nunca se
/// invoca y la API sigue funcionando exactamente igual que antes (sección 4 del patch).
///
/// CONTRATO:
///   Header esperado: X-Api-Key.
///   - Header ausente -&gt; AuthenticateResult.NoResult() (ni éxito ni fallo explícito;
///     así es como ASP.NET Core distingue "no se intentó autenticar con este esquema" de
///     "se intentó y falló" -- solo importa una vez que algún endpoint exija autorización).
///   - Header presente pero no coincide con ApiAuthenticationOptions.ApiKey (incluida una
///     clave configurada vacía) -&gt; AuthenticateResult.Fail(...).
///   - Header presente y coincide -&gt; AuthenticateResult.Success con un ClaimsPrincipal
///     mínimo (un único claim de nombre) -- no hay usuarios ni roles en una app single-user.
///
/// La comparación usa CryptographicOperations.FixedTimeEquals para no filtrar por
/// temporización cuántos caracteres de la clave coinciden.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> schemeOptions,
    IOptions<ApiAuthenticationOptions> apiAuthenticationOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var providedValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var configuredKey = apiAuthenticationOptions.Value.ApiKey;
        var providedKey = providedValues.ToString();

        if (!IsMatch(providedKey, configuredKey))
            return Task.FromResult(AuthenticateResult.Fail("Clave de API inválida."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "ApiKeyUser")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Nunca coincide si la clave configurada está vacía (sección 3 del patch: una
    /// configuración inválida/ausente no debe autenticar a nadie), aunque el valor
    /// provisto también esté vacío.
    /// </summary>
    private static bool IsMatch(string providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(providedKey))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
