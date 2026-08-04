using Microsoft.AspNetCore.Diagnostics;

namespace FinancialSystem.Api.ErrorHandling;

/// <summary>
/// Punto único de configuración de ProblemDetails para excepciones no controladas
/// (Patch 0064, PATCH-015) -- mismo criterio organizativo que
/// ApiAuthenticationServiceCollectionExtensions (Patch 0058): Program.cs solo llama a
/// AddApiProblemDetails, sin que el detalle de qué se expone en cada entorno quede
/// disperso ahí.
///
/// Con AddProblemDetails() + app.UseExceptionHandler() (sin delegate propio, ver
/// Program.cs), ASP.NET Core ya devuelve por default un ProblemDetails (RFC 9457) con
/// status 500 para cualquier excepción no controlada, sin stack trace ni nombres de
/// clases internos en ningún entorno -- "evitar middlewares personalizados" del patch
/// se cumple usando exactamente ese mecanismo estándar, sin ninguna clase de
/// middleware propia. Lo único que agrega este método es:
///   (a) un traceId, en todos los entornos, para poder correlacionar con los logs
///       del servidor sin exponer nada del error en sí;
///   (b) en Development únicamente, el tipo, mensaje y stack trace de la excepción
///       real, para diagnóstico local -- nunca en el resto de los entornos.
///
/// No afecta a ninguna respuesta ya controlada por los endpoints (Results.BadRequest,
/// Results.NotFound, Results.Problem, etc.): IProblemDetailsService solo interviene acá
/// porque UseExceptionHandler lo consulta explícitamente para excepciones no
/// controladas -- no se registra UseStatusCodePages ni [ApiController], así que ningún
/// código 2xx/4xx ya existente cambia de forma.
/// </summary>
public static class ApiProblemDetailsServiceCollectionExtensions
{
    public static IServiceCollection AddApiProblemDetails(
        this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                if (!environment.IsDevelopment())
                    return;

                var exception = context.HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (exception is null)
                    return;

                context.ProblemDetails.Extensions["exceptionType"] = exception.GetType().FullName;
                context.ProblemDetails.Extensions["exceptionMessage"] = exception.Message;
                context.ProblemDetails.Extensions["stackTrace"] = exception.StackTrace;
            };
        });

        return services;
    }
}
