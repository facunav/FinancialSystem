using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints
{
    public static class ReconciliationEndpoints
    {
        public static IEndpointRouteBuilder MapReconciliationEndpoints(
        this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/reconciliation")
                .WithTags("Reconciliation");

            group.MapGet("/suggestions", GetSuggestions);
            group.MapPost("/confirm", Confirm);
            group.MapGet("/reconciled", GetReconciled);

            return app;
        }

        // ── GET /api/reconciliation/suggestions ───────────────────────

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

        // ── POST /api/reconciliation/confirm ─────────────────────────

        private static async Task<IResult> Confirm(
            [FromBody] ConfirmRequest request,
            [FromServices] MovementHydrationService hydrationService,
            [FromServices] ReconciliationConfirmationService confirmationService,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmedBy))
                return Results.BadRequest("confirmedBy es requerido");

            if (request.Pairs.Count == 0)
                return Results.BadRequest("La lista de pares no puede estar vacía");

            // Paso 1: rehidratar. Mantener índice para correlacionar con el DTO original.
            var results = new List<ConfirmPairResultDto>();
            var hydratedIndexed = new List<(int DtoIndex, PairConfirmationRequest Req)>();

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

                var pair = await hydrationService.HydrateAsync(
                    dto.ReferenceId, refSource,
                    dto.CandidateId, candSource,
                    dto.OriginalScore, dto.OriginalConfidence, ct);

                if (pair is null)
                {
                    results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                        false, Error: "No se encontró alguno de los movimientos en la base de datos"));
                    continue;
                }

                hydratedIndexed.Add((i, new PairConfirmationRequest(pair, dto.ConfirmationSource)));
            }

            if (hydratedIndexed.Count == 0)
                return Results.Ok(new ConfirmResponse(0, results.Count, results));

            // Paso 2: confirmar en batch los pares rehidratados
            var batchResult = await confirmationService.ConfirmBatchAsync(
                hydratedIndexed.Select(x => x.Req).ToList(),
                request.ConfirmedBy,
                request.PeriodStart, request.PeriodEnd, ct);

            // Paso 3: correlacionar resultados del batch con los DTOs originales
            // Los successes y failures del batch están en el mismo orden que hydratedIndexed
            var successQueue = new Queue<ConfirmationSuccess>(batchResult.Successes);
            var failureQueue = new Queue<PairFailure>(batchResult.Failures);

            foreach (var (dtoIndex, req) in hydratedIndexed)
            {
                var originalDto = request.Pairs[dtoIndex];
                var refId = req.Pair.Reference.Id;
                var candId = req.Pair.Candidate.Id;

                // El servicio procesa los pares en orden: failures primero (los que no pasaron
                // validación) y luego successes. Como aquí ya pasaron la hidratación,
                // buscamos por Id de movimiento para correlacionar correctamente.
                var failure = batchResult.Failures
                    .FirstOrDefault(f => f.Pair?.Reference.Id == refId);

                if (failure is not null)
                {
                    results.Add(new ConfirmPairResultDto(refId, candId,
                        false, Error: failure.Reason));
                }
                else
                {
                    // Asignamos successes en orden de inserción (los que no fallaron)
                    var success = successQueue.Count > 0 ? successQueue.Dequeue() : null;
                    results.Add(new ConfirmPairResultDto(refId, candId,
                        true, ExpenseId: success?.ExpenseId));
                }
            }

            return Results.Ok(new ConfirmResponse(
                batchResult.TotalSucceeded,
                results.Count(r => !r.Success),
                results));
        }

        // ── GET /api/reconciliation/reconciled ────────────────────────

        private static async Task<IResult> GetReconciled(
            [FromQuery] DateOnly from,
            [FromQuery] DateOnly to,
            [FromServices] IReconciledExpenseRepository repository,
            CancellationToken ct)
        {
            if (from >= to)
                return Results.BadRequest("'from' debe ser anterior a 'to'");

            var expenses = await repository.GetByPeriodAsync(
                from, to,
                status: ReconciledExpenseStatus.Confirmed,
                ct: ct);

            return Results.Ok(ReconciledExpensesResponse.From(from, to, expenses));
        }

        private static bool TryParseSource(string raw, out MovementSource source) =>
            Enum.TryParse(raw, ignoreCase: true, out source);
    }
}
