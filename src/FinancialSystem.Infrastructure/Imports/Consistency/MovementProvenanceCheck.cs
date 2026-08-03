using FinancialSystem.Application.Abstractions;
using FinancialSystem.Application.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialSystem.Infrastructure.Imports.Consistency;

/// <summary>
/// Verificación de identidad (Patch 0056, sección 2 del patch): confirma que la cantidad
/// de movimientos efectivamente persistidos durante esta corrida -- Transactions y
/// BankStatements con el mismo SourceFile, creados dentro de la ventana
/// [StartedAtUtc, CompletedAtUtc] de esta corrida -- coincide con InsertedCount. Se
/// consultan ambas tablas (no una según HandlerName) para que este check no necesite
/// conocer qué handler produjo la corrida -- ver ADR de unificación de contrato, Patch
/// 0055 -- ni requerir ningún cambio cuando se agregue un handler nuevo que persista en
/// una de estas dos tablas.
///
/// Ventana de tiempo en vez de una FK a ImportBatch: Transaction/BankStatement no tienen
/// (ni este patch agrega) una referencia directa a ImportBatch -- agregarla sería un
/// cambio de esquema en las tablas financieras, fuera del alcance de una verificación de
/// solo lectura. La ventana ya es exacta hoy: CompletedAtUtc se calcula después de que
/// handler.HandleAsync (y su propio SaveChangesAsync) ya terminó.
/// </summary>
internal sealed class MovementProvenanceCheck(IServiceScopeFactory scopeFactory) : IImportConsistencyCheck
{
    public string Name => "Identity";

    public async Task<IReadOnlyList<string>> CheckAsync(ImportConsistencyContext context, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var transactionCount = await db.Transactions
            .AsNoTracking()
            .Where(t => t.SourceFile == context.SourceFile)
            .Where(t => t.CreatedAtUtc >= context.StartedAtUtc && t.CreatedAtUtc <= context.CompletedAtUtc)
            .CountAsync(ct);

        var bankStatementCount = await db.BankStatements
            .AsNoTracking()
            .Where(b => b.SourceFile == context.SourceFile)
            .Where(b => b.ImportedAtUtc >= context.StartedAtUtc && b.ImportedAtUtc <= context.CompletedAtUtc)
            .CountAsync(ct);

        var actualInserted = transactionCount + bankStatementCount;

        if (actualInserted != context.ExpectedInserted)
        {
            return
            [
                $"Se reportaron {context.ExpectedInserted} movimientos insertados, pero se " +
                $"encontraron {actualInserted} (Transactions={transactionCount}, " +
                $"BankStatements={bankStatementCount}) con SourceFile='{context.SourceFile}' " +
                $"creados durante esta corrida -- posible movimiento persistido sin origen o " +
                $"movimiento reportado que no llegó a persistirse."
            ];
        }

        return [];
    }
}
