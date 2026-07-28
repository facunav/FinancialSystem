using System.ComponentModel;
using System.Reflection;
using System.Text;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de investigación básica del propio servidor MCP — Fase 1 de
/// docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md.
///
/// PRINCIPIO: igual que FinancialTools, este archivo no contiene lógica de negocio.
/// Ping y Version no dependen de nada del dominio. Health lee AppDbContext.Database
/// directamente (conectividad, proveedor, última migración) porque es información
/// de infraestructura, no una regla de negocio — no se introduce un contrato nuevo
/// en Application solo para esto (ver ADR-006: "la complejidad aparece únicamente
/// cuando exista una necesidad concreta").
///
/// Ninguna tool de este archivo escribe datos. WithToolsFromAssembly() (Program.cs)
/// ya descubre esta clase automáticamente por tener [McpServerToolType] — agregar
/// una tool nueva acá, o una clase de tools nueva en este mismo directorio, no
/// requiere tocar Program.cs.
/// </summary>
[McpServerToolType]
public sealed class SystemTools
{
    private readonly AppDbContext _db;

    public SystemTools(AppDbContext db)
    {
        _db = db;
    }

    // ── Ping ─────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Verifica que el servidor MCP está corriendo y responde. Usar para confirmar la " +
        "conexión desde un cliente MCP antes de invocar cualquier otra herramienta.")]
    public string Ping() => "pong";

    // ── Version ──────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Devuelve la versión de la aplicación, el protocolo MCP negociado con este cliente, " +
        "el commit de origen (si el build lo incluye) y la fecha de compilación del servidor. " +
        "Usar para confirmar qué versión del MCP está corriendo antes de reportar un bug.")]
    public string Version(McpServer server)
    {
        var assembly = typeof(SystemTools).Assembly;

        // El SDK de .NET agrega automáticamente "+<commit sha>" al informational version
        // cuando el build corre dentro de un repositorio git (por defecto desde .NET 8 SDK,
        // sin paquete de SourceLink) -- no es un mecanismo nuevo, es el comportamiento
        // estándar del SDK que ya se usa para compilar este proyecto.
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var parts = informationalVersion?.Split('+', 2);
        var appVersion = parts is { Length: > 0 } && !string.IsNullOrWhiteSpace(parts[0])
            ? parts[0]
            : assembly.GetName().Version?.ToString() ?? "desconocida";
        var commit = parts is { Length: 2 } ? parts[1] : null;

        // Fecha de última escritura del ensamblado compilado -- metadata de archivo ya
        // disponible, sin agregar ningún mecanismo de sellado de build nuevo.
        DateTime? buildDateUtc = null;
        if (!string.IsNullOrEmpty(assembly.Location))
        {
            try { buildDateUtc = File.GetLastWriteTimeUtc(assembly.Location); }
            catch { /* No disponible, ej. single-file publish sin ubicación de archivo. */ }
        }

        var sb = new StringBuilder();
        sb.AppendLine("FinancialSystem.McpServer");
        sb.AppendLine($"  Versión de la app:          {appVersion}");
        sb.AppendLine($"  Protocolo MCP negociado:    {server.NegotiatedProtocolVersion ?? "desconocido"}");
        sb.AppendLine($"  Commit:                     {commit ?? "no disponible"}");
        sb.AppendLine($"  Fecha de compilación (UTC): {(buildDateUtc is { } d ? d.ToString("O") : "no disponible")}");
        return sb.ToString();
    }

    // ── Health ───────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Devuelve el estado básico del servidor: si está conectado a la base de datos, qué " +
        "proveedor usa, qué migración de esquema tiene aplicada y la hora UTC actual. Usar " +
        "para diagnosticar si el MCP puede leer datos antes de investigar un problema puntual.")]
    public async Task<string> Health(CancellationToken ct = default)
    {
        bool canConnect;
        try
        {
            canConnect = await _db.Database.CanConnectAsync(ct);
        }
        catch
        {
            canConnect = false;
        }

        string? lastMigration = null;
        if (canConnect)
        {
            try
            {
                lastMigration = (await _db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
            }
            catch
            {
                // Conectó pero no pudo leer el historial de migraciones -- se informa igual,
                // sin que esto tumbe la tool completa.
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Estado general:                {(canConnect ? "OK" : "Degradado")}");
        sb.AppendLine($"  Conectado a la base de datos: {(canConnect ? "sí" : "no")}");
        sb.AppendLine($"  Proveedor:                    {_db.Database.ProviderName ?? "desconocido"}");
        sb.AppendLine($"  Última migración aplicada:    {lastMigration ?? "desconocida"}");
        sb.AppendLine($"  Hora UTC actual:               {DateTime.UtcNow:O}");
        return sb.ToString();
    }
}
