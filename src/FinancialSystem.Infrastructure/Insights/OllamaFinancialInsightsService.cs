using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinancialSystem.Application.Insights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Insights;

internal sealed class OllamaFinancialInsightsService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaFinancialInsightsService> logger) : IFinancialInsightsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string SystemPrompt =
        """
        Eres un analista financiero personal. Analizas listas de transacciones en español.
        Identifica patrones de gasto, categorías probables, y anomalías (montos inusuales, duplicados, picos).
        Responde en español, de forma clara y concisa (máximo 3 párrafos cortos).
        Si no hay datos suficientes, indícalo brevemente.
        """;

    public Task<TransactionInsightResult> GetInsightsAsync(
        IReadOnlyList<TransactionSummary> transactions,
        CancellationToken cancellationToken = default) =>
        GetInsightsAsync(new TransactionInsightRequest(transactions), cancellationToken);

    public async Task<TransactionInsightResult> GetInsightsAsync(
        TransactionInsightRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Transactions.Count == 0)
        {
            return new TransactionInsightResult(
                Success: false,
                Summary: string.Empty,
                Error: "No hay transacciones para analizar.");
        }

        var ollama = options.Value;
        var userContent = BuildUserPrompt(request);

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
                return new TransactionInsightResult(
                    Success: false,
                    Summary: string.Empty,
                    RawResponse: body,
                    Error: $"Ollama HTTP {(int)response.StatusCode}");
            }

            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions);
            var content = chatResponse?.Message?.Content ?? chatResponse?.Response;

            if (!string.IsNullOrWhiteSpace(chatResponse?.Error))
            {
                logger.LogWarning("Ollama error: {Error}", chatResponse.Error);
                return new TransactionInsightResult(
                    Success: false,
                    Summary: string.Empty,
                    RawResponse: body,
                    Error: chatResponse.Error);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return new TransactionInsightResult(
                    Success: false,
                    Summary: string.Empty,
                    RawResponse: body,
                    Error: "Respuesta vacía de Ollama.");
            }

            return new TransactionInsightResult(
                Success: true,
                Summary: content.Trim(),
                RawResponse: body);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama request timed out after {Timeout}s", ollama.TimeoutSeconds);
            return new TransactionInsightResult(
                Success: false,
                Summary: string.Empty,
                Error: "Tiempo de espera agotado al consultar Ollama.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo conectar con Ollama en {BaseUrl}", ollama.BaseUrl);
            return new TransactionInsightResult(
                Success: false,
                Summary: string.Empty,
                Error: "No se pudo conectar con Ollama. ¿Está ejecutándose localmente?");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado al consultar Ollama");
            return new TransactionInsightResult(
                Success: false,
                Summary: string.Empty,
                Error: ex.Message);
        }
    }

    private static string BuildUserPrompt(TransactionInsightRequest request)
    {
        var payload = request.Transactions.Select(t => new
        {
            fecha = t.Date.ToString("yyyy-MM-dd"),
            descripcion = t.Description,
            monto = t.Amount,
            moneda = t.Currency
        });

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var builder = new StringBuilder();
        builder.AppendLine("Analiza estas transacciones:");
        builder.AppendLine(json);

        if (!string.IsNullOrWhiteSpace(request.Focus))
        {
            builder.AppendLine();
            builder.Append("Enfoque adicional: ").Append(request.Focus);
        }

        builder.AppendLine();
        builder.Append(
            "Incluye: 1) resumen de patrones de gasto, 2) categorías o rubros más probables, 3) posibles anomalías.");

        return builder.ToString();
    }
}
