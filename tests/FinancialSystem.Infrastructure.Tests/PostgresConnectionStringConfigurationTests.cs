using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinancialSystem.Infrastructure.Tests;

/// <summary>
/// Cubre la resolución de ConnectionStrings:Postgres tras el Patch 0062 (PATCH-013):
/// AddInfrastructure (compartida por FinancialMcp.Api, FinancialSystem.Worker y
/// FinancialSystem.McpServer) sigue leyendo la misma clave lógica vía
/// IConfiguration.GetConnectionString("Postgres") -- lo único que cambia es que
/// appsettings.json versionado ya no trae una credencial real, así que la resolución
/// depende de la cadena estándar de proveedores de configuración de .NET (appsettings
/// -> User Secrets en Development -> variables de entorno -> línea de comandos).
///
/// No hay un test dedicado a "funciona con User Secrets": mecánicamente, User Secrets
/// es un proveedor de configuración JSON más (secrets.json fuera del repositorio,
/// cargado automáticamente en Development por Host.CreateApplicationBuilder /
/// WebApplication.CreateBuilder cuando el host tiene UserSecretsId) que termina
/// fusionado en el mismo IConfiguration que consume GetConnectionString -- ya cubierto
/// por AddInfrastructure_WithConnectionStringInConfiguration_DoesNotThrow, sin
/// necesidad de un secrets.json real en disco para probarlo.
/// </summary>
public class PostgresConnectionStringConfigurationTests
{
    [Fact]
    public void AddInfrastructure_WithConnectionStringInConfiguration_DoesNotThrow()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=financialsystem_test;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddInfrastructure(configuration));

        Assert.Null(exception);
    }

    [Fact]
    public void AddInfrastructure_WithConnectionStringFromEnvironmentVariable_DoesNotThrow()
    {
        // Mismo mecanismo que ya documenta docs/Architecture/ConfiguracionCredenciales.md
        // (ConnectionStrings__Postgres, doble guion bajo) -- AddEnvironmentVariables()
        // lo traduce automáticamente a la clave jerárquica "ConnectionStrings:Postgres"
        // que consume GetConnectionString, sin ningún código propio en AddInfrastructure.
        const string envVarName = "ConnectionStrings__Postgres";
        Environment.SetEnvironmentVariable(
            envVarName, "Host=localhost;Port=5432;Database=financialsystem_test;Username=test;Password=test");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection();

            var exception = Record.Exception(() => services.AddInfrastructure(configuration));

            Assert.Null(exception);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public void AddInfrastructure_WithMissingConnectionString_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("Postgres", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_WithBlankConnectionString_ThrowsInvalidOperationException()
    {
        // Mismo escenario que appsettings.json versionado desde este patch: la clave
        // ConnectionStrings:Postgres existe pero con valor "" -- antes del patch, el
        // "?? throw" original solo cubría la clave ausente (null), no una clave
        // presente con valor vacío, que pasaba en silencio a Npgsql.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ""
            })
            .Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("Postgres", exception.Message);
    }
}
