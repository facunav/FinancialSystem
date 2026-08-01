using FinancialSystem.Domain.Enums;

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
    long DurationMs,
    int MisclassifiedDetected,
    int MisclassifiedReviewed,
    IReadOnlyList<MisclassifiedMovementDto> MisclassifiedMovements);

// ── Clasificaciones dudosas, en detalle ───────────────────────────────────────
//
// Refleja MisclassifiedMovement/MisclassifiedMotivo (AuditReportService) campo a
// campo. Reviewed/ReviewedAtUtc vienen de MovementAuditDecision -- el movimiento
// revisado sigue apareciendo acá, no se filtra ("no oculta el hallazgo").

public sealed record MisclassifiedMovementDto(
    SourceEntityType SourceEntityType,
    Guid SourceId,
    DateTime Date,
    string Description,
    string CurrentCategory,
    string CurrentCounterparty,
    string CurrentMovementType,
    string CurrentFinancialImpact,
    IReadOnlyList<MisclassifiedMotivoDto> Motivos,
    bool Reviewed,
    DateTime? ReviewedAtUtc);

public sealed record MisclassifiedMotivoDto(
    string Dimension,
    string CurrentValue,
    string SuggestedValue,
    int? MatchCount,
    int? WinnerCount);

// ── POST /api/audit/reviews ───────────────────────────────────────────────────
//
// Registra que una o más movimientos fueron revisados -- ver MovementAuditDecision. Acepta
// una lista para no necesitar un endpoint separado para revisión en lote.

public sealed record ReviewMovementsRequest(IReadOnlyList<ReviewMovementRequestItem> Movements);

public sealed record ReviewMovementRequestItem(SourceEntityType SourceEntityType, Guid SourceId);
