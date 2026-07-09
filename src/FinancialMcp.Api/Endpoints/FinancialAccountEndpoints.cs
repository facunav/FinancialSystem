using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;
using FinancialSystem.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

public static class FinancialAccountEndpoints
{
    public static IEndpointRouteBuilder MapFinancialAccountEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts");

        group.MapGet("/", GetAll);
        group.MapGet("/{id:guid}", GetById);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Deactivate);

        return app;
    }

    // GET /api/accounts
    private static async Task<IResult> GetAll(
        [FromQuery] bool includeDeactivated,
        [FromQuery] string? search,
        [FromServices] IFinancialAccountQueryService service,
        CancellationToken ct)
    {
        var accounts = await service.GetAllAsync(includeDeactivated, search, ct);
        return Results.Ok(accounts.Select(FinancialAccountDto.Create));
    }

    // GET /api/accounts/{id}
    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] IFinancialAccountQueryService service,
        CancellationToken ct)
    {
        var account = await service.GetByIdAsync(id, ct);
        return account is null ? Results.NotFound() : Results.Ok(FinancialAccountDto.Create(account));
    }

    // POST /api/accounts
    private static async Task<IResult> Create(
        [FromBody] CreateFinancialAccountRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("name es requerido");

        if (!Enum.TryParse<FinancialAccountType>(request.Type, ignoreCase: true, out var type))
            return Results.BadRequest($"type inválido: '{request.Type}'");

        var account = new FinancialAccount
        {
            Name = request.Name.Trim(),
            Type = type,
            AccountNumber = string.IsNullOrWhiteSpace(request.AccountNumber) ? null : request.AccountNumber.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "ARS" : request.Currency.Trim().ToUpperInvariant(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsDeactivated = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.FinancialAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/accounts/{account.Id}", ToDto(account));
    }

    // PUT /api/accounts/{id}
    private static async Task<IResult> Update(
        Guid id,
        [FromBody] UpdateFinancialAccountRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var account = await db.FinancialAccounts.FindAsync([id], ct);
        if (account is null) return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name))
            account.Name = request.Name.Trim();

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<FinancialAccountType>(request.Type, ignoreCase: true, out var type))
                return Results.BadRequest($"type inválido: '{request.Type}'");
            account.Type = type;
        }

        if (request.AccountNumber is not null)
            account.AccountNumber = string.IsNullOrWhiteSpace(request.AccountNumber) ? null : request.AccountNumber.Trim();

        if (!string.IsNullOrWhiteSpace(request.Currency))
            account.Currency = request.Currency.Trim().ToUpperInvariant();

        if (request.Notes is not null)
            account.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(account));
    }

    // DELETE /api/accounts/{id} — desactiva, no elimina
    private static async Task<IResult> Deactivate(
        Guid id,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        var account = await db.FinancialAccounts.FindAsync([id], ct);
        if (account is null) return Results.NotFound();
        if (account.IsDeactivated) return Results.Ok(new { Message = "Ya estaba desactivada" });

        account.IsDeactivated = true;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { Message = $"Cuenta '{account.Name}' desactivada" });
    }

    private static FinancialAccountDto ToDto(FinancialAccount a) => new(
        a.Id, a.Name, a.Type.ToString(), a.AccountNumber, a.Currency, a.Notes, a.IsDeactivated);
}

public sealed record CreateFinancialAccountRequest(
    string Name,
    string Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);

public sealed record UpdateFinancialAccountRequest(
    string? Name,
    string? Type,
    string? AccountNumber,
    string? Currency,
    string? Notes);
