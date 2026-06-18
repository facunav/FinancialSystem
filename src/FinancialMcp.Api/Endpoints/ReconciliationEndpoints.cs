using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Reconciliation;
using FinancialSystem.Domain.Entities;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Reconciliation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
            group.MapGet("/unmatched-movements", GetUnmatchedMovements);
            group.MapPost("/confirm-group", ConfirmGroup);

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

        /// <summary>
        /// REFACTOR (Paso 3):
        ///   1. Hidratación en batch (HydrateBatchAsync) — máximo 3 queries
        ///      en lugar de hasta 2N queries individuales.
        ///   2. Correlación determinística por (referenceId, candidateId)
        ///      en lugar de buscar Failures.FirstOrDefault por Reference.Id solo.
        ///      Esto evita asociar el resultado equivocado cuando dos pares
        ///      del batch comparten el mismo movimiento (caso EC1 de duplicados).
        /// </summary>
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

            var results = new List<ConfirmPairResultDto>(request.Pairs.Count);

            // ── Paso 1: validar sources y construir requests de hidratación ──
            // hydrationRequests preserva el índice original (dtoIndex) en HydrationRequest.Index
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

            // ── Paso 2: hidratar en batch (máximo 3 queries totales) ─────────
            var hydrationResults = await hydrationService.HydrateBatchAsync(hydrationRequests, ct);

            // hydratedByIndex: dtoIndex -> (Pair, ConfirmationSource) para los que hidrataron OK
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

                hydratedByIndex[hr.Index] = new PairConfirmationRequest(hr.Pair!, dto.ConfirmationSource);
            }

            if (hydratedByIndex.Count == 0)
                return Results.Ok(new ConfirmResponse(0, results.Count, results));

            // ── Paso 3: confirmar en batch ────────────────────────────────────
            // El orden de hydratedByIndex.Values determina el orden en que
            // ConfirmBatchAsync procesa los pares. Mantenemos esa misma lista
            // para poder correlacionar por (referenceId, candidateId) después.
            var orderedIndices = hydratedByIndex.Keys.ToList();
            var orderedRequests = orderedIndices.Select(idx => hydratedByIndex[idx]).ToList();

            var batchResult = await confirmationService.ConfirmBatchAsync(
                orderedRequests,
                request.ConfirmedBy,
                request.PeriodStart, request.PeriodEnd, ct);

            // ── Paso 4: correlación determinística por (referenceId, candidateId) ──
            // Construimos diccionarios keyed por la tupla (Reference.Id, Candidate.Id).
            // Esta tupla es única por par tal como fue enviado por el cliente —
            // a diferencia de Reference.Id solo, que puede repetirse entre pares
            // distintos del mismo batch (caso EC1: el mismo movimiento bancario
            // referenciado por dos pares con candidatos distintos).
            var successByPair = new Dictionary<(Guid, Guid), ConfirmationSuccess>();
            var failureByPair = new Dictionary<(Guid, Guid), PairFailure>();

            // batchResult.Successes y batchResult.Failures están en el mismo orden
            // relativo que orderedRequests (ConfirmBatchAsync preserva el orden de
            // los supervivientes tras descartar duplicados intra-batch). Recorremos
            // orderedRequests y consumimos de las colas de successes/failures
            // en orden — pero para evitar depender del orden interno, usamos
            // la tupla como clave de verdad: si dos requests del batch tuvieran
            // la MISMA tupla (refId, candId), serían literalmente el mismo par
            // duplicado, lo cual ConfirmBatchAsync ya detecta como EC1 y reporta
            // como failure — por lo que la tupla es segura como clave única.
            var successQueue = new Queue<ConfirmationSuccess>(batchResult.Successes);
            var failureQueue = new Queue<PairFailure>(batchResult.Failures);

            // Primero: failures — tienen el Pair adjunto, correlacionamos directo por tupla
            while (failureQueue.Count > 0)
            {
                var failure = failureQueue.Dequeue();
                if (failure.Pair is null) continue; // AllFailed sin pair (confirmedBy vacío, etc.)
                var key = (failure.Pair.Reference.Id, failure.Pair.Candidate.Id);
                failureByPair[key] = failure;
            }

            // Successes no traen el Pair, pero su orden corresponde a los requests
            // que NO aparecieron en failureByPair, en el mismo orden de orderedRequests.
            foreach (var req in orderedRequests)
            {
                var key = (req.Pair.Reference.Id, req.Pair.Candidate.Id);
                if (failureByPair.ContainsKey(key)) continue;

                if (successQueue.Count > 0)
                    successByPair[key] = successQueue.Dequeue();
            }

            // ── Paso 5: construir resultados finales en el orden original ────
            foreach (var dtoIndex in orderedIndices)
            {
                var dto = request.Pairs[dtoIndex];
                var pair = hydratedByIndex[dtoIndex].Pair;
                var key = (pair.Reference.Id, pair.Candidate.Id);

                if (failureByPair.TryGetValue(key, out var failure))
                {
                    results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                        false, Error: failure.Reason));
                }
                else if (successByPair.TryGetValue(key, out var success))
                {
                    results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                        true, ExpenseId: success.ExpenseId));
                }
                else
                {
                    // No debería ocurrir: significa que ConfirmBatchAsync no reportó
                    // ni success ni failure para este par. Lo marcamos explícitamente
                    // para no perder silenciosamente el resultado.
                    results.Add(new ConfirmPairResultDto(dto.ReferenceId, dto.CandidateId,
                        false, Error: "Resultado no reportado por el servicio de confirmación"));
                }
            }

            return Results.Ok(new ConfirmResponse(
                results.Count(r => r.Success),
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

        private static async Task<IResult> GetUnmatchedMovements(
    [FromQuery] DateOnly from,
    [FromQuery] DateOnly to,
    [FromServices] IApplicationDbContext db,
    [FromServices] IManualExpenseRepository manualExpenses,
    [FromServices] IReconciledExpenseRepository reconciledRepo,
    CancellationToken ct)
        {
            if (from >= to)
                return Results.BadRequest("'from' debe ser anterior a 'to'");

            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            // Cargar referencias
            var transactions = await db.Transactions
                .AsNoTracking()
                .Where(t => t.Date >= fromUtc && t.Date <= toUtc)
                .ToListAsync(ct);

            var bankStatements = await db.BankStatements
                .AsNoTracking()
                .Where(b => b.Date >= fromUtc && b.Date <= toUtc)
                .ToListAsync(ct);

            // Cargar candidatos
            var expenses = await manualExpenses.GetByPeriodAsync(from, to, sheet: null, ct);

            // Qué ya está reconciliado
            var txIds = transactions.Select(t => t.Id).ToList();
            var bsIds = bankStatements.Select(b => b.Id).ToList();
            var expIds = expenses.Select(e => e.Id).ToList();

            var reconciledTx = (await reconciledRepo.GetAlreadyReconciledSourceIdsAsync(SourceEntityType.Transaction, txIds, ct)).ToHashSet();
            var reconciledBs = (await reconciledRepo.GetAlreadyReconciledSourceIdsAsync(SourceEntityType.BankStatement, bsIds, ct)).ToHashSet();
            var reconciledExp = (await reconciledRepo.GetAlreadyReconciledSourceIdsAsync(SourceEntityType.ManualExpense, expIds, ct)).ToHashSet();

            var references = transactions
                .Select(t => new UnmatchedMovementDto(t.Id, "CreditCard", t.Date,
                    t.Description ?? string.Empty, t.Amount, t.Currency ?? "ARS",
                    reconciledTx.Contains(t.Id)))
                .Concat(bankStatements.Select(b => new UnmatchedMovementDto(b.Id, "BankDebit", b.Date,
                    b.Concept, b.Amount, b.Currency,
                    reconciledBs.Contains(b.Id))))
                .OrderBy(r => r.Date)
                .ToList();

            var candidates = expenses
                .Select(e => new UnmatchedMovementDto(e.Id,
                    e.Sheet == ManualExpenseSheet.Dynamic ? "ManualDynamic" : "ManualFixed",
                    e.Date, e.Category, e.Amount, e.Currency,
                    reconciledExp.Contains(e.Id)))
                .OrderBy(c => c.Date)
                .ToList();

            return Results.Ok(new UnmatchedMovementsResponse(from, to, references, candidates));
        }

        private static async Task<IResult> ConfirmGroup(
            [FromBody] ConfirmGroupRequest request,
            [FromServices] MovementHydrationService hydrationService,
            [FromServices] IReconciledExpenseRepository repository,
            [FromServices] IOptionsSnapshot<ReconciliationOptions> options,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmedBy))
                return Results.BadRequest("confirmedBy es requerido");

            if (request.ReferenceItems.Count == 0 || request.CandidateItems.Count == 0)
                return Results.BadRequest("Se requiere al menos un movimiento en cada lado");

            // Hidratar todos los movimientos (ambos lados juntos en un solo batch)
            var allRequests = request.ReferenceItems
                .Select((r, i) =>
                {
                    TryParseSource(r.Source, out var src);
                    return new HydrationRequest(i, r.Id, src, Guid.Empty, MovementSource.ManualDynamic);
                })
                .Concat(request.CandidateItems
                    .Select((c, i) =>
                    {
                        TryParseSource(c.Source, out var src);
                        return new HydrationRequest(request.ReferenceItems.Count + i,
                            Guid.Empty, MovementSource.BankDebit, c.Id, src);
                    }))
                .ToList();

            // Hidratar referencias y candidatos por separado (HydrateAsync individual por ahora)
            var refMovements = new List<FinancialMovement>();
            var candMovements = new List<FinancialMovement>();

            foreach (var r in request.ReferenceItems)
            {
                if (!TryParseSource(r.Source, out var src))
                    return Results.BadRequest($"Source inválido: '{r.Source}'");

                var m = await hydrationService.HydrateAsync(r.Id, src, Guid.Empty, MovementSource.ManualDynamic, ct: ct);
                // HydrateAsync devuelve null si no encuentra — extraemos solo la referencia
                var single = await LoadSingleMovement(hydrationService, r.Id, src, ct);
                if (single is null) return Results.BadRequest($"Movimiento no encontrado: {r.Id}");
                refMovements.Add(single);
            }

            foreach (var c in request.CandidateItems)
            {
                if (!TryParseSource(c.Source, out var src))
                    return Results.BadRequest($"Source inválido: '{c.Source}'");

                var single = await LoadSingleMovement(hydrationService, c.Id, src, ct);
                if (single is null) return Results.BadRequest($"Movimiento no encontrado: {c.Id}");
                candMovements.Add(single);
            }

            // Validar monedas
            var allCurrencies = refMovements.Concat(candMovements).Select(m => m.Currency).Distinct().ToList();
            if (allCurrencies.Count > 1)
                return Results.BadRequest($"Monedas inconsistentes en el grupo: {string.Join(", ", allCurrencies)}");

            // Calcular totales y delta
            var refTotal = refMovements.Sum(m => Math.Abs(m.Amount));
            var candTotal = candMovements.Sum(m => Math.Abs(m.Amount));
            var delta = Math.Abs(refTotal - candTotal);
            var tolerance = (decimal)(options.Value.AmountAbsoluteTolerance);
            var mismatch = delta > tolerance;

            // Construir expense
            var now = DateTime.UtcNow;
            var description = refMovements.Count == 1
                ? refMovements[0].Description
                : $"Grupo {refMovements.Count} ref. / {candMovements.Count} cand.";

            var expense = new ReconciledExpense
            {
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                EffectiveDate = refMovements.Min(m => m.Date),
                TotalAmount = refTotal,
                Currency = allCurrencies[0],
                Description = description,
                Status = ReconciledExpenseStatus.Confirmed,
                MatchScore = 0.0,
                MatchConfidence = MatchConfidence.None.ToString(),
                ConfirmationSource = ConfirmationSource.Manual,
                AmountDelta = delta,
                HasAmountMismatch = mismatch,
                GroupingMode = ReconciliationGroupingMode.ManualGroup,
                CreatedAt = now,
                ConfirmedAt = now,
                ConfirmedBy = request.ConfirmedBy,
            };

            foreach (var m in refMovements)
                expense.Items.Add(BuildGroupItem(m, ReconciliationItemRole.Reference));
            foreach (var m in candMovements)
                expense.Items.Add(BuildGroupItem(m, ReconciliationItemRole.Candidate));

            await repository.SaveAsync(expense, ct);

            return Results.Ok(new ConfirmGroupResponse(
                true, expense.Id, refTotal, candTotal, delta, mismatch));
        }

        // Helper — carga un movimiento individual reutilizando HydrateAsync con un dummy
        private static async Task<FinancialMovement?> LoadSingleMovement(
            MovementHydrationService svc, Guid id, MovementSource source, CancellationToken ct)
        {
            // HydrateAsync carga Reference y Candidate — pasamos el mismo ID en ambos lados
            // y tomamos solo el Reference. Es un workaround hasta tener HydrateGroupAsync.
            var pair = await svc.HydrateAsync(id, source, id, source, ct: ct);
            return pair?.Reference;
        }

        private static ReconciledExpenseItem BuildGroupItem(
            FinancialMovement m, ReconciliationItemRole role) =>
            new()
            {
                SourceEntityType = m.Source switch
                {
                    MovementSource.CreditCard => SourceEntityType.Transaction,
                    MovementSource.BankDebit => SourceEntityType.BankStatement,
                    _ => SourceEntityType.ManualExpense,
                },
                SourceId = m.Id,
                Role = role,
                OriginalAmount = m.Amount,
                OriginalDate = m.Date,
                OriginalDescription = m.Description,
                OriginalCurrency = m.Currency,
                OriginalSourceFile = m.SourceFile,
            };
    }
}
