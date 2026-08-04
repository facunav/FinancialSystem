# FinancialSystem.McpServer — qué es y cómo usarlo

Guía operativa para clonar el repositorio, levantar `FinancialSystem.McpServer` y
conectarlo a un cliente MCP real, sin tener que leer el código primero. Para el
roadmap y el criterio de diseño de cada tool, ver
`docs/Decisions/ADR-006-financial-mcp-roadmap-investigacion.md` y
`docs/Architecture/Decisions/ADR-007-McpMemory.md` — este documento es
exclusivamente operativo (cómo correrlo), no de diseño.

## Qué es

`FinancialSystem.McpServer` (`hosts/FinancialSystem.McpServer/`) es un servidor
[Model Context Protocol](https://modelcontextprotocol.io/) que expone el estado del
sistema financiero (movimientos, clasificación, cuentas, categorías, contrapartes,
documentación del proyecto e investigaciones/memoria) como *tools* que un cliente
MCP (Claude Desktop, Claude Code, un cliente propio, etc.) puede invocar durante una
conversación. Es un proceso .NET independiente, separado de `FinancialMcp.Api` y de
`FinancialSystem.Worker` — comparte con ellos la misma base de datos y las mismas
capas `Application`/`Infrastructure`, pero no depende de que ningún otro host esté
corriendo.

Con la única excepción de `POST /api/investigations` (que también existe como
endpoint HTTP en `FinancialMcp.Api`, ver `CreateInvestigation`), el MCP **no escribe
datos financieros**: toda escritura de datos financieros sigue pasando por
`FinancialMcp.Api`. Las tools de investigaciones (`InvestigationTools.cs`) sí
escriben en sus propias tablas de memoria (`Investigations`, `InvestigationReferences`,
`InvestigationFindings`) — nunca en `Transaction`/`BankStatement`/`ClassifiedMovement`.

## Dependencias

* **.NET 9 SDK** (mismo que el resto del repositorio — `TargetFramework` `net9.0`).
* **PostgreSQL** accesible con el connection string configurado (ver más abajo). El
  MCP no necesita ningún otro servicio corriendo — ni `FinancialMcp.Api` ni
  `FinancialSystem.Worker` son requisito.

## Cómo configurar el connection string

Desde el Patch 0062 (PATCH-013), `hosts/FinancialSystem.McpServer/appsettings.json` y
`appsettings.Development.json` ya **no** traen una credencial real -- solo el nombre
lógico vacío, para que el archivo siga documentando qué clave espera la aplicación sin
versionar ningún dato sensible:

```json
"ConnectionStrings": {
  "Postgres": ""
}
```

Si `ConnectionStrings:Postgres` llega vacío o ausente, `AddInfrastructure` (compartida
por los tres hosts -- API, Worker y este servidor MCP) falla rápido al arrancar con un
`InvalidOperationException` explicando cómo configurarlo, en vez de fallar más
adelante con un error críptico de Npgsql. Ver
`docs/Architecture/ConfiguracionCredenciales.md` para el detalle completo (User
Secrets, variables de entorno, por qué ningún host depende de una credencial
embebida) -- acá solo el resumen aplicado a este host:

**Desarrollo, vía User Secrets** (no queda en ningún archivo versionado):

```bash
cd hosts/FinancialSystem.McpServer
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=financialsystem;Username=...;Password=..."
```

**Cualquier entorno, vía variable de entorno estándar de .NET** (`Host.CreateApplicationBuilder`
ya incluye el proveedor correspondiente, sin código adicional):

```bash
export ConnectionStrings__Postgres="Host=mi-host;Port=5432;Database=financialsystem;Username=...;Password=..."
```

## Cómo compilarlo

Desde la raíz del repositorio:

```bash
dotnet build hosts/FinancialSystem.McpServer/FinancialSystem.McpServer.csproj
```

(o `dotnet build FinancialSystem.sln` para compilar todo el repositorio).

## Cómo levantarlo / ejecutarlo manualmente

No hace falta correr migraciones a mano antes de arrancar: `Program.cs` llama a
`DatabaseMigrationExtensions.ApplyMigrationsAsync` al inicio, que aplica cualquier
migración pendiente contra la base configurada y sale con error si Postgres no está
accesible. Con Postgres corriendo y el connection string apuntando a él, alcanza con:

```bash
dotnet run --project hosts/FinancialSystem.McpServer/FinancialSystem.McpServer.csproj
```

Esto compila (si hace falta) y arranca el proceso. También se puede ejecutar el
binario ya compilado directamente:

```bash
dotnet hosts/FinancialSystem.McpServer/bin/Debug/net9.0/FinancialSystem.McpServer.dll
```

El proceso no imprime nada por stdout en uso normal (ver "Qué transporte utiliza" más
abajo) — el log de arranque (incluida la aplicación de migraciones) se escribe por
stderr (`consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace` en
`Program.cs`), así que ver texto en la terminal al arrancar es esperable y no es un
error.

## Qué transporte utiliza

**stdio.** `Program.cs` configura `AddMcpServer().WithStdioServerTransport()` — el
servidor no abre ningún puerto HTTP ni escucha conexiones de red. Está pensado para
que un cliente MCP lo lance como subproceso y le hable por stdin/stdout; el proceso
vive mientras ese cliente lo mantenga abierto (`await host.RunAsync()` bloquea hasta
que el transporte se cierra). Correrlo "suelto" en una terminal (como en el paso
anterior) sirve para verificar que arranca sin errores, pero no hay ninguna
interacción útil por teclado: un cliente MCP real es quien le manda los mensajes
JSON-RPC del protocolo.

## Cómo probar que responde correctamente

La forma más directa es conectarlo a un cliente MCP (ver la sección siguiente) y
llamar a la tool `Ping` — debe devolver exactamente `pong`. `Version` y `Health`
(ambas en `SystemTools.cs`) dan más detalle: `Health` confirma que puede conectarse a
Postgres y qué migración de esquema tiene aplicada.

Sin un cliente de IA a mano, el [MCP Inspector](https://modelcontextprotocol.io/legacy/tools/inspector)
oficial permite conectarse por stdio y listar/invocar tools manualmente:

```bash
npx @modelcontextprotocol/inspector dotnet run --project hosts/FinancialSystem.McpServer/FinancialSystem.McpServer.csproj
```

## Cómo conectarlo desde un cliente MCP

Todos los clientes stdio necesitan, en esencia, el mismo dato: qué comando y qué
argumentos ejecutar. Para este proyecto:

* **Comando:** `dotnet`
* **Argumentos:** `run --project hosts/FinancialSystem.McpServer/FinancialSystem.McpServer.csproj`
* **Directorio de trabajo:** la raíz del repositorio (para que las rutas relativas de
  `appsettings.json` y de `docs/` — usadas por `ProjectTools.cs` — resuelvan bien).

### Claude Code

El repositorio incluye `.mcp.json` en la raíz con esta configuración lista para usar
— Claude Code lo detecta automáticamente al abrir el proyecto, no hace falta ningún
paso manual adicional.

### Claude Desktop

Agregar al `claude_desktop_config.json` del usuario (la ubicación depende del SO —
ver la documentación de Claude Desktop — este archivo vive fuera del repositorio, no
se puede versionar acá):

```json
{
  "mcpServers": {
    "financial-mcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/ruta/absoluta/a/FinancialSystem/hosts/FinancialSystem.McpServer/FinancialSystem.McpServer.csproj"
      ]
    }
  }
}
```

(Con Claude Desktop hace falta ruta absoluta al `.csproj`, a diferencia de
`.mcp.json` de Claude Code, que ya resuelve rutas relativas a la raíz del proyecto
abierto.)

### VS Code y otros clientes MCP

VS Code (extensión de Copilot/MCP) y otros clientes que soporten servidores MCP por
stdio aceptan la misma pareja comando/argumentos de arriba — el archivo o la sección
exacta de configuración donde se declara depende de cada cliente; consultar su propia
documentación.

### Open WebUI

Open WebUI no queda cubierto acá con instrucciones paso a paso: su mecanismo de
integración con servidores MCP no es parte de este repositorio y su configuración
exacta depende de la versión de Open WebUI que se use. El dato reutilizable es el
mismo comando/argumentos de arriba; el resto (cómo Open WebUI específicamente lo
registra) hay que resolverlo contra la documentación de esa herramienta.

## Qué herramientas expone actualmente

Todas de solo lectura salvo las marcadas (**escribe memoria**) — ésas escriben en las
tablas de investigaciones del propio MCP, nunca en datos financieros.

**`FinancialTools.cs`** (preexistente al roadmap de ADR-006):
* `GetMonthlySummary` — resumen financiero de un mes (ingresos, gastos, ahorro, cantidad de movimientos).
* `GetExpensesByCategory` — gastos reales agrupados por categoría en un período.
* `GetMonthlyTrend` — evolución de gastos e ingresos mes a mes.
* `CompareWithPreviousMonth` — compara un mes contra el anterior, con variación por categoría.

**`SystemTools.cs`**:
* `Ping` — verifica que el servidor responde (`pong`).
* `Version` — versión de ensamblado, commit y fecha de build.
* `Health` — conectividad a Postgres, proveedor y última migración aplicada.

**`MovementTools.cs`**:
* `SearchMovements` — busca movimientos (banco y tarjeta, pendientes y clasificados) en un rango.
* `GetMovement` — detalle completo de un movimiento por `SourceEntityType`+`SourceId`.
* `ExplainMovement` — explicación estructurada y estable de un movimiento.
* `ExplainClassification` — por qué un movimiento terminó con su clasificación actual.

**`AuditTools.cs`**:
* `FindSuspiciousMovements` — grupos de movimientos sospechosos detectados por el motor existente.
* `FindMisclassifiedMovements` — movimientos ya clasificados que podrían estar mal clasificados, con motivos.

**`ProjectTools.cs`**:
* `ListArchitectureDocuments` — lista los documentos de `docs/Architecture/`.
* `ReadArchitectureDocument` — contenido crudo de un documento de esa carpeta.
* `SearchDocumentation` — búsqueda literal (sin IA) en toda `docs/`.
* `GetRoadmap` — contenido de `docs/RoadMaps/FinancialMcp-vNext.md`.

**`ConfigurationTools.cs`**:
* `ListFinancialAccounts`, `ListCategories`, `ListCounterparties` — catálogos completos.
* `GetCounterparty` — detalle de una contraparte con sus defaults.
* `SearchCounterparties` — búsqueda por nombre.

**`InvestigationTools.cs`** (memoria — ver ADR-007):
* `CreateInvestigation` (**escribe memoria**) — crea una investigación en estado Open.
* `LinkMovement` (**escribe memoria**) — asocia un movimiento existente a una investigación.
* `AddFinding` (**escribe memoria**) — registra un hallazgo en una investigación.
* `UpdateInvestigationStatus` (**escribe memoria**) — cambia el estado de una investigación.
* `GetInvestigation` — detalle completo de una investigación (datos, referencias, hallazgos).
* `SearchInvestigations` — busca investigaciones por status/tag/texto.

## Qué fases del roadmap ya están implementadas y cuáles no

### ADR-006 (roadmap general de tools)

| Fase | Objetivo | Estado |
|---|---|---|
| Fase 1 — Investigación básica | `Ping`/`Version`/`Health`/`SearchMovements`/`GetMovement`/`ExplainMovement` | ✅ Implementada, más `ExplainClassification` (no estaba en el listado original de la ADR). |
| Fase 1.5 — Conocimiento del proyecto | Que el LLM lea `docs/` sin repetir explicaciones | ✅ Implementada como `ProjectTools.cs` — los nombres de tool difieren de los sugeridos originalmente en la ADR (`SearchDocs`/`ReadAdr`/`ExplainConcept`/`GetArchitecture`); lo implementado es `ListArchitectureDocuments`/`ReadArchitectureDocument`/`SearchDocumentation`/`GetRoadmap`. |
| Fase 2 — Auditoría | Encontrar inconsistencias automáticamente | ⚠️ Parcial: `FindSuspiciousMovements` y `FindMisclassifiedMovements` existen (en `AuditTools.cs`); `FindDuplicates` y `FindUnclassified`, mencionadas en la ADR, no están implementadas. |
| Fase 3 — IA local (Ollama) | Tools puntuales que usen Ollama | ❌ No implementada. No hay ninguna tool del MCP que use Ollama/OpenAI hoy (la configuración `Ollama`/`OpenAI` en `appsettings.json` la usa `FinancialSystem.Worker`, no el MCP). |
| Fase 4 — Memoria | Investigaciones persistentes | Reemplazada por su propia ADR — ver ADR-007 abajo. |

`ConfigurationTools.cs` no está listada explícitamente en ninguna fase de ADR-006 —
se agregó como complemento natural de Fase 1 (catálogos de configuración) sin encajar
literalmente en el texto original de la ADR.

### ADR-007 (memoria/investigaciones)

| Fase | Objetivo | Estado |
|---|---|---|
| Fase 1 — Sin memoria | Tools de solo lectura ya construidas | ✅ (es la Fase 1/1.5/2 de ADR-006 de arriba). |
| Fase 2 — Persistencia de investigaciones | Tablas `Investigation`/`InvestigationReference`/`InvestigationFinding` | ✅ Implementada. |
| Fase 3 — Tools de investigaciones | Crear/actualizar/consultar investigaciones | ✅ Implementada (`InvestigationTools.cs`, ver arriba). |
| Fase 4 — Integración con Ollama | Ollama como contexto adicional sobre lo que ya devuelven las tools | ❌ No implementada. |
| Fase 5 — Auditorías inteligentes | Nuevas señales de auditoría basadas en el historial de investigaciones | ❌ No implementada. |
