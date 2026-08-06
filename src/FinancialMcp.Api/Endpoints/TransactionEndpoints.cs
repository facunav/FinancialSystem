using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Transactions.Commands;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Migrado al patrón Command/Query + Handler en PATCH-049 (ver
/// docs/Decisions/ADR-008-command-handler-sin-mediatr.md) -- ya no tiene lógica de
/// negocio propia: arma el Command, lo pasa al Handler de
/// FinancialSystem.Application.Transactions.Commands, y traduce el resultado a una
/// respuesta HTTP. La validación de que el FinancialAccountId informado exista es una
/// regla de negocio (requiere acceso a datos) y vive en el Handler, no acá.
/// </summary>
public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        // Patch 0059 (PATCH-010): asignar cuenta financiera modifica un movimiento --
        // protegido con la misma infraestructura del Patch 0058.
        var group = app.MapGroup("/api/transactions").WithTags("Transactions").RequireAuthorization();

        group.MapPut("/{id:guid}/financial-account", AssignFinancialAccount);

        return app;
    }

    // PUT /api/transactions/{id}/financial-account — asigna o desasigna (null) una cuenta
    private static async Task<IResult> AssignFinancialAccount(
        Guid id,
        [FromBody] AssignFinancialAccountRequest request,
        [FromServices] AssignTransactionFinancialAccountHandler handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(
            new AssignTransactionFinancialAccountCommand(id, request.FinancialAccountId), ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                AssignTransactionFinancialAccountFailureReason.InvalidFinancialAccountId =>
                    Results.BadRequest(
                        $"financialAccountId inválido: no existe una cuenta con id '{result.InvalidFinancialAccountId}'"),
                _ => Results.NotFound(),
            };
        }

        return Results.Ok(new { TransactionId = result.TransactionId, FinancialAccountId = result.AssignedFinancialAccountId });
    }
}
