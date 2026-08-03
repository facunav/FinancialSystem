using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Infrastructure.Imports.Consistency;

/// <summary>
/// Verificación de cantidades (Patch 0056, sección 1 del patch): vuelve a leer el
/// ImportBatch ya persistido -- sin usar la instancia en memoria que lo escribió, para que
/// esta verificación no sea tautológica -- y confirma que InsertedCount/DuplicateCount/
/// FailedCount/SkippedCount coinciden exactamente con lo que el handler reportó
/// (ImportConsistencyContext), y que ninguno es negativo. Una diferencia acá indica que lo
/// que se guardó en el historial no es lo mismo que se calculó, sin importar la causa
/// (mapeo de EF Core, condición de carrera, etc.).
/// </summary>
internal sealed class QuantityConsistencyCheck(IServiceScopeFactory scopeFactory) : IImportConsistencyCheck
{
    public string Name => "Quantities";

    public async Task<IReadOnlyList<string>> CheckAsync(ImportConsistencyContext context, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var persisted = await db.ImportBatches
            .AsNoTracking()
            .Where(b => b.Id == context.ImportBatchId)
            .Select(b => new { b.InsertedCount, b.DuplicateCount, b.FailedCount, b.SkippedCount })
            .SingleOrDefaultAsync(ct);

        if (persisted is null)
        {
            return [$"No se encontró el ImportBatch {context.ImportBatchId} al releerlo para verificar cantidades."];
        }

        var issues = new List<string>();

        if (persisted.InsertedCount < 0 || persisted.DuplicateCount < 0 ||
            persisted.FailedCount < 0 || persisted.SkippedCount < 0)
        {
            issues.Add(
                $"Se encontraron conteos negativos en el ImportBatch persistido " +
                $"(Inserted={persisted.InsertedCount}, Duplicates={persisted.DuplicateCount}, " +
                $"Failed={persisted.FailedCount}, Skipped={persisted.SkippedCount}).");
        }

        if (persisted.InsertedCount != context.ExpectedInserted)
            issues.Add($"InsertedCount persistido ({persisted.InsertedCount}) no coincide con el esperado ({context.ExpectedInserted}).");

        if (persisted.DuplicateCount != context.ExpectedDuplicates)
            issues.Add($"DuplicateCount persistido ({persisted.DuplicateCount}) no coincide con el esperado ({context.ExpectedDuplicates}).");

        if (persisted.FailedCount != context.ExpectedFailed)
            issues.Add($"FailedCount persistido ({persisted.FailedCount}) no coincide con el esperado ({context.ExpectedFailed}).");

        if (persisted.SkippedCount != context.ExpectedSkipped)
            issues.Add($"SkippedCount persistido ({persisted.SkippedCount}) no coincide con el esperado ({context.ExpectedSkipped}).");

        return issues;
    }
}
