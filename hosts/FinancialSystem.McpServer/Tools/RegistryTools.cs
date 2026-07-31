using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Expone ToolRegistry (../ToolRegistry.cs) como una tool MCP. Sin dependencias: solo
/// formatea la metadata estática ya escrita a mano en el registro -- no ejecuta
/// ninguna tool, no decide cuál usar, no encadena llamadas. Preparación para que un
/// modelo pueda en el futuro conocer qué tools existen; el tool calling en sí queda
/// fuera de alcance de este archivo.
/// </summary>
[McpServerToolType]
public sealed class RegistryTools
{
    [McpServerTool]
    [Description(
        "Devuelve el registro completo de tools MCP disponibles (nombre, descripción corta, " +
        "cuándo usarla, parámetros y qué devuelve), agrupadas por clase. Reutiliza ToolRegistry, " +
        "un registro estático escrito a mano -- no escanea ensamblados ni ejecuta ninguna tool.")]
    public string ListAvailableTools()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{ToolRegistry.Tools.Count} tool(s) disponible(s):");
        sb.AppendLine();

        foreach (var group in ToolRegistry.Tools.GroupBy(t => t.ClassName))
        {
            sb.AppendLine($"{group.Key}:");
            foreach (var tool in group)
            {
                sb.AppendLine($"- {tool.Name}");
                sb.AppendLine($"    Descripción: {tool.ShortDescription}");
                sb.AppendLine($"    Cuándo usarla: {tool.WhenToUse}");
                sb.AppendLine($"    Parámetros: {tool.Parameters}");
                sb.AppendLine($"    Devuelve: {tool.Returns}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
