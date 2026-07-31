namespace FinancialSystem.Application.Insights;

/// <summary>Resultado de <see cref="ILocalAiService.AskAsync"/>: éxito con la respuesta, o motivo de fallo.</summary>
public sealed record LocalAiResult(bool Success, string Answer, string? Error = null);
