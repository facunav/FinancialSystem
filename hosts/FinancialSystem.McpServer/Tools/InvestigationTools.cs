using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Investigations.Commands;
using FinancialSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Tools de memoria del Financial MCP — Fase 3 de
/// docs/Architecture/Decisions/ADR-007-McpMemory.md ("tools para crear, actualizar y
/// consultar investigaciones"), acotada por ahora a creación, a asociar movimientos
/// existentes, a registrar hallazgos y a leer una investigación completa (sin
/// historial, sin IA).
///
/// PRINCIPIO: no reimplementa ningún caso de uso — cada tool de escritura delega en
/// el handler de Application que ya resuelve el caso (CreateInvestigationHandler,
/// LinkMovementToInvestigationHandler, AddInvestigationFindingHandler,
/// UpdateInvestigationStatusHandler), el mismo que usaría cualquier otra entrada
/// (HTTP, en el caso de CreateInvestigation vía InvestigationEndpoints.cs —
/// LinkMovement/AddFinding/UpdateInvestigationStatus no tienen endpoint HTTP, ver
/// 0016/0017/0019). GetInvestigation/SearchInvestigations son de solo lectura: no
/// existe un servicio de consulta dedicado para Investigation (igual que
/// Category/Counterparty en ConfigurationTools.cs), así que consultan
/// IApplicationDbContext directo, mismo patrón que esa clase ya usa para esas dos
/// entidades.
/// </summary>
[McpServerToolType]
public sealed class InvestigationTools
{
    private readonly CreateInvestigationHandler _createInvestigationHandler;
    private readonly LinkMovementToInvestigationHandler _linkMovementToInvestigationHandler;
    private readonly AddInvestigationFindingHandler _addInvestigationFindingHandler;
    private readonly UpdateInvestigationStatusHandler _updateInvestigationStatusHandler;
    private readonly IApplicationDbContext _db;

    public InvestigationTools(
        CreateInvestigationHandler createInvestigationHandler,
        LinkMovementToInvestigationHandler linkMovementToInvestigationHandler,
        AddInvestigationFindingHandler addInvestigationFindingHandler,
        UpdateInvestigationStatusHandler updateInvestigationStatusHandler,
        IApplicationDbContext db)
    {
        _createInvestigationHandler = createInvestigationHandler;
        _linkMovementToInvestigationHandler = linkMovementToInvestigationHandler;
        _addInvestigationFindingHandler = addInvestigationFindingHandler;
        _updateInvestigationStatusHandler = updateInvestigationStatusHandler;
        _db = db;
    }

    [McpServerTool]
    [Description(
        "Crea una investigación nueva en estado Open — reutiliza exactamente el mismo " +
        "caso de uso que POST /api/investigations (CreateInvestigationHandler). No busca, " +
        "no edita, no crea referencias ni memoria automática: solo registra la pregunta de " +
        "investigación.")]
    public async Task<string> CreateInvestigation(
        [Description("Pregunta o hipótesis que da origen a la investigación.")]
        string question,
        [Description("Etiquetas libres separadas por coma, ej. 'tarjeta-visa,contraparte-desconocida'. Opcional.")]
        string? tags = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            return "Error: question es obligatorio.";

        var command = new CreateInvestigationCommand(question, tags);
        var investigation = await _createInvestigationHandler.Handle(command, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"InvestigationId: {investigation.Id}");
        sb.AppendLine($"Status: {investigation.Status}");
        sb.AppendLine($"CreatedAt (UTC): {investigation.CreatedAt:O}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Asocia un movimiento existente (Transaction o BankStatement) a una investigación ya " +
        "creada — reutiliza exactamente el mismo caso de uso que LinkMovementToInvestigationHandler. " +
        "Idempotente: si el movimiento ya estaba asociado a esa investigación, no crea una " +
        "referencia duplicada. No crea hallazgos, comentarios ni historial, y no modifica la " +
        "investigación.")]
    public async Task<string> LinkMovement(
        [Description("Id de la investigación (Investigation.Id).")]
        Guid investigationId,
        [Description("Tipo de origen del movimiento: 'Transaction' o 'BankStatement'.")]
        string sourceEntityType,
        [Description("Id del movimiento original en su tabla (Transaction.Id o BankStatement.Id).")]
        Guid sourceId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(sourceEntityType, ignoreCase: true, out SourceEntityType parsedSource)
            || parsedSource is not (SourceEntityType.Transaction or SourceEntityType.BankStatement))
        {
            return $"Error: sourceEntityType inválido ('{sourceEntityType}'). " +
                "Valores permitidos: Transaction, BankStatement.";
        }

        var command = new LinkMovementToInvestigationCommand(investigationId, parsedSource, sourceId);
        var result = await _linkMovementToInvestigationHandler.Handle(command, ct);

        if (!result.IsSuccess)
            return $"Error: no se encontró ninguna investigación con Id {investigationId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"InvestigationId: {investigationId}");
        sb.AppendLine($"SourceEntityType: {parsedSource}");
        sb.AppendLine($"SourceId: {sourceId}");
        sb.AppendLine($"Resultado: {result.Outcome}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Registra un hallazgo (InvestigationFinding) en una investigación ya creada — " +
        "reutiliza exactamente el mismo caso de uso que AddInvestigationFindingHandler. No " +
        "modifica el estado de la investigación ni sus referencias. Sin IA, sin resumen: el " +
        "texto se guarda tal como se pasa.")]
    public async Task<string> AddFinding(
        [Description("Id de la investigación (Investigation.Id).")]
        Guid investigationId,
        [Description("Texto del hallazgo, en lenguaje natural.")]
        string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Error: text es obligatorio.";

        var command = new AddInvestigationFindingCommand(investigationId, text);
        var result = await _addInvestigationFindingHandler.Handle(command, ct);

        if (!result.IsSuccess)
            return $"Error: no se encontró ninguna investigación con Id {investigationId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"InvestigationId: {investigationId}");
        sb.AppendLine($"FindingId: {result.FindingId}");
        sb.AppendLine($"CreatedAt (UTC): {result.CreatedAt:O}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Devuelve la información completa de una investigación: datos generales " +
        "(Question/Status/Tags/Conclusion/CreatedAt/UpdatedAt), sus referencias " +
        "(SourceEntityType+SourceId) y sus hallazgos ordenados por fecha. Solo lee " +
        "exactamente lo persistido en Investigation/InvestigationReference/" +
        "InvestigationFinding — no resuelve movimientos ni llama a GetMovement.")]
    public async Task<string> GetInvestigation(
        [Description("Id de la investigación (Investigation.Id).")]
        Guid investigationId,
        CancellationToken ct = default)
    {
        var investigation = await _db.Investigations
            .AsNoTracking()
            .Include(x => x.References)
            .Include(x => x.Findings)
            .FirstOrDefaultAsync(x => x.Id == investigationId, ct);

        if (investigation is null)
            return $"Error: no se encontró ninguna investigación con Id {investigationId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Id: {investigation.Id}");
        sb.AppendLine($"Question: {investigation.Question}");
        sb.AppendLine($"Status: {investigation.Status}");
        sb.AppendLine($"Tags: {investigation.Tags ?? "-"}");
        sb.AppendLine($"Conclusion: {investigation.Conclusion ?? "-"}");
        sb.AppendLine($"CreatedAt (UTC): {investigation.CreatedAt:O}");
        sb.AppendLine($"UpdatedAt (UTC): {investigation.UpdatedAt:O}");
        sb.AppendLine();

        sb.AppendLine($"Referencias ({investigation.References.Count}):");
        if (investigation.References.Count == 0)
        {
            sb.AppendLine("  (ninguna)");
        }
        else
        {
            foreach (var reference in investigation.References)
                sb.AppendLine($"  - SourceEntityType: {reference.SourceEntityType}, SourceId: {reference.SourceId}");
        }
        sb.AppendLine();

        var findings = investigation.Findings.OrderBy(f => f.CreatedAt).ToList();
        sb.AppendLine($"Hallazgos ({findings.Count}):");
        if (findings.Count == 0)
        {
            sb.AppendLine("  (ninguno)");
        }
        else
        {
            foreach (var finding in findings)
            {
                sb.AppendLine($"  - Id: {finding.Id}");
                sb.AppendLine($"    Text: {finding.Text}");
                sb.AppendLine($"    CreatedAt (UTC): {finding.CreatedAt:O}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Cambia el estado de una investigación (Open/InProgress/Resolved/Discarded) — " +
        "reutiliza exactamente el mismo caso de uso que UpdateInvestigationStatusHandler. " +
        "Si status es 'Resolved', conclusion es obligatoria y se guarda; en cualquier otro " +
        "estado, conclusion no se modifica.")]
    public async Task<string> UpdateInvestigationStatus(
        [Description("Id de la investigación (Investigation.Id).")]
        Guid investigationId,
        [Description("Nuevo estado: 'Open', 'InProgress', 'Resolved' o 'Discarded'.")]
        string status,
        [Description("Conclusión de la investigación. Obligatoria solo si status es 'Resolved'.")]
        string? conclusion = null,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(status, ignoreCase: true, out InvestigationStatus parsedStatus))
        {
            return $"Error: status inválido ('{status}'). " +
                "Valores permitidos: Open, InProgress, Resolved, Discarded.";
        }

        var command = new UpdateInvestigationStatusCommand(investigationId, parsedStatus, conclusion);
        var result = await _updateInvestigationStatusHandler.Handle(command, ct);

        if (!result.IsSuccess)
        {
            return result.FailureReason switch
            {
                UpdateInvestigationStatusFailureReason.InvestigationNotFound =>
                    $"Error: no se encontró ninguna investigación con Id {investigationId}.",
                UpdateInvestigationStatusFailureReason.ConclusionRequired =>
                    "Error: conclusion es obligatoria cuando status es 'Resolved'.",
                _ => "Error desconocido al actualizar el estado de la investigación.",
            };
        }

        var sb = new StringBuilder();
        sb.AppendLine($"InvestigationId: {investigationId}");
        sb.AppendLine($"Status: {parsedStatus}");
        sb.AppendLine($"UpdatedAt (UTC): {result.UpdatedAt:O}");

        return sb.ToString();
    }

    [McpServerTool]
    [Description(
        "Busca investigaciones por status (comparación exacta), tag (contiene, sin " +
        "distinguir mayúsculas) y/o texto libre (contiene en Question o Conclusion, sin " +
        "distinguir mayúsculas). Todos los parámetros son opcionales — sin ninguno, " +
        "devuelve todas las investigaciones. Solo cuenta hallazgos y referencias, no las " +
        "devuelve ni resuelve movimientos.")]
    public async Task<string> SearchInvestigations(
        [Description("Estado exacto a filtrar: 'Open', 'InProgress', 'Resolved' o 'Discarded'. Opcional.")]
        string? status = null,
        [Description("Etiqueta a buscar dentro de Tags (contiene, sin distinguir mayúsculas). Opcional.")]
        string? tag = null,
        [Description("Texto a buscar en Question o Conclusion (contiene, sin distinguir mayúsculas). Opcional.")]
        string? text = null,
        CancellationToken ct = default)
    {
        var query = _db.Investigations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out InvestigationStatus parsedStatus))
            {
                return $"Error: status inválido ('{status}'). " +
                    "Valores permitidos: Open, InProgress, Resolved, Discarded.";
            }

            query = query.Where(i => i.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = tag.ToLower();
            query = query.Where(i => i.Tags != null && i.Tags.ToLower().Contains(normalizedTag));
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalizedText = text.ToLower();
            query = query.Where(i =>
                i.Question.ToLower().Contains(normalizedText)
                || (i.Conclusion != null && i.Conclusion.ToLower().Contains(normalizedText)));
        }

        var investigations = await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id,
                i.Question,
                i.Status,
                i.Tags,
                i.CreatedAt,
                i.UpdatedAt,
                FindingsCount = i.Findings.Count,
                ReferencesCount = i.References.Count,
            })
            .ToListAsync(ct);

        if (investigations.Count == 0)
            return "No se encontraron investigaciones con esos criterios.";

        var sb = new StringBuilder();
        sb.AppendLine($"{investigations.Count} investigación(es):");
        sb.AppendLine();
        foreach (var i in investigations)
        {
            sb.AppendLine($"- InvestigationId: {i.Id}");
            sb.AppendLine($"  Question: {i.Question}");
            sb.AppendLine($"  Status: {i.Status}");
            sb.AppendLine($"  Tags: {i.Tags ?? "-"}");
            sb.AppendLine($"  CreatedAt (UTC): {i.CreatedAt:O}");
            sb.AppendLine($"  UpdatedAt (UTC): {i.UpdatedAt:O}");
            sb.AppendLine($"  FindingsCount: {i.FindingsCount}");
            sb.AppendLine($"  ReferencesCount: {i.ReferencesCount}");
        }

        return sb.ToString();
    }
}
