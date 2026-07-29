using System.ComponentModel;
using System.Text;
using FinancialSystem.Application.Investigations.Commands;
using ModelContextProtocol.Server;

namespace FinancialSystem.McpServer.Tools;

/// <summary>
/// Primera tool de memoria del Financial MCP — Fase 3 de
/// docs/Architecture/Decisions/ADR-007-McpMemory.md ("tools para crear, actualizar y
/// consultar investigaciones"), acotada por ahora solo a creación.
///
/// PRINCIPIO: no reimplementa el caso de uso — delega en CreateInvestigationHandler,
/// exactamente el mismo handler que ya usa POST /api/investigations
/// (InvestigationEndpoints.cs, ver 0014). Una sola implementación de "crear una
/// investigación", consumida por dos entradas (HTTP y MCP), igual que
/// ExplainMovement/GetMovement reutilizan IMovementLookupService en vez de duplicar
/// su lógica.
/// </summary>
[McpServerToolType]
public sealed class InvestigationTools
{
    private readonly CreateInvestigationHandler _createInvestigationHandler;

    public InvestigationTools(CreateInvestigationHandler createInvestigationHandler)
    {
        _createInvestigationHandler = createInvestigationHandler;
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
}
