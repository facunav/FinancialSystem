using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Accounts;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Herramientas de configuración/catálogo del sistema -- qué cuentas, categorías y
/// contrapartes existen realmente, y qué defaults tiene configurada cada
/// contraparte. Sin IA, sin memoria, sin reglas nuevas: todo sale de lo que ya está
/// persistido, nunca hardcodeado.
///
/// PRINCIPIO: se reutiliza lo que ya existe, no se crea un servicio nuevo por
/// entidad. FinancialAccount ya tiene un servicio dedicado
/// (IFinancialAccountQueryService, el mismo que usa hoy /api/accounts) -- se
/// reutiliza tal cual. Category y Counterparty NO tienen un servicio equivalente:
/// sus propios endpoints (CategoryEndpoints/CounterpartyEndpoints) consultan
/// IApplicationDbContext directo, sin capa intermedia -- este archivo sigue
/// exactamente el mismo patrón para esas dos entidades (mismo Include, mismos
/// filtros de búsqueda) en vez de inventar un servicio que la propia Api tampoco
/// tiene.
///
/// Category no tiene campos "Tipo" de negocio ni "Impacto financiero" en el
/// dominio (ver Category.cs) -- esas son dos de las 4 dimensiones de
/// ClassifiedMovement, independientes entre sí y de Category (ver ADR-001).
/// ListCategories no inventa esos valores: expone IsSystem (Sistema/Usuario), la
/// única distinción de "tipo" que Category realmente tiene, y omite lo que no
/// existe en vez de fabricarlo.
/// </summary>
[McpServerToolType]
public sealed class ConfigurationTools
{
    private readonly IFinancialAccountQueryService _accountQueryService;
    private readonly IApplicationDbContext _db;

    public ConfigurationTools(IFinancialAccountQueryService accountQueryService, IApplicationDbContext db)
    {
        _accountQueryService = accountQueryService;
        _db = db;
    }

    // ── ListFinancialAccounts ─────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Lista todas las cuentas financieras configuradas (activas e inactivas) -- reutiliza " +
        "IFinancialAccountQueryService, el mismo servicio que ya usa GET /api/accounts. Usar " +
        "para saber qué cuentas existen antes de filtrar otras tools por financialAccountId.")]
    public async Task<string> ListFinancialAccounts(CancellationToken ct = default)
    {
        var accounts = await _accountQueryService.GetAllAsync(includeDeactivated: true, search: null, ct: ct);

        if (accounts.Count == 0)
            return "No hay cuentas financieras configuradas.";

        var sb = new StringBuilder();
        sb.AppendLine($"{accounts.Count} cuenta(s) financiera(s):");
        sb.AppendLine();
        foreach (var a in accounts)
        {
            sb.AppendLine($"- Id: {a.Id}");
            sb.AppendLine($"  Nombre: {a.Name}");
            sb.AppendLine($"  Tipo: {a.Type}");
            sb.AppendLine($"  Moneda: {a.Currency}");
            sb.AppendLine($"  Estado: {(a.IsDeactivated ? "inactiva" : "activa")}");
        }

        return sb.ToString();
    }

    // ── ListCategories ─────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Lista todas las categorías configuradas (activas e inactivas), sin joins -- mismos " +
        "campos que expone GET /api/categories. Category no tiene 'Tipo' de negocio ni " +
        "'Impacto financiero' en el dominio (esas son dimensiones de ClassifiedMovement, no de " +
        "Category -- ver ADR-001); se expone IsSystem (Sistema/Usuario) en su lugar, la única " +
        "distinción de tipo que Category realmente tiene.")]
    public async Task<string> ListCategories(CancellationToken ct = default)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.DisplayName)
            .Select(c => new { c.Id, c.Name, c.DisplayName, c.IsSystem, c.IsDeactivated })
            .ToListAsync(ct);

        if (categories.Count == 0)
            return "No hay categorías configuradas.";

        var sb = new StringBuilder();
        sb.AppendLine($"{categories.Count} categoría(s):");
        sb.AppendLine();
        foreach (var c in categories)
        {
            sb.AppendLine($"- Id: {c.Id}");
            sb.AppendLine($"  Nombre: {c.DisplayName} ({c.Name})");
            sb.AppendLine($"  Tipo: {(c.IsSystem ? "Sistema" : "Usuario")}");
            sb.AppendLine($"  Estado: {(c.IsDeactivated ? "inactiva" : "activa")}");
        }

        return sb.ToString();
    }

    // ── ListCounterparties ────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Lista todas las contrapartes configuradas (activas e inactivas), sin joins. Para ver " +
        "los defaults configurados de una contraparte puntual usar GetCounterparty.")]
    public async Task<string> ListCounterparties(CancellationToken ct = default)
    {
        var counterparties = await _db.Counterparties
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Type, c.IsDeactivated })
            .ToListAsync(ct);

        if (counterparties.Count == 0)
            return "No hay contrapartes configuradas.";

        var sb = new StringBuilder();
        sb.AppendLine($"{counterparties.Count} contraparte(s):");
        sb.AppendLine();
        foreach (var c in counterparties)
        {
            sb.AppendLine($"- Id: {c.Id}");
            sb.AppendLine($"  Nombre: {c.Name}");
            sb.AppendLine($"  Tipo: {c.Type}");
            sb.AppendLine($"  Estado: {(c.IsDeactivated ? "inactiva" : "activa")}");
        }

        return sb.ToString();
    }

    // ── GetCounterparty ───────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Devuelve el detalle completo de una contraparte, incluyendo sus defaults " +
        "configurados (DefaultCategoryId/DefaultMovementType/DefaultFinancialImpact) con el " +
        "nombre de la categoría ya resuelto -- mismo patrón (Include DefaultCategory) que ya " +
        "usa GET /api/counterparties/{id}.")]
    public async Task<string> GetCounterparty(
        [Description("Id de la contraparte (Counterparty.Id).")]
        Guid id,
        CancellationToken ct = default)
    {
        var c = await _db.Counterparties
            .AsNoTracking()
            .Include(x => x.DefaultCategory)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (c is null)
            return $"No se encontró ninguna contraparte con Id {id}.";

        var defaultCategoryText = c.DefaultCategoryId is { } categoryId
            ? $"{categoryId} ({c.DefaultCategory?.DisplayName ?? "(no resuelve)"})"
            : "-";

        var sb = new StringBuilder();
        sb.AppendLine($"Id: {c.Id}");
        sb.AppendLine($"Nombre: {c.Name}");
        sb.AppendLine($"Tipo: {c.Type}");
        sb.AppendLine($"Estado: {(c.IsDeactivated ? "inactiva" : "activa")}");
        sb.AppendLine($"Notas: {c.Notes ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("Defaults configurados:");
        sb.AppendLine($"- DefaultCategoryId: {defaultCategoryText}");
        sb.AppendLine($"- DefaultMovementType: {c.DefaultMovementType?.ToString() ?? "-"}");
        sb.AppendLine($"- DefaultFinancialImpact: {c.DefaultFinancialImpact?.ToString() ?? "-"}");
        sb.AppendLine();
        sb.AppendLine($"Creada (UTC): {c.CreatedAt:O}");
        sb.AppendLine($"Actualizada (UTC): {c.UpdatedAt:O}");

        return sb.ToString();
    }

    // ── SearchCounterparties ──────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Busca contrapartes por nombre (contiene, sin distinguir mayúsculas) -- mismo filtro " +
        "que ya usa GET /api/counterparties?search=. Sin ranking, sin fuzzy search.")]
    public async Task<string> SearchCounterparties(
        [Description("Texto a buscar en el nombre de la contraparte.")]
        string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Error: text es obligatorio.";

        var counterparties = await _db.Counterparties
            .AsNoTracking()
            .Where(c => c.Name.ToLower().Contains(text.ToLower()))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Type, c.IsDeactivated })
            .ToListAsync(ct);

        if (counterparties.Count == 0)
            return $"No se encontraron contrapartes que contengan '{text}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"{counterparties.Count} contraparte(s) que contienen '{text}':");
        sb.AppendLine();
        foreach (var c in counterparties)
        {
            sb.AppendLine($"- Id: {c.Id}");
            sb.AppendLine($"  Nombre: {c.Name}");
            sb.AppendLine($"  Tipo: {c.Type}");
            sb.AppendLine($"  Estado: {(c.IsDeactivated ? "inactiva" : "activa")}");
        }

        return sb.ToString();
    }
}
