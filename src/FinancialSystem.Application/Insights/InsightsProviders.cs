namespace FinancialSystem.Application.Insights;

public static class InsightsProviders
{
    /// <summary>
    /// Patch 0066 (PATCH-017): valor explícito para "no generar insights con ningún
    /// proveedor" -- antes de este patch, un valor vacío o desconocido caía en la
    /// misma rama de "Unknown InsightsWorker:Provider" que un typo real, sin una
    /// forma de expresar intencionalmente "deshabilitado" en la configuración. Ver
    /// InsightsProviderSelector.
    /// </summary>
    public const string None = "None";

    public const string OpenAI = "OpenAI";
    public const string Ollama = "Ollama";
    public const string Both = "Both";
}
