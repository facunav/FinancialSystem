using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Movements;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Auditoría integral del sistema -- un resumen único combinando lo que ya calculan
/// las tools/servicios de auditoría existentes, para el mes en curso (mismo período
/// por defecto que ya usan FindSuspiciousMovements/FindMisclassifiedMovements/
/// SearchMovements cuando no se les pasa from/to).
///
/// PRINCIPIO: cero reglas nuevas. AuditDatabase() no detecta nada por sí mismo --
/// llama a AuditTools.FindSuspiciousMovements y AuditTools.FindMisclassifiedMovements
/// (las tools ya existentes, inyectando la clase AuditTools tal cual -- el SDK
/// ModelContextProtocol registra cada [McpServerToolType] en el contenedor de DI, así
/// que resolverla como dependencia normal no requiere ningún wiring nuevo) y arma su
/// resumen a partir de esas dos respuestas más una única lectura de
/// IMovementsQueryService (que ya internamente reutiliza IReviewEngine e
/// IClassificationSuggestionService -- ver el doc-comment de esa interfaz) y una
/// lectura directa de IApplicationDbContext.Investigations (mismo patrón que ya usa
/// InvestigationTools/ConfigurationTools, no un servicio nuevo).
///
/// Las cantidades de "Grupos sospechosos" y "Movimientos posiblemente mal
/// clasificados" se toman leyendo el número inicial que esas dos tools ya reportan en
/// su primera línea (ej. "5 grupo(s) sospechoso(s)...") -- no se vuelve a ejecutar
/// ISuspicionDetector ni la comparación de sugerencias/defaults para "contarlos de
/// nuevo": son exactamente los mismos números que ya calcularon, solo leídos.
/// </summary>
[McpServerToolType]
public sealed class AuditDatabaseTools
{
    private readonly AuditTools _auditTools;
    private readonly IMovementsQueryService _movementsQuery;
    private readonly IApplicationDbContext _db;

    public AuditDatabaseTools(AuditTools auditTools, IMovementsQueryService movementsQuery, IApplicationDbContext db)
    {
        _auditTools = auditTools;
        _movementsQuery = movementsQuery;
        _db = db;
    }

    [McpServerTool]
    [Description(
        "Ejecuta todas las auditorías existentes (FindSuspiciousMovements, " +
        "FindMisclassifiedMovements, pendientes vía IMovementsQueryService, " +
        "investigaciones abiertas/resueltas) para el mes en curso y devuelve un único " +
        "resumen: totales, problemas encontrados por categoría, y una conclusión. No " +
        "detecta nada nuevo -- solo combina lo que esas tools ya calculan.")]
    public async Task<string> AuditDatabase(CancellationToken ct = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(to.Year, to.Month, 1);

        var movements = await _movementsQuery.GetAsync(from, to, financialAccountId: null, search: null, ct);
        var pending = movements.Where(m => m.Status is null).ToList();
        var classifiedCount = movements.Count - pending.Count;

        var suspiciousText = await _auditTools.FindSuspiciousMovements(null, null, null, ct);
        var misclassifiedText = await _auditTools.FindMisclassifiedMovements(null, null, null, ct);
        var suspiciousGroupsCount = ParseLeadingCount(suspiciousText);
        var misclassifiedCount = ParseLeadingCount(misclassifiedText);

        var investigations = await _db.Investigations.AsNoTracking().ToListAsync(ct);
        var openInvestigations = investigations.Where(i => i.Status == InvestigationStatus.Open).ToList();
        var resolvedInvestigationsCount = investigations.Count(i => i.Status == InvestigationStatus.Resolved);

        var sb = new StringBuilder();

        sb.AppendLine("Resumen");
        sb.AppendLine($"Movimientos analizados: {movements.Count}");
        sb.AppendLine($"Pendientes: {pending.Count}");
        sb.AppendLine($"Clasificados: {classifiedCount}");
        sb.AppendLine($"Grupos sospechosos: {suspiciousGroupsCount}");
        sb.AppendLine($"Movimientos posiblemente mal clasificados: {misclassifiedCount}");
        sb.AppendLine($"Investigaciones abiertas: {openInvestigations.Count}");
        sb.AppendLine($"Investigaciones resueltas: {resolvedInvestigationsCount}");
        sb.AppendLine();

        sb.AppendLine("Problemas encontrados");
        sb.AppendLine();

        sb.AppendLine("Clasificaciones dudosas");
        sb.AppendLine(misclassifiedCount > 0 ? misclassifiedText.TrimEnd() : "(ninguna)");
        sb.AppendLine();

        // Nombrado "Grupos sospechosos" (no "Duplicados"): ISuspicionDetector agrupa
        // por 3 motivos distintos (PossibleDuplicate, SplitTransaction,
        // RoundingAnomaly, ver SuspicionReason) -- llamar a esto "Duplicados" a secas
        // describiría mal los grupos que en realidad son splits o anomalías de
        // redondeo. El detalle completo, con el motivo real de cada grupo, ya viene
        // en el texto de FindSuspiciousMovements.
        sb.AppendLine("Grupos sospechosos");
        sb.AppendLine(suspiciousGroupsCount > 0 ? suspiciousText.TrimEnd() : "(ninguno)");
        sb.AppendLine();

        sb.AppendLine("Pendientes");
        if (pending.Count == 0)
        {
            sb.AppendLine("(ninguno)");
        }
        else
        {
            foreach (var m in pending)
                sb.AppendLine($"- {m.SourceId} | {m.Date:yyyy-MM-dd} | {m.Description} | {m.Currency} {m.Amount:N2}");
        }
        sb.AppendLine();

        sb.AppendLine("Investigaciones abiertas");
        if (openInvestigations.Count == 0)
        {
            sb.AppendLine("(ninguna)");
        }
        else
        {
            foreach (var investigation in openInvestigations)
                sb.AppendLine($"- {investigation.Id} | {investigation.Question}");
        }
        sb.AppendLine();

        var totalProblems = misclassifiedCount + suspiciousGroupsCount + pending.Count + openInvestigations.Count;
        sb.AppendLine("Conclusión");
        sb.AppendLine(totalProblems == 0
            ? "No se detectaron problemas."
            : $"Se detectaron {totalProblems} posibles problemas que requieren revisión.");

        return sb.ToString();
    }

    // Lee el número al inicio de la primera línea que ya reportan
    // FindSuspiciousMovements/FindMisclassifiedMovements (ej. "5 grupo(s)..."), 0 si
    // el mensaje es el de "no se encontraron" (empieza con una letra, no un dígito).
    private static int ParseLeadingCount(string text)
    {
        var span = text.AsSpan();
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
            i++;

        return i == 0 ? 0 : int.Parse(span[..i]);
    }
}
