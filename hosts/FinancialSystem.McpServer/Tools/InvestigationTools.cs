using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Investigations.Commands;
using FinancialSystem.Domain.Enums;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Tools de memoria del Financial MCP — Fase 3 de
/// docs/Architecture/Decisions/ADR-007-McpMemory.md ("tools para crear, actualizar y
/// consultar investigaciones"), acotada por ahora a creación y a asociar movimientos
/// existentes (sin hallazgos, sin historial, sin IA).
///
/// PRINCIPIO: no reimplementa ningún caso de uso — cada tool delega en el handler de
/// Application que ya resuelve el caso (CreateInvestigationHandler,
/// LinkMovementToInvestigationHandler), el mismo que usaría cualquier otra entrada
/// (HTTP, en el caso de CreateInvestigation vía InvestigationEndpoints.cs — LinkMovement
/// no tiene endpoint HTTP, ver 0016), igual que ExplainMovement/GetMovement reutilizan
/// IMovementLookupService en vez de duplicar su lógica.
/// </summary>
[McpServerToolType]
public sealed class InvestigationTools
{
    private readonly CreateInvestigationHandler _createInvestigationHandler;
    private readonly LinkMovementToInvestigationHandler _linkMovementToInvestigationHandler;

    public InvestigationTools(
        CreateInvestigationHandler createInvestigationHandler,
        LinkMovementToInvestigationHandler linkMovementToInvestigationHandler)
    {
        _createInvestigationHandler = createInvestigationHandler;
        _linkMovementToInvestigationHandler = linkMovementToInvestigationHandler;
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
}
