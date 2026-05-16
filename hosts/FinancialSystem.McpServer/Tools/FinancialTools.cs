using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FinancialSystem.McpServer.Tools;

[McpServerToolType]
public sealed class FinancialTools(IDbContextFactory<AppDbContext> dbFactory)
{
    [McpServerTool, Description("Returns how many transactions exist (read-only query).")]
    public async Task<int> GetTransactionCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Transactions.AsNoTracking().CountAsync(cancellationToken);
    }
}
