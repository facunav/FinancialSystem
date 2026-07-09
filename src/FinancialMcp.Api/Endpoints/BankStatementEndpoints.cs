using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

public static class BankStatementEndpoints
{
    public static IEndpointRouteBuilder MapBankStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-statements").WithTags("BankStatements");

        group.MapPut("/{id:guid}/financial-account", AssignFinancialAccount);

        return app;
    }

    // PUT /api/bank-statements/{id}/financial-account — asigna o desasigna (null) una cuenta
    private static async Task<IResult> AssignFinancialAccount(
        Guid id,
        [FromBody] AssignFinancialAccountRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var bankStatement = await db.BankStatements.FindAsync([id], ct);
        if (bankStatement is null) return Results.NotFound();

        if (request.FinancialAccountId is { } accountId)
        {
            var exists = await db.FinancialAccounts.AnyAsync(a => a.Id == accountId, ct);
            if (!exists)
                return Results.BadRequest($"financialAccountId inválido: no existe una cuenta con id '{accountId}'");
        }

        bankStatement.FinancialAccountId = request.FinancialAccountId;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { BankStatementId = bankStatement.Id, bankStatement.FinancialAccountId });
    }
}
