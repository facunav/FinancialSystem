namespace FinancialSystem.Application.Insights;

/// <summary>
/// Abstracción mínima para consultar un modelo de IA local (Ollama) con contexto +
/// pregunta en lenguaje natural, sin forma de dominio propia -- a diferencia de
/// IFinancialInsightsService (que recibe TransactionSummary y devuelve un resultado
/// con forma de insight financiero), esta interfaz no asume nada sobre el contenido
/// del contexto ni de la pregunta. Es infraestructura para consultar el modelo, no
/// un agente: no decide qué contexto cargar, no encadena llamadas, no elige tools.
/// </summary>
public interface ILocalAiService
{
    Task<LocalAiResult> AskAsync(
        string context, string question, CancellationToken cancellationToken = default);
}
