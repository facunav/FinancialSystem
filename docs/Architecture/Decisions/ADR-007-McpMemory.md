# ADR-007 — Memoria del Financial MCP

**Estado:** Aceptado — Fases 2 a 4 implementadas, Fase 5 pendiente. ~~(documento arquitectónico de visión y principios — ninguna fase de esta ADR está implementada. La Fase 1 vigente hoy, ver ADR-006, es puramente de lectura y no tiene memoria propia)~~ **Actualización (PATCH-030):** la afirmación de que "ninguna fase está implementada" ya no es cierta — verificado contra el código, la Fase 2 (persistencia: `Investigation`/`InvestigationFinding`/`InvestigationReference`, migración `AddInvestigationPersistence`), la Fase 3 (tools `CreateInvestigation`/`LinkMovement`/`AddFinding`/`GetInvestigation`/`UpdateInvestigationStatus`/`SearchInvestigations` en `InvestigationTools`) y la Fase 4 (`AskInvestigation`, que consulta Ollama vía `ILocalAiService` con el contexto real de la investigación) están implementadas. Solo la Fase 5 (§8) sigue sin construirse. El texto tachado queda como referencia histórica de cuándo se escribió esta ADR. Reemplaza la versión anterior de este mismo ADR con un modelo conceptual más preciso; no contradice sus principios centrales.

## 1. Objetivo

**Qué problema resuelve.** Hoy cada conversación con el MCP empieza de cero: si ya se investigó por qué una contraparte llega mal clasificada, o ya se concluyó que un movimiento puntual estaba mal categorizado, esa conclusión no queda registrada en ningún lado accesible al MCP — la próxima conversación repite el mismo trabajo. La memoria resuelve esto: le da al MCP la capacidad de recordar qué se investigó antes y qué se concluyó, para que el asistente pueda decir "esto ya se investigó" en vez de reinvestigar desde cero.

**Qué problema NO resuelve.** La memoria no es una segunda fuente de verdad de datos financieros, no reemplaza ninguna consulta a la base real, y no automatiza ninguna corrección de datos. No resuelve "¿cómo está clasificado este movimiento ahora?" — esa pregunta la sigue respondiendo siempre la base real, vía las tools existentes (§6). La memoria tampoco resuelve la necesidad de una ADR formal para una regla de negocio nueva: eso sigue siendo un PR humano en `docs/Decisions/`, como hasta ahora.

## 2. Principios

La memoria del MCP:

* **nunca reemplaza la base de datos** — la base real (movimientos, clasificación, catálogos) sigue siendo, siempre, la única fuente de verdad de datos financieros;
* **nunca duplica movimientos** — no guarda una copia de un `Transaction`, `BankStatement` o `ClassifiedMovement`, solo una referencia a ellos (§4);
* **nunca guarda snapshots financieros** — ningún campo financiero (importe, moneda, categoría actual, etc.) se copia a memoria en ningún momento, ni siquiera como parte de una investigación o un hallazgo;
* **siempre referencia entidades existentes** — toda mención a un movimiento, cuenta, categoría o contraparte se hace por identificador real (§4), nunca repitiendo su contenido;
* **puede quedar obsoleta y debe indicarlo** — una investigación describe una interpretación en un momento dado; si la base real cambió después (por ejemplo, el usuario reclasificó el movimiento), la memoria no se actualiza sola ni se borra sola — sigue existiendo con su fecha original, y es tarea de quien la lee (humano o IA) notar que puede estar desactualizada frente al dato real vigente;
* **debe poder reconstruirse consultando nuevamente la base** — si la memoria completa se perdiera, el sistema financiero seguiría siendo consistente y completo; la memoria es un complemento de interpretación, nunca una dependencia para que los datos financieros tengan sentido.

## 3. Modelo conceptual

Sin modelo de datos ni clases — solo los conceptos que la memoria maneja y cómo se relacionan entre sí.

**Investigación.** La unidad central de la memoria: una pregunta o hipótesis abierta durante una conversación con el MCP, junto con su desarrollo a lo largo del tiempo y su conclusión, si llegó a tener una. Ejemplo: *"¿Por qué los movimientos de esta contraparte llegan sin categoría asignada?"*

**Hallazgo.** Una observación puntual encontrada en el curso de una investigación — una interpretación sobre un dato, nunca el dato en sí. Ejemplo: *"El movimiento X parece mal clasificado según el patrón habitual de esta contraparte."* Un hallazgo pertenece a una investigación y puede señalar una o más referencias.

**Referencia.** El vínculo entre un elemento de memoria (una investigación o un hallazgo) y una entidad real del sistema financiero — un movimiento, una cuenta, una categoría, una contraparte. La referencia apunta al dato, nunca lo copia (§2); es lo que permite que la memoria se reconstruya o se contraste contra la base real en cualquier momento.

**Estado.** La situación actual de una investigación dentro de su ciclo de vida (§5) — distingue lo que sigue abierto de lo ya resuelto o descartado, para que la memoria vieja no se lea como vigente por defecto.

**Etiquetas.** Palabras o frases cortas asociadas libremente a una investigación para agruparla temáticamente — por ejemplo, todas las investigaciones relacionadas con una misma tarjeta o una misma contraparte — y facilitar encontrarla más tarde por búsqueda libre, sin depender de recordar su identificador exacto.

**Historial.** La secuencia de cambios de una investigación a lo largo del tiempo: cuándo cambió de estado, qué hallazgos se le fueron agregando. Permite reconstruir cómo se llegó a una conclusión, no solo cuál fue.

## 4. Referencias a movimientos

Toda referencia de memoria a un movimiento usa exclusivamente la convención de identificación que ya existe en el proyecto: **`SourceEntityType` + `SourceId`** — la misma que ya usan `ClassifiedMovementItem`, `GetMovement`, `ExplainMovement` y `ExplainClassification`. No se inventa una nueva forma de identificar un movimiento para la memoria.

## 5. Estados

Modelo conceptual de estados por los que atraviesa una investigación:

* **Abierta** (`InvestigationStatus.Open`) — la investigación se creó, todavía no tiene desarrollo.
* **En progreso** (`InProgress`) — tiene desarrollo (uno o más hallazgos) pero todavía no llegó a una conclusión.
* **Resuelta** (`Resolved`) — llegó a una conclusión.
* **Descartada** (`Discarded`) — se determinó que no amerita seguir investigándose, sin haber llegado a una conclusión.

**Actualización (PATCH-030):** la Fase 3 (§8) ya está implementada — `UpdateInvestigationStatusHandler` confirma cómo quedaron resueltas las transiciones: no hay un grafo de transiciones válidas, cualquier estado puede pasar a cualquier otro; la única validación es que `Conclusion` es obligatoria al pasar a `Resolved` (y solo se guarda en ese caso). "Quién puede dispararlas" tampoco quedó restringido — cualquier llamador con acceso a la tool MCP `UpdateInvestigationStatus` puede cambiar el estado de cualquier investigación. Si en el futuro se necesita una máquina de estados más estricta, corresponde una ADR nueva que lo decida explícitamente, no una extensión silenciosa de esta.

## 6. Relación con el resto del MCP

* **MovementTools** (`SearchMovements`, `GetMovement`, `ExplainMovement`, `ExplainClassification`): la memoria las complementa, nunca las reemplaza. Dado un movimiento, la memoria puede responder *"¿ya investigamos esto antes?"*, pero el estado real de clasificación sigue viniendo siempre en vivo de estas tools — nunca de lo guardado en memoria.
* **AuditTools** (`FindSuspiciousMovements`, `FindMisclassifiedMovements`): las reglas de detección en sí siguen siendo objetivas y fijas, sin memoria ni IA, y eso no cambia con esta ADR. **Actualización (PATCH-030):** `AuditReportService` (el reporte completo, no las reglas de detección) ya incluye un conteo y listado de investigaciones abiertas/resueltas junto al resto de los hallazgos — es una superficie compartida en el mismo reporte, no una señal de auditoría nueva derivada del historial de investigaciones. Recién en la Fase 5 (§8) el historial acumulado de investigaciones podrá alimentar nuevas señales de auditoría propiamente dichas — como sugerencia para revisión humana, nunca como corrección automática.
* **ProjectTools** (`ListArchitectureDocuments`, `ReadArchitectureDocument`, `SearchDocumentation`, `GetRoadmap`): siguen siendo la forma de leer el conocimiento ya consolidado del proyecto. La memoria referencia ese conocimiento cuando corresponde (ej. una investigación puede decir "ver ADR-003") en vez de repetirlo con sus propias palabras.
* **ConfigurationTools** (`ListFinancialAccounts`, `ListCategories`, `ListCounterparties`, `GetCounterparty`, `SearchCounterparties`): la memoria puede referenciar una cuenta, categoría o contraparte por su identificador real; estas tools siguen siendo la forma de resolver ese identificador a su configuración vigente.

## 7. Relación con Ollama

Ollama nunca consulta la base de datos directamente, en ninguna fase. Toda información que Ollama usa para razonar le llega exclusivamente a través de tools del MCP — el mismo principio que ya rige para el MCP en general. La memoria es una fuente de contexto más entre las que Ollama recibe a través de tools: agrega antecedentes (por ejemplo, que un caso similar ya se investigó y qué se concluyó), pero no reemplaza ni modifica el razonamiento del modelo. Ollama sigue decidiendo qué hacer con esa información, igual que con el resultado de cualquier otra tool.

**Actualización (PATCH-030):** esto ya no es un principio a futuro — `AskInvestigation` (`InvestigationTools`) lo implementa exactamente así: arma un contexto con el catálogo de tools (`ToolRegistry.ToLlmCatalog()`), la investigación completa (estado, pregunta, conclusión, hallazgos) y el detalle de cada movimiento referenciado (vía `IMovementLookupService`), y hace una única llamada a `ILocalAiService.AskAsync` — sin escribir nada en la investigación ni encadenar llamadas.

## 8. Roadmap

* ✅ **Fase 2 — Persistencia de investigaciones.** Las investigaciones (§3) dejan de vivir solo en la conversación y empiezan a persistir entre sesiones. **Implementada (PATCH-030):** `Investigation`/`InvestigationFinding`/`InvestigationReference` (`Domain/Memory`), migración `AddInvestigationPersistence`.
* ✅ **Fase 3 — Tools para crear, actualizar y consultar investigaciones.** El MCP expone tools para registrar una investigación nueva, agregarle hallazgos, cambiar su estado (§5) y consultarla — por movimiento referenciado o por búsqueda libre. **Implementada (PATCH-030):** `CreateInvestigation`, `LinkMovement`, `AddFinding`, `GetInvestigation`, `UpdateInvestigationStatus`, `SearchInvestigations` (`InvestigationTools`).
* ✅ **Fase 4 — Integración con Ollama.** Ollama empieza a poder usar la memoria como contexto adicional (§7), sobre información que ya le llega a través de tools. **Implementada (PATCH-030):** `AskInvestigation` (`InvestigationTools`), vía `ILocalAiService`.
* 📋 **Fase 5 — Auditorías inteligentes basadas en memoria.** Nuevas señales de auditoría que aprovechan el historial acumulado de investigaciones (§6, AuditTools) — siempre como sugerencia para revisión humana, nunca como corrección automática. **Pendiente (verificado PATCH-030):** `AuditReportService` ya lista investigaciones abiertas/resueltas dentro del reporte general (§6), pero eso no equivale a esta fase — no hay todavía ninguna señal de auditoría *derivada* del historial de investigaciones.

## 9. Consecuencias

* El MCP sigue sin escribir datos financieros en ninguna fase de esta ADR. **Verificado (PATCH-030):** con la memoria ya implementada (Fases 2-4), ningún Command de `Investigations` toca `Transactions`/`BankStatements`/`ClassifiedMovements` — el principio se sostuvo en la implementación real, no solo en el diseño.
* Ninguna investigación o hallazgo puede diseñarse sin referenciar sus movimientos con la convención `SourceEntityType` + `SourceId` (§4) — no se introduce una identificación nueva.
* `docs/` (ADRs, Architecture.md) sigue siendo la única fuente de verdad de convenciones y reglas ya consolidadas; la memoria las referencia, nunca las duplica.
* Ninguna fase del roadmap (§8) se implementa por el solo hecho de llegar a ella — cada una necesita su propio diseño técnico y su propio PR, evaluados con el mismo criterio YAGNI que ya aplicó cada tool existente del MCP.
* Si en el futuro aparece evidencia de que alguno de estos principios no funciona en la práctica, corresponde un ADR nuevo que reemplace explícitamente a este, no una extensión silenciosa por acumulación.
