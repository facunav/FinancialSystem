using System.Reflection;
using FinancialSystem.McpServer;
using ModelContextProtocol.Server;
using Xunit;

namespace FinancialSystem.McpServer.Tests;

/// <summary>
/// Verifica que ToolRegistry (../../hosts/FinancialSystem.McpServer/ToolRegistry.cs --
/// registro manual, ver su propio doc-comment para por qué es manual a propósito: sin
/// reflexión, sin escaneo de ensamblados, sin generación automática) queda
/// sincronizado con las tools MCP realmente implementadas (Patch 0077, PATCH-024).
///
/// No existía ningún mecanismo (test o de otro tipo) para detectar automáticamente
/// diferencias entre el registro y la implementación real antes de este patch
/// (confirmado por búsqueda en todo el repo) -- este archivo lo agrega. No cambia en
/// nada cómo se mantiene ToolRegistry.Tools: sigue siendo una lista editada a mano;
/// esto es solo una red de seguridad que falla el build si alguien agrega, saca o
/// renombra una tool y se olvida de actualizar el registro.
///
/// El inventario "real" se obtiene por reflexión sobre el propio ensamblado
/// FinancialSystem.McpServer: cualquier tipo con [McpServerToolType] y, dentro de él,
/// cualquier método público con [McpServerTool] -- exactamente el mismo criterio que
/// usa el SDK de ModelContextProtocol en tiempo de ejecución (Program.cs,
/// WithToolsFromAssembly()) para descubrir qué exponer.
/// </summary>
public class ToolRegistrySyncTests
{
    private static readonly Assembly McpServerAssembly = typeof(ToolRegistry).Assembly;

    [Fact]
    public void EveryImplementedTool_AppearsInTheRegistry()
    {
        var missing = ImplementedTools().Except(RegisteredTools()).ToList();

        Assert.True(missing.Count == 0,
            "Tools implementadas sin entrada en ToolRegistry: " +
            string.Join(", ", missing.Select(t => $"{t.ClassName}.{t.Name}")));
    }

    [Fact]
    public void TheRegistry_DoesNotContainNonExistentTools()
    {
        var phantom = RegisteredTools().Except(ImplementedTools()).ToList();

        Assert.True(phantom.Count == 0,
            "Entradas en ToolRegistry sin tool implementada detrás (renombrada o eliminada): " +
            string.Join(", ", phantom.Select(t => $"{t.ClassName}.{t.Name}")));
    }

    [Fact]
    public void TheRegistry_HasNoDuplicateEntries()
    {
        var duplicates = ToolRegistry.Tools
            .GroupBy(t => (t.ClassName, t.Name))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.ClassName}.{g.Key.Name}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Entradas duplicadas en ToolRegistry: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void TheRegistry_HasNoIncompleteEntries_AndItsLlmCatalogListsEveryTool()
    {
        // Consistencia del catálogo: ninguna entrada con campos vacíos, y el texto que
        // efectivamente recibiría un LLM (ToLlmCatalog -- lo que arman
        // AskProjectKnowledge/AskInvestigation como contexto) menciona a todas las
        // tools registradas, no solo que el registro "diga" tenerlas.
        foreach (var tool in ToolRegistry.Tools)
        {
            var label = $"{tool.ClassName}.{tool.Name}";
            Assert.False(string.IsNullOrWhiteSpace(tool.ClassName), $"{label}: ClassName vacío.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"{label}: Name vacío.");
            Assert.False(string.IsNullOrWhiteSpace(tool.ShortDescription), $"{label}: ShortDescription vacía.");
            Assert.False(string.IsNullOrWhiteSpace(tool.WhenToUse), $"{label}: WhenToUse vacío.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Parameters), $"{label}: Parameters vacío.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Returns), $"{label}: Returns vacío.");
        }

        var catalog = ToolRegistry.ToLlmCatalog();
        foreach (var tool in ToolRegistry.Tools)
            Assert.Contains($"Name: {tool.Name}", catalog);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HashSet<(string ClassName, string Name)> ImplementedTools() =>
        McpServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
                .Select(m => (ClassName: t.Name, Name: m.Name)))
            .ToHashSet();

    private static HashSet<(string ClassName, string Name)> RegisteredTools() =>
        ToolRegistry.Tools.Select(t => (t.ClassName, t.Name)).ToHashSet();
}
