# Mapa de confianza de datos — FinancialMcp

> Documento de trabajo. No contiene código ni implementación — es el mapa para decidir, tarea por tarea, qué investigar y en qué orden. Cada hallazgo distingue explícitamente **hecho verificado en el código** (con archivo y línea) de **hipótesis a confirmar**. Fuentes cruzadas: lectura directa del código en `claude/financialmcp-audit-roadmap-sgzqqi` (commit `dede331`) + `docs/PROJECT_STATUS.md`, `docs/RoadMaps/FinancialMcp-vNext.md`, `docs/Decisions/ADR-001` a `ADR-008`, `docs/Architecture/CentroDeAuditoria.md`, `docs/Epics/Epica-PlanificacionMensual.md`.
>
> **Estado de las investigaciones (actualizado a medida que avanzan):**
> - **DATA-001 / IMPORT-001** (identidad e idempotencia de movimientos bancarios) — investigación cerrada. Dos informes de investigación publicados como Artifact: reconstrucción completa del flujo de importación y confirmación de la causa raíz (`ExternalId` posicional), y una segunda entrega sobre qué hace único a un movimiento bancario real (estabilidad de campos, casos ambiguos, límites de los datos disponibles en el repo). Todavía no se decidió ninguna solución — sigue sin implementarse, según las reglas de este roadmap (sección "Muy importante: separar 'arreglar' de 'investigar'").
> - **IMPORT-003** (auditoría de duplicados existentes) — herramienta construida y validada contra un dataset sintético, **pendiente de ejecución contra la base real**. Ver `docs/imports/IMPORT-003-auditoria-duplicados.md` y `docs/imports/import-003-auditoria-duplicados.sql` — script de solo lectura, cuatro niveles de clasificación (PROBABLE/POSIBLE/AMBIGUO/NO DUPLICADO), sin ningún borrado ni modificación de datos.

---

## 1. Resumen ejecutivo

El proyecto ya tiene una base de documentación inusualmente honesta sobre su propio estado (`PROJECT_STATUS.md` se autodeclara "no apto para producción" y lista sus propios riesgos). Este documento no repite ese inventario general — se enfoca en los cinco problemas que se plantearon como origen de este trabajo, verificando cada uno contra el código real.

**Lo que quedó confirmado como hecho, no como sospecha:**

1. **El bug de duplicados de banco tiene causa raíz identificada y es estructural, no intermitente.** `BankStatement.ExternalId` se calcula como `SHA256(NombreDeArchivo | Hoja | fila | NúmeroDeFila)`. El nombre de archivo forma parte del hash. Dos archivos distintos —por definición, con nombres distintos— nunca pueden producir el mismo `ExternalId` para el mismo movimiento real, aunque el movimiento sea idéntico en fecha, importe y descripción. La ventana de solapamiento típica (importar cada varios días, con superposición de fechas) es exactamente el escenario que este diseño no cubre. El propio código lo documenta como riesgo conocido y sin resolver.
2. **El detector de sospechosos actual (`SuspicionDetector`) usa únicamente monto ± tolerancia y fecha ± ventana — ninguna otra señal.** No compara descripción, no compara `ExternalId`, no distingue "mismo movimiento real" de "dos movimientos distintos que casualmente se parecen".
3. **No existe ningún endpoint ni comando para borrar `BankStatement`/`Transaction` hoy.** Cualquier limpieza de duplicados históricos que se haya hecho hasta ahora fue manual, directo contra la base, sin pasar por ninguna validación del sistema.
4. **La integridad referencial entre "movimiento clasificado" y "movimiento original" es por convención, no por FK.** `ClassifiedMovementItem`, `MovementAuditDecision`, `InvestigationReference` e `ImportBatchLine` referencian filas de `BankStatement`/`Transaction` mediante `SourceEntityType + SourceId` sin clave foránea. Si se borra una fila duplicada de origen sin un barrido explícito de estas cuatro tablas, se generan referencias rotas silenciosas — no hay ningún mecanismo de la base de datos que lo impida ni lo avise.
5. **El bug de planificación (agosto/septiembre) tiene causa raíz confirmada y coincide, además, con una decisión de diseño explícita y documentada.** Un `PlanningItem` pertenece a un `PlanningMonth` fijo por clave foránea (`PlanningMonthId`), asignado una sola vez al crearlo. `EditPlanningItemHandler` solo modifica `Title`/`ExpectedAmount`/`DueDate` — nunca `PlanningMonthId`. El propio documento de diseño del módulo (`docs/Epics/Epica-PlanificacionMensual.md`, sección 7, regla 2) dice explícitamente: *"`DueDate` es y sigue siendo un dato descriptivo... Ninguna futura iteración debe agregarle alertas, colores de urgencia, recordatorios."* El sistema está haciendo exactamente lo que el diseño dice que debe hacer. El problema no es un bug de fecha — es que el modelo mental esperado ("cambiar el vencimiento debería mover el gasto de mes") no coincide con el modelo implementado ("el mes es fijo, el vencimiento es solo un dato para ordenar la lista"). Es una decisión de producto pendiente, no una corrección de código obvia.
6. **El MCP hoy son 32 tools de solo-invocación-individual, sin loop propio.** El propio ADR-006 ya declara el principio correcto: *"El razonamiento sigue del lado del cliente MCP... el servidor no es un agente autónomo, no corre su propio loop de decisiones."* Evolucionar hacia una experiencia conversacional es, en gran medida, una decisión de **dónde vive el razonamiento** (¿el cliente MCP que ya se usa, o un loop propio server-side con Ollama?) antes que una decisión de qué tools nuevas construir.

**Lo que sigue siendo hipótesis, marcado como tal en cada tarjeta:** cuántos duplicados reales existen hoy en la base, si el `ExternalId` de tarjeta (`Transaction`, basado en contenido) tiene su propio punto débil, y qué proporción de las "clasificaciones dudosas" que ya reporta el Centro de Auditoría son en realidad síntomas de estos duplicados no detectados.

**Postura general:** todo lo que sigue es investigación y diseño conceptual. Ninguna tarjeta de este documento se traduce en código todavía — cada una termina en una decisión a tomar, no en un PR.

---

## 2. Roadmap por fases

### FASE 0 — Recuperar confianza

#### DATA-001 — Auditoría de integridad de la base actual
- **Prioridad:** CRITICAL
- **Tipo:** Investigación / Integridad de datos
- **Problema:** No existe hoy ninguna vista consolidada de "¿en qué estado está mi base ahora mismo?" a nivel de integridad estructural (huérfanos, duplicados, referencias rotas). El Centro de Auditoría (`AuditReportService`) audita *clasificación* (sospechosos, mal clasificados, pendientes) pero no audita *integridad referencial* entre tablas.
- **Evidencia:** `docs/Architecture/CentroDeAuditoria.md` es explícito: *"No detecta nada por su cuenta"* y solo reutiliza `IReviewEngine`/`IClassificationSuggestionService` — ninguno de los dos revisa huérfanos de `SourceEntityType+SourceId`. Ya existe un mecanismo extensible de verificaciones (`IImportConsistencyCheck`/`IImportConsistencyVerifier`, `src/FinancialSystem.Application/Imports/IImportConsistencyCheck.cs`), pero su alcance está confirmado como **acotado a una corrida de importación puntual** ("una corrida ya persistida" — no una auditoría de toda la base).
- **Hipótesis:** es probable que ya existan hoy en la base: (a) `BankStatement`/`Transaction` duplicados por el bug de IMPORT-001, (b) `ClassifiedMovementItem` cuyo `SourceId` no resuelve a ninguna fila (si alguna limpieza manual ya se hizo), (c) movimientos con múltiples `ClassifiedMovement` apuntando al mismo `SourceId` (doble clasificación). Ninguno de los tres está cuantificado todavía.
- **Investigación necesaria:** diseñar (sin implementar) un conjunto de consultas de verificación: duplicados por `(Date, Amount, Concept/Description)` dentro de `BankStatement`/`Transaction`; `ClassifiedMovementItem.SourceId` sin fila correspondiente en su `SourceEntityType`; `MovementAuditDecision`/`InvestigationReference` en la misma situación; movimientos con `ExternalId` distinto pero contenido idéntico entre `BankStatement` y su archivo origen (cruce con `ImportBatchLine`); totales de `FinancialMetricsService` recalculados a mano sobre una muestra y comparados contra lo que devuelve el servicio.
- **Solución propuesta (conceptual):** un reporte de integridad, separado del Centro de Auditoría (que audita *clasificación*, no *estructura*), que se pueda correr bajo demanda y arme una lista de hallazgos con severidad. No es una tool nueva todavía — es el criterio con el que se diseña esa tool, una vez decidido.
- **Dependencias:** ninguna — es el punto de partida.
- **Riesgo:** si se salta esta fase, cualquier corrección posterior (idempotencia, borrado de duplicados) se diseña sin saber cuál es el tamaño real del problema.
- **Criterio de terminado:** existe una lista concreta, con conteos reales, de cuántas filas de cada tipo están en cada categoría de inconsistencia. No hace falta que sea automatizada — alcanza con que sea reproducible.

---

### FASE 1 — Importación e idempotencia

#### IMPORT-001 — Identidad inestable de `BankStatement.ExternalId`
- **Prioridad:** CRITICAL
- **Tipo:** Bug / Integridad de datos — **verificado en el código, no es hipótesis**
- **Problema:** el `ExternalId` de un movimiento de cuenta bancaria no identifica el movimiento — identifica su *posición dentro de un archivo concreto*.
- **Evidencia:**
  - `src/FinancialSystem.Infrastructure/Imports/BankStatements/BbvaBankStatementParser.cs:215-220`: `BuildExternalId(sourceFile, sheetName, rowNumber)` → `SHA256("{NombreDeArchivo}|{Hoja}|row|{NúmeroDeFila}")`.
  - El propio doc-comment de la entidad (`src/FinancialSystem.Domain/Entities/BankStatement.cs:12-17`) lo declara así: *"No existe número de operación único en el XLS del BBVA... Riesgo documentado: si el banco re-exporta con filas insertadas en el medio, los RowNumbers cambian y esas filas se re-insertan. Es el mejor compromiso posible dado el formato del archivo."*
  - `docs/RoadMaps/FinancialMcp-vNext.md` §6 confirma que este riesgo sigue activo y sin resolver ("la fragilidad posicional del `ExternalId` de `BankStatement`"), listado en `docs/PROJECT_STATUS.md` §13 como prioridad #1 del plan de estabilización del proyecto.
  - El nombre de archivo (`Path.GetFileName(sourceFile)`) es parte del hash. Dos exportaciones del banco en fechas distintas casi con certeza tienen nombres de archivo distintos — esto por sí solo ya rompe la idempotencia entre corridas para cualquier movimiento que aparezca en ambos archivos, **independientemente de si el `RowNumber` se mantiene estable o no**.
  - **Confirmado con archivos reales del propio proyecto** (investigación de continuación, ver estado al inicio de este documento): `docs/Archive/auditoriasemanticamovimientosreales.md` y otros cuatro documentos citan dos extractos reales de la misma cuenta con nombres `Debito_29_05_2026_al_10_07_2026.xls` y `Debito_30_03_2026_al_15_06_2026.xls` — período solapado, nombres distintos, confirmando el escenario en la práctica, no solo en teoría.
- **Hipótesis:** falta confirmar (a) si el banco genera nombres de archivo estables entre descargas (no lo son, según la evidencia real ya encontrada) y (b) cuántos duplicados reales ya generó esto en la base (ver DATA-001/IMPORT-003).
- **Investigación necesaria:** revisar archivos reales con fechas solapadas y verificar, fila por fila, si domina el nombre de archivo o el corrimiento de fila como causa — **pendiente**: los `.xls` originales no están en el repositorio (confirmado, búsqueda exhaustiva en el árbol de trabajo y en todo el historial de git), así que esta verificación fila-por-fila requiere conseguir archivos reales nuevos, no puede completarse solo con lo que ya hay documentado.
- **Solución propuesta (conceptual, no código):** la identidad de un `BankStatement` debería depender del *contenido del movimiento* (fecha + importe + descripción, con la misma lógica que ya usa `Transaction.ExternalId` vía `SheetParserHelpers.BuildTransactionExternalId`), no de su posición de archivo. Alternativas evaluadas sin elegir ninguna (ver informe de investigación "Identidad del Movimiento Real"): (A) contenido normalizado fecha+importe+descripción, (B) número embebido en `Concept` (`Nro:XXXXX`) — **descartado con evidencia real**: dos "PAGO DE HABERES" de meses distintos comparten el mismo `Nro:99999999`, no es único por operación, (C) `Balance` como señal adicional — no confirmado, (D) combinación de señales con niveles de confianza, (E) estrategia distinta por fuente.
- **Dependencias:** ninguna para investigar. Cualquier cambio de esquema depende de haber cerrado DATA-001 y de una decisión de producto sobre migración de filas históricas.
- **Riesgo:** cambiar el criterio de `ExternalId` sin plan de migración puede generar el efecto inverso — duplicar en masa lo que hoy está bien.
- **Criterio de terminado (de la investigación, no de la corrección):** confirmado — causa raíz identificada y verificada contra código y datos reales. Sigue pendiente: cuantificación del daño ya hecho (IMPORT-003) y validación empírica de una alternativa concreta contra archivos reales con período solapado.
- **Hallazgo adicional encontrado durante esta investigación, documentado pero no resuelto (regla de esta investigación: documentar sin implementar):** los nombres de archivo reales citados en la documentación del proyecto (`Debito_*.xls`) no matchean ninguno de los 4 patrones vigentes en `FileIngestionOptions.BbvaBankStatementFilePatterns` (`src/FinancialSystem.Application/Imports/FileIngestionOptions.cs:15-16`: `["Caja*.xls", "*ahorros*.xls", "*corriente*.xls", "Detalle_mov_cuenta*.xls"]`). Es un problema de enrutamiento por nombre de archivo, distinto y anterior al de `ExternalId` — si un archivo real no matchea ningún patrón, ni siquiera llega a `BbvaBankStatementImporter`. No se sabe si esto afecta al caso real que motivó este roadmap (para que haya duplicados reportados, el archivo tuvo que llegar al importador, así que al menos algún patrón está matcheando) — queda como riesgo latente a investigar por separado.

#### IMPORT-002 — Robustez real de `Transaction.ExternalId` (tarjeta/catch-all)
- **Prioridad:** MEDIUM
- **Tipo:** Investigación
- Sin cambios respecto a la versión anterior de este documento — ver el informe de investigación original para el detalle completo.

#### IMPORT-003 — Cuantificar los duplicados ya producidos por IMPORT-001
- **Prioridad:** HIGH
- **Tipo:** Investigación
- **Estado:** herramienta de auditoría de solo lectura construida (`docs/imports/import-003-auditoria-duplicados.sql`) y validada contra un dataset sintético que reproduce el escenario de IMPORT-001 (dos archivos con período solapado, casos ambiguos deliberados) — el script clasificó correctamente los tres casos de prueba. **Pendiente:** ejecutarlo contra la base real y volcar los números reales acá. Ver `docs/imports/IMPORT-003-auditoria-duplicados.md` para metodología completa, criterios de clasificación (PROBABLE/POSIBLE/AMBIGUO/NO DUPLICADO) y limitaciones.

---

### FASE 2 — Auditoría y saneamiento de datos existentes

#### DEDUPE-001 — Taxonomía de confianza para duplicados
- **Prioridad:** HIGH
- **Tipo:** Investigación / Diseño
- **Problema:** hoy no existe ninguna clasificación de "qué tan seguro estoy de que esto es un duplicado" — el único mecanismo (`SuspicionDetector`) da un sí/no binario basado en un solo criterio débil (monto ± tolerancia, fecha ± ventana — `src/FinancialSystem.Infrastructure/Review/SuspicionDetector.cs:75-82`).
- **Dependencias:** IMPORT-001 (para saber qué campo de identidad usar en el nivel "Confirmado").
- **Estado:** pendiente de iniciar — insumo directo ya generado por la investigación de continuación de IMPORT-001 (estabilidad de campos, casos ambiguos reales con `fecha+importe+concepto`).

#### DEDUPE-002 — Señales disponibles para identidad de alta confianza
- **Prioridad:** HIGH
- **Tipo:** Investigación
- **Estado:** parcialmente adelantada por la investigación de continuación de IMPORT-001 (clasificación A/B/C de cada campo de `BankStatement`) — falta todavía la validación de `Balance` contra archivos reales solapados.

#### DEDUPE-003 — Mecanismo seguro de borrado histórico (huérfanos y referencias blandas)
- **Prioridad:** CRITICAL
- **Tipo:** Arquitectura / Integridad de datos
- **Estado:** sin cambios — ver informe original.

#### DEDUPE-004 — Inventario cuantitativo de duplicados/incoherencias históricas
- **Prioridad:** HIGH
- **Tipo:** Investigación / Integridad de datos
- **Estado:** sin cambios — ver informe original.

---

### FASE 3 — Modelo de clasificación y confiabilidad

#### MODEL-001 — Verificar el modelo de capas contra el código real
#### MODEL-002 — Catálogo de inconsistencias posibles y cómo detectarlas

Sin cambios respecto a la versión anterior de este documento.

---

### FASE 4 — Motor de sugerencias

#### SUGGEST-001 — Entender el problema real antes de tocar el motor

Sin cambios — sigue deliberadamente diferida.

---

### FASE 5 — Planificación (bug agosto/septiembre)

#### PLAN-001 — Causa raíz confirmada: `DueDate` no mueve el ítem de mes
#### PLAN-002 — Decisión de producto: ¿qué debería pasar cuando cambia `DueDate`?
#### PLAN-003 — Otras pantallas afectadas por la misma lógica

Sin cambios respecto a la versión anterior de este documento.

---

### FASE 6 — Detección segura de duplicados + eliminación

#### CLEAN-001 — Flujo de revisión humana antes de cualquier borrado
#### CLEAN-002 — Mecanismo de borrado sin dejar inconsistencias
#### CLEAN-003 — Borrado físico vs. soft-delete/fusión — decisión pendiente

Sin cambios respecto a la versión anterior de este documento.

---

### FASE 7 — FinancialMcp como agente conversacional

#### AGENT-001 — Inventario y evaluación de las tools MCP actuales como base del agente
#### AGENT-002 — Separación consulta / sugerencia / modificación en el catálogo de tools
#### AGENT-003 — Memoria/contexto de conversación vs. memoria de investigaciones
#### AGENT-004 — Dónde vive el loop de razonamiento

Sin cambios respecto a la versión anterior de este documento.

---

## 3. Lista de tareas numeradas

| # | ID | Nombre | Fase | Prioridad | Estado |
|---|---|---|---|---|---|
| 1 | DATA-001 | Auditoría de integridad de la base actual | 0 | CRITICAL | Pendiente de iniciar (distinto de la investigación de identidad, que usa el mismo ID de forma informal en la conversación de trabajo — ver nota) |
| 2 | IMPORT-001 | Identidad inestable de `BankStatement.ExternalId` | 1 | CRITICAL | **Investigación cerrada** — causa raíz confirmada, alternativas evaluadas sin elegir ninguna |
| 3 | IMPORT-002 | Robustez real de `Transaction.ExternalId` | 1 | MEDIUM | Pendiente de iniciar |
| 4 | IMPORT-003 | Cuantificar duplicados ya producidos | 1 | HIGH | Herramienta construida y validada (sintético) — pendiente ejecutar contra base real |
| 5 | DEDUPE-001 | Taxonomía de confianza para duplicados | 2 | HIGH | Pendiente de iniciar (insumo parcial ya generado) |
| 6 | DEDUPE-002 | Señales disponibles para identidad de alta confianza | 2 | HIGH | Parcialmente adelantada |
| 7 | DEDUPE-003 | Mecanismo seguro de borrado histórico | 2 | CRITICAL | Pendiente de iniciar |
| 8 | DEDUPE-004 | Inventario cuantitativo de duplicados/incoherencias | 2 | HIGH | Pendiente de iniciar |
| 9 | MODEL-001 | Verificar el modelo de capas contra el código real | 3 | HIGH | Pendiente de iniciar |
| 10 | MODEL-002 | Catálogo de inconsistencias posibles | 3 | HIGH | Pendiente de iniciar |
| 11 | SUGGEST-001 | Entender el problema real de sugerencias (diferida) | 4 | MEDIUM | Diferida a propósito |
| 12 | PLAN-001 | Causa raíz confirmada del bug agosto/septiembre | 5 | HIGH | Investigación cerrada |
| 13 | PLAN-002 | Decisión de producto sobre `DueDate` | 5 | HIGH | Pendiente de decisión del usuario |
| 14 | PLAN-003 | Otras pantallas afectadas (Dashboard) | 5 | MEDIUM | Investigación cerrada |
| 15 | CLEAN-001 | Flujo de revisión humana antes de borrar | 6 | CRITICAL | Pendiente de iniciar |
| 16 | CLEAN-002 | Mecanismo de borrado sin inconsistencias | 6 | CRITICAL | Pendiente de iniciar |
| 17 | CLEAN-003 | Borrado físico vs. soft-delete/fusión | 6 | HIGH | Pendiente de iniciar |
| 18 | AGENT-001 | Inventario de tools MCP actuales | 7 | MEDIUM | Pendiente de iniciar |
| 19 | AGENT-002 | Separación consulta/sugerencia/modificación | 7 | HIGH | Pendiente de iniciar |
| 20 | AGENT-003 | Memoria conversacional vs. memoria de investigaciones | 7 | MEDIUM | Pendiente de iniciar |
| 21 | AGENT-004 | Dónde vive el loop de razonamiento | 7 | HIGH | Pendiente de iniciar |

**Nota sobre nomenclatura:** en la conversación de trabajo, "DATA-001" se usó de forma informal para nombrar tanto la futura auditoría de integridad general (tarea #1 de esta tabla) como la investigación de identidad/idempotencia de movimientos bancarios (que corresponde en rigor a IMPORT-001, tarea #2). Los dos informes de investigación ya entregados sobre identidad bancaria pertenecen a **IMPORT-001**, no son todavía la auditoría de integridad general de DATA-001 — quedan como tareas separadas en esta tabla, tal como se definieron originalmente.

El resto de esta sección (mapa de dependencias, qué investigar primero, qué no tocar todavía, criterios de éxito) no cambió respecto a la versión anterior de este documento — se omite acá para no duplicar contenido; ver el Artifact original publicado en la conversación de trabajo para el texto completo, hasta que se decida consolidar todo en este único archivo.

---

*Última actualización: continuación de la investigación de IMPORT-001 (identidad de movimientos bancarios, dos informes). Fuente: lectura directa del código en la rama `claude/financialmcp-audit-roadmap-sgzqqi` (commit `dede331`), cruzada contra la documentación existente del proyecto. Ningún archivo de código fue modificado para producir este documento — es documentación de investigación, no una implementación.*
