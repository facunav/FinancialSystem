// DEDUPE-004-CONV — PREVIEW real de DedupeEngine contra una base existente.
//
// SOLO LECTURA, GARANTIZADO EN DOS NIVELES:
//   1. A nivel de código: nunca se referencia IDedupeEngine.ApplyAsync ni
//      AppDbContext.SaveChangesAsync en ningún punto de este archivo.
//   2. A nivel de base de datos: se ejecuta "SET default_transaction_read_only = on;"
//      sobre la conexión ANTES de cualquier consulta -- el propio Postgres rechaza
//      cualquier intento de escritura en esa sesión, sin depender de que el código
//      de arriba esté libre de bugs. Mismo mecanismo que se usó durante toda la
//      investigación previa vía "$env:PGOPTIONS = '-c default_transaction_read_only=on'".
//
// NO llama DatabaseMigrationExtensions.ApplyMigrationsAsync (a diferencia de los 3
// hosts reales) -- no aplica ninguna migración pendiente.
//
// Uso:
//   dotnet run --project tools/DedupePreviewCli
//
// Requiere la misma connection string que ya usa el resto del proyecto
// (ConnectionStrings:Postgres, vía User Secrets / variable de entorno
// ConnectionStrings__Postgres / appsettings.Development.json de
// hosts/FinancialSystem.Worker -- mismo mecanismo que AppDbContextFactory).

using System.Text.RegularExpressions;
using FinancialSystem.Application;
using FinancialSystem.Application.Dedupe;
using FinancialSystem.Domain.Dedupe;
using FinancialSystem.Infrastructure;
using FinancialSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("==============================================================");
Console.WriteLine("DEDUPE-004-CONV — PREVIEW real (solo lectura)");
Console.WriteLine("==============================================================");

// ── Resolución de configuración (mismo criterio que AppDbContextFactory,
// incluido el mismo valor de reserva -- AddInfrastructure, a diferencia de
// AppDbContextFactory, no tiene fallback propio y tira si la cadena queda
// vacía; se iguala acá para no fallar donde "dotnet ef migrations add" ya
// funcionó) ──────────────────────────────────────────────────────────────
var basePath = ResolveWorkerConfigPath();
var baseConfiguration = new ConfigurationBuilder()
    .SetBasePath(basePath)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

const string fallbackConnectionString =
    "Host=localhost;Port=5432;Database=financialsystem;Username=postgres;Password=postgres";

var resolvedConnectionString = baseConfiguration.GetConnectionString("Postgres");
var configuration = string.IsNullOrWhiteSpace(resolvedConnectionString)
    ? new ConfigurationBuilder()
        .AddConfiguration(baseConfiguration)
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fallbackConnectionString,
        })
        .Build()
    : baseConfiguration;

var services = new ServiceCollection();
services.AddApplication();
services.AddInfrastructure(configuration); // solo registra servicios -- no toca la base

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var dedupeEngine = scope.ServiceProvider.GetRequiredService<IDedupeEngine>();

// ── Confirmación visual de a qué base nos conectamos, SIN imprimir la contraseña ──
var rawConnectionString = appDbContext.Database.GetConnectionString() ?? "";
var dbNameMatch = Regex.Match(rawConnectionString, @"Database=([^;]+)", RegexOptions.IgnoreCase);
var hostMatch = Regex.Match(rawConnectionString, @"Host=([^;]+)", RegexOptions.IgnoreCase);
Console.WriteLine($"Host:      {(hostMatch.Success ? hostMatch.Groups[1].Value : "(no resuelto)")}");
Console.WriteLine($"Database:  {(dbNameMatch.Success ? dbNameMatch.Groups[1].Value : "(no resuelto)")}");
Console.WriteLine();

// ── Guardián de solo lectura a nivel de sesión Postgres ─────────────────────────
await appDbContext.Database.OpenConnectionAsync();
await appDbContext.Database.ExecuteSqlRawAsync("SET default_transaction_read_only = on;");
Console.WriteLine("Sesión marcada SET default_transaction_read_only = on -- cualquier intento de");
Console.WriteLine("escritura en esta conexión será rechazado por Postgres, no solo por el código.");
Console.WriteLine();

var totalAntes = await appDbContext.BankStatements.AsNoTracking().CountAsync();

// ── PREVIEW real — cuenta completa (no hay forma de acotar a los 27 por GUID: ────
// no se conocen sus Id reales desde acá, ver explicación entregada aparte).
var resultados = await dedupeEngine.PreviewAsync();

var totalDespues = await appDbContext.BankStatements.AsNoTracking().CountAsync();

Console.WriteLine($"BankStatements antes del preview:  {totalAntes}");
Console.WriteLine($"BankStatements después del preview: {totalDespues}");
Console.WriteLine(totalAntes == totalDespues
    ? "OK -- cantidad de filas idéntica, ninguna fue insertada/eliminada."
    : "*** ALERTA *** la cantidad de filas cambió -- esto NO debería pasar nunca.");
Console.WriteLine();

// ── Identificadores reales conocidos (los 5 casos adversariales de DEDUPE-001j) ──
// NOTA HONESTA: no se listan acá los 22 originales -- sus identificadores de
// negocio (Nro/importe/fecha puntuales) no están disponibles en este entorno
// (se perdieron en una compactación de contexto anterior en esta conversación,
// ya reportado explícitamente). Este preview los va a encontrar de todas formas
// si el motor los detecta (es un escaneo de TODA la cuenta), pero no van a quedar
// marcados como "conocidos" en el reporte -- van a aparecer como FUERTE sin más,
// y hace falta cruzarlos a mano contra la investigación original.
string[] nrosAdversariales = ["026888", "904607", "899728", "337206", "684228"];

Console.WriteLine("==============================================================");
Console.WriteLine($"DETALLE — {resultados.Count} candidatos encontrados (DESCARTADO no se reporta acá:");
Console.WriteLine("PreviewAsync los filtra por diseño, ver IDedupeEngine.PreviewAsync xmldoc)");
Console.WriteLine("==============================================================");

var i = 0;
foreach (var r in resultados.OrderBy(r => r.PendienteDate))
{
    i++;
    var esConocido = nrosAdversariales.Any(n => r.PendienteConcept.Contains(n) || r.LiquidadoConcept.Contains(n));
    Console.WriteLine($"--- [{i}/{resultados.Count}] {(esConocido ? "*** CASO ADVERSARIAL CONOCIDO ***" : "")}");
    Console.WriteLine($"  Pendiente : Id={r.PendienteId}  Fecha={r.PendienteDate:yyyy-MM-dd}  Importe={r.PendienteAmount}");
    Console.WriteLine($"              Concepto=\"{r.PendienteConcept}\"  SourceFile={r.PendienteSourceFile}");
    Console.WriteLine($"  Liquidado : Id={r.LiquidadoId}  Fecha={r.LiquidadoDate:yyyy-MM-dd}  Importe={r.LiquidadoAmount}");
    Console.WriteLine($"              Concepto=\"{r.LiquidadoConcept}\"  SourceFile={r.LiquidadoSourceFile}");
    Console.WriteLine($"  Clasificación : {r.Classification}");
    Console.WriteLine($"  Evidencia     : {r.Evidence}");
    Console.WriteLine($"  Carry-forward : {(r.CarryForwardMemberIds.Count > 0 ? $"SÍ ({r.CarryForwardMemberIds.Count} miembro(s) adicional(es): {string.Join(",", r.CarryForwardMemberIds)})" : "no")}");
    Console.WriteLine();
}

// ── Reconciliación ──────────────────────────────────────────────────────────────
var fuertes = resultados.Where(r => r.Classification == IdentityClassification.Fuerte).ToList();
var posibles = resultados.Count(r => r.Classification == IdentityClassification.Posible);
var indeterminados = resultados.Count(r => r.Classification == IdentityClassification.Indeterminado);
var adversarialesEncontrados = fuertes.Count(r =>
    nrosAdversariales.Any(n => r.PendienteConcept.Contains(n) || r.LiquidadoConcept.Contains(n)));

Console.WriteLine("==============================================================");
Console.WriteLine("RECONCILIACIÓN");
Console.WriteLine("==============================================================");
Console.WriteLine($"Confirmados históricos (DEDUPE-001)              : 27 (22 originales + 5 adversariales)");
Console.WriteLine($"FUERTE encontrados por DedupeEngine              : {fuertes.Count}");
Console.WriteLine($"  de los cuales, 5 adversariales conocidos       : {adversarialesEncontrados} / 5");
Console.WriteLine($"  de los cuales, sin identificador conocido acá  : {fuertes.Count - adversarialesEncontrados}");
Console.WriteLine($"                 (candidatos a ser los 22 originales -- requiere cruce manual,");
Console.WriteLine($"                  no tengo su lista de identificadores en este entorno)");
Console.WriteLine($"POSIBLE                                          : {posibles}");
Console.WriteLine($"INDETERMINADO                                    : {indeterminados}");
Console.WriteLine($"DESCARTADO                                       : no reportado por PreviewAsync (por diseño)");
Console.WriteLine();

// ── Validaciones adicionales pedidas ────────────────────────────────────────────
Console.WriteLine("==============================================================");
Console.WriteLine("VALIDACIONES");
Console.WriteLine("==============================================================");

var fuertesConMasDeUnCandidato = fuertes
    .GroupBy(r => r.PendienteId)
    .Where(g => g.Count() > 1)
    .ToList();
Console.WriteLine(fuertesConMasDeUnCandidato.Count == 0
    ? "1. OK -- ningún FUERTE tiene más de un candidato."
    : $"1. *** ALERTA *** {fuertesConMasDeUnCandidato.Count} pendiente(s) FUERTE con más de un candidato.");

var todosLosIds = fuertes.SelectMany(r => new[] { r.PendienteId, r.LiquidadoId }.Concat(r.CarryForwardMemberIds));
var idsRepetidos = todosLosIds.GroupBy(id => id).Where(g => g.Count() > 1).ToList();
Console.WriteLine(idsRepetidos.Count == 0
    ? "2. OK -- ningún SourceId aparece en más de un vínculo potencial FUERTE."
    : $"2. *** ALERTA *** {idsRepetidos.Count} SourceId(s) aparecen en más de un vínculo potencial.");

var conM = fuertes.Where(r => r.Evidence.Contains("M:")).ToList();
Console.WriteLine(conM.Count == 0
    ? "3. OK -- ningún FUERTE fue alcanzado pese a (o degradado por) el guardián M en este resultado."
    : $"3. Atención -- {conM.Count} resultado(s) mencionan M en su evidencia -- revisar manualmente si degradaron algo.");
var posiblesPorM = resultados.Where(r => r.Classification == IdentityClassification.Posible && r.Evidence.Contains("M:")).ToList();
if (posiblesPorM.Count > 0)
{
    Console.WriteLine($"   {posiblesPorM.Count} candidato(s) quedaron en POSIBLE específicamente por el guardián M:");
    foreach (var p in posiblesPorM)
        Console.WriteLine($"     - Pendiente {p.PendienteId} / Liquidado {p.LiquidadoId}: {p.Evidence}");
}

Console.WriteLine(adversarialesEncontrados == 5
    ? "4. OK -- los 5 casos adversariales (026888, 904607, 899728, 337206, 684228) siguen FUERTE."
    : $"4. *** ALERTA *** solo {adversarialesEncontrados}/5 casos adversariales aparecen como FUERTE -- ver detalle arriba.");

Console.WriteLine($"5. El motor evaluó TODA la cuenta (sin acotar a los 27) -- {fuertes.Count - adversarialesEncontrados} " +
                   "candidato(s) FUERTE no identificados como uno de los 5 adversariales quedan listados arriba " +
                   "para cruce manual contra los 22 originales (no se ocultó ningún resultado).");

Console.WriteLine();
Console.WriteLine("==============================================================");
Console.WriteLine("CONFIRMACIÓN FINAL");
Console.WriteLine("==============================================================");
Console.WriteLine("Este proceso NO llamó ApplyAsync en ningún momento.");
Console.WriteLine("Este proceso NO llamó SaveChangesAsync en ningún momento.");
Console.WriteLine("Este proceso NO aplicó ninguna migración.");
Console.WriteLine("No se creó ninguna fila en MovementIdentityLinks (la tabla, de hecho, ni siquiera existe todavía).");
Console.WriteLine("La sesión Postgres estuvo en modo read-only forzado durante toda la ejecución.");

return;

static string ResolveWorkerConfigPath()
{
    var cwd = Directory.GetCurrentDirectory();
    string[] candidates =
    [
        Path.Combine(cwd, "hosts", "FinancialSystem.Worker"),
        Path.GetFullPath(Path.Combine(cwd, "..", "..", "hosts", "FinancialSystem.Worker")),
        Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "hosts", "FinancialSystem.Worker")),
        Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "..", "hosts", "FinancialSystem.Worker")),
    ];

    foreach (var candidate in candidates)
    {
        if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            return candidate;
    }

    return cwd;
}
