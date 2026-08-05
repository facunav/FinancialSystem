# ADR-005 — `ImportBatch` como mecanismo estándar de trazabilidad de importación

**Estado:** Aceptado e implementado. ~~(planificación; entidad no implementada — ver `docs/Epics/EpicaI-Importacion.md`)~~ **Actualización (PATCH-030):** `ImportBatch`/`ImportBatchLine` existen, están migradas y en uso — las 3 fuentes (banco, tarjeta/catch-all, Excel legacy vía el mismo pipeline catch-all) persisten un `ImportBatch` por corrida a través de `FileImportRouter` (ver "Decisión tomada" y "Consecuencias" para el detalle real). El texto original de esta sección queda tachado como referencia histórica de cuándo se escribió esta ADR, no como estado vigente.

## Contexto

Las 3 fuentes de importación (banco, tarjeta, Excel legacy) generan información valiosa en cada corrida — cuántas filas se insertaron, cuántas eran duplicadas, cuántas fallaron y por qué. Hoy esa información se calcula de maneras distintas según la fuente: banco y Excel la calculan como `ImportResult` (variable local con `Inserted`/`Duplicates`/`ParseErrors`/`SkippedRows`/`Diagnostics`) y la pierden apenas termina el proceso; tarjeta directamente descarta el detalle (`FileParseResult.SkippedRows`/`Diagnostics` se completan con `0`/`[]` de forma hardcodeada, ver `docs/Epics/EpicaI-Importacion.md` §1.2).

**Actualización (PATCH-030):** el problema de tarjeta descrito en el párrafo anterior ya no existe — `PdfStatementParserBase.ParseLines` devuelve `SkippedLines`/`Diagnostics` reales, que hoy llegan completos a `FileParseResult` (verificado contra el código, ver también `docs/RoadMaps/FinancialMcp-vNext.md` §6, ítem 2). El párrafo se conserva sin editar porque describe correctamente el problema que motivó esta decisión en su momento.

## Problema

Sin un registro persistido de cada corrida, nadie puede responder después "¿cuántas líneas se descartaron en la última importación de tarjeta, y por qué?" — la única fuente es el log del proceso, que además filtra buena parte de esta información por nivel (`LogTrace`). Cada fuente resolviendo esto por su cuenta (o no resolviéndolo) también deja la puerta abierta a que una cuarta fuente futura repita el mismo error de diseño.

## Decisión tomada

Se introduce `ImportBatch` como entidad única y compartida por las 3 fuentes actuales (y cualquier fuente futura): un registro por corrida de importación con archivo, hash de contenido, handler que la procesó, timestamp, y contadores de insertados/duplicados/fallidos, más el detalle de líneas ignoradas asociado. El pipeline de banco (`BbvaBankStatementImporter.PersistAsync`) ya calcula toda la información necesaria en memoria — `ImportBatch` la persiste en vez de descartarla; no se inventa un cálculo nuevo, se le da destino final al que ya existe.

**Actualización (PATCH-030) — cómo quedó implementado realmente:** `ImportBatch` (`src/FinancialSystem.Domain/Entities/ImportBatch.cs`) tiene hoy más campos que los descriptos arriba, todos agregados en patches posteriores a esta decisión sin cambiarla: `Duration` (derivado de `StartedAtUtc`/`CompletedAtUtc`), `FileSizeBytes`, `ParserUsed` (parser específico, distinto de `HandlerName`), `Outcome` (`ImportBatchOutcome`), y `ConsistencyVerified`/`ConsistencyIssues` (verificación de integridad post-importación). La persistencia, en vez de quedar a cargo de cada importador por separado, se centralizó en `FileImportRouter` — un único punto que persiste el `ImportBatch` (+ sus `ImportBatchLine`) de cualquier fuente, incluidas las corridas rechazadas o fallidas antes de llegar a un handler. Es una evolución del "quién persiste", no un cambio a la decisión de que `ImportBatch` es la entidad única y compartida.

## Consecuencias

* Las 3 fuentes quedan obligadas a producir el mismo contrato de salida (`ImportBatch`) — cualquier fuente nueva que se agregue en el futuro hereda el mismo patrón por diseño, no por convención informal.
* Habilita la pantalla "Importaciones" (Épica K, pantalla 4, ver `docs/UX/ClassificationUX.md`) y el endpoint `GET /api/imports/history` (Épica I, PR I5) — sin `ImportBatch` no hay datos que mostrar ahí. **Ambos ya están en uso** (`imports.html`, `GET /api/imports/history` en `ImportBatchEndpoints`).
* El orden de implementación quedó fijado en `docs/Epics/EpicaI-Importacion.md` §4: `ImportBatch` (PR I2) no depende de nada, pero el resto de la observabilidad de importación (PRs I4-I7) sí depende de que exista primero. **Estado real (PATCH-030):** PR I2 e I4 completados (entidad, persistencia centralizada, diagnóstico de líneas). El ítem I7 (ruteo Visa/Mastercard, ver `docs/RoadMaps/FinancialMcp-vNext.md` §6) sigue pendiente — es un problema de selección de parser, no de trazabilidad, y no bloquea nada de lo que decide esta ADR.
