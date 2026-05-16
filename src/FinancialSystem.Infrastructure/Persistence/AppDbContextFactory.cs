using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FinancialSystem.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core CLI (dotnet ef) at design time.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = ResolveWorkerConfigPath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=financialsystem;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string ResolveWorkerConfigPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        string[] candidates =
        [
            Path.Combine(cwd, "hosts", "FinancialSystem.Worker"),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "hosts", "FinancialSystem.Worker")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "hosts", "FinancialSystem.Worker"))
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }
        }

        return cwd;
    }
}
