# Épica O — Importación Manual e Historial

> Estado: 📋 planificada, no implementada. Documento de diseño previo a implementación — registra la decisión de arquitectura antes de escribir el primer PR, siguiendo el mismo protocolo que `docs/Epics/EpicaI-Importacion.md`. Contiene una decisión explícitamente **no tomada todavía** (sección 7) que debe resolverse antes de PR-O1.

## 1. Cómo funciona hoy la importación (verificado contra el código)

Un `FileSystemWatcher` dentro de `ImportsFolderWatcherHostedService` (`hosts/FinancialSystem.Worker/Services/`) observa una carpeta configurada (`FileIngestionOptions.ImportsPath`). Ante un evento de archivo nuevo/modificado, aplica debounce (`DebounceMilliseconds`) y espera a que el archivo esté completamente escrito (`WaitForFileReadyAsync`, hasta 20 reintentos abriendo en modo exclusivo). Hecho esto, llama a una sola línea:

```csharp
await importRouter.RouteAsync(filePath, ct);
```

`IFileImportRouter` (`FileImportRouter`, registrado vía `AddInfrastructure`) itera los `IFileImportHandler` registrados en DI **en orden de registro** y delega al primero cuyo `CanHandle(filePath)` sea verdadero:

1. `BbvaBankStatementImportHandler` — `.xls`, nombre matchea patrones de Caja de Ahorro.
2. `BbvaDebitCardEnrichmentHandler` — `.xlsx`, nombre matchea patrones de Tarjeta de Débito.
3. `TransactionImportHandler` — catch-all (PDF/CSV/XLSX/XLS restantes), delega a `IImportFileSink`/`ImportFileProcessingSink`, que a su vez usa `FileParserFactory` para elegir parser (por extensión en CSV/Excel, por **contenido** — `IStatementParser.CanHandle(lines)` — en PDF, para distinguir Visa de Mastercard sin depender del nombre del archivo).

Después de que el handler devuelve un `ImportRunResult`, el router persiste un `ImportBatch` (+ `ImportBatchLine` por diagnóstico) — esto corre siempre, sin importar el handler, y es la fuente de `GET /api/imports/history` (`imports.html`).

**Detección de cuenta financiera: no existe hoy.** Ningún parser ni importer asigna `FinancialAccountId` — queda `null` siempre. Hay una señal parcial sin usar: `BbvaBankStatementParser` ya extrae el número de cuenta del título del archivo hacia `BankStatement.AccountNumber`, y `FinancialAccount.AccountNumber` ya existe como campo administrable (`accounts.html`) — pero nada cruza ambos valores. Ver sección 6.

**Manejo de duplicados**, tres niveles de robustez distintos: `BbvaBankStatementImporter` y `BbvaDebitCardEnrichmentHandler` consultan el estado existente en la base antes de escribir (no pueden chocar contra un constraint). `ImportFileProcessingSink` (catch-all Visa/Mastercard/CSV) solo dedupea dentro del archivo con un `HashSet` en memoria — una reimportación puede chocar contra el índice único de `Transactions.ExternalId` en `SaveChangesAsync`, sin try/catch alrededor. Gap preexistente, no introducido por esta épica — no se corrige acá (fuera de alcance de este documento).

## 2. El Worker: responsabilidad real

**Es, literal y únicamente, un disparador.** No orquesta el proceso de negocio, no contiene lógica de negocio, no valida contenido de archivos, no decide bancos ni cuentas. Todo el código propio de `ImportsFolderWatcherHostedService` es manejo de E/S de sistema de archivos (watcher, debounce, espera de archivo listo, cancelación) — cero conocimiento de parsers, bancos o movimientos.

**Cómo interactúa con `IFileImportRouter`:** lo recibe inyectado por DI y le hace una única llamada, `RouteAsync(filePath, ct)`, exactamente la misma que cualquier otro caller (por ejemplo un endpoint HTTP) podría hacer. El Worker no conoce `IFileImportHandler`, parsers, ni entidades de dominio.

**Por qué está desacoplado de la lógica de negocio:** `IFileImportRouter` vive en `FinancialSystem.Application`/`Infrastructure`, no en `FinancialSystem.Worker`. Los tres hosts del proyecto (`FinancialSystem.Worker`, `FinancialMcp.Api`, `FinancialSystem.McpServer`) llaman a `AddApplication()` + `AddInfrastructure()` de forma idéntica (verificado leyendo los tres `Program.cs`) — `IFileImportRouter` y todos los handlers/parsers/importers ya están disponibles en el contenedor de DI de la API **hoy**, sin escribir código nuevo. No hay nada que "desacoplar del Worker": nunca estuvo acoplado.

**Por qué no conviene eliminarlo sin analizar el impacto:** es el único mecanismo que soporta importación sin intervención humana — corre continuamente, es la base natural para automatizaciones futuras (sincronización bancaria, carpeta monitoreada, Dropbox/Drive/OneDrive — ver sección 8). Eliminarlo cierra esa puerta sin necesidad: no compite con la importación manual, la precede en la misma tubería.

## 3. Comparación de arquitecturas alternativas

| | A: Solo UI | B: Solo Worker | C: Ambos, mismo motor | D (recomendada) |
|---|---|---|---|---|
| Automatización sin usuario presente | ✗ | ✓ | ✓ | ✓ |
| Import manual con feedback inmediato | ✓ | ✗ | ✓ | ✓ |
| Riesgo de duplicar lógica de negocio | — | — | Ninguno (`IFileImportRouter` ya es compartido) | Ninguno |
| Costo de implementación | Reescribir un disparador en background el día que haga falta automatización | No resuelve el pedido de este documento | Bajo — falta solo el punto de entrada desde UI | Bajo, con una decisión de diseño pendiente (sección 7) |
| Compatible con evolución futura (sección 8) | Requiere reconstruir esa pieza | ✓ pero sin UI | ✓ | ✓ |

**Riesgos por alternativa:**
- **A (eliminar el Worker):** pierde toda automatización sin intervención humana. Ningún costo real de mantenimiento hoy justifica esto — el Worker son ~180 líneas, ya aisladas, sin lógica de negocio.
- **B (solo Worker):** no resuelve el objetivo del negocio (importación manual con feedback).
- **C (ambos, mismo motor):** riesgo bajo — es aditivo sobre una arquitectura que ya separa disparador de motor. El único riesgo real es de implementación, no de diseño: decidir bien el punto de entrada desde la UI (sección 7).
- **D:** es la Opción C con la precisión de que la reutilización de `IFileImportRouter` no requiere ningún refactor — ya es un servicio compartido.

**Recomendación:** Opción D (= C con la decisión de la sección 7 resuelta antes de PR-O1). Mantener el Worker, agregar un punto de entrada manual desde la UI que reutilice exactamente la misma infraestructura, sin modificar handlers, parsers ni importers existentes.

## 4. Arquitectura objetivo

```
                          ┌───────────────────────────┐
                          │   IFileImportRouter          │  ← YA existe, compartido,
                          │   (Application/Infrastructure) │    sin cambios
                          └──────────────┬────────────┘
                                         │ RouteAsync(filePath, ct)
              ┌───────────────────────────┼───────────────────────────┐
              │                                                        │
   ┌──────────▼──────────────┐                            ┌───────────▼────────────┐
   │ Worker (sin cambios)       │                            │ API                       │
   │ FileSystemWatcher            │                            │ Punto de entrada nuevo    │
   │ → RouteAsync                 │                            │ (ver sección 7: A vs B)   │
   └────────────────────────────┘                            └───────────┬────────────┘
                                                                          │
              ┌─────────────────────────────────┬─────────────────────────┘
              │                                  │
   ┌──────────▼──────────┐          ┌────────────▼─────────────┐
   │ Parsers                 │          │ Importers                    │
   │ (por banco/formato,       │          │ (idempotencia, persistencia)  │
   │  sin cambios)              │          │  sin cambios)                  │
   └──────────────────────┘          └────────────┬─────────────┘
                                                    │
                                       ┌────────────▼─────────────┐
                                       │ ImportBatch / ImportBatchLine │
                                       │ (historial — sin cambios,      │
                                       │  ya expuesto en imports.html)  │
                                       └───────────────────────────┘
                                                    │
                                       ┌────────────▼─────────────┐
                                       │ UI: botón "Importar"          │
                                       │ selector de archivo             │
                                       │ + resultado + historial          │
                                       └───────────────────────────┘
```

Ningún componente de la mitad izquierda (Worker) ni del centro (`IFileImportRouter`, Parsers, Importers, `ImportBatch`) cambia. Lo nuevo es exclusivamente el punto de entrada de la derecha (API + UI).

## 5. Importación manual — comportamiento esperado (sin definir implementación)

Desde la UI, el usuario podrá:

1. **Seleccionar un archivo** desde su equipo (no depende de que el archivo ya esté en el servidor).
2. **Importarlo** con una acción explícita (botón "Importar").
3. **Ver el resultado** de esa corrida: éxito, cantidad de movimientos, o error — mismo tipo de información que ya expone `ImportBatch` hoy, sin inventar un modelo nuevo.
4. **Conocer los errores reales**, no genéricos — mismo criterio ya aplicado en `imports.html` (PR-P3): el motivo real persistido en `ImportBatchLine.Reason`, no un mensaje inventado por la UI.
5. **Revisar el historial** de todas las corridas, manuales y automáticas, en el mismo lugar — `imports.html` ya lista `ImportBatch` sin distinguir su origen; una corrida manual debería aparecer ahí sin necesidad de una pantalla separada.

No se define acá el contrato HTTP, el manejo del archivo en el servidor, ni el detalle de la UI — eso corresponde a los PRs de la sección 9, una vez resuelta la decisión de la sección 7.

## 6. Resolución de la cuenta financiera

Estrategia en 3 pasos, extensible a cualquier banco/cuenta sin hardcodear nombres:

1. **Detección automática cuando la información ya está disponible.** `BbvaBankStatementParser` ya extrae `AccountNumber` del título del archivo hacia `BankStatement.AccountNumber`. `FinancialAccount.AccountNumber` ya existe como campo administrado por el usuario (`accounts.html`). El primer paso es cruzar ambos valores — dato que hoy se calcula y se descarta, no una extracción nueva.
2. **Si hay exactamente un `FinancialAccount` activo con ese `AccountNumber`, asignarlo automáticamente.** Mismo criterio de "match único → auto-asignar, ambiguo o sin dato → no tocar" ya validado con datos reales en el enriquecimiento de Tarjeta de Débito (PR1-PR3, `docs/patch/enriquecimiento-tarjeta-debito.md`) — consistencia de criterio entre ambas features, no una regla nueva.
3. **Si no puede determinarse de forma unívoca, la UI ofrece elegir.** Solo para lo que quedó sin resolver en el paso 2 — nunca bloqueante antes de importar, nunca para lo que ya se resolvió solo. La lista de cuentas la sirve `GET /api/accounts`, ya existente — ninguna cuenta ni banco queda hardcodeado en código.

Para fuentes sin número de cuenta en el archivo (Visa/Mastercard/Tarjeta de Débito — son tarjetas, no cuentas bancarias) el paso 1 no aplica y se pasa directo al paso 3, igual que hoy.

## 7. Pendiente de decisión arquitectónica

**Esta decisión no está tomada.** Debe resolverse antes de PR-O1 — las dos alternativas son válidas y el resto del documento (secciones 4-6, 9) es compatible con cualquiera de las dos.

### Alternativa A — la UI llama directamente al motor de importación

```
UI → API → IFileImportRouter → Parsers → Persistencia
```

El endpoint de la API recibe el archivo, lo persiste temporalmente si hace falta, y llama a `IFileImportRouter.RouteAsync` en el mismo request (o en un `Task` disparado desde ahí).

- **Ventajas:** más simple de implementar y de razonar — un solo proceso involucrado, sin coordinación entre servicios. Resultado disponible inmediatamente para responder al request HTTP.
- **Desventajas:** un archivo grande o un parser lento bloquea (o corre en background dentro de) el proceso web, compitiendo por recursos con el tráfico normal de la API. No hay un mecanismo de reintento si el proceso de la API se reinicia a mitad de un import.

### Alternativa B — la UI crea un `ImportJob`, el Worker lo procesa

```
UI → ImportJob → Worker → IFileImportRouter → Parsers → Persistencia
```

La API solo persiste un registro (`ImportJob`, entidad nueva) con el archivo recibido y estado `Pending`. El Worker (o un segundo watcher sobre esa tabla, en vez de sobre una carpeta) toma el job, lo procesa con exactamente la misma `IFileImportRouter`, y actualiza el estado.

- **Ventajas:** el proceso web nunca hace trabajo pesado de importación — separación de responsabilidades más estricta entre "recibir" y "procesar". Reutiliza el mismo proceso que ya corre 24/7 y que ya está pensado para trabajo en background (alineado con la visión de sincronización bancaria de la sección 8).
- **Desventajas:** requiere una entidad nueva (`ImportJob`) y un mecanismo de polling o notificación entre API y Worker — más piezas que coordinar. El usuario no ve el resultado en el mismo request; la UI necesita algún mecanismo de espera/actualización (polling del estado del job, como mínimo).

Ninguna de las dos alternativas obliga a tocar `IFileImportRouter`, los parsers, ni los importers — la diferencia es exclusivamente **quién** y **cuándo** llama a `RouteAsync`.

## 8. Evolución futura

La arquitectura de la sección 4 (disparador desacoplado del motor) es la misma que habilitaría, sin rediseñar nada de lo existente:

- **Importaciones automáticas adicionales** — cualquier nuevo tipo de archivo/banco es un `IFileImportHandler` nuevo, registrado en DI, sin tocar el router.
- **Sincronización bancaria** (API del banco en vez de archivo) — un disparador nuevo (análogo al Worker o a la Alternativa B) que obtenga los datos y llame al mismo `IFileImportRouter`, o a un `IFileImportHandler` que reciba el payload ya en memoria en vez de un `filePath`.
- **Dropbox / Google Drive / OneDrive** — un disparador que sincronice la carpeta remota a la carpeta local ya observada (reutiliza el Worker sin cambios) o que dispare `RouteAsync` directamente sobre el archivo descargado.
- **MCP** — `FinancialSystem.McpServer` ya llama a `AddInfrastructure()`; una herramienta MCP que reciba un archivo podría llamar a `IFileImportRouter` de la misma forma que la UI.
- **APIs externas** — mismo patrón: el disparador cambia, el motor (`IFileImportRouter` → Parsers → Importers → `ImportBatch`) no.

## 9. Plan de implementación — PRs pequeños

| PR | Objetivo único | Depende de |
|---|---|---|
| **O1** | Resolver la decisión de la sección 7 (Alternativa A vs. B) y documentar la elección acá mismo, con su justificación. | — |
| **O2** | Endpoint de importación manual en la API, siguiendo la alternativa elegida en O1. Reutiliza `IFileImportRouter` sin modificarlo. Probable por API antes de tocar UI. | O1 |
| **O3** | Botón "Importar" en `imports.html`: selector de archivo, sube al endpoint de O2, muestra el resultado de la corrida y refresca el historial. | O2 |
| **O4** | Detección automática de cuenta financiera por `AccountNumber` (paso 1-2 de la sección 6), aplicada en `BbvaBankStatementImporter`. Sin UI nueva — se refleja en `movements.html`, que ya sabe mostrar la cuenta asignada. | — (independiente de O1-O3) |
| **O5** | Selector de cuenta en la UI para lo que quedó sin auto-asignar tras O4 (paso 3 de la sección 6). | O3, O4 |
| **O6** | Reprocesamiento: reintentar una corrida fallida del historial sin volver a subir el archivo (reutiliza `ImportBatch.SourceFile`/`ContentHash` ya persistidos). | O2 |

O4 no depende de O1-O3 y puede implementarse en paralelo — es una mejora sobre el pipeline existente, no sobre el punto de entrada nuevo.
