# Arquitectura — Centro de Auditoría

> Documento vivo. Describe la implementación real del Centro de Auditoría (`audit.html` + `AuditReportService` + tools MCP `AuditTools`/`AuditDatabaseTools`) tal como existe en el código a la fecha de esta versión. No es un documento de diseño previo a la implementación: se escribió leyendo el código ya construido — ver `docs/PROJECT_STATUS.md` §2, que lista este módulo como "Terminado (funcionalmente)" y señala, hasta este documento, la ausencia de documentación de diseño propia como su principal brecha.

---

## 1. Objetivo

### Qué problema resuelve

Ninguna otra pantalla del sistema responde, en un solo lugar, "¿puedo confiar en los datos de este período?". `movements.html` clasifica movimiento por movimiento; `dashboard.html` (Épica L) muestra cobertura de clasificación (cuánto está clasificado vs. pendiente) pero no evalúa si lo ya clasificado es *correcto*. El Centro de Auditoría combina, para un período elegido, las señales que ya existen en el dominio — sospechosos, posibles mal clasificados, pendientes, investigaciones abiertas — en un único reporte con un estado general ("Sin datos" / "Correcta" / "Con problemas") y una lista priorizada de qué revisar primero.

### Qué responsabilidades tiene

* Ejecutar, para un período dado, las detecciones que ya existen (`IReviewEngine`/`ISuspicionDetector` para sospechosos; `IClassificationSuggestionService` + `Counterparty.Default*` para clasificaciones dudosas) y combinarlas en un reporte único.
* Contar movimientos pendientes de clasificar e investigaciones abiertas/resueltas dentro del mismo reporte.
* Registrar que una persona revisó un movimiento marcado como potencialmente mal clasificado y decidió mantener su clasificación actual (`MovementAuditDecision`).
* Ofrecer navegación directa hacia `movements.html` para corregir lo que el reporte señaló.
* Exponer exactamente la misma lógica tanto a un cliente MCP (`AuditTools`/`AuditDatabaseTools`) como a la interfaz web (`audit.html`), sin dos implementaciones distintas de las mismas reglas.

### Qué responsabilidades NO tiene

* **No detecta nada por su cuenta.** Cada señal que expone (sospechosos, mal clasificados) es la salida de un motor que ya existe y que ya usan otras pantallas (`IReviewEngine`, `IClassificationSuggestionService`) — el Centro de Auditoría no agrega ninguna heurística propia de detección.
* **No corrige ni reclasifica nada.** No existe ningún flujo donde el Centro de Auditoría escriba `ClassifiedMovement`, `Category`, `Counterparty` ni ningún dato financiero. La única escritura propia del módulo es `MovementAuditDecision` — un registro de que una persona revisó algo, no una corrección de datos (ver sección 5).
* **No es una fuente de verdad de datos financieros.** Todo lo que reporta se recalcula en cada ejecución a partir de las mismas tablas que usa el resto del sistema; no persiste ningún resultado de auditoría propio (más allá de `MovementAuditDecision`).
* **No decide automáticamente qué es "correcto".** Una clasificación dudosa es una sugerencia basada en historial/defaults, no una certeza — el reporte la presenta como candidata a revisión humana, nunca como un error confirmado.
* **No gestiona investigaciones.** Las cuenta y las lista (abiertas/resueltas), pero crear, actualizar o resolver una investigación es exclusivamente responsabilidad de `InvestigationTools` (MCP) — no hay ningún control en `audit.html` para eso.

---

## 2. Flujo general

El flujo completo, desde que el usuario abre `audit.html` hasta que actúa sobre un hallazgo:

1. **Carga inicial (sin ejecutar auditoría todavía).** `loadStatus()` llama a `GET /api/audit/status`, que es deliberadamente liviano: conectividad a la base (`Database.CanConnectAsync`), la última importación (`IImportHistoryQueryService`) y un conteo rápido de movimientos del mes en curso (`IMovementsQueryService`) — sin ejecutar `IReviewEngine` ni `IClassificationSuggestionService`. Objetivo explícito: que la pantalla cargue rápido antes de que el usuario decida ejecutar algo pesado.
2. **Selección del período.** Cuatro atajos (`currentMonth`/`previousMonth`/`last30`/`last90`) completan los campos Desde/Hasta, que el usuario puede seguir editando a mano. No existe la opción "todo el historial": el costo de `IClassificationSuggestionService` corriendo por cada movimiento clasificado del período haría inviable auditar sin límite. El rango máximo permitido es de 90 días (mismo límite y misma razón que `MovementTools`/`GET /api/movements`).
3. **Ejecución (`runAudit()`).** Al hacer clic en "Ejecutar auditoría", el frontend llama a `GET /api/audit/report?from=&to=`, que delega en `AuditReportService.BuildFullAuditReportAsync(from, to)`.
4. **Carga de movimientos.** `BuildFullAuditReportAsync` obtiene todos los movimientos del período vía `IMovementsQueryService.GetAsync` (el mismo servicio que usa `GET /api/movements`) y separa pendientes de clasificados.
5. **Sugerencias (clasificaciones dudosas).** Sobre los movimientos ya clasificados, se ejecuta `IClassificationSuggestionService.SuggestAsync` (el mismo motor que sugiere valores en `movements.html`) y se compara cada resultado contra el valor actualmente persistido; además se compara cada movimiento contra los `Counterparty.Default*` de su contraparte (ADR-003). Cualquier diferencia en Categoría/Tipo/Impacto/Contraparte se registra como un "motivo".
6. **Movimientos sospechosos.** En paralelo, se ejecuta `IReviewEngine.GenerateAsync` (el mismo motor que alimenta el aviso ⚠ de `movements.html`, K6) para detectar posibles duplicados o transacciones divididas dentro del período.
7. **Investigaciones.** Se cuentan las `Investigation` con `Status = Open` (se listan por pregunta) y `Status = Resolved` (solo se cuentan) — lectura directa, sin pasar por ninguna tool MCP.
8. **Generación del reporte.** Todo lo anterior se combina en un `FullAuditReport`: un estado (`Sin datos` si no hay movimientos en el período; `Correcta` si `TotalProblems == 0`; `Con problemas` en cualquier otro caso), un resumen numérico, una lista de acciones recomendadas (orden fijo: sospechosos → pendientes → clasificaciones dudosas → investigaciones abiertas) y cuatro bloques expandibles con el detalle de cada categoría.
9. **Navegación hacia movimientos.** Cada hallazgo individual (`Modificar clasificación`, `Ver movimientos`) arma una URL hacia `movements.html` con `from`/`to` y, según el caso, `movementId` (abre el modal de clasificación de ese movimiento directamente) o `search` (precarga el filtro de texto con la descripción del grupo). El botón "Ir a Movimientos" del encabezado navega sin filtros. En ningún caso el Centro de Auditoría clasifica nada por sí mismo — siempre entrega el control a `movements.html`.
10. **Decisión del usuario sobre una clasificación dudosa.** En vez de navegar a corregir, el usuario puede hacer clic en "✓ Mantener clasificación" (individual o para un grupo completo de movimientos con la misma descripción y la misma sugerencia) — esto llama a `POST /api/audit/reviews`, que persiste un `MovementAuditDecision` por movimiento y vuelve a ejecutar la auditoría completa (`runAudit()`) para reflejar el cambio.

---

## 3. Arquitectura

Componentes reales, de punta a punta:

* **`AuditReportService`** (`FinancialSystem.Infrastructure/Audit/AuditReportService.cs`) — el corazón del módulo. Orquesta `IReviewEngine`, `IMovementsQueryService`, `IClassificationSuggestionService` e `IApplicationDbContext` (para `Counterparty.Default*`, `FinancialAccount`, `Investigation`, `MovementAuditDecision`) y arma tanto los reportes de texto plano (consumidos por las tools MCP) como el `FullAuditReport` estructurado (consumido por la API/`audit.html`). Vive en `Infrastructure` — no en `FinancialMcp.Api` ni en `hosts/FinancialSystem.McpServer` — específicamente para que ambos hosts, que no se referencian entre sí, puedan compartir la misma clase sin duplicar lógica.
* **`ReviewMovementsCommand`/`ReviewMovementsHandler`** (`FinancialSystem.Application/Audit/Commands/`) — el único caso de uso de escritura del módulo. Registra uno o varios `MovementAuditDecision`, es idempotente (un movimiento ya revisado no genera una fila duplicada) y usa una sola consulta batch para todo el lote.
* **`MovementAuditDecision`** (`FinancialSystem.Domain/Review/MovementAuditDecision.cs`) — la entidad que persiste la revisión humana. Se identifica con `SourceEntityType` + `SourceId`, la misma convención que ya usan `ClassifiedMovementItem`/`InvestigationReference`. Su sola existencia es el estado (no hay un campo `Status`): si hay una fila para ese movimiento, fue revisado.
* **`AuditEndpoints`** (`FinancialMcp.Api/Endpoints/AuditEndpoints.cs`) — expone `GET /api/audit/status`, `GET /api/audit/report` y `POST /api/audit/reviews`, los tres protegidos con `RequireAuthorization()`. No reimplementa ninguna regla: `/report` delega directamente en `AuditReportService.BuildFullAuditReportAsync`; `/reviews` delega en `ReviewMovementsHandler`.
* **`AuditDtos`** (`FinancialMcp.Api/DTOs/AuditDtos.cs`) — los contratos HTTP (`AuditStatusResponse`, `AuditReportResponse`, `MisclassifiedMovementDto`, `ReviewMovementsRequest`, etc.) que traducen los tipos de `AuditReportService` al JSON que consume `audit.html`.
* **`AuditTools`** (`hosts/FinancialSystem.McpServer/Tools/AuditTools.cs`) — expone `FindSuspiciousMovements`/`FindMisclassifiedMovements` como tools MCP. Solo valida los parámetros de fecha (formato, rango máximo de 90 días) y delega en `AuditReportService`; no contiene ninguna regla de auditoría propia.
* **`AuditDatabaseTools`** (`hosts/FinancialSystem.McpServer/Tools/AuditDatabaseTools.cs`) — expone `AuditDatabase`, sin parámetros (siempre usa el mes en curso), delegando en `AuditReportService.BuildFullAuditReportAsync` y devolviendo `FullAuditReport.ReportText` tal cual.
* **`audit.html`** — la UI web: selector de período con atajos, botón "Ejecutar auditoría", banner de estado, resumen numérico, lista de acciones recomendadas y cuatro secciones expandibles (sospechosos, pendientes, clasificaciones dudosas, investigaciones abiertas). Agrupa en el cliente (no en el backend) los movimientos con la misma descripción exacta y la misma sugerencia, para no repetir la misma acción muchas veces sobre movimientos equivalentes.

**Componentes que el Centro de Auditoría reutiliza sin modificar ni envolver con lógica propia:**
`IReviewEngine`/`ISuspicionDetector` (Review), `IClassificationSuggestionService` (Suggestions), `IMovementsQueryService` (Movements), `IImportHistoryQueryService` (Imports) — los cuatro ya existían para otros módulos antes de que el Centro de Auditoría los consumiera.

---

## 4. Modelo conceptual

* **Movimiento** (`Transaction`/`BankStatement`, identificado por `SourceEntityType` + `SourceId`) es la unidad sobre la que gira todo — el Centro de Auditoría nunca lo persiste ni lo copia, solo lo lee vía `IMovementsQueryService` y lo referencia por su identificador real.
* **Sugerencia** (`ClassificationSuggestion`, de `IClassificationSuggestionService`) es una interpretación efímera, recalculada en cada ejecución — nunca se persiste. El Centro de Auditoría la usa como una de las dos fuentes de "motivo" para marcar una clasificación como dudosa (la otra es `Counterparty.Default*`).
* **Auditoría** (`FullAuditReport`) es el resultado de una ejecución puntual: no es una entidad persistida, es un valor calculado que existe mientras dura la respuesta HTTP/la llamada a la tool. Ejecutar la auditoría dos veces seguidas sobre el mismo período puede dar resultados distintos si algo cambió en el medio (una reclasificación, una nueva importación) — es intencional, refleja el estado actual, no un snapshot congelado.
* **Investigación** (`Investigation`, de la memoria del MCP — ADR-007) es un concepto externo al Centro de Auditoría: éste solo la lee (cuenta y lista las abiertas) para incluirla como una señal más del panorama del período. El Centro de Auditoría no crea, actualiza ni resuelve investigaciones.
* **Decisión del usuario** (`MovementAuditDecision`) es lo único que el módulo persiste. Representa "una persona vio este hallazgo y decidió no actuar sobre él" — no es una corrección del movimiento (eso sigue siendo un `ClassifyMovementCommand` en `movements.html`, fuera del Centro de Auditoría) ni una supresión del hallazgo: el movimiento revisado se sigue reportando como potencialmente mal clasificado, solo que separado visualmente ("Revisadas") y sin contar para el total de problemas activos.

Relación entre ellos: un **movimiento** puede tener una **sugerencia** (efímera, del motor de Suggestions) que difiere de su clasificación actual; si difiere, el Centro de Auditoría lo incluye en la **auditoría** como candidato dudoso; frente a eso, el usuario puede navegar a reclasificarlo (fuera del módulo) o registrar una **decisión** de mantenerlo. Las **investigaciones** son ortogonales a este ciclo — se muestran junto al resto de las señales, pero no interactúan con `ClassificationSuggestion` ni con `MovementAuditDecision`.

---

## 5. Principios de diseño

* **La auditoría nunca modifica datos automáticamente.** Todas las detecciones (`IReviewEngine`, `IClassificationSuggestionService`, `Counterparty.Default*`) son de solo lectura; la única escritura del módulo (`MovementAuditDecision`) registra una decisión humana, no un dato financiero. Este límite se sostiene incluso en la acción "Mantener clasificación en lote": guarda N registros de revisión, nunca N cambios de clasificación.
* **Genera recomendaciones, no aplica cambios.** Una clasificación dudosa es una discrepancia entre lo persistido y lo que el motor de sugerencias/los defaults de contraparte indicarían — no una certeza. Corregirla exige pasar por `movements.html` (`ClassifyMovementCommand`), el único punto de escritura de clasificación en todo el sistema; el Centro de Auditoría solo arma el enlace.
* **Cero reglas de detección nuevas.** Tanto los comentarios de `AuditReportService` como los de `AuditTools`/`AuditEndpoints` son explícitos en este punto: el módulo reutiliza motores que ya existían para otras pantallas (`IReviewEngine` para sospechosos, `IClassificationSuggestionService` para sugerencias) en vez de construir una heurística de auditoría independiente que pudiera divergir de lo que el usuario ya ve en `movements.html`.
* **Una sola implementación para MCP y web.** `AuditReportService` vive en `Infrastructure` específicamente para que `FinancialMcp.Api` (Centro de Auditoría web) y `hosts/FinancialSystem.McpServer` (tools `AuditTools`/`AuditDatabaseTools`), que no se referencian entre sí, obtengan exactamente el mismo resultado ante la misma pregunta — no dos lógicas de auditoría que pudieran desalinearse con el tiempo.
* **El hallazgo revisado no se oculta.** `MovementAuditDecision` es una anotación, no una supresión: un movimiento marcado como revisado sigue apareciendo en el reporte (en su propia sección), y el reporte distingue explícitamente "detectados en total" de "pendientes de revisar" (`MisclassifiedDetected` vs. `Misclassified`) para que la elección de ocultar visualmente lo ya revisado en la UI no se confunda con haber dejado de detectarlo.
* **Las investigaciones están separadas de la clasificación y de la auditoría misma.** El Centro de Auditoría las lee como una señal más (cuántas quedan abiertas), pero no tiene ninguna lógica que las relacione con una sugerencia de clasificación o con un `MovementAuditDecision` — son dos sistemas de memoria/decisión independientes que comparten pantalla de reporte, no un flujo integrado.
* **`/status` liviano, separado de `/report` pesado.** La carga inicial de la pantalla no ejecuta `IReviewEngine` ni `IClassificationSuggestionService` — esas dos operaciones, con costo no lineal en el tamaño del período, solo corren cuando el usuario pide explícitamente "Ejecutar auditoría".

---

## 6. Limitaciones actuales

Observadas directamente en el código, no proyectadas:

* **Sin cobertura de tests.** No existe ningún test para `AuditReportService`, `ReviewMovementsHandler` ni `MovementAuditDecision` — la única prueba relacionada con este módulo (`PlanningAuditInvestigationsProtectedEndpointsTests`) verifica que los endpoints exigen autenticación, no su lógica de auditoría.
* **Recomputación redundante dentro de una misma ejecución.** `BuildFullAuditReportAsync` llama a `BuildMisclassifiedMovementsReportAsync` (para el texto) y, por separado, a `GetMisclassifiedMovementsAsync` (para la lista estructurada que usa `audit.html`) — ambas invocan internamente `ComputeFlaggedMovementsAsync`, que a su vez corre `IClassificationSuggestionService.SuggestAsync` sobre todos los movimientos clasificados del período. El resultado es el mismo cálculo de sugerencias ejecutado dos veces por cada auditoría completa (tanto desde `/api/audit/report` como desde la tool MCP `AuditDatabase`).
* **Sin paginación ni "todo el historial".** El rango está limitado a 90 días por el costo de `IReviewEngine`/`IClassificationSuggestionService`; auditar un año completo requiere varias ejecuciones manuales con rangos distintos.
* **Las clasificaciones dudosas son heurísticas, no certezas.** Se basan en coincidencia de descripción exacta con el historial y en los defaults configurados de la contraparte — un movimiento legítimamente distinto con la misma descripción puede aparecer como falso positivo; el propio flujo (revisar y decidir) asume esto.
* **Sin gestión de investigaciones desde la UI.** `audit.html` lista las investigaciones abiertas (pregunta + Id) pero no tiene ningún control para crearlas, actualizarlas o resolverlas — eso solo es posible hoy desde un cliente MCP (`InvestigationTools`).
* **El agrupamiento de clasificaciones dudosas es solo de presentación.** `groupMisclassified` (en `audit.html`) agrupa por descripción exacta + sugerencia en el cliente; el backend (`AuditReportService`) no tiene ningún concepto de "grupo" — cada llamada a "Mantener clasificación para los N" termina enviando N `MovementKey` individuales al mismo endpoint que la acción individual.

---

## 7. Relación con otros módulos

* **Clasificación (`movements.html`/`ClassifyMovementCommand`).** El Centro de Auditoría nunca clasifica: lee el estado ya persistido (`IMovementsQueryService`) y las sugerencias que el mismo motor de Suggestions daría hoy, y navega a `movements.html` para que la corrección real ocurra ahí. Es, en ese sentido, un consumidor de Clasificación, no un módulo paralelo con su propio camino de escritura de datos financieros.
* **Dashboard (`dashboard.html`).** No hay ningún código compartido entre ambos — el indicador de cobertura de clasificación del Dashboard (Épica L, `GET /api/metrics/classification-coverage`) y el Centro de Auditoría resuelven preguntas relacionadas pero distintas (cuánto está clasificado vs. si lo clasificado es confiable) con servicios completamente independientes (`FinancialMetricsService` vs. `AuditReportService`).
* **Planificación Mensual (`planning.html`).** Sin relación alguna verificada en el código: `AuditReportService` no referencia `PlanningMonth`/`PlanningItem` en ningún punto, y `planning.html` no consume ningún endpoint de `/api/audit/*`.
* **Importaciones (`imports.html`/`ImportBatch`).** La única conexión es de solo lectura y liviana: `GET /api/audit/status` consulta `IImportHistoryQueryService` para mostrar la última importación (archivo y fecha) como parte del panorama general — el Centro de Auditoría no dispara importaciones ni depende de `ImportBatch` para el resto de su reporte.
* **Investigaciones / memoria del MCP (`InvestigationTools`).** Relación de solo lectura: el Centro de Auditoría cuenta y lista `Investigation` existentes como una señal más del período, pero no las crea ni las modifica — esa responsabilidad es exclusiva de `InvestigationTools` (ver ADR-007).
* **Servidor MCP (`AuditTools`/`AuditDatabaseTools`).** No es un módulo externo sino la otra cara de la misma implementación: ambas tools delegan en el mismo `AuditReportService` que usa `AuditEndpoints`, así que un cliente MCP y un usuario de `audit.html` obtienen, para el mismo período, exactamente el mismo resultado.

---

*Fuente: lectura directa de `AuditReportService.cs`, `AuditEndpoints.cs`, `AuditDtos.cs`, `MovementAuditDecision.cs`, `ReviewMovementsCommand.cs`/`ReviewMovementsHandler.cs`, `AuditTools.cs`, `AuditDatabaseTools.cs` y `audit.html`, contrastada con `docs/PROJECT_STATUS.md` (§2, §5) y `docs/RoadMaps/FinancialMcp-vNext.md` (que no incluye este módulo en su roadmap por épicas I-O, ver su §1 — el Centro de Auditoría se construyó fuera de ese plan).*
