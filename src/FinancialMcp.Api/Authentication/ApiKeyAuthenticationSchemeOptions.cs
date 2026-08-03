using Microsoft.AspNetCore.Authentication;

namespace FinancialSystem.Api.Authentication;

/// <summary>
/// Opciones de plomería exigidas por AuthenticationHandler&lt;TOptions&gt; (Patch 0058).
/// La configuración real (la clave esperada) vive en <see cref="ApiAuthenticationOptions"/>,
/// no acá -- esta clase no tiene campos propios porque el esquema no necesita nada más
/// que lo que ya provee AuthenticationSchemeOptions.
/// </summary>
public sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions;
