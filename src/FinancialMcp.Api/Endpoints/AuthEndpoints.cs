using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinancialSystem.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Módulo mínimo de autenticación para la UI web (Patch 0067A): login, logout y
/// verificación de sesión, sobre el esquema Cookie registrado por
/// ApiAuthenticationServiceCollectionExtensions.AddApiAuthentication. Sin registro de
/// usuarios ni recuperación de contraseña -- una única contraseña compartida
/// (WebAuthenticationOptions.Password), misma filosofía single-user que el resto del
/// sistema.
///
/// No usa .RequireAuthorization() en el grupo: Login y Logout tienen que ser
/// alcanzables SIN sesión previa (si no, nadie podría loguearse la primera vez, ni
/// cerrar sesión con una cookie ya vencida/inválida) -- se protege únicamente /session,
/// que es la única ruta cuyo propósito es justamente "decime si estoy autenticado".
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login);
        group.MapPost("/logout", Logout);
        group.MapGet("/session", GetSession).RequireAuthorization();

        return app;
    }

    // ── POST /api/auth/login ────────────────────────────────────────────────
    // Emite la cookie de sesión si la contraseña coincide con
    // WebAuthenticationOptions.Password. Nunca autentica si esa contraseña no está
    // configurada (mismo criterio que ApiKeyAuthenticationHandler.IsMatch para
    // ApiAuthenticationOptions.ApiKey: una configuración vacía no debe degradar en
    // "aceptar cualquier cosa").

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IOptions<WebAuthenticationOptions> options,
        HttpContext httpContext)
    {
        if (!IsMatch(request.Password, options.Value.Password))
            return Results.Unauthorized();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "WebUser")], CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        return Results.Ok(new { Message = "Sesión iniciada" });
    }

    // ── POST /api/auth/logout ───────────────────────────────────────────────
    // Sin RequireAuthorization() a propósito: cerrar sesión sin tener una sesión
    // válida (cookie ausente o ya vencida) debe ser un no-op exitoso, no un error --
    // SignOutAsync es seguro de llamar en ese caso (solo asegura que la respuesta
    // limpie la cookie).

    private static async Task<IResult> Logout(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { Message = "Sesión cerrada" });
    }

    // ── GET /api/auth/session ───────────────────────────────────────────────
    // RequireAuthorization() acepta el esquema dual (ApiKey o Cookie, ver
    // AddApiAuthentication) -- 200 si cualquiera de los dos es válido, 401 si no, sin
    // ninguna verificación manual acá: el framework ya resolvió la pregunta antes de
    // que este handler se ejecute.

    private static IResult GetSession() => Results.Ok(new SessionResponse(Authenticated: true));

    private static bool IsMatch(string providedPassword, string configuredPassword)
    {
        if (string.IsNullOrEmpty(configuredPassword) || string.IsNullOrEmpty(providedPassword))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(providedPassword);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredPassword);
        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}

public sealed record LoginRequest(string Password);
public sealed record SessionResponse(bool Authenticated);
