using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Insights;
using FinancialSystem.McpServer;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de conocimiento del proyecto — Fase 1.5 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md, más
/// AskProjectKnowledge (Fase 4 de esa misma ADR / Fase 4 de ADR-007 — integración
/// inicial con Ollama).
///
/// PRINCIPIO: no hay lógica de negocio ni de dominio financiero acá — son lectura
/// directa de archivos .md ya existentes en docs/, sin interpretarlos. No se
/// introduce ningún servicio nuevo en Application/Infrastructure para esto (a
/// diferencia de las tools de movimientos): leer un archivo de texto es
/// infraestructura pura, sin ninguna regla de negocio que justifique un contrato
/// nuevo — mismo criterio que ya usa SystemTools.Health con AppDbContext.Database
/// directo, aplicado acá a System.IO directo.
///
/// docs/ se copia al directorio de salida del build (ver
/// FinancialSystem.McpServer.csproj) preservando su estructura de carpetas — mismo
/// mecanismo que ya usa este proyecto para appsettings.json/
/// appsettings.Development.json, no uno nuevo.
///
/// Ninguna tool de este archivo parsea Markdown ni usa IA para leer documentación:
/// ReadArchitectureDocument y GetRoadmap devuelven el archivo tal cual está guardado
/// (texto crudo); SearchDocumentation hace una búsqueda de texto literal línea por
/// línea (case insensitive), sin ranking ni interpretación — el mismo criterio que ya
/// aplican ExplainMovement/FindMisclassifiedMovements: solo exponer datos ya
/// existentes. La única excepción es AskProjectKnowledge, que sí usa IA (Ollama, vía
/// ILocalAiService) -- pero solo para responder en lenguaje natural sobre contexto ya
/// leído por esta misma clase (GetRoadmap), nunca para decidir qué leer ni para
/// encadenar otras tools.
/// </summary>
[McpServerToolType]
public sealed class ProjectTools
{
    // Tope de salida de SearchDocumentation -- no es un umbral de relevancia (no hay
    // ranking, no hay IA): solo evita volcar cientos de líneas en una sola respuesta.
    // Mismo espíritu que MaxDateRangeDays en MovementTools/AuditTools -- protege el
    // tamaño de la respuesta, no decide qué es "relevante".
    private const int MaxSearchMatches = 50;

    private static readonly string DocsRoot = Path.Combine(AppContext.BaseDirectory, "docs");
    private static readonly string ArchitectureRoot = Path.Combine(DocsRoot, "Architecture");
    private static readonly string RoadmapPath = Path.Combine(DocsRoot, "RoadMaps", "FinancialMcp-vNext.md");

    private readonly ILocalAiService _localAiService;

    public ProjectTools(ILocalAiService localAiService)
    {
        _localAiService = localAiService;
    }

    // ── ListArchitectureDocuments ────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Lista los documentos de docs/Architecture/ (nombre de archivo relativo, uno por " +
        "línea). Usar antes de ReadArchitectureDocument para saber qué nombre pedir.")]
    public string ListArchitectureDocuments()
    {
        if (!Directory.Exists(ArchitectureRoot))
            return "Error: no se encontró docs/Architecture/ en el directorio de salida del build.";

        var files = Directory.EnumerateFiles(ArchitectureRoot, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(ArchitectureRoot, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            return "No hay documentos .md en docs/Architecture/.";

        var sb = new StringBuilder();
        sb.AppendLine($"{files.Count} documento(s) en docs/Architecture/:");
        foreach (var f in files)
            sb.AppendLine($"- {f}");

        return sb.ToString();
    }

    // ── ReadArchitectureDocument ─────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Devuelve el contenido crudo (sin interpretar, sin renderizar Markdown) de un " +
        "documento de docs/Architecture/, identificado por el nombre relativo que devuelve " +
        "ListArchitectureDocuments.")]
    public async Task<string> ReadArchitectureDocument(
        [Description(
            "Nombre de archivo relativo a docs/Architecture/, ej. 'Architecture.md'. " +
            "Ver ListArchitectureDocuments para los nombres disponibles.")]
        string fileName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Error: fileName es obligatorio.";

        if (!TryResolveWithinRoot(ArchitectureRoot, fileName, out var fullPath, out var error))
            return error!;

        if (!File.Exists(fullPath))
            return $"Error: no existe '{fileName}' en docs/Architecture/.";

        return await File.ReadAllTextAsync(fullPath, ct);
    }

    // ── SearchDocumentation ───────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Busca un texto literal (sin distinguir mayúsculas, sin IA, sin ranking) en todos " +
        "los documentos .md de docs/ (Architecture, Decisions, Epics, RoadMaps, UX, Archive, " +
        "patch). Devuelve, por archivo y número de línea, las líneas que contienen el texto " +
        "-- igual que un grep simple, nunca una interpretación del contenido.")]
    public async Task<string> SearchDocumentation(
        [Description("Texto a buscar (contiene, sin distinguir mayúsculas).")]
        string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query es obligatorio.";

        if (!Directory.Exists(DocsRoot))
            return "Error: no se encontró docs/ en el directorio de salida del build.";

        var matches = new List<string>();
        var truncated = false;

        foreach (var filePath in Directory.EnumerateFiles(DocsRoot, "*.md", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (matches.Count >= MaxSearchMatches)
            {
                truncated = true;
                break;
            }

            var relativePath = Path.GetRelativePath(DocsRoot, filePath).Replace('\\', '/');
            var lines = await File.ReadAllLinesAsync(filePath, ct);

            for (var i = 0; i < lines.Length; i++)
            {
                if (matches.Count >= MaxSearchMatches)
                {
                    truncated = true;
                    break;
                }

                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    matches.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
            }
        }

        if (matches.Count == 0)
            return $"Sin resultados para '{query}' en docs/.";

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{matches.Count} coincidencia(s) para '{query}' en docs/" +
            (truncated ? $" (tope de {MaxSearchMatches} alcanzado, hay más sin mostrar):" : ":"));
        foreach (var m in matches)
            sb.AppendLine($"- {m}");

        return sb.ToString();
    }

    // ── GetRoadmap ────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Devuelve el contenido crudo de docs/RoadMaps/FinancialMcp-vNext.md -- la fuente de " +
        "verdad del roadmap del proyecto, según su propio encabezado. Sin interpretar.")]
    public async Task<string> GetRoadmap(CancellationToken ct = default)
    {
        if (!File.Exists(RoadmapPath))
            return "Error: no se encontró docs/RoadMaps/FinancialMcp-vNext.md en el directorio de salida del build.";

        return await File.ReadAllTextAsync(RoadmapPath, ct);
    }

    // ── AskProjectKnowledge ───────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Responde una pregunta sobre el proyecto usando un modelo local de Ollama, con " +
        "docs/RoadMaps/FinancialMcp-vNext.md (la fuente de verdad del roadmap, misma " +
        "lectura que ya hace GetRoadmap) más el catálogo de tools MCP disponibles " +
        "(ToolRegistry.ToLlmCatalog) como contexto. No accede a la base de datos, no " +
        "escribe memoria, no modifica investigaciones, no llama a ninguna otra tool ni " +
        "decide qué tool usar: arma contexto + pregunta y devuelve la respuesta de Ollama " +
        "tal cual.")]
    public async Task<string> AskProjectKnowledge(
        [Description("Pregunta sobre el proyecto, en lenguaje natural.")]
        string question,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return "Error: question es obligatorio.";

        var roadmap = await GetRoadmap(ct);
        var context = ToolRegistry.ToLlmCatalog() + "\n" + roadmap;
        var result = await _localAiService.AskAsync(context, question, ct);

        return result.Success
            ? result.Answer
            : $"Error: {result.Error ?? "no se pudo obtener respuesta de Ollama."}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Evita que fileName escape la carpeta permitida (ej. "../../../algo" o una ruta
    // absoluta) -- Path.GetFullPath resuelve ".." y separadores antes de comparar, así
    // que no alcanza con revisar el string crudo. Nota: si fileName ya es una ruta
    // absoluta, Path.Combine descarta "root" y devuelve fileName tal cual -- por eso la
    // validación real ocurre acá, comparando el resultado final contra el root, no
    // confiando en Combine.
    private static bool TryResolveWithinRoot(
        string root, string relativePath, out string fullPath, out string? error)
    {
        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            error = "Error: fileName debe ser un nombre de archivo dentro de docs/Architecture/, sin '..'.";
            return false;
        }

        error = null;
        return true;
    }
}
