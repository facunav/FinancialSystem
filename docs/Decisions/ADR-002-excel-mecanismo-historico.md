# ADR-002 — Excel como mecanismo histórico de migración, no núcleo del producto

**Estado:** Aceptado, parcialmente superado (ver nota PR-L4 más abajo).

## Nota (PR-L4, Épica K)

El diagnóstico de este ADR (Excel es histórico, no un flujo activo) se mantiene vigente y fue, de hecho, el argumento para el siguiente paso: en PR-L4 se eliminó por completo el backend de matching contra `LegacyImportedExpense` (`IMatchScorer`, las 4 `IMatchingRule`, `ConfirmMatchCommand`/`DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`) y, con él, `group-reconciliation.html` (ver Épica K, limpieza de soporte Legacy — PR-L1 a PR-L4). La decisión de la sección "Decisión tomada" de mantener esa pantalla como secundaria "para la ventana de tiempo en que todavía queda historial Excel por conciliar" queda superada: esa ventana se cerró sin que nadie retomara la conciliación manual, y el mecanismo completo (UI + comandos) se retiró en vez de mantenerse en desuso. La entidad `LegacyImportedExpense` todavía existe en base — su remoción está planificada para una PR posterior (PR-L5), informada por un conteo real de datos (PR-L4.5). Este ADR se conserva sin reescribir por su valor histórico (por qué se tomó esa decisión en su momento); no reabrir su "Decisión tomada" para reflejar el estado actual.

## Contexto

El sistema tiene tres fuentes de datos: extractos bancarios (XLS), resúmenes de tarjeta (PDF) y un importador de Excel legacy (`LegacyImportedExpense`, `ExcelLegacyExpenseImporter`). Este último existe porque, antes de este sistema, los movimientos se registraban manualmente en una planilla Excel — es el puente para no perder ese historial. `group-reconciliation.html` fue diseñada con Excel como columna de igual jerarquía que Banco/Tarjeta ("Banco / Tarjeta" vs. "Excel / Manual", con balance bar comparando ambos).

## Problema

Tratar a Excel como una fuente de datos permanente y de igual jerarquía que banco/tarjeta genera dos problemas: (1) la UI principal (`group-reconciliation.html`) queda centrada en un flujo — conciliar Excel contra banco — que solo tiene sentido durante la migración del historial pre-sistema, no como uso diario continuo; (2) planificar nuevas épocas (Épica K, nueva UX) asumiendo que Excel sigue siendo relevante a futuro lleva a sobre-invertir en ese flujo en vez de en clasificación directa de banco/tarjeta, que es el flujo real de uso continuo.

## Decisión tomada

Excel/`LegacyImportedExpense` se trata como mecanismo **histórico** de migración de datos pre-existentes, no como una fuente de datos activa del producto en régimen. Consecuencias de diseño:
* La nueva UX de clasificación (Épica K, ver `docs/UX/ClassificationUX.md`) tiene como flujo primario clasificar movimientos de banco/tarjeta directamente — no depende de que exista un candidato Excel para conciliar.
* `group-reconciliation.html` (y sus comandos `ConfirmMatchCommand`/`DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`) se mantiene como pantalla secundaria, para la ventana de tiempo en que todavía queda historial Excel por conciliar — no se elimina, pero tampoco se sigue invirtiendo en ella como pantalla principal.
* No se agregan nuevas fuentes de importación tipo "planilla manual" a futuro — el patrón objetivo para fuentes nuevas es el mismo que banco/tarjeta (parser + `ImportBatch` + `ExternalId`, ver ADR-005), no una hoja editable a mano.

## Consecuencias

* La Épica K puede diseñar "Movimientos pendientes" y "Clasificación" sin modelar dependencia alguna con `LegacyImportedExpense`.
* El trabajo de robustecer importación (Épica I) prioriza banco y tarjeta; Excel legacy no recibe la misma inversión porque su volumen de uso decrece con el tiempo por diseño.
* Cuando el historial pre-sistema esté completamente migrado y conciliado, `group-reconciliation.html` puede quedar en desuso — esa decisión no se toma en este ADR, pero queda habilitada por esta separación de responsabilidades.
