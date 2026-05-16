using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialSystem.Infrastructure.Insights;

internal sealed class OpenAiChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("response_format")]
    public OpenAiResponseFormat ResponseFormat { get; set; } = new();
}

internal sealed class OpenAiResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "json_object";
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal sealed class OpenAiChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public OpenAiError? Error { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; set; }
}

internal sealed class OpenAiError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class OpenAiInsightsPayload
{
    [JsonPropertyName("gastos_hormiga")]
    public JsonElement GastosHormiga { get; set; }

    [JsonPropertyName("categorias_dominantes")]
    public JsonElement CategoriasDominantes { get; set; }

    [JsonPropertyName("suscripciones")]
    public JsonElement Suscripciones { get; set; }

    [JsonPropertyName("habitos_repetitivos")]
    public JsonElement HabitosRepetitivos { get; set; }

    [JsonPropertyName("resumen")]
    public string? Resumen { get; set; }
}
