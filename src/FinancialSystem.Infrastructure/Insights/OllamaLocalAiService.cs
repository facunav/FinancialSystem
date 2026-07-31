using System.Net.Http.Json;
using System.Text.Json;
using FinancialSystem.Application.Insights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Insights;

/// <summary>
/// Implementación de ILocalAiService sobre Ollama. Reutiliza OllamaOptions (misma
/// sección "Ollama" de appsettings que ya usa OllamaFinancialInsightsService, sin
/// agregar configuración nueva) y los mismos modelos de request/response de la API
/// de Ollama (OllamaChatRequest/OllamaChatMessage/OllamaChatResponse, definidos en
/// OllamaApiModels.cs) -- mismo endpoint (api/chat), mismo manejo de errores/timeout,
/// sin duplicar esas clases. La única diferencia real con
/// OllamaFinancialInsightsService es el prompt: acá no hay dominio financiero, el
/// contexto y la pregunta los arma quien llama (ver AskProjectKnowledge en
/// ProjectTools.cs), no esta clase.
/// </summary>
internal sealed class OllamaLocalAiService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLocalAiService> logger) : ILocalAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string SystemPrompt =
        """
        Respondés preguntas sobre un proyecto de software usando únicamente el contexto que
        se te da a continuación. Si el contexto no alcanza para responder, decilo
        explícitamente en vez de inventar información. Responde en español, de forma clara
        y concisa.
        """;

    public async Task<LocalAiResult> AskAsync(
        string context, string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return new LocalAiResult(false, string.Empty, "question es obligatorio.");

        var ollama = options.Value;
        var userContent = $"Contexto del proyecto:\n{context}\n\nPregunta: {question}";

        var chatRequest = new OllamaChatRequest
        {
            Model = ollama.Model,
            Stream = false,
            Messages =
            [
                new OllamaChatMessage { Role = "system", Content = SystemPrompt },
                new OllamaChatMessage { Role = "user", Content = userContent }
            ]
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/chat",
                chatRequest,
                JsonOptions,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Ollama returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body);
                return new LocalAiResult(false, string.Empty, $"Ollama HTTP {(int)response.StatusCode}");
            }

            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions);
            var content = chatResponse?.Message?.Content ?? chatResponse?.Response;

            if (!string.IsNullOrWhiteSpace(chatResponse?.Error))
            {
                logger.LogWarning("Ollama error: {Error}", chatResponse.Error);
                return new LocalAiResult(false, string.Empty, chatResponse.Error);
            }

            if (string.IsNullOrWhiteSpace(content))
                return new LocalAiResult(false, string.Empty, "Respuesta vacía de Ollama.");

            return new LocalAiResult(true, content.Trim());
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama request timed out after {Timeout}s", ollama.TimeoutSeconds);
            return new LocalAiResult(false, string.Empty, "Tiempo de espera agotado al consultar Ollama.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo conectar con Ollama en {BaseUrl}", ollama.BaseUrl);
            return new LocalAiResult(false, string.Empty, "No se pudo conectar con Ollama. ¿Está ejecutándose localmente?");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado al consultar Ollama");
            return new LocalAiResult(false, string.Empty, ex.Message);
        }
    }
}
