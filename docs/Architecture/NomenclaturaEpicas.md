# Nomenclatura de Épicas — guía de referencia

> Documento vivo, no de diseño. Resuelve una ambigüedad puramente documental: distintos documentos del repositorio usan la letra de "Épica" con esquemas de numeración distintos, generados en rondas de trabajo separadas en el tiempo, sin que ninguna ronda posterior haya sabido de la anterior al elegir la letra siguiente. Ningún código, comportamiento ni decisión de producto cambia por este documento — es exclusivamente un mapa de "qué letra significa qué, en qué documento, y por qué a veces la misma letra aparece dos veces con significados distintos".

---

## 1. El esquema oficial vigente

**`docs/RoadMaps/FinancialMcp-vNext.md` es la única fuente de verdad para el significado de una letra de Épica hoy.** Si cualquier otro documento de este repositorio (incluido este mismo) contradice a `vNext.md` sobre qué es la Épica X, `vNext.md` gana — ver su propio encabezado ("es la fuente de verdad del proyecto").

Su roadmap por épicas (sección 7) numera **I → O**, continuando explícitamente la numeración de letra que ya venía usando **Review & Classification Engine v2 (Épicas A → D)**, hoy completo y archivado (`docs/Archive/ReviewClassificationEnginev2ADR.md`). No hay una "Épica E" a "Épica H" documentada en ningún lado — el salto de D a I no está explicado en ningún documento existente y no se investiga acá (no hay código ni entidad afectada por ese salto; es puramente una discontinuidad de numeración histórica).

| Letra | Nombre | Estado (ver `vNext.md` §7 para el detalle real) |
|---|---|---|
| A–D | Review & Classification Engine v2 | Archivada — completa |
| I | Confiabilidad de importación | Parcial |
| J | Modelo de Cuentas Financieras | Terminada (núcleo) |
| K | Nueva UX de clasificación | Completada |
| L | Visibilidad de cobertura | Terminada |
| M | Cuentas de inversión | Planificada — no iniciada |
| N | Simplificación del formulario de clasificación | Planificada — no iniciada |
| O | Importación Manual e Historial | Terminada |

---

## 2. Épicas construidas fuera del roadmap I–O, sin letra

**Centro de Auditoría** y **Planificación Mensual** son módulos completos, terminados, que el proyecto construyó sin que estuvieran planificados en `vNext.md` — su propio §1 lo dice explícitamente desde PATCH-029. Ninguno de los dos tiene (ni necesita) una letra de Épica: no forman parte de la secuencia I–O y no hay ninguna razón para forzarlos dentro de ella. Se documentan por nombre, no por letra:

* Centro de Auditoría — `docs/Architecture/CentroDeAuditoria.md`.
* Planificación Mensual — `docs/Epics/Epica-PlanificacionMensual.md`, que además trae su propia nota explícita sobre esta misma desincronización de letras (ver sección 5 de este documento).

---

## 3. La serie S / U / UI — documentada, pero fuera de `vNext.md`

`docs/PROJECT_STATUS.md` (sección 5, tabla de Épicas) lista tres épicas más que **no aparecen en ningún lado de `vNext.md`**:

* **S — Motor de sugerencias** (terminada) — sin documento de épica formal; su serie de PRs (`PR-S1` a `PR-S14`, con revisiones intermedias `S1.5`/`S5.1`/`S9.5`) vive documentada en la serie `PRS1`/`PRS6`/`PRS8`/`PRS11`/`PRS12`, hoy archivada (`docs/Archive/`, PATCH-026).
* **U — UX de un clic** (terminada, mayormente) — serie `PR-U1` a `PR-U3` (con más ítems propuestos, `PR-U4` en adelante, en `docs/Architecture/PRU1analisisexperienciaclasificacion.md`, todavía activo pero clasificado como histórico en `PROJECT_STATUS.md` §7 — "tiene valor de referencia, no de verdad activa").
* **UI — Arquitectura de UI compartida** (pendiente) — sin documento de épica formal; su análisis vive en `docs/Architecture/PRUI1analisisarquitecturaui.md`, marcado en `PROJECT_STATUS.md` §7 como "plan vigente, no ejecutado".

**Por qué no colisionan con I–O:** ninguna letra de S/U/UI coincide con I–O — son simplemente épicas adicionales, documentadas de forma menos formal (sin su propio archivo `EpicaX-*.md` en `docs/Epics/`), que `vNext.md` nunca incorporó a su tabla. `docs/Epics/Epica-PlanificacionMensual.md` ya señala esta misma desincronización en su propia "Nota sobre numeración" — este documento la retoma y la completa con el resto de los casos (M y L, abajo), no la contradice.

---

## 4. Colisión real #1 — la letra "M"

**Dos documentos usan "Épica M" para cosas completamente distintas:**

1. `vNext.md` §7 — **Épica M = Cuentas de inversión** (`FinancialAccount.Type=Investment`, todavía sin iniciar).
2. `docs/Architecture/EpicaMImportWorkflow.md` — hasta este patch, titulado "Épica M — Mejoras al flujo de importación" (historias `M1` a `M9`, mayormente implementadas: `M2`/`M5` ya en producción, ver `docs/Architecture/EstadoMVP.md`).

Esta colisión ya estaba señalada — sin resolver — en `docs/Archive/AuditoriaMVP.md` §"Paso 5" ("Colisión de nombre 'Épica M'... Sigue sin resolverse") desde antes de esta ronda de patches.

**Resolución (este patch):** `docs/Architecture/EpicaMImportWorkflow.md` deja de titularse "Épica M" — pasa a llamarse **"Mejoras al flujo de importación (historias M1–M9)"**, sin reclamar el nombre "Épica" en absoluto. Las historias `M1`–`M9` **no se renumeran**: son un identificador propio de ese documento, no de la Épica M de `vNext.md`, y ya están referenciadas por número tal cual en `docs/Architecture/EstadoMVP.md`, `docs/PROJECT_STATUS.md` y en comentarios de código (`BankStatement.cs`, `FinancialAccount.cs`, que citan la ruta del archivo, no su título — el archivo **no se movió ni se renombró**, así que esas referencias siguen siendo válidas). "M2"/"M5" siguen significando exactamente lo mismo que significaban antes de este patch.

**Regla práctica para no confundirse:** si una referencia dice "Épica M" a secas, es la de inversión (`vNext.md`). Si dice "M2", "M5", "historia M9" o cita el archivo `EpicaMImportWorkflow.md`, es una historia del flujo de importación — nunca inversión.

---

## 5. Colisión real #2 — la letra "L"

**Tres usos distintos de "L" conviven en el repositorio, con un origen histórico verificable:**

1. `vNext.md` §7 — **Épica L = Visibilidad de cobertura** (endpoint + indicador de `dashboard.html`, terminada). Este es el significado vigente.
2. `docs/UX/ClassificationUX.md` y la propia intro de `vNext.md` — **"PR-L1" a "PR-L5"** (más `PR-L4.5`): la serie de PRs que retiró por completo `group-reconciliation.html`, el motor de matching Legacy (`IMatchScorer`, 4 `IMatchingRule`) y la entidad `LegacyImportedExpense`. Estos PRs pertenecen organizativamente a la **Épica K** (Nueva UX de clasificación) — `vNext.md` es explícito: *"Épica K... completada. PR-L1 a PR-L5 retiraron..."*. La letra "L" acá es un prefijo de lote de PRs, no una letra de épica.
3. `docs/Architecture/PRU1analisisexperienciaclasificacion.md` §12 — usa literalmente **"Épica L"** para referirse a ese mismo retiro del backend Legacy (*"el backend de matching Legacy retirado en Épica L"*, *"eliminada por completo en Épica L, con migración incluida"*). Este documento es anterior a que `vNext.md` fijara su numeración I–O actual, y quedó clasificado como histórico en `PROJECT_STATUS.md` §7 ("tiene valor de referencia, no de verdad activa") — no se edita acá (sería reescribir una fuente histórica), pero su uso de "Épica L" **no debe leerse como el significado vigente**.

**Regla práctica:** el único significado vigente de "Épica L" es Visibilidad de cobertura. Cualquier mención de "PR-L" (con guion, numerado) o de "Épica L" dentro de `PRU1analisisexperienciaclasificacion.md` se refiere al retiro del matching Legacy, que hoy se entiende como parte de la Épica K.

---

## 6. Prefijos de PR que no son letras de Épica

Además de los casos de arriba, el repositorio usa varios prefijos `PR-X` que **nunca tuvieron una "Épica X" propia** — son lotes de trabajo puntuales, documentados en el lugar donde se hicieron, sin un documento de épica formal:

* **`PR-P1`/`PR-P3`** — ajustes de UX puntuales (`movements.html`: cuenta financiera de solo lectura, alta rápida de contraparte; `imports.html`: motivos de error reales). Documentados en `docs/UX/ClassificationUX.md` y `docs/Epics/EpicaO-ImportacionManual.md` respectivamente. No existe ninguna "Épica P".
* **`PR-Nav`** — ajuste de navegación puntual, mencionado solo en el ya archivado `docs/Archive/AuditoriaMVP.md`. No existe ninguna "Épica Nav".

No asumir que un prefijo `PR-X` implica una épica `X` — solo `I`, `J`, `K`, `L`, `M`, `N`, `O` (más las históricas `A`–`D` y las adicionales `S`/`U`/`UI`) son letras de épica reales, listadas en las secciones 1 y 3 de este documento.

---

## 7. Tabla resumen — qué esquema usar para leer cada documento

| Documento | Esquema de numeración | Vigente hoy |
|---|---|---|
| `docs/RoadMaps/FinancialMcp-vNext.md` | Letra de Épica, I–O | ✅ Fuente de verdad |
| `docs/PROJECT_STATUS.md` §5 | Letra de Épica (I–O + S/U/UI + sin letra) | ✅ Vigente, complementa a `vNext.md` |
| `docs/Epics/EpicaI-Importacion.md` | PRs `I1`–`I7` dentro de Épica I | ✅ Vigente (texto de estado desactualizado, contenido técnico vigente — ver `PROJECT_STATUS.md` §7) |
| `docs/Epics/EpicaO-ImportacionManual.md` | PRs `PR-O1`–`PR-O9` dentro de Épica O | ✅ Vigente (misma salvedad que EpicaI) |
| `docs/Epics/Epica-PlanificacionMensual.md` | Sin letra (deliberado) | ✅ Vigente |
| `docs/Architecture/EpicaMImportWorkflow.md` | Historias `M1`–`M9`, propio, **no es la Épica M** | ✅ Vigente, retitulado en este patch |
| `docs/Architecture/PRU1analisisexperienciaclasificacion.md` | PRs `PR-U1`–`PR-U3`+; usa "Épica L" en sentido histórico (§5 de este documento) | 🕓 Histórico — ver `PROJECT_STATUS.md` §7 |
| `docs/Archive/*` (incluye la serie `PRS*`, `RoadmapMVP.md`, `AuditoriaMVP.md`, `MVPDefinitivo.md`) | A–D y numeración pre-`vNext.md` | 🕓 Archivado — valor histórico únicamente |

---

## 8. Para trabajo nuevo

Antes de asignarle una letra o un prefijo `PR-X` a un trabajo nuevo: revisar la tabla de la sección 1 y las secciones 4–6 de este documento para confirmar que esa letra no está en uso con otro significado. Ante la duda, preferir un nombre descriptivo (como ya hacen `Centro de Auditoría`, `Planificación Mensual` o las historias `M1`–`M9`) antes que reutilizar una letra — es exactamente la falta de esa verificación previa lo que produjo las dos colisiones documentadas acá.

---

*Fuente: lectura cruzada de `docs/RoadMaps/FinancialMcp-vNext.md`, `docs/PROJECT_STATUS.md`, `docs/Epics/*`, `docs/Architecture/EpicaMImportWorkflow.md`, `docs/Architecture/PRU1analisisexperienciaclasificacion.md`, `docs/UX/ClassificationUX.md` y `docs/Archive/AuditoriaMVP.md` (que ya señalaba, sin resolver, la colisión de la sección 4).*
