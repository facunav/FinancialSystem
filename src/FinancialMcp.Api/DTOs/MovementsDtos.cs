using FinancialSystem.Application.Movements;
using FinancialSystem.Application.Suggestions;
using FinancialSystem.Domain.Enums;

namespace FinancialSystem.Api.DTOs;

// ── GET /api/movements ──────────────────────────────────────────────────────
// DTO propio de este endpoint. Contiene solo lo que la pantalla Movimientos
// necesita: listar, clasificar y asignar/reasignar cuenta — pendientes y ya
// clasificados (K3), con sospechosos (K6) y sugerencias de clasificación (PR-S4)
// como contexto adicional de solo lectura.
//
// PR-L4: hasta acá también incluía Suggestion (K4/K5) — se retiró junto con todo
// el backend de matching contra movimientos legacy. PR-S4 reintroduce un campo
// Suggestions con una fuente y una forma completamente distintas (recomendaciones
// de clasificación desde historial, ver ClassificationSuggestion) — no una
// restauración de aquel. Ver MovementView (Application) para el detalle.

public sealed record MovementListItemDto(
    Guid SourceId,
    DateTime Date,
    string Description,
    decimal Amount,
    string Currency,
    string Source,
    Guid? FinancialAccountId,
    // "Pending" cuando el movimiento todavía no tiene ClassifiedMovementItem.
    // Si no, el Status real del ClassifiedMovement ("Reviewed"/"Confirmed").
    string Status,
    Guid? CategoryId,
    Guid? CounterpartyId,
    string? MovementType,
    string? FinancialImpact,
    // K6: contexto de solo lectura, null si el movimiento no cayó en ningún grupo
    // sospechoso. Nunca presente en movimientos ya clasificados (ver MovementView.Warning).
    MovementWarningDto? Warning,
    // PR-S4: contexto de solo lectura, lista vacía (nunca null) si el motor no
    // encontró señal suficiente. Nunca presente en movimientos ya clasificados (ver
    // MovementView.Suggestions). Todavía sin consumidor en movements.html — ver PR-S5.
    IReadOnlyList<ClassificationSuggestionDto> Suggestions,
    // PR3: comercio real y fecha/hora exacta desde Tarjeta de Débito, cuando el
    // BankStatement de origen fue enriquecido. Null si no aplica — no reemplaza
    // Description (ver MovementView.Merchant).
    string? Merchant,
    DateTime? MerchantAtUtc)
{
    public static MovementListItemDto Create(MovementView m) => new(
        m.SourceId,
        m.Date,
        m.Description,
        m.Amount,
        m.Currency,
        m.Source.ToString(),
        m.FinancialAccountId,
        m.Status?.ToString() ?? "Pending",
        m.CategoryId,
        m.CounterpartyId,
        m.MovementType?.ToString(),
        m.FinancialImpact?.ToString(),
        m.Warning is null ? null : MovementWarningDto.Create(m.Warning),
        m.Suggestions.Select(ClassificationSuggestionDto.Create).ToList(),
        m.Merchant,
        m.MerchantAtUtc);
}

public sealed record MovementWarningDto(
    string Reason,
    string Description,
    int GroupSize)
{
    public static MovementWarningDto Create(MovementWarning w) => new(
        w.Reason.ToString(),
        w.Description,
        w.GroupSize);
}

// PR-S4: diseñado desde cero para representar una recomendación de clasificación
// (ver ClassificationSuggestion, Application) — no reutiliza ni copia la forma del
// extinto MovementSuggestionDto (K4/K5, matching Legacy), que representaba un
// movimiento candidato con score de confianza, no una recomendación por dimensión.
//
// Value siempre viaja como string, sea cual sea el tipo real detrás de Dimension
// (Guid para Category/Counterparty, nombre de enum para MovementType/FinancialImpact)
// — mismo criterio que ya usa el resto de este DTO para enums (Source, Status,
// MovementType, FinancialImpact: siempre string en el wire). Un consumidor debe
// interpretar Value según Dimension, igual que ya debe hacerlo con Domain.
public sealed record ClassificationSuggestionDto(
    string Dimension,
    string Value,
    string Confidence,
    string Reason)
{
    public static ClassificationSuggestionDto Create(ClassificationSuggestion s) => new(
        s.Dimension.ToString(),
        FormatValue(s.Dimension, s.Value),
        s.Confidence.ToString(),
        s.Reason);

    private static string FormatValue(SuggestionDimension dimension, object value) => dimension switch
    {
        SuggestionDimension.Category or SuggestionDimension.Counterparty => ((Guid)value).ToString(),
        SuggestionDimension.MovementType => ((MovementType)value).ToString(),
        SuggestionDimension.FinancialImpact => ((FinancialImpact)value).ToString(),
        _ => value.ToString() ?? string.Empty,
    };
}
