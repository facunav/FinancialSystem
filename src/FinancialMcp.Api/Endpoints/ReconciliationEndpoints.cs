using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Application.Reconciliation.Commands;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialSystem.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reconciliation").WithTags("Reconciliation");

        group.MapGet("/suggestions", GetSuggestions);
        group.MapPost("/confirm", Confirm);
        group.MapGet("/processed", GetProcessed);
        group.MapGet("/unmatched-movements", GetUnmatchedMovements);
        group.MapPost("/confirm-group", ConfirmGroup);
        group.MapPost("/review", ReviewMovementAsync);
        group.MapPost("/discard-candidates", DiscardCandidates);
        group.MapPost("/restore-candidates", RestoreCandidates);

        return app;
    }

    // ── POST /api/reconciliation/review ──────────────────────────────────────

    private static async Task<IResult> ReviewMovementAsync(
        [FromBody] ReviewMovementRequest request,
        [FromServices] ReviewMovementHandler handler,
        CancellationToken ct)
    {
        var command = new ReviewMovementCommand(
            request.SourceEntityType, request.SourceId,
            request.Reason, request.CategoryId, request.FinancialImpact, request.Notes);

        var result = await handler.Handle(command, ct);
        return result.Success
            ? Results.Ok(new { Success = true, ExpenseId = result.ExpenseId })
            : Results.BadRequest(result.Error);
    }

    // ── POST /api/reconciliation/discard-candidates ───────────────────────────

    private static async Task<IResult> DiscardCandidates(
        [FromBody] DiscardCandidatesRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        if (request.Ids.Count == 0)
            return Results.BadRequest("La lista de IDs no puede estar vacía");

        var expenses = await db.ManualExpenses
            .Where(e => request.Ids.Contains(e.Id) && !e.IsDiscarded)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var e in expenses)
        {
            e.IsDiscarded = true;
            e.DiscardedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new DiscardCandidatesResponse(
            Discarded: expenses.Count,
            NotFound: request.Ids.Count - expenses.Count));
    }

    // ── POST /api/reconciliation/restore-candidates ───────────────────────────

    private static async Task<IResult> RestoreCandidates(
        [FromBody] RestoreCandidatesRequest request,
        [FromServices] IApplicationDbContext db,
        CancellationToken ct)
    {
        if (request.Ids.Count == 0)
            return Results.BadRequest("La lista de IDs no puede estar vacía");

        var expenses = await db.ManualExpenses
            .Where(e => request.Ids.Contains(e.Id) && e.IsDiscarded)
            .ToListAsync(ct);

        foreach (var e in expenses)
        {
            e.IsDiscarded = false;
            e.DiscardedAt = null;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new RestoreCandidatesResponse(
            Restored: expenses.Count,
            NotFound: request.Ids.Count - expenses.Count));
    }

    // ── GET /api/reconciliation/suggestions ──────────────────────────────────

    private static async Task<IResult> GetSuggestions(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] ReconciliationOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (from >= to)
            return Results.BadRequest("'from' debe ser anterior a 'to'");

        var suggestions = await orchestrator.RunAsync(from, to, ct: ct);
        return Results.Ok(SuggestionsResponse.FromSuggestions(suggestions));
    }

    // ── POST /api/reconciliation/confirm ─────────────────────────────────────

    private static async Task<IResult> Confirm(
        [FromBody] ConfirmRequest request,
        [FromServices] MovementHydrationService hydrationService,
        [FromServices] ReconciliationConfirmationService confirmationService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConfirmedBy))
            return Results.BadRequest("confirmedBy es requerido");
        if (request.CategoryId == Guid.Empty)
            return Results.BadRequest("categoryId es requerido");
        if (request.Pairs.Count == 0)
            return Results.BadRequest("La lista de pares no puede estar vacía");

        var results = new List<ConfirmPairResultDto>(request.Pairs.Count);
        var hydrationRequests = new List<HydrationRequest>(request.Pairs.Count);

        for (var i = 0; i < request.Pairs.Count; i++)
        {
            var dto = request.Pairs[i];
            if (!TryParseSource(dto.ReferenceSource, out var refSource))
            {
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                    false, Error: $"ReferenceSource inválido: '{dto.ReferenceSource}'"));
                continue;
            }
            if (!TryParseSource(dto.CandidateSource, out var candSource))
            {
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                    false, Error: $"CandidateSource inválido: '{dto.CandidateSource}'"));
                continue;
            }
            hydrationRequests.Add(new HydrationRequest(
                Index: i,
                ReferenceId: dto.ReferenceId, ReferenceSource: refSource,
                CandidateId: dto.CandidateId, CandidateSource: candSource,
                OriginalScore: dto.OriginalScore, OriginalConfidence: dto.OriginalConfidence));
        }

        if (hydrationRequests.Count == 0)
            return Results.Ok(new ConfirmResponse(0, results.Count, results));

        var hydrationResults = await hydrationService.HydrateBatchAsync(hydrationRequests, ct);
        var hydratedByIndex = new Dictionary<int, PairConfirmationRequest>();

        foreach (var hr in hydrationResults)
        {
            var dto = request.Pairs[hr.Index];
            if (!hr.Success)
            {
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                    false, Error: hr.NotFoundReason));
                continue;
            }
            hydratedByIndex[hr.Index] = new PairConfirmationRequest(
                hr.Pair!, request.CategoryId, request.FinancialImpact, dto.ProcessingSource);
        }

        if (hydratedByIndex.Count == 0)
            return Results.Ok(new ConfirmResponse(0, results.Count, results));

        var orderedIndices = hydratedByIndex.Keys.ToList();
        var orderedRequests = orderedIndices.Select(idx => hydratedByIndex[idx]).ToList();
        var batchResult = await confirmationService.ConfirmBatchAsync(orderedRequests, request.ConfirmedBy, ct);

        var successByPair = new Dictionary<(Guid, Guid), ConfirmationSuccess>();
        var failureByPair = new Dictionary<(Guid, Guid), PairFailure>();
        var successQueue = new Queue<ConfirmationSuccess>(batchResult.Successes);
        var failureQueue = new Queue<PairFailure>(batchResult.Failures);

        while (failureQueue.Count > 0)
        {
            var failure = failureQueue.Dequeue();
            if (failure.Pair is null) continue;
            failureByPair[(failure.Pair.Reference.Id, failure.Pair.Candidate.Id)] = failure;
        }
        foreach (var req in orderedRequests)
        {
            var key = (req.Pair.Reference.Id, req.Pair.Candidate.Id);
            if (!failureByPair.ContainsKey(key) && successQueue.Count > 0)
                successByPair[key] = successQueue.Dequeue();
        }
        foreach (var dtoIndex in orderedIndices)
        {
            var dto = request.Pairs[dtoIndex];
            var pair = hydratedByIndex[dtoIndex].Pair;
            var key = (pair.Reference.Id, pair.Candidate.Id);
            if (failureByPair.TryGetValue(key, out var failure))
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId, false, Error: failure.Reason));
            else if (successByPair.TryGetValue(key, out var success))
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId, true, ExpenseId: success.ExpenseId));
            else
                results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId, false,
                    Error: "Resultado no reportado por el servicio de confirmación"));
        }

        return Results.Ok(new ConfirmResponse(results.Count(r => r.Success), results.Count(r => !r.Success), results));
    }

    // ── GET /api/reconciliation/processed ────────────────────────────────────

    private static async Task<IResult> GetProcessed(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromServices] IProcessedExpenseRepository repository,
        CancellationToken ct)
    {
        if (from >= to)
            return Results.BadRequest("'from' debe ser anterior a 'to'");

        var expenses = await repository.GetByPeriodAsync(from, to, ct: ct);
        return Results.Ok(ProcessedExpensesResponse.FromExpenses(from, to, expenses));
    }

    // ── GET /api/reconciliation/unmatched-movements ───────────────────────────
    // Excluye automáticamente los candidatos descartados (IsDiscarded = true).

    private static async Task<IResult> GetUnmatchedMovements(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] IApplicationDbContext db,
        [FromServices] IManualExpenseRepository manualExpenses,
        [FromServices] IProcessedExpenseRepository processedRepo,
        CancellationToken ct)
    {
        if (from >= to)
            return Results.BadRequest("'from' debe ser anterior a 'to'");

        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= fromUtc && t.Date <= toUtc).ToListAsync(ct);
        var bankStatements = await db.BankStatements.AsNoTracking()
            .Where(b => b.Date >= fromUtc && b.Date <= toUtc).ToListAsync(ct);

        // Excluye descartados: solo aparecen los activos
        var expenses = await db.ManualExpenses.AsNoTracking()
            .Where(e => e.Date >= fromUtc && e.Date <= toUtc && !e.IsDiscarded)
            .OrderBy(e => e.Date)
            .ToListAsync(ct);

        var txIds = transactions.Select(t => t.Id).ToList();
        var bsIds = bankStatements.Select(b => b.Id).ToList();
        var expIds = expenses.Select(e => e.Id).ToList();

        var processedTx = (await processedRepo.GetAlreadyProcessedSourceIdsAsync(SourceEntityType.Transaction, txIds, ct)).ToHashSet();
        var processedBs = (await processedRepo.GetAlreadyProcessedSourceIdsAsync(SourceEntityType.BankStatement, bsIds, ct)).ToHashSet();
        var processedExp = (await processedRepo.GetAlreadyProcessedSourceIdsAsync(SourceEntityType.ManualExpense, expIds, ct)).ToHashSet();

        var references = transactions
            .Select(t => new UnmatchedMovementDto(t.Id, "CreditCard", t.Date,
                t.Description ?? string.Empty, t.Amount, t.Currency ?? "ARS", processedTx.Contains(t.Id)))
            .Concat(bankStatements.Select(b => new UnmatchedMovementDto(b.Id, "BankDebit", b.Date,
                b.Concept, b.Amount, b.Currency, processedBs.Contains(b.Id))))
            .OrderBy(r => r.Date)
            .ToList();

        var candidates = expenses
            .Select(e => new UnmatchedMovementDto(e.Id,
                e.Sheet == ManualExpenseSheet.Dynamic ? "ManualDynamic" : "ManualFixed",
                e.Date, e.Description, e.Amount, e.Currency, processedExp.Contains(e.Id)))
            .ToList();

        return Results.Ok(new UnmatchedMovementsResponse(from, to, references, candidates));
    }

    // ── POST /api/reconciliation/confirm-group ────────────────────────────────

    private static async Task<IResult> ConfirmGroup(
        [FromBody] ConfirmGroupRequest request,
        [FromServices] ConfirmGroupHandler handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConfirmedBy))
            return Results.BadRequest("confirmedBy es requerido");
        if (request.CategoryId == Guid.Empty)
            return Results.BadRequest("categoryId es requerido");
        if (request.ReferenceItems.Count == 0 || request.CandidateItems.Count == 0)
            return Results.BadRequest("Se requiere al menos un movimiento en cada lado");

        var refItems = new List<(Guid, MovementSource)>();
        var candItems = new List<(Guid, MovementSource)>();

        foreach (var r in request.ReferenceItems)
        {
            if (!TryParseSource(r.Source, out var src))
                return Results.BadRequest($"Source inválido: '{r.Source}'");
            refItems.Add((r.Id, src));
        }
        foreach (var c in request.CandidateItems)
        {
            if (!TryParseSource(c.Source, out var src))
                return Results.BadRequest($"Source inválido: '{c.Source}'");
            candItems.Add((c.Id, src));
        }

        var command = new ConfirmGroupCommand(
            request.ConfirmedBy, request.CategoryId, request.FinancialImpact, refItems, candItems);

        var result = await handler.Handle(command, ct);
        return result.Success
            ? Results.Ok(new ConfirmGroupResponse(true, result.ExpenseId,
                result.ReferenceTotal, result.CandidateTotal, result.AmountDelta))
            : Results.BadRequest(new ConfirmGroupResponse(false, Error: result.Error));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryParseSource(string raw, out MovementSource source) =>
        Enum.TryParse(raw, ignoreCase: true, out source);
}