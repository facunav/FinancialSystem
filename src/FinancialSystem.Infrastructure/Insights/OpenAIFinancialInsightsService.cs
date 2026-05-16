using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinancialSystem.Application.Insights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialSystem.Infrastructure.Insights;

internal sealed class OpenAIFinancialInsightsService(
    HttpClient httpClient,
    IOptions<OpenAIOptions> options,
    ILogger<OpenAIFinancialInsightsService> logger) : IOpenAIFinancialInsightsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt =
        """
        Eres un analista financiero personal. Analizas transacciones bancarias en español (Argentina).
        Responde ÚNICAMENTE con un objeto JSON válido (sin markdown) con estas claves:
        - "gastos_hormiga": array de strings (gastos pequeños y frecuentes detectados)
        - "categorias_dominantes": array de strings (rubros donde más se gasta)
        - "suscripciones": array de strings (pagos recurrentes tipo Netflix, gym, etc.)
        - "habitos_repetitivos": array de strings (patrones repetidos en comercio o monto)
        - "resumen": string breve con visión general
        Si no hay evidencia para una sección, usa array vacío []. Sé concreto y basado en los datos.
        """;

    public Task<OpenAIFinancialInsightResult> GetStructuredInsightsAsync(
        IReadOnlyList<TransactionSummary> transactions,
        CancellationToken cancellationToken = default) =>
        GetStructuredInsightsAsync(new TransactionInsightRequest(transactions), cancellationToken);

    public async Task<OpenAIFinancialInsightResult> GetStructuredInsightsAsync(
        TransactionInsightRequest request,
        CancellationToken cancellationToken = default)
    {
        var openAi = options.Value;
        if (string.IsNullOrWhiteSpace(openAi.ApiKey))
        {
            return new OpenAIFinancialInsightResult(
                Success: false,
                Error: "OpenAI API key no configurada. Use OpenAI:ApiKey, user secrets o OPENAI_API_KEY.");
        }

        if (request.Transactions.Count == 0)
        {
            return new OpenAIFinancialInsightResult(
                Success: false,
                Error: "No hay transacciones para analizar.");
        }

        var chatRequest = new OpenAiChatCompletionRequest
        {
            Model = openAi.Model,
            Messages =
            [
                new OpenAiChatMessage { Role = "system", Content = SystemPrompt },
                new OpenAiChatMessage { Role = "user", Content = BuildUserPrompt(request) }
            ]
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = JsonContent.Create(chatRequest, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAi.ApiKey);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI returned {StatusCode}", (int)response.StatusCode);
                return new OpenAIFinancialInsightResult(
                    Success: false,
                    RawResponse: body,
                    Error: $"OpenAI HTTP {(int)response.StatusCode}");
            }

            var completion = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(completion?.Error?.Message))
            {
                return new OpenAIFinancialInsightResult(
                    Success: false,
                    RawResponse: body,
                    Error: completion.Error.Message);
            }

            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return new OpenAIFinancialInsightResult(
                    Success: false,
                    RawResponse: body,
                    Error: "Respuesta vacía de OpenAI.");
            }

            var report = ParseReport(content);
            return new OpenAIFinancialInsightResult(Success: true, Report: report, RawResponse: body);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "OpenAI request timed out");
            return new OpenAIFinancialInsightResult(
                Success: false,
                Error: "Tiempo de espera agotado al consultar OpenAI.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo conectar con OpenAI");
            return new OpenAIFinancialInsightResult(
                Success: false,
                Error: "No se pudo conectar con OpenAI.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "No se pudo interpretar la respuesta JSON de OpenAI");
            return new OpenAIFinancialInsightResult(
                Success: false,
                Error: "Respuesta JSON inválida de OpenAI.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado al consultar OpenAI");
            return new OpenAIFinancialInsightResult(Success: false, Error: ex.Message);
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

        var builder = new StringBuilder();
        builder.AppendLine("Analiza estas transacciones y devuelve el JSON solicitado:");
        builder.AppendLine(JsonSerializer.Serialize(payload, JsonOptions));

        if (!string.IsNullOrWhiteSpace(request.Focus))
        {
            builder.AppendLine().Append("Enfoque: ").Append(request.Focus);
        }

        return builder.ToString();
    }

    private static FinancialInsightsReport ParseReport(string content)
    {
        var json = ExtractJsonObject(content);
        var payload = JsonSerializer.Deserialize<OpenAiInsightsPayload>(json, JsonOptions)
            ?? throw new JsonException("Payload nulo");

        return new FinancialInsightsReport(
            Summary: payload.Resumen ?? string.Empty,
            GastosHormiga: ParseJsonElementAsStrings(payload.GastosHormiga),
            CategoriasDominantes: ParseJsonElementAsStrings(payload.CategoriasDominantes),
            Suscripciones: ParseJsonElementAsStrings(payload.Suscripciones),
            HabitosRepetitivos: ParseJsonElementAsStrings(payload.HabitosRepetitivos));
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed[firstBrace..(lastBrace + 1)];
            }
        }

        return trimmed;
    }

    private static IReadOnlyList<string> ParseJsonElementAsStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }
                else if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    list.Add(item.GetRawText());
                }
            }

            return list;
        }

        return [element.GetRawText()];
    }
}
