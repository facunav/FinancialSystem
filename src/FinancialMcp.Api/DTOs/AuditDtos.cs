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
// Refleja FullAuditReport (FinancialSystem.Infrastructure.Audit.AuditReportService)
// campo a campo -- el mismo cálculo que ya usa la tool MCP AuditDatabase, sin
// reinterpretarlo. Los cuatro *Text son los mismos bloques de "Problemas
// encontrados" que antes solo existían concatenados dentro de un único string que
// el cliente partía buscando un marcador de texto -- ahora vienen ya separados.

public sealed record AuditReportResponse(
    DateOnly From,
    DateOnly To,
    int MovementsAnalyzed,
    int Pending,
    int Classified,
    int SuspiciousGroups,
    int Misclassified,
    int OpenInvestigations,
    int ResolvedInvestigations,
    int TotalProblems,
    string MisclassifiedText,
    string SuspiciousText,
    string PendingText,
    string InvestigationsText,
    DateTime GeneratedAtUtc,
    long DurationMs);
