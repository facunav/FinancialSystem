using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").WithTags("Transactions");

        group.MapPut("/{id:guid}/financial-account", AssignFinancialAccount);

        return app;
    }

    // PUT /api/transactions/{id}/financial-account — asigna o desasigna (null) una cuenta
    private static async Task<IResult> AssignFinancialAccount(
        Guid id,
        [FromBody] AssignFinancialAccountRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var transaction = await db.Transactions.FindAsync([id], ct);
        if (transaction is null) return Results.NotFound();

        if (request.FinancialAccountId is { } accountId)
        {
            var exists = await db.FinancialAccounts.AnyAsync(a => a.Id == accountId, ct);
            if (!exists)
                return Results.BadRequest($"financialAccountId inválido: no existe una cuenta con id '{accountId}'");
        }

        transaction.FinancialAccountId = request.FinancialAccountId;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { TransactionId = transaction.Id, transaction.FinancialAccountId });
    }
}
