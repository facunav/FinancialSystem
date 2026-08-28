using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Patch 0112: único punto de producción que invoca <c>IDedupeEngine.ApplyAsync</c> --
/// hasta este patch, <c>ApplyAsync</c> no tenía ningún llamador real (confirmado por
/// búsqueda exhaustiva antes de implementar: <c>BbvaBankStatementImporter</c> solo llama
/// <c>PreviewAsync</c> y descarta el resultado a propósito -- ver su doc-comment --, y
/// <c>tools/DedupePreviewCli</c> es de solo lectura garantizado a 2 niveles). Grupo
/// protegido con <c>RequireAuthorization()</c>, mismo esquema "ApiKeyOrCookie" que el
/// resto de la API (Patches 0058-0061).
///
/// CONTRATO -- no acepta un <c>DedupeCandidateResult</c> fabricado por el cliente: el
/// cliente identifica únicamente los <c>BankStatement.Id</c> físicos que quiere aplicar
/// (<c>bankStatementIds</c>, plural, sin distinguir "pendiente" de "liquidado" -- esa
/// distinción es interna al motor, ver <c>IsCandidatePair.roleOk</c> en
/// <c>DedupeEngine</c>, y el cliente no debe necesitar conocerla). El servidor SIEMPRE
/// reconstruye el candidato llamando <c>PreviewAsync(focusBankStatementIds:
/// bankStatementIds)</c> con la lista COMPLETA recibida -- nunca con un solo Id: la vía B
/// (duplicado exacto) reconstruye el mismo candidato con cualquier subconjunto no vacío
/// de sus miembros, pero el pipeline principal (F/K/L/D+E) exige que el lado con forma
/// Nro esté presente en <c>focusIds</c> para poder actuar como "pendiente" (<c>roleOk</c>
/// lo rechaza si no) -- mandar solo uno de los dos Ids puede no encontrar nada. Mandar la
/// lista completa evita depender de que el cliente sepa cuál lado es cuál.
///
/// El candidato se considera "el correspondiente a la solicitud" únicamente si su
/// conjunto exacto de miembros físicos (Pendiente + Liquidado + CarryForward, si los
/// hubiera) coincide, sin faltantes ni sobrantes, con <c>bankStatementIds</c>. Ante
/// cualquier ambigüedad (0 o 2+ candidatos que califican) no se aplica nada.
/// </summary>
public static class DedupeEndpoints
{
    public static IEndpointRouteBuilder MapDedupeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dedupe").WithTags("Dedupe").RequireAuthorization();

        group.MapPost("/apply", Apply);

        return app;
    }

    // ── POST /api/dedupe/apply ───────────────────────────────────────────────

    private static async Task<IResult> Apply(
        [FromBody] DedupeApplyRequest request,
        [FromServices] IDedupeEngine dedupeEngine,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.BankStatementIds is null || request.BankStatementIds.Count == 0)
            return Results.BadRequest("bankStatementIds no puede ser nulo ni vacío.");

        // Un candidato FUERTE (Pendiente + Liquidado, como mínimo) siempre tiene 2+
        // miembros físicos -- ver DedupeCandidateResult.CarryForwardMemberIds para el
        // caso de 3+.
        if (request.BankStatementIds.Count < 2)
            return Results.BadRequest(
                "bankStatementIds debe incluir al menos los 2 miembros físicos del candidato.");

        var requestedIds = request.BankStatementIds;
        if (requestedIds.Distinct().Count() != requestedIds.Count)
            return Results.BadRequest("bankStatementIds contiene identificadores duplicados.");

        var requestedSet = requestedIds.ToHashSet();

        // Reconstrucción server-side, con la lista COMPLETA recibida -- nunca con un solo
        // Id (ver doc-comment de la clase). El cliente nunca fabrica el candidato que se
        // va a aplicar: esto es siempre lo que PreviewAsync devuelve AHORA, contra el
        // estado actual de la cuenta -- no lo que el cliente afirma haber visto antes.
        var candidates = await dedupeEngine.PreviewAsync(requestedIds, ct);

        // El candidato debe corresponder EXACTAMENTE a los bankStatementIds enviados --
        // mismo conjunto de miembros físicos, ni más ni menos. Una coincidencia parcial
        // (ej. el cliente mandó 2 de los 3 miembros de un grupo con carry-forward) también
        // se reporta como "no encontrado" -- nunca se aplica un subconjunto arbitrario.
        // Se busca sobre TODAS las clasificaciones (no solo Fuerte) para poder distinguir
        // "no existe candidato" de "existe, pero no es Fuerte" -- validaciones distintas.
        var matchingAnyClassification = candidates
            .Where(c => requestedSet.SetEquals(MemberIdsOf(c)))
            .ToList();
        var matchingFuerte = matchingAnyClassification
            .Where(c => c.Classification == IdentityClassification.Fuerte)
            .ToList();

        if (matchingFuerte.Count > 1)
        {
            // No debería poder ocurrir -- Evaluate no emite 2 resultados FUERTE con el
            // mismo conjunto exacto de miembros (ver DegradarConflictosDeIdentidadFisica
            // en DedupeEngine) -- pero el contrato de este endpoint es explícito: ante
            // cualquier ambigüedad, no aplicar nada, nunca elegir arbitrariamente.
            return Results.Conflict(
                $"Ambiguo: {matchingFuerte.Count} candidatos FUERTE distintos coinciden " +
                "exactamente con los bankStatementIds enviados. No se aplicó ninguno.");
        }

        if (matchingFuerte.Count == 0)
        {
            if (matchingAnyClassification.Count > 0)
            {
                return Results.NotFound(
                    "Existe un candidato para los bankStatementIds enviados, pero su " +
                    $"clasificación actual es {matchingAnyClassification[0].Classification}, no Fuerte. " +
                    "No se aplicó nada.");
            }

            var algunoTocaLaSolicitud = candidates.Any(c => MemberIdsOf(c).Overlaps(requestedSet));

            return algunoTocaLaSolicitud
                ? Results.Conflict(
                    "Existe al menos un candidato que comparte alguno de los bankStatementIds " +
                    "enviados, pero ninguno coincide EXACTAMENTE con el conjunto completo " +
                    "solicitado (¿faltan o sobran Ids?). No se aplicó nada.")
                : Results.NotFound(
                    "No se encontró ningún candidato para los bankStatementIds enviados " +
                    "(no existen, o no forman un candidato entre sí).");
        }

        var candidate = matchingFuerte[0];
        var createdBy = httpContext.User.Identity?.Name ?? "desconocido";

        // Nunca se reconstruye el DedupeCandidateResult a mano, nunca se inserta
        // MovementIdentityLink directamente acá -- toda la persistencia pasa por
        // ApplyAsync, que vuelve a revalidar este candidato contra el estado actual antes
        // de persistir (SigueSiendoFuerte, DEDUPE-005 invariante I4) y nunca confía
        // ciegamente en lo que le llega.
        var outcome = await dedupeEngine.ApplyAsync([candidate], createdBy, ct);

        return Results.Ok(DedupeApplyResponseDto.Create(outcome));
    }

    private static HashSet<Guid> MemberIdsOf(DedupeCandidateResult candidate) =>
        new[] { candidate.PendienteId, candidate.LiquidadoId }
            .Concat(candidate.CarryForwardMemberIds)
            .ToHashSet();
}
