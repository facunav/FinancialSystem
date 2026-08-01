namespace FinancialSystem.Api.DTOs;

// ── GET /api/audit/status ─────────────────────────────────────────────────────
//
// Solo lectura, sin ejecutar ninguna auditoría (sin IReviewEngine ni
// IClassificationSuggestionService) -- pensado para cargar rápido al abrir la
// pantalla. Mismo período por defecto (mes en curso) que /api/audit/report.

public sealed record AuditStatusResponse(
    bool DatabaseConnected,
    AuditLastImportDto? LastImport,
    int MovementsAnalyzed,
    int Pending,
    int Classified);

public sealed record AuditLastImportDto(string SourceFile, DateTime CompletedAtUtc, string Status);

// ── GET /api/audit/report ─────────────────────────────────────────────────────
//
// Report es exactamente el texto que devuelve AuditReportService.BuildFullAuditReportAsync
// -- la misma lógica que ya usa la tool MCP AuditDatabase, sin reinterpretarlo.

public sealed record AuditReportResponse(string Report);
