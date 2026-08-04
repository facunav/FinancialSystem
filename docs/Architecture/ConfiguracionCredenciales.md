# Configuración de credenciales (Postgres) — cómo levantar el entorno local

Documento operativo del Patch 0062 (PATCH-013, épica de Seguridad y endurecimiento).
Cubre exclusivamente la cadena de conexión a PostgreSQL (`ConnectionStrings:Postgres`),
compartida por los tres hosts del repositorio:

* `src/FinancialMcp.Api`
* `hosts/FinancialSystem.Worker`
* `hosts/FinancialSystem.McpServer`

Los tres la resuelven de la misma forma porque los tres llaman a
`FinancialSystem.Infrastructure.DependencyInjection.AddInfrastructure(configuration)`
— un único punto de lectura (`configuration.GetConnectionString("Postgres")`), sin
mecanismos paralelos por host.

## Qué cambió

Antes del Patch 0062, `appsettings.json` de los tres hosts traía una credencial real
versionada en git (`Username=postgres;Password=postgres`, ver `docs/PROJECT_STATUS.md`,
riesgo conocido #2). Ahora esos archivos solo declaran la clave, vacía:

```json
"ConnectionStrings": {
  "Postgres": ""
}
```

El nombre lógico de la clave (`ConnectionStrings:Postgres`) **no cambió** — ningún
código consumidor de `IApplicationDbContext` se enteró de este patch. Lo que cambió es
de dónde viene el valor real.

## De dónde puede venir el valor real (orden de prioridad)

`WebApplication.CreateBuilder` (API) y `Host.CreateApplicationBuilder` (Worker, MCP
Server) ya arman esta cadena de proveedores de configuración por default, sin ningún
código adicional en este repositorio — de menor a mayor prioridad (el último que
define un valor gana):

1. `appsettings.json` (versionado — hoy siempre vacío para `ConnectionStrings:Postgres`).
2. `appsettings.{Environment}.json` (versionado — mismo criterio).
3. **User Secrets**, solo en `Environment=Development` (no versionado — ver abajo).
4. **Variables de entorno** estándar de .NET (no versionado — ver abajo).
5. Argumentos de línea de comandos.

Si al arrancar `ConnectionStrings:Postgres` queda vacío o ausente después de fusionar
todo lo anterior, `AddInfrastructure` falla rápido con un `InvalidOperationException`
que explica cómo configurarlo — ningún host arranca en silencio con una cadena de
conexión vacía, ni depende de una credencial embebida en el código o en un archivo
versionado.

## Desarrollo local: User Secrets (recomendado)

Cada host ya tiene su propio `UserSecretsId` (`FinancialMcp.Api`,
`FinancialSystem.Worker` y, desde este patch, `FinancialSystem.McpServer` también).
Los tres se configuran igual, parados en la carpeta del host correspondiente:

```bash
cd src/FinancialMcp.Api            # o hosts/FinancialSystem.Worker / hosts/FinancialSystem.McpServer
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=financialsystem;Username=tu_usuario;Password=tu_password"
```

Esto escribe en `secrets.json` **fuera del repositorio**
(`~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` en Linux/macOS,
`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` en Windows) — nunca se
versiona, y solo se carga cuando `DOTNET_ENVIRONMENT`/`ASPNETCORE_ENVIRONMENT` es
`Development` (el valor por defecto al correr `dotnet run` sin especificar entorno).

## Cualquier entorno (incluido CI o producción local): variable de entorno

El proveedor estándar de variables de entorno de .NET traduce automáticamente el
separador `__` (doble guion bajo) al separador `:` de configuración jerárquica, así
que alcanza con:

```bash
export ConnectionStrings__Postgres="Host=mi-host;Port=5432;Database=financialsystem;Username=...;Password=..."
```

Esta vía tiene prioridad sobre User Secrets (ver orden arriba) — útil para overridear
puntualmente sin tocar `secrets.json`, o en cualquier entorno donde User Secrets no
aplica (no es exclusivo de Development).

## Ejemplo de error si falta la configuración

```
System.InvalidOperationException: Connection string 'Postgres' no está configurada.
Definila vía User Secrets (dotnet user-secrets set "ConnectionStrings:Postgres" "...")
en desarrollo, o vía la variable de entorno ConnectionStrings__Postgres en cualquier
otro entorno -- ver docs/Architecture/ConfiguracionCredenciales.md.
```

Los tres hosts aplican migraciones al arrancar
(`DatabaseMigrationExtensions.ApplyMigrationsAsync`) inmediatamente después de
construir el `IServiceProvider` — con la configuración ausente, este error aparece
antes de intentar ninguna conexión real a Postgres.

## Qué NO cambió

* El código que consume `IApplicationDbContext` (Application/Infrastructure/API) —
  cero cambios, por diseño del patch.
* El comportamiento con una cadena de conexión válida presente — arranca exactamente
  igual que antes.
* Ningún endpoint, regla de negocio, ni modelo de dominio.
