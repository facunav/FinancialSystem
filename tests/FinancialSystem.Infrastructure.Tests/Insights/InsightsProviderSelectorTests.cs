using FinancialSystem.Application.Insights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests.Insights;

/// <summary>
/// Cubre InsightsProviderSelector (Patch 0066, PATCH-017) -- la única decisión de qué
/// proveedor de insights se ejecuta, extraída de TransactionInsightsWorker para poder
/// probarla de forma aislada sin depender de BackgroundService/PeriodicTimer (que
/// vive en hosts/FinancialSystem.Worker, sin un proyecto de tests propio).
/// </summary>
public class InsightsProviderSelectorTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData(" None ")]
    public void Resolve_WithNone_ReturnsDisabled(string provider)
    {
        var result = InsightsProviderSelector.Resolve(provider);

        Assert.True(result.IsDisabled);
        Assert.False(result.RunOllama);
        Assert.False(result.RunOpenAi);
        Assert.False(result.IsUnrecognized);
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("ollama")]
    [InlineData(" Ollama ")]
    public void Resolve_WithOllama_RunsOnlyOllama(string provider)
    {
        var result = InsightsProviderSelector.Resolve(provider);

        Assert.True(result.RunOllama);
        Assert.False(result.RunOpenAi);
        Assert.False(result.IsDisabled);
        Assert.False(result.IsUnrecognized);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("openai")]
    public void Resolve_WithOpenAI_RunsOnlyOpenAi(string provider)
    {
        var result = InsightsProviderSelector.Resolve(provider);

        Assert.True(result.RunOpenAi);
        Assert.False(result.RunOllama);
        Assert.False(result.IsDisabled);
        Assert.False(result.IsUnrecognized);
    }

    [Fact]
    public void Resolve_WithBoth_RunsBothProviders()
    {
        var result = InsightsProviderSelector.Resolve("Both");

        Assert.True(result.RunOllama);
        Assert.True(result.RunOpenAi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("bogus")]
    public void Resolve_WithUnrecognizedValue_ReturnsUnrecognized(string? provider)
    {
        var result = InsightsProviderSelector.Resolve(provider);

        Assert.True(result.IsUnrecognized);
        Assert.False(result.RunOllama);
        Assert.False(result.RunOpenAi);
        Assert.False(result.IsDisabled);
    }

    [Fact]
    public void Resolve_NeverSelectsOpenAi_UnlessTheProviderStringIsExactlyOpenAiOrBoth()
    {
        // Sección 5 del patch: Resolve no recibe ni consulta OpenAIOptions/ApiKey en
        // ningún momento -- la única entrada es el string de Provider. Con cualquier
        // otro valor, RunOpenAi siempre es false.
        foreach (var provider in new[] { "Ollama", "None", "", "unexpected", "  " })
        {
            var result = InsightsProviderSelector.Resolve(provider);
            Assert.False(result.RunOpenAi);
        }
    }

    [Fact]
    public void DefaultInsightsWorkerOptions_ProviderIsOllama_NotOpenAi()
    {
        // Ausencia de regresión + sección 1 del patch: el default nunca es OpenAI.
        var options = new InsightsWorkerOptions();

        Assert.Equal(InsightsProviders.Ollama, options.Provider);
        Assert.True(InsightsProviderSelector.Resolve(options.Provider).RunOllama);
    }

    [Fact]
    public void InsightsWorkerOptions_BindsProviderExplicitlyFromConfiguration()
    {
        // Sección 2 del patch: selección explícita vía configuración.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{InsightsWorkerOptions.SectionName}:Provider"] = InsightsProviders.OpenAI
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<InsightsWorkerOptions>(configuration.GetSection(InsightsWorkerOptions.SectionName));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<InsightsWorkerOptions>>();

        Assert.Equal(InsightsProviders.OpenAI, options.Value.Provider);
    }

    [Fact]
    public void ConfiguringOnlyOpenAiApiKey_DoesNotImplicitlySelectOpenAi()
    {
        // Sección 5 del patch: configurar OpenAI:ApiKey por sí solo nunca cambia el
        // proveedor seleccionado -- InsightsWorker:Provider y OpenAI:ApiKey son
        // secciones de configuración independientes, sin ninguna lógica que las cruce.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "sk-test-key-configurada-sin-elegir-proveedor"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<InsightsWorkerOptions>(configuration.GetSection(InsightsWorkerOptions.SectionName));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<InsightsWorkerOptions>>();
        var selection = InsightsProviderSelector.Resolve(options.Value.Provider);

        Assert.Equal(InsightsProviders.Ollama, options.Value.Provider);
        Assert.False(selection.RunOpenAi);
        Assert.True(selection.RunOllama);
    }
}
