using FinancialSystem.Api.DTOs;
using FinancialSystem.Application.Imports;
using FinancialSystem.Application.Movements;
using FinancialSystem.Infrastructure.Audit;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace FinancialSystem.Api.Endpoints;

/// <summary>
/// Backend del Centro de Auditoría (audit.html) -- Fase 4 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md, sin Ollama, sin IA,
/// sin MCP: la pantalla es solo una interfaz para el usuario del sistema sobre
/// capacidades que ya existen. La tool MCP AuditDatabase sigue existiendo tal cual
/// para clientes externos (Claude Desktop, etc.) -- este archivo es un consumidor
/// más de los mismos servicios de Application, no un reemplazo.
///
/// /status es de solo lectura (IImportHistoryQueryService, IMovementsQueryService,
/// AppDbContext.Database.CanConnectAsync -- mismo mecanismo que ya usa
/// SystemTools.Health en el MCP) y no ejecuta ninguna auditoría, para que la
/// pantalla cargue rápido. /report ejecuta la auditoría completa vía
/// AuditReportService.BuildFullAuditReportAsync -- la misma clase, sin
/// reimplementar ninguna regla, que ya usa AuditDatabaseTools.AuditDatabase() en el
/// MCP (ver AuditReportService.cs para el porqué de esa ubicación compartida).
///
/// /report ya no decide "mes en curso" de forma implícita: from/to son query params
/// opcionales con el mismo patrón (nullable + fallback a mes en curso + límite de
/// rango) que ya usa MovementsEndpoints.GetAll -- el Centro de Auditoría (audit.html)
/// siempre va a mandarlos explícitos desde su selector de período; el fallback solo
/// cubre a un caller que no los mande (compatibilidad, no una decisión de negocio).
/// </summary>
public static class AuditEndpoints
{
    // Mismo límite y misma razón que MovementsEndpoints.MaxDateRangeDays: acota el
    // costo de IReviewEngine/IClassificationSuggestionService sobre el período.
    private const int MaxDateRangeDays = 90;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit");

        group.MapGet("/status", GetStatus);
        group.MapGet("/report", GetReport);

        return app;
    }

    // GET /api/audit/status
    private static async Task<IResult> GetStatus(
        [FromServices] AppDbContext db,
        [FromServices] IImportHistoryQueryService importHistory,
        [FromServices] IMovementsQueryService movementsQuery,
        CancellationToken ct)
    {
        bool databaseConnected;
        try
        {
            databaseConnected = await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            databaseConnected = false;
        }

        var recentImports = await importHistory.GetHistoryAsync(take: 1, ct);
        var lastImport = recentImports.Count == 0
            ? null
            : new AuditLastImportDto(
                recentImports[0].SourceFile, recentImports[0].CompletedAtUtc, recentImports[0].Status.ToString());

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(to.Year, to.Month, 1);
        var movements = await movementsQuery.GetAsync(from, to, financialAccountId: null, search: null, ct);
        var pending = movements.Count(m => m.Status is null);

        return Results.Ok(new AuditStatusResponse(
            databaseConnected, lastImport, movements.Count, pending, movements.Count - pending));
    }

    // GET /api/audit/report?from=&to=
    private static async Task<IResult> GetReport(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] AuditReportService auditReportService,
        CancellationToken ct)
    {
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveFrom = from ?? new DateOnly(effectiveTo.Year, effectiveTo.Month, 1);

        if (effectiveFrom > effectiveTo)
            return Results.BadRequest("'from' debe ser anterior o igual a 'to'");

        var rangeDays = effectiveTo.DayNumber - effectiveFrom.DayNumber + 1;
        if (rangeDays > MaxDateRangeDays)
            return Results.BadRequest($"El rango máximo permitido es de {MaxDateRangeDays} días");

        var result = await auditReportService.BuildFullAuditReportAsync(effectiveFrom, effectiveTo, ct);

        return Results.Ok(new AuditReportResponse(
            result.From, result.To, result.MovementsAnalyzed, result.Pending, result.Classified,
            result.SuspiciousGroups, result.Misclassified, result.OpenInvestigations,
            result.ResolvedInvestigations, result.TotalProblems,
            result.MisclassifiedText, result.SuspiciousText, result.PendingText, result.InvestigationsText));
    }
}
