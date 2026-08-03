using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Infrastructure.Imports.Consistency;

/// <summary>
/// Verificación del historial (Patch 0056, sección 3 del patch): confirma que existe
/// exactamente un ImportBatch para esta corrida (ni cero -- el registro de auditoría se
/// perdió -- ni más de uno -- se duplicó), y que HandlerName/ParserUsed persistidos
/// coinciden con lo que efectivamente procesó el archivo. A diferencia de
/// QuantityConsistencyCheck (cantidades), este check mira la identidad del propio
/// registro de historial, no sus contadores.
/// </summary>
internal sealed class ImportHistoryConsistencyCheck(IServiceScopeFactory scopeFactory) : IImportConsistencyCheck
{
    public string Name => "History";

    public async Task<IReadOnlyList<string>> CheckAsync(ImportConsistencyContext context, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var matches = await db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Id == context.ImportBatchId)
            .Select(b => new { b.HandlerName, b.ParserUsed })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return [$"El historial no contiene ningún ImportBatch con id {context.ImportBatchId}."];

        if (matches.Count > 1)
            return [$"El historial contiene {matches.Count} registros de ImportBatch con el mismo id {context.ImportBatchId}."];

        var persisted = matches[0];
        var issues = new List<string>();

        if (persisted.HandlerName != context.HandlerName)
            issues.Add($"HandlerName persistido ('{persisted.HandlerName}') no coincide con el esperado ('{context.HandlerName}').");

        if (persisted.ParserUsed != context.ExpectedParserUsed)
            issues.Add(
                $"ParserUsed persistido ('{persisted.ParserUsed ?? "<null>"}') no coincide con el " +
                $"esperado ('{context.ExpectedParserUsed ?? "<null>"}').");

        return issues;
    }
}
