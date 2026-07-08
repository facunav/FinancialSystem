# ADR-005 — `ImportBatch` como mecanismo estándar de trazabilidad de importación

**Estado:** Aceptado (planificación; entidad no implementada — ver `docs/Epics/EpicaI-Importacion.md`).

## Contexto

Las 3 fuentes de importación (banco, tarjeta, Excel legacy) generan información valiosa en cada corrida — cuántas filas se insertaron, cuántas eran duplicadas, cuántas fallaron y por qué. Hoy esa información se calcula de maneras distintas según la fuente: banco y Excel la calculan como `ImportResult` (variable local con `Inserted`/`Duplicates`/`ParseErrors`/`SkippedRows`/`Diagnostics`) y la pierden apenas termina el proceso; tarjeta directamente descarta el detalle (`FileParseResult.SkippedRows`/`Diagnostics` se completan con `0`/`[]` de forma hardcodeada, ver `docs/Epics/EpicaI-Importacion.md` §1.2).

## Problema

Sin un registro persistido de cada corrida, nadie puede responder después "¿cuántas líneas se descartaron en la última importación de tarjeta, y por qué?" — la única fuente es el log del proceso, que además filtra buena parte de esta información por nivel (`LogTrace`). Cada fuente resolviendo esto por su cuenta (o no resolviéndolo) también deja la puerta abierta a que una cuarta fuente futura repita el mismo error de diseño.

## Decisión tomada

Se introduce `ImportBatch` como entidad única y compartida por las 3 fuentes actuales (y cualquier fuente futura): un registro por corrida de importación con archivo, hash de contenido, handler que la procesó, timestamp, y contadores de insertados/duplicados/fallidos, más el detalle de líneas ignoradas asociado. El pipeline de banco (`BbvaBankStatementImporter.PersistAsync`) ya calcula toda la información necesaria en memoria — `ImportBatch` la persiste en vez de descartarla; no se inventa un cálculo nuevo, se le da destino final al que ya existe.

## Consecuencias

* Las 3 fuentes quedan obligadas a producir el mismo contrato de salida (`ImportBatch`) — cualquier fuente nueva que se agregue en el futuro hereda el mismo patrón por diseño, no por convención informal.
* Habilita la pantalla "Importaciones" (Épica K, pantalla 4, ver `docs/UX/ClassificationUX.md`) y el endpoint `GET /api/imports/history` (Épica I, PR I5) — sin `ImportBatch` no hay datos que mostrar ahí.
* El orden de implementación queda fijado en `docs/Epics/EpicaI-Importacion.md` §4: `ImportBatch` (PR I2) no depende de nada, pero el resto de la observabilidad de importación (PRs I4-I7) sí depende de que exista primero.
