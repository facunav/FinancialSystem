using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using FinancialSystem.Domain.Review;
using FinancialSystem.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de investigación de movimientos financieros — Fase 1 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: igual que FinancialTools/SystemTools, este archivo no recalcula nada
/// por su cuenta. SearchMovements delega la carga de movimientos (pendientes +
/// clasificados, banco + tarjeta, con IReviewEngine/IClassificationSuggestionService
/// ya resueltos) a IMovementsQueryService — exactamente el mismo servicio que usa
/// GET /api/movements, que a su vez es lo que consume movements.html. Los filtros
/// que ese servicio no expone como parámetro (categoría, contraparte, tipo, impacto,
/// estado, moneda, importe) se aplican en memoria sobre el resultado ya calculado,
/// sobre campos que MovementView ya expone — no es una segunda implementación del
/// query, es un recorte del mismo resultado.
/// </summary>
[McpServerToolType]
public sealed class MovementTools
{
    // Mismo límite y misma razón que MovementsEndpoints.GetAll
    // (src/FinancialMcp.Api/Endpoints/MovementsEndpoints.cs): IMovementsQueryService
    // ejecuta IReviewEngine/ISuspicionDetector internamente, que compara movimientos
    // par a par dentro del período (O(N²) acotado) — acotar el rango evita que N
    // crezca sin límite. Esta tool usa el mismo servicio, así que hereda la misma
    // razón para el mismo límite.
    private const int MaxDateRangeDays = 90;

    private readonly IMovementsQueryService _movementsQuery;
    private readonly IMovementLookupService _movementLookup;
    private readonly IApplicationDbContext _db;

    // PATCH-0106: mismo patrón de inyección directa que ya usan AuditTools/
    // AuditDatabaseTools -- AuditReportService está registrado en DI como clase
    // concreta, sin interfaz propia (ver DependencyInjection.cs).
    private readonly AuditReportService _auditReportService;

    public MovementTools(
        IMovementsQueryService movementsQuery,
        IMovementLookupService movementLookup,
        IApplicationDbContext db,
        AuditReportService auditReportService)
    {
        _movementsQuery = movementsQuery;
        _movementLookup = movementLookup;
        _db = db;
        _auditReportService = auditReportService;
    }

    [McpServerTool]
    [Description(
        "Busca movimientos financieros (banco y tarjeta, pendientes y ya clasificados) dentro " +
        "de un período, con filtros opcionales. Reutiliza el mismo servicio que la pantalla " +
        "Movimientos -- no recalcula nada por su cuenta. Usar para investigar qué movimientos " +
        "componen un total, por qué uno no aparece, o para buscar por texto, categoría, " +
        "contraparte, tipo, impacto financiero, estado, moneda o rango de importe. Cada " +
        "resultado incluye SourceEntityType además de SourceId (Patch 0109) -- los dos " +
        "identificadores exactos que piden GetMovement/ExplainMovement/ExplainClassification, " +
        "sin necesidad de adivinar entre 'Transaction' y 'BankStatement'.")]
    public async Task<string> SearchMovements(
        [Description("Fecha de inicio (yyyy-MM-dd). Por defecto, el primer día del mes de 'to'.")]
        string? from,
        [Description("Fecha de fin (yyyy-MM-dd). Por defecto, hoy (UTC). El rango máximo es de 90 días.")]
        string? to,
        [Description("Texto a buscar en la descripción del movimiento (contiene, sin distinguir mayúsculas).")]
        string? text = null,
        [Description("Id de Category (Category.Id) para filtrar. Solo aplica a movimientos ya clasificados.")]
        Guid? categoryId = null,
        [Description("Id de Counterparty (Counterparty.Id) para filtrar. Solo aplica a movimientos ya clasificados.")]
        Guid? counterpartyId = null,
        [Description("FinancialImpact: Expense, Income, InternalMovement o DebtPayment. Solo aplica a clasificados.")]
        string? financialImpact = null,
        [Description(
            "MovementType: Purchase, Transfer, Payment, Receipt, Fee, Interest, Refund, Adjustment " +
            "u Other. Solo aplica a movimientos ya clasificados.")]
        string? movementType = null,
        [Description("Estado: Pending (sin clasificar todavía), Reviewed o Confirmed.")]
        string? status = null,
        [Description("Moneda exacta, ej. ARS o USD.")]
        string? currency = null,
        [Description(
            "Importe mínimo. Convención de signo igual que el resto del sistema: positivo = " +
            "débito/gasto, negativo = crédito/ingreso.")]
        decimal? minAmount = null,
        [Description("Importe máximo, misma convención de signo que minAmount.")]
        decimal? maxAmount = null,
        CancellationToken ct = default)
    {
        if (!TryParseDate(to, DateOnly.FromDateTime(DateTime.UtcNow), out var effectiveTo))
            return $"Error: 'to' inválido ('{to}'). Usar formato yyyy-MM-dd.";
        if (!TryParseDate(from, new DateOnly(effectiveTo.Year, effectiveTo.Month, 1), out var effectiveFrom))
            return $"Error: 'from' inválido ('{from}'). Usar formato yyyy-MM-dd.";

        if (effectiveFrom > effectiveTo)
            return "Error: 'from' debe ser anterior o igual a 'to'.";

        var rangeDays = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return $"Error: el rango máximo permitido es de {MaxDateRangeDays} días.";

        FinancialImpact? parsedImpact = null;
        if (!string.IsNullOrWhiteSpace(financialImpact))
        {
            if (!Enum.TryParse(financialImpact, ignoreCase: true, out FinancialImpact impactValue))
                return $"Error: financialImpact inválido ('{financialImpact}'). " +
                       $"Valores válidos: {string.Join(", ", Enum.GetNames<FinancialImpact>())}.";
            parsedImpact = impactValue;
        }

        MovementType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(movementType))
        {
            if (!Enum.TryParse(movementType, ignoreCase: true, out MovementType typeValue))
                return $"Error: movementType inválido ('{movementType}'). " +
                       $"Valores válidos: {string.Join(", ", Enum.GetNames<MovementType>())}.";
            parsedType = typeValue;
        }

        var filterPending = false;
        ClassificationStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                filterPending = true;
            }
            else if (Enum.TryParse(status, ignoreCase: true, out ClassificationStatus statusValue))
            {
                parsedStatus = statusValue;
            }
            else
            {
                return $"Error: status inválido ('{status}'). " +
                       $"Valores válidos: Pending, {string.Join(", ", Enum.GetNames<ClassificationStatus>())}.";
            }
        }

        var movements = await _movementsQuery.GetAsync(
            effectiveFrom, effectiveTo, financialAccountId: null, search: text, ct);

        IEnumerable<MovementView> filtered = movements;
        if (categoryId is { } cid) filtered = filtered.Where(m => m.CategoryId == cid);
        if (counterpartyId is { } coid) filtered = filtered.Where(m => m.CounterpartyId == coid);
        if (parsedImpact is { } impact) filtered = filtered.Where(m => m.FinancialImpact == impact);
        if (parsedType is { } type) filtered = filtered.Where(m => m.MovementType == type);
        if (filterPending) filtered = filtered.Where(m => m.Status is null);
        if (parsedStatus is { } st) filtered = filtered.Where(m => m.Status == st);
        if (!string.IsNullOrWhiteSpace(currency))
            filtered = filtered.Where(m => string.Equals(m.Currency, currency, StringComparison.OrdinalIgnoreCase));
        if (minAmount is { } min) filtered = filtered.Where(m => m.Amount >= min);
        if (maxAmount is { } max) filtered = filtered.Where(m => m.Amount <= max);

        var results = filtered.OrderByDescending(m => m.Date).ToList();

        if (results.Count == 0)
            return $"No se encontraron movimientos entre {effectiveFrom:dd/MM/yyyy} y " +
                   $"{effectiveTo:dd/MM/yyyy} con esos filtros.";

        var (categoryNames, counterpartyNames) = await ResolveNamesAsync(results, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"{results.Count} movimiento(s) entre {effectiveFrom:dd/MM/yyyy} y {effectiveTo:dd/MM/yyyy}:");
        sb.AppendLine();

        foreach (var m in results)
        {
            // Patch 0109: SourceEntityType explícito -- sin esto, un consumidor que
            // encuentra un movimiento acá no tiene forma de saber qué pasarle a
            // GetMovement/ExplainMovement/ExplainClassification (los 3 exigen
            // SourceEntityType) salvo adivinar o probar los dos valores. Reutiliza
            // MovementSourceExtensions.ToSourceEntityType() (Domain.Review, PATCH-042) --
            // la misma conversión que ya usan AuditReportService/ClassificationSuggestionService,
            // no un mapeo nuevo.
            sb.AppendLine($"SourceEntityType: {m.Source.ToSourceEntityType()}");
            sb.AppendLine($"SourceId: {m.SourceId}");
            sb.AppendLine($"  Fecha bancaria:      {m.Date:dd/MM/yyyy}");
            sb.AppendLine($"  Período financiero:  {(m.EffectiveDate is { } eff ? eff.ToString("dd/MM/yyyy") : "(sin clasificar)")}");
            sb.AppendLine($"  Descripción:         {m.Description}");
            sb.AppendLine($"  Importe:             {m.Currency} {m.Amount:N2}");
            sb.AppendLine($"  Categoría:           {(m.CategoryId is { } catId ? categoryNames.GetValueOrDefault(catId, "(desconocida)") : "(sin clasificar)")}");
            sb.AppendLine($"  Contraparte:         {(m.CounterpartyId is { } cpId ? counterpartyNames.GetValueOrDefault(cpId, "(desconocida)") : "-")}");
            sb.AppendLine($"  Tipo:                {(m.MovementType?.ToString() ?? "(sin clasificar)")}");
            sb.AppendLine($"  Impacto financiero:  {(m.FinancialImpact?.ToString() ?? "(sin clasificar)")}");
            sb.AppendLine($"  Estado:              {(m.Status?.ToString() ?? "Pendiente")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Devuelve el detalle completo de un movimiento identificado por su tipo de origen y su Id " +
        "-- los mismos datos que usa ClassifyMovementCommand para identificarlo, no una convención " +
        "nueva. Incluye dato crudo, cuenta financiera, información técnica, clasificación (si ya la " +
        "tiene), información de procesamiento y grupo de matching. Usar para investigar a fondo un " +
        "movimiento puntual, por ejemplo uno encontrado con SearchMovements.")]
    public async Task<string> GetMovement(
        [Description("Tipo de origen: Transaction (tarjeta) o BankStatement (banco).")]
        string sourceEntityType,
        [Description("Id del movimiento en su tabla de origen (Transaction.Id o BankStatement.Id).")]
        Guid sourceId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(sourceEntityType, ignoreCase: true, out SourceEntityType parsedSource)
            || parsedSource is not (SourceEntityType.Transaction or SourceEntityType.BankStatement))
        {
            return $"Error: sourceEntityType inválido ('{sourceEntityType}'). " +
                   "Valores válidos: Transaction, BankStatement.";
        }

        var detail = await _movementLookup.GetBySourceAsync(parsedSource, sourceId, ct);
        if (detail is null)
            return $"No se encontró ningún {parsedSource} con Id {sourceId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Id: {detail.SourceId} ({detail.SourceEntityType})");
        sb.AppendLine();

        sb.AppendLine("Movimiento:");
        sb.AppendLine($"  Fecha bancaria:      {detail.Date:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"  Descripción:         {detail.Description}");
        sb.AppendLine($"  Importe:             {detail.Currency} {detail.Amount:N2}");
        sb.AppendLine($"  Cuenta financiera:   {detail.FinancialAccountName ?? "(sin asignar)"}");
        sb.AppendLine();

        sb.AppendLine("Información técnica:");
        sb.AppendLine($"  Archivo de origen:   {detail.SourceFile ?? "-"}");
        sb.AppendLine($"  ExternalId:          {detail.ExternalId ?? "-"}");
        sb.AppendLine($"  Registrado (UTC):    {detail.SourceRecordedAtUtc:O}");
        if (detail.SourceEntityType == SourceEntityType.Transaction)
        {
            sb.AppendLine($"  Cupón:               {detail.CouponNumber ?? "-"}");
            sb.AppendLine($"  Línea cruda:         {detail.RawLine ?? "-"}");
        }
        else
        {
            sb.AppendLine($"  Banco:               {detail.BankName ?? "-"}");
            sb.AppendLine($"  Cuenta (número):     {detail.AccountNumber ?? "-"}");
            sb.AppendLine($"  Detalle:             {detail.BankDetail ?? "-"}");
            sb.AppendLine($"  Saldo posterior:     {(detail.Balance is { } bal ? bal.ToString("N2") : "-")}");
            sb.AppendLine($"  Hoja / fila:         {detail.SheetName ?? "-"} / {(detail.RowNumber?.ToString() ?? "-")}");
            sb.AppendLine($"  Comercio (débito):   {detail.Merchant ?? "-"}");
            sb.AppendLine($"  Hora comercio (UTC): {(detail.MerchantAtUtc is { } mAt ? mAt.ToString("O") : "-")}");
        }
        sb.AppendLine();

        if (detail.Classification is null)
        {
            sb.AppendLine("Clasificación: pendiente (todavía no tiene ClassifiedMovement).");
            return sb.ToString();
        }

        var c = detail.Classification;
        sb.AppendLine("Clasificación:");
        sb.AppendLine($"  ClassifiedMovementId: {c.ClassifiedMovementId}");
        sb.AppendLine($"  Período financiero (EffectiveDate): {c.EffectiveDate:dd/MM/yyyy}");
        sb.AppendLine($"  Categoría:            {c.CategoryName ?? "(desconocida)"}");
        sb.AppendLine($"  Contraparte:          {c.CounterpartyName ?? "-"}");
        sb.AppendLine($"  Tipo:                 {c.MovementType}");
        sb.AppendLine($"  Impacto financiero:   {c.FinancialImpact}");
        sb.AppendLine($"  Estado:               {c.Status}");
        sb.AppendLine($"  Comentario:           {c.Comment ?? "-"}");
        sb.AppendLine();

        sb.AppendLine("Información de procesamiento:");
        sb.AppendLine($"  Origen del procesamiento: {c.ProcessingSource}");
        sb.AppendLine($"  MatchScore:          {(c.MatchScore is { } score ? score.ToString("N2") : "-")}");
        sb.AppendLine($"  AmountDelta:         {(c.AmountDelta is { } delta ? delta.ToString("N2") : "-")}");
        sb.AppendLine($"  Creado (UTC):        {c.CreatedAt:O}");
        sb.AppendLine($"  Procesado (UTC):     {c.ProcessedAt:O}");
        sb.AppendLine($"  Procesado por:       {c.ProcessedBy ?? "-"}");
        sb.AppendLine();

        sb.AppendLine($"Grupo de matching ({c.GroupItems.Count} ítem(s); este movimiento es {c.ItemRole}):");
        foreach (var gi in c.GroupItems)
        {
            var marker = gi.SourceEntityType == detail.SourceEntityType && gi.SourceId == detail.SourceId
                ? " (este)"
                : "";
            sb.AppendLine($"  - {gi.SourceEntityType} {gi.SourceId} — {gi.Role}{marker}");
        }

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Devuelve una explicación estructurada y estable de un movimiento -- mismas secciones " +
        "siempre (Movimiento, Origen de importación, Clasificación, Procesamiento, Matching, " +
        "Observaciones), sin lenguaje natural. Reutiliza el mismo lookup que GetMovement (una " +
        "sola consulta) pero organizado por tema, con una sección de Observaciones derivada " +
        "exclusivamente de datos ya existentes -- no evalúa nada nuevo, no usa IA. PATCH-0107: " +
        "'Origen de importación' responde de dónde vino el movimiento (ImportBatchId, archivo, " +
        "handler, parser, corrida) a partir de ImportBatch (Patch 0105) -- ausente para " +
        "movimientos anteriores a ese patch, y mostrado como referencia sin resolver cuando el " +
        "ImportBatch vinculado ya no existe. Pensada para que un LLM la use como base de " +
        "razonamiento sin tener que interpretar prosa.")]
    public async Task<string> ExplainMovement(
        [Description("Tipo de origen: Transaction (tarjeta) o BankStatement (banco).")]
        string sourceEntityType,
        [Description("Id del movimiento en su tabla de origen (Transaction.Id o BankStatement.Id).")]
        Guid sourceId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(sourceEntityType, ignoreCase: true, out SourceEntityType parsedSource)
            || parsedSource is not (SourceEntityType.Transaction or SourceEntityType.BankStatement))
        {
            return $"Error: sourceEntityType inválido ('{sourceEntityType}'). " +
                   "Valores válidos: Transaction, BankStatement.";
        }

        var detail = await _movementLookup.GetBySourceAsync(parsedSource, sourceId, ct);
        if (detail is null)
            return $"No se encontró ningún {parsedSource} con Id {sourceId}.";

        var c = detail.Classification;
        var sb = new StringBuilder();

        sb.AppendLine($"Id: {detail.SourceId} ({detail.SourceEntityType})");
        sb.AppendLine();

        sb.AppendLine("Movimiento");
        sb.AppendLine($"- Banco: {detail.BankName ?? "-"}");
        sb.AppendLine($"- Cuenta: {detail.FinancialAccountName ?? "(sin asignar)"}");
        sb.AppendLine($"- Fecha bancaria: {detail.Date:yyyy-MM-dd}");
        sb.AppendLine($"- EffectiveDate: {(c is not null ? c.EffectiveDate.ToString("yyyy-MM-dd") : "(sin clasificar)")}");
        sb.AppendLine($"- Importe: {detail.Amount:N2}");
        sb.AppendLine($"- Moneda: {detail.Currency}");
        sb.AppendLine();

        // PATCH-0107: responde "¿de dónde vino este movimiento?" -- MovementTools no
        // consulta la base para esto, solo consume MovementDetail.ImportBatchId/ImportOrigin,
        // ya resueltos por MovementLookupService (mismo criterio que ResolveFinancialAccountNameAsync,
        // sin N+1). Distingue explícitamente los 3 casos posibles (ver doc-comment de
        // MovementDetail.ImportBatchId): sin corrida registrada (movimiento anterior a
        // Patch 0105), corrida resuelta, o referencia guardada que ya no resuelve (referencia
        // blanda, sin FK -- no se oculta como excepción).
        sb.AppendLine("Origen de importación");
        if (detail.ImportBatchId is null)
        {
            sb.AppendLine("- Sin corrida de importación registrada (movimiento anterior a Patch 0105).");
        }
        else if (detail.ImportOrigin is null)
        {
            sb.AppendLine($"- ImportBatchId: {detail.ImportBatchId}");
            sb.AppendLine(
                "- ImportBatch: (no resuelve) -- el vínculo está guardado pero no existe actualmente " +
                "el registro de esa corrida (referencia blanda, sin FK; ver Patch 0105).");
        }
        else
        {
            var origin = detail.ImportOrigin;
            sb.AppendLine($"- ImportBatchId: {origin.ImportBatchId}");
            sb.AppendLine($"- Archivo: {origin.SourceFile ?? "-"}");
            sb.AppendLine($"- Handler: {origin.HandlerName}");
            sb.AppendLine($"- Parser: {origin.ParserUsed ?? "-"}");
            sb.AppendLine($"- Inicio de la corrida (UTC): {origin.StartedAtUtc:O}");
            sb.AppendLine($"- Resultado de la corrida: {(origin.Outcome is { } outcome ? outcome.ToString() : "-")}");
        }
        sb.AppendLine();

        sb.AppendLine("Clasificación");
        sb.AppendLine($"- Categoría: {(c is not null ? c.CategoryName ?? "(no resuelve)" : "-")}");
        sb.AppendLine($"- Contraparte: {c?.CounterpartyName ?? "-"}");
        sb.AppendLine($"- Tipo: {(c is not null ? c.MovementType.ToString() : "-")}");
        sb.AppendLine($"- Impacto: {(c is not null ? c.FinancialImpact.ToString() : "-")}");
        sb.AppendLine($"- Estado: {(c is not null ? c.Status.ToString() : "Pendiente")}");
        sb.AppendLine();

        sb.AppendLine("Procesamiento");
        sb.AppendLine($"- ProcessingSource: {(c is not null ? c.ProcessingSource.ToString() : "-")}");
        sb.AppendLine($"- MatchScore: {(c?.MatchScore is { } score ? score.ToString("N2") : "-")}");
        sb.AppendLine($"- AmountDelta: {(c?.AmountDelta is { } delta ? delta.ToString("N2") : "-")}");
        sb.AppendLine();

        sb.AppendLine("Matching");
        sb.AppendLine($"- Cantidad de Items: {(c is not null ? c.GroupItems.Count.ToString() : "0")}");
        sb.AppendLine($"- Roles: {(c is not null && c.GroupItems.Count > 0 ? string.Join(", ", c.GroupItems.Select(i => i.Role.ToString())) : "-")}");
        sb.AppendLine($"- Movimiento individual o grupo: {(c is not null ? (c.GroupItems.Count > 1 ? "Grupo" : "Individual") : "-")}");
        sb.AppendLine();

        sb.AppendLine("Observaciones");
        var observations = BuildObservations(detail, c);
        if (observations.Count == 0)
            sb.AppendLine("- (sin observaciones)");
        else
            foreach (var observation in observations)
                sb.AppendLine($"- {observation}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Responde puntualmente '¿por qué este movimiento terminó con esta clasificación?' -- " +
        "estado actual, cómo se obtuvo (ProcessingSource, sin inventar el origen si el sistema " +
        "no lo registra), información de procesamiento, grupo de matching y observaciones, todo " +
        "en formato estructurado. Reutiliza el mismo lookup que GetMovement/ExplainMovement (una " +
        "sola consulta). PATCH-0106: además, para un movimiento ya clasificado, incluye una " +
        "sección 'Comparación con la sugerencia actual' -- si la clasificación vigente coincide " +
        "con lo que IClassificationSuggestionService/Counterparty.Default* sugerirían hoy, " +
        "reutilizando exactamente la misma interpretación que ya usa el Centro de Auditoría " +
        "(AuditReportService, ver FindMisclassifiedMovements) -- sin duplicarla ni recalcularla " +
        "con un criterio propio.")]
    public async Task<string> ExplainClassification(
        [Description("Tipo de origen: Transaction (tarjeta) o BankStatement (banco).")]
        string sourceEntityType,
        [Description("Id del movimiento en su tabla de origen (Transaction.Id o BankStatement.Id).")]
        Guid sourceId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(sourceEntityType, ignoreCase: true, out SourceEntityType parsedSource)
            || parsedSource is not (SourceEntityType.Transaction or SourceEntityType.BankStatement))
        {
            return $"Error: sourceEntityType inválido ('{sourceEntityType}'). " +
                   "Valores válidos: Transaction, BankStatement.";
        }

        var detail = await _movementLookup.GetBySourceAsync(parsedSource, sourceId, ct);
        if (detail is null)
            return $"No se encontró ningún {parsedSource} con Id {sourceId}.";

        var c = detail.Classification;
        var sb = new StringBuilder();

        sb.AppendLine($"Id: {detail.SourceId} ({detail.SourceEntityType})");
        sb.AppendLine();

        sb.AppendLine("Movimiento");
        sb.AppendLine($"- Id: {detail.SourceId}");
        sb.AppendLine($"- Fecha: {detail.Date:yyyy-MM-dd}");
        sb.AppendLine($"- Descripción: {detail.Description}");
        sb.AppendLine($"- Importe: {detail.Currency} {detail.Amount:N2}");
        sb.AppendLine($"- Cuenta: {detail.FinancialAccountName ?? "(sin asignar)"}");
        sb.AppendLine();

        sb.AppendLine("Estado");
        sb.AppendLine($"- {(c is not null ? "Clasificado" : "Pendiente")}");
        sb.AppendLine($"- Categoría: {(c is not null ? c.CategoryName ?? "(no resuelve)" : "-")}");
        sb.AppendLine($"- Tipo: {(c is not null ? c.MovementType.ToString() : "-")}");
        sb.AppendLine($"- Impacto: {(c is not null ? c.FinancialImpact.ToString() : "-")}");
        sb.AppendLine($"- Contraparte: {c?.CounterpartyName ?? "-"}");
        sb.AppendLine($"- EffectiveDate: {(c is not null ? c.EffectiveDate.ToString("yyyy-MM-dd") : "-")}");
        sb.AppendLine($"- Status: {(c is not null ? c.Status.ToString() : "-")}");
        sb.AppendLine();

        sb.AppendLine("Cómo se obtuvo esa clasificación");
        foreach (var line in ExplainClassificationOrigin(c))
            sb.AppendLine($"- {line}");
        sb.AppendLine();

        // PATCH-0106: la tool no compara nada por su cuenta -- arma un MovementView con
        // los mismos campos que ya tiene (ninguno nuevo) y le pide la comparación a
        // AuditReportService, que reutiliza exactamente el mismo cálculo que ya usa el
        // Centro de Auditoría (BuildSuggestionMotivos/BuildDefaultMotivos). Para un
        // movimiento pendiente (c is null) no se llama al servicio -- mismo criterio
        // que el resto de las secciones de este método ("- (sin clasificar, no aplica)").
        sb.AppendLine("Comparación con la sugerencia actual");
        if (c is null)
        {
            sb.AppendLine("- (sin clasificar, no aplica)");
        }
        else
        {
            var comparisonView = ToComparisonMovementView(detail, c);
            var comparison = await _auditReportService.ExplainCurrentClassificationAsync(comparisonView, ct);

            if (!comparison.HasSuggestion)
            {
                sb.AppendLine(
                    "- Sin sugerencia disponible: no hay historial de descripciones idénticas ni " +
                    "valores por defecto de contraparte suficientes para comparar. Esto NO significa " +
                    "que la clasificación actual sea correcta ni incorrecta.");
            }
            else if (comparison.Motivos.Count == 0)
            {
                sb.AppendLine(
                    "- Coincide: la clasificación actual coincide con la sugerencia vigente del " +
                    "motor de clasificación.");
            }
            else
            {
                sb.AppendLine($"- No coincide ({comparison.Motivos.Count} dimensión(es) divergente(s)):");
                foreach (var motivo in comparison.Motivos)
                {
                    sb.AppendLine($"  - Dimensión: {motivo.Dimension}");
                    sb.AppendLine($"    Valor actual: {motivo.ValorActual}");
                    sb.AppendLine($"    Valor sugerido: {motivo.ValorSugerido}");
                    sb.AppendLine($"    Origen: {motivo.Origen}");
                    sb.AppendLine($"    Confianza: {motivo.Confianza ?? "-"}");
                    // Tier 0 (AGENT-004): misma evidencia y misma semántica que
                    // FindMisclassifiedMovements (AuditReportService.FormatMisclassifiedMovementsReport)
                    // -- MatchCount/WinnerCount/Reason ya calculados por
                    // ClassificationSuggestionService, solo faltaba imprimirlos acá también.
                    if (motivo.MatchCount is not null && motivo.WinnerCount is not null)
                        sb.AppendLine($"    Evidencia: {motivo.WinnerCount} de {motivo.MatchCount}");
                    if (motivo.Reason is not null)
                        sb.AppendLine($"    Reason: {motivo.Reason}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("Matching");
        if (c is not null)
        {
            sb.AppendLine($"- Cantidad de Items: {c.GroupItems.Count}");
            sb.AppendLine($"- Roles: {string.Join(", ", c.GroupItems.Select(i => i.Role.ToString()))}");
            sb.AppendLine($"- {(c.GroupItems.Count > 1 ? "Grupo" : "Individual")}");
        }
        else
        {
            sb.AppendLine("- (sin clasificar, no aplica)");
        }
        sb.AppendLine();

        sb.AppendLine("Procesamiento");
        if (c is not null)
        {
            sb.AppendLine($"- ProcessingSource: {c.ProcessingSource}");
            sb.AppendLine($"- MatchScore: {(c.MatchScore is { } score ? score.ToString("N2") : "-")}");
            sb.AppendLine($"- AmountDelta: {(c.AmountDelta is { } delta ? delta.ToString("N2") : "-")}");
            sb.AppendLine($"- CreatedAt: {c.CreatedAt:O}");
            sb.AppendLine($"- ProcessedAt: {c.ProcessedAt:O}");
            sb.AppendLine($"- ProcessedBy: {c.ProcessedBy ?? "-"}");
        }
        else
        {
            sb.AppendLine("- (sin clasificar, no aplica)");
        }
        sb.AppendLine();

        sb.AppendLine("Observaciones");
        var observations = BuildClassificationObservations(detail, c);
        if (observations.Count == 0)
            sb.AppendLine("- (sin observaciones)");
        else
            foreach (var observation in observations)
                sb.AppendLine($"- {observation}");

        return sb.ToString();
    }

    // "Cómo se obtuvo esa clasificación": exclusivamente lo que ProcessingSource ya
    // persiste. Deliberadamente NO se compara contra IClassificationSuggestionService
    // ni contra Counterparty.Default* para adivinar si influyeron -- ClassifyMovementHandler
    // no consulta ninguno de los dos al escribir, así que el sistema no guarda esa
    // relación causal en ningún lado. Afirmarla acá sería inventar el origen, exactamente
    // lo que esta tool tiene prohibido hacer (esa comparación sí es legítima, pero como
    // señal de auditoría -- "¿la clasificación actual coincide con lo que el motor sugeriría
    // hoy?" -- no como afirmación de procedencia; ver la sección "Comparación con la
    // sugerencia actual" más abajo, PATCH-0106, y FindMisclassifiedMovements).
    private static List<string> ExplainClassificationOrigin(MovementClassificationDetail? c)
    {
        if (c is null)
            return ["Sin clasificar todavía -- no hay origen que explicar."];

        var lines = new List<string>
        {
            c.ProcessingSource switch
            {
                ProcessingSource.ManualReview =>
                    "Clasificación manual: el usuario clasificó este movimiento directamente, " +
                    "sin coincidencia externa registrada.",
                ProcessingSource.ManualMatch =>
                    "Coincidencia armada manualmente por el usuario (mecanismo de matching legado, " +
                    "retirado en PR-L4 -- sin productor activo hoy; solo puede venir de datos históricos).",
                ProcessingSource.ConfirmedFromSuggestion =>
                    "Confirmación de una sugerencia de confianza media del motor de matching legado " +
                    "(retirado en PR-L4 -- sin productor activo hoy; solo puede venir de datos históricos).",
                ProcessingSource.ConfirmedFromHighConfidenceSuggestion =>
                    "Confirmación de una sugerencia de alta confianza del motor de matching legado " +
                    "(retirado en PR-L4 -- sin productor activo hoy; solo puede venir de datos históricos).",
                _ => "El origen exacto de esta clasificación no está disponible en el modelo actual.",
            },
        };

        // ClassifyMovementHandler no actualiza ProcessingSource al reclasificar (solo lo
        // setea al crear) -- si hubo una reclasificación posterior, este campo describe el
        // origen de la clasificación INICIAL, no necesariamente el de los valores actuales.
        if (c.ProcessedAt > c.CreatedAt)
            lines.Add(
                "Este movimiento fue reclasificado después de su clasificación inicial. " +
                "ProcessingSource describe el origen de esa clasificación inicial -- no se " +
                "actualiza al reclasificar, así que no necesariamente describe el origen de " +
                "los valores actuales.");

        return lines;
    }

    // PATCH-0106: arma el MovementView que AuditReportService.ExplainCurrentClassificationAsync
    // necesita -- ningún campo nuevo, todos ya vienen de MovementDetail/MovementClassificationDetail
    // (ya resueltos por IMovementLookupService.GetBySourceAsync, misma consulta que ya usa
    // el resto del método). Warning/Suggestions quedan vacíos: son campos de MovementView
    // pensados para movimientos PENDIENTES (avisos de ISuspicionDetector, sugerencias de
    // IClassificationSuggestionService para clasificar) -- no aplican a un movimiento que ya
    // tiene clasificación, y ExplainCurrentClassificationAsync no los lee. Mapeo
    // SourceEntityType -> MovementSource idéntico al que ya usa MovementsQueryService.LoadClassifiedAsync.
    private static MovementView ToComparisonMovementView(MovementDetail detail, MovementClassificationDetail c) => new(
        detail.SourceId,
        detail.Date,
        detail.Description,
        detail.Amount,
        detail.Currency,
        detail.SourceEntityType == SourceEntityType.BankStatement ? MovementSource.BankDebit : MovementSource.CreditCard,
        detail.FinancialAccountId,
        Status: c.Status,
        CategoryId: c.CategoryId,
        CounterpartyId: c.CounterpartyId,
        MovementType: c.MovementType,
        FinancialImpact: c.FinancialImpact,
        Warning: null,
        Suggestions: [],
        Merchant: detail.Merchant,
        MerchantAtUtc: detail.MerchantAtUtc,
        EffectiveDate: c.EffectiveDate);

    // Mismas 5 observaciones que pide la tool, cada una una comparación directa sobre
    // campos que MovementDetail/MovementClassificationDetail ya exponen -- ninguna
    // heurística nueva. Subconjunto deliberado de BuildObservations (ExplainMovement):
    // esta tool solo necesita las observaciones relacionadas con el ORIGEN/procedencia de
    // la clasificación, no todas las de ExplainMovement (ej. no repite la de CategoryId
    // huérfano, que ya cubre GetMovement/ExplainMovement).
    private static List<string> BuildClassificationObservations(MovementDetail detail, MovementClassificationDetail? c)
    {
        var observations = new List<string>();

        if (c is null)
        {
            observations.Add("Pendiente de clasificar.");
            return observations;
        }

        if (c.EffectiveDate.Date != detail.Date.Date)
            observations.Add("EffectiveDate distinto de la fecha original (fue ajustado manualmente).");

        if (!string.IsNullOrWhiteSpace(c.Comment))
            observations.Add("Tiene comentario.");

        if (c.CounterpartyId is null)
            observations.Add("No tiene contraparte.");

        if (c.GroupItems.Count > 1)
            observations.Add($"Tiene grupo de matching ({c.GroupItems.Count} movimientos).");

        if (c.ProcessedAt > c.CreatedAt)
            observations.Add("Fue reclasificado.");

        return observations;
    }

    // Observaciones derivadas exclusivamente de campos que MovementDetail/
    // MovementClassificationDetail ya exponen -- ninguna heurística nueva, ningún
    // umbral inventado. Por eso, deliberadamente, no hay una observación de "score
    // bajo": MatchScore no tiene ningún umbral definido en ningún lado del proyecto
    // (era del motor de matching retirado en PR-L4) y agregar uno acá sería inventar
    // un criterio que no existe -- ver Ideas futuras en el mensaje del PR.
    private static List<string> BuildObservations(MovementDetail detail, MovementClassificationDetail? c)
    {
        var observations = new List<string>();

        if (c is null)
        {
            observations.Add("Pendiente de clasificar.");
            return observations;
        }

        if (c.EffectiveDate.Date != detail.Date.Date)
            observations.Add("EffectiveDate fue ajustado manualmente (difiere de la fecha bancaria).");

        observations.Add(c.CounterpartyId is not null
            ? "Tiene contraparte asignada."
            : "Sin contraparte asignada.");

        if (c.CategoryName is null)
            observations.Add("CategoryId no resuelve a ninguna categoría existente.");

        if (!string.IsNullOrWhiteSpace(c.Comment))
            observations.Add("Tiene comentario.");

        if (c.ProcessedAt > c.CreatedAt)
            observations.Add("Fue reclasificado al menos una vez (ProcessedAt posterior a CreatedAt).");

        if (c.GroupItems.Count > 1)
            observations.Add($"Pertenece a un grupo de matching de {c.GroupItems.Count} movimientos (no es 1:1).");

        if (c.AmountDelta is { } amountDelta && amountDelta != 0)
            observations.Add("Tiene una diferencia de importe registrada entre References y Candidates del grupo (AmountDelta != 0).");

        return observations;
    }

    // Resolución en bloque (2 queries, nunca una por fila) -- mismo criterio que ya
    // usa MovementsQueryService.LoadClassifiedAsync para resolver cuentas asignadas.
    // MovementView solo trae CategoryId/CounterpartyId (sin hidratar la entidad, ver
    // doc-comment de MovementView) -- acá se resuelve el nombre porque, a diferencia
    // de la pantalla Movimientos (que ya tiene los catálogos cargados en el cliente),
    // el resultado de esta tool tiene que ser legible por sí solo.
    private async Task<(Dictionary<Guid, string> Categories, Dictionary<Guid, string> Counterparties)> ResolveNamesAsync(
        List<MovementView> results, CancellationToken ct)
    {
        var categoryIds = results
            .Where(m => m.CategoryId is not null)
            .Select(m => m.CategoryId!.Value)
            .Distinct()
            .ToList();
        var counterpartyIds = results
            .Where(m => m.CounterpartyId is not null)
            .Select(m => m.CounterpartyId!.Value)
            .Distinct()
            .ToList();

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Categories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        var counterparties = counterpartyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Counterparties
                .AsNoTracking()
                .Where(c => counterpartyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return (categories, counterparties);
    }

    private static bool TryParseDate(string? value, DateOnly fallback, out DateOnly result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = fallback;
            return true;
        }

        return DateOnly.TryParse(value, out result);
    }
}
