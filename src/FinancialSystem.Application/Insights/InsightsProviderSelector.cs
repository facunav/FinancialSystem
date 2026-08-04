namespace FinancialSystem.Application.Insights;

/// <summary>
/// Interpreta InsightsWorkerOptions.Provider (Patch 0066, PATCH-017) -- único punto de
/// esta decisión, extraído de TransactionInsightsWorker para que la selección de
/// proveedor sea un mecanismo simple, explícito y testeable de forma aislada, sin
/// depender de BackgroundService/PeriodicTimer. No cambia el pipeline de generación de
/// insights en sí (OpenAIFinancialInsightsService/OllamaFinancialInsightsService, los
/// prompts, el formato de respuesta) -- solo decide qué proveedor(es) invocar.
///
/// Garantía central de este patch (secciones 1 y 5): la ÚNICA entrada de Resolve es el
/// string de configuración InsightsWorker:Provider. RunOpenAi solo puede ser true si
/// ese valor es exactamente "OpenAI" o "Both" -- no existe ninguna ruta en la que la
/// presencia de OpenAI:ApiKey, o cualquier otra configuración, seleccione OpenAI de
/// forma implícita. Ver docs/Architecture/ProveedorDeInsights.md.
/// </summary>
public static class InsightsProviderSelector
{
    public static InsightsProviderSelection Resolve(string? provider)
    {
        var trimmed = provider?.Trim() ?? string.Empty;

        if (trimmed.Equals(InsightsProviders.None, StringComparison.OrdinalIgnoreCase))
            return InsightsProviderSelection.Disabled;

        var runOpenAi = trimmed.Equals(InsightsProviders.OpenAI, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(InsightsProviders.Both, StringComparison.OrdinalIgnoreCase);
        var runOllama = trimmed.Equals(InsightsProviders.Ollama, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(InsightsProviders.Both, StringComparison.OrdinalIgnoreCase);

        if (!runOpenAi && !runOllama)
            return InsightsProviderSelection.Unrecognized;

        return new InsightsProviderSelection(runOllama, runOpenAi, IsDisabled: false, IsUnrecognized: false);
    }
}
