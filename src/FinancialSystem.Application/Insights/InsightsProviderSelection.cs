namespace FinancialSystem.Application.Insights;

/// <summary>
/// Resultado de interpretar InsightsWorkerOptions.Provider -- ver
/// InsightsProviderSelector.Resolve (Patch 0066, PATCH-017).
/// </summary>
public sealed record InsightsProviderSelection(bool RunOllama, bool RunOpenAi, bool IsDisabled, bool IsUnrecognized)
{
    /// <summary>Provider="None": ningún proveedor debe ejecutarse, de forma intencional.</summary>
    public static readonly InsightsProviderSelection Disabled = new(
        RunOllama: false, RunOpenAi: false, IsDisabled: true, IsUnrecognized: false);

    /// <summary>Provider vacío o con un valor que no coincide con ninguno reconocido.</summary>
    public static readonly InsightsProviderSelection Unrecognized = new(
        RunOllama: false, RunOpenAi: false, IsDisabled: false, IsUnrecognized: true);
}
