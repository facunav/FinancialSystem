using System.Text.RegularExpressions;
using FinancialSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinancialSystem.Infrastructure.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        IServiceProvider services,
        string applicationName = "FinancialSystem",
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseMigration");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("{Application} — starting database initialization", applicationName);

        var connection = db.Database.GetDbConnection();
        LogConnectionDetails(logger, connection.ConnectionString, connection.DataSource, connection.Database);

        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            logger.LogInformation("PostgreSQL CanConnectAsync: {CanConnect}", canConnect);

            if (!canConnect)
                logger.LogWarning("Cannot connect to PostgreSQL yet. MigrateAsync may create the database if permissions allow.");

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            var appliedBefore = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

            logger.LogInformation(
                "Migrations — applied: {AppliedCount}, pending: {PendingCount}",
                appliedBefore.Count, pending.Count);

            if (pending.Count == 0)
            {
                logger.LogInformation("No pending migrations.");
            }
            else
            {
                logger.LogInformation("Pending migrations: {Migrations}", string.Join(", ", pending));
                var migrator = db.GetService<IMigrator>();
                foreach (var migration in pending)
                {
                    logger.LogInformation("Applying migration: {MigrationName}", migration);
                    migrator.Migrate(migration);
                    logger.LogInformation("Applied migration: {MigrationName}", migration);
                }
            }

            await SeedCategoriesAsync(db, logger, cancellationToken);

            var appliedAfter = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            logger.LogInformation(
                "Database schema is up to date ({AppliedCount} migration(s) applied).",
                appliedAfter.Count);

            var transactionCount = await db.Transactions.AsNoTracking().CountAsync(cancellationToken);
            logger.LogInformation("Transactions table has {RowCount} rows.", transactionCount);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to apply EF Core migrations: {ErrorMessage}. " +
                "Verify PostgreSQL is running and ConnectionStrings:Postgres is correct.",
                ex.Message);
            throw;
        }
    }

    // ── Seed de categorías ────────────────────────────────────────────────────────
    // Idempotente: usa INSERT ... ON CONFLICT DO NOTHING por Name.
    // Se ejecuta en cada startup — si las categorías ya existen, no hace nada.
    // Si se agrega una categoría nueva al array, aparece automáticamente.

    private static readonly (string Name, string DisplayName, int SortOrder)[] SystemCategories =
    [
        ("Food",           "Alimentación",    1),
        ("Health",         "Salud",           2),
        ("Transport",      "Transporte",      3),
        ("Services",       "Servicios",       4),
        ("Insurance",      "Seguros",         5),
        ("Education",      "Educación",       6),
        ("Entertainment",  "Entretenimiento", 7),
        ("Subscription",   "Suscripciones",   8),
        ("Transfer",       "Transferencias",  9),
        ("Income",         "Ingresos",        10),
        ("Other",          "Otros",           11),
    ];

    private static async Task SeedCategoriesAsync(
        AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var existing = await db.Categories
            .AsNoTracking()
            .Select(c => c.Name)
            .ToHashSetAsync(ct);

        var toInsert = SystemCategories
            .Where(c => !existing.Contains(c.Name))
            .Select(c => new Category
            {
                Id = Guid.NewGuid(),
                Name = c.Name,
                DisplayName = c.DisplayName,
                SortOrder = c.SortOrder,
                IsSystem = true,
            })
            .ToList();

        if (toInsert.Count == 0)
        {
            logger.LogInformation("Categories seed: all {Count} system categories already present.", existing.Count);
            return;
        }

        db.Categories.AddRange(toInsert);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Categories seed: inserted {Count} new system categories ({Names}).",
            toInsert.Count,
            string.Join(", ", toInsert.Select(c => c.Name)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void LogConnectionDetails(
        ILogger logger, string? connectionString, string? dataSource, string? database)
    {
        logger.LogInformation(
            "PostgreSQL connection string (redacted): {ConnectionString}",
            RedactPassword(connectionString));

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                logger.LogInformation(
                    "PostgreSQL target — Host: {Host}, Port: {Port}, Database: {Database}, Username: {Username}",
                    builder.Host, builder.Port, builder.Database, builder.Username);
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not parse connection string with NpgsqlConnectionStringBuilder");
            }
        }

        logger.LogInformation(
            "PostgreSQL target — DataSource: {DataSource}, Database: {Database}",
            dataSource ?? "(unknown)", database ?? "(unknown)");
    }

    private static string RedactPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "(empty)";
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password)) builder.Password = "***";
            return builder.ToString();
        }
        catch
        {
            return Regex.Replace(
                connectionString,
                @"(Password|Pwd)\s*=\s*[^;]*",
                "$1=***",
                RegexOptions.IgnoreCase);
        }
    }
}