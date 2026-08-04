namespace FinancialSystem.Application.Insights;

public sealed class InsightsWorkerOptions
{
    public const string SectionName = "InsightsWorker";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    public int TransactionBatchSize { get; set; } = 50;

    /// <summary>
    /// Valores válidos: "None" (no genera insights con ningún proveedor), "Ollama"
    /// (local, ningún dato sale del sistema), "OpenAI" (envía datos de transacciones a
    /// la API de OpenAI -- requiere elegirlo acá explícitamente, nunca se activa solo
    /// porque OpenAI:ApiKey esté configurada), o "Both".
    ///
    /// Patch 0066 (PATCH-017): el default es "Ollama", nunca "OpenAI" -- usar OpenAI
    /// es siempre una decisión explícita del operador, hecha acá, nunca implícita por
    /// la presencia de una API key. Ver InsightsProviderSelector.Resolve para la
    /// interpretación de este valor y docs/Architecture/ProveedorDeInsights.md para
    /// qué información sale del sistema en cada caso.
    /// </summary>
    public string Provider { get; set; } = InsightsProviders.Ollama;
}
