# ADR-007 — Memoria del Financial MCP

**Estado:** Aceptado (documento de visión y principios; ninguna fase de esta ADR está implementada. La Fase 1 que describe §11 es la ya construida — Ping/Version/Health/SearchMovements/GetMovement/ExplainMovement/FindSuspiciousMovements/FindMisclassifiedMovements, ver ADR-006 — que es puramente de lectura, sin memoria).

## 1. Contexto

ADR-006 fijó el roadmap del Financial MCP y, en su Fase 4 ("Memoria"), anticipó explícitamente esta necesidad sin diseñarla: *"incorporar memoria persistente de investigaciones... no forma parte del MVP. Se diseña mediante una ADR independiente cuando exista una necesidad real. Este documento no fija su modelo de datos ni su mecanismo de escritura."* Esta ADR es esa pieza pendiente.

La visión de largo plazo es que el MCP deje de ser solo un conjunto de herramientas de consulta puntual y se convierta en un compañero permanente del proyecto: capaz de recordar qué se investigó antes, qué se concluyó, y no obligar a repetir una investigación ya resuelta en una conversación anterior.

## 2. Problema

Diseñar memoria para un sistema que además es la fuente de verdad de datos financieros reales tiene un riesgo concreto y no hipotético: que la memoria termine actuando como una segunda fuente de verdad que puede desincronizarse en silencio de los datos reales. Ejemplo directo: si memoria guardara *"el movimiento X está mal clasificado, debería ser Categoría=Salud"* como un hecho, y después el usuario lo reclasifica correctamente desde `movements.html`, la memoria nunca se entera — un LLM que confíe en ella sin volver a consultar el dato real repetiría información obsoleta como si fuera vigente. Todo el diseño de esta ADR gira alrededor de evitar exactamente ese escenario.

El segundo riesgo es el opuesto: diseñar las 5 fases de §11 con nivel de detalle de implementación hoy, sin evidencia de uso real de las fases tempranas, sería la sobreingeniería que el proyecto viene evitando desde ADR-006. Esta ADR fija principios y contratos conceptuales estables — qué se recuerda, qué no, dónde vive, cómo se evita la contradicción — no el modelo de datos ni las tools exactas de cada fase. Eso se diseña recién al implementar cada fase, con la misma disciplina YAGNI que ya aplicó cada PR de la Fase 1.

## 3. Principio central: la memoria guarda interpretación, nunca dato

Todo lo demás en esta ADR se deriva de una sola regla:

> **La memoria del MCP nunca almacena una copia de un valor financiero. Solo almacena una referencia a ese valor (`SourceEntityType` + `SourceId`, o el `Id` de un `ClassifiedMovement` — la misma convención de identificación que ya usan `ClassifiedMovementItem`, `GetMovement` y `ExplainMovement`, no una nueva) junto con la interpretación humana o de IA sobre ese dato, con su propia fecha y su propio estado.**

Como consecuencia directa: la memoria nunca puede "tener razón" o "estar mal" sobre un dato financiero, porque nunca pretende ser ese dato — es un registro de qué se pensó en un momento dado. Cualquier tool que combine memoria con estado financiero actual (§9) resuelve el estado actual en vivo contra la base real, nunca desde lo guardado en memoria.

## 4. Qué debe recordar el MCP

* **Investigaciones** (Fase 2, §11): una pregunta de investigación, su desarrollo, y su conclusión — ej. *"Analizamos el movimiento X y concluimos que estaba mal clasificado; se corrigió el DD/MM."* Incluye hipótesis descartadas en el camino, no solo la conclusión final.
* **Observaciones sueltas** (Fase 2-3): hallazgos puntuales que todavía no ameritan una investigación formal ni una decisión — ej. *"Los movimientos de esta contraparte casi siempre llegan sin descripción útil."*
* **Decisiones funcionales informales** (Fase 3): acuerdos entre el usuario y el modelo sobre cómo interpretar un caso ambiguo del dominio, cuando ese acuerdo todavía no se consolidó en una ADR formal en `docs/Decisions/`.
* **Contexto de convenciones y reglas de negocio ya consolidadas — por referencia, no por copia** (Fase 3): la memoria debe poder decir *"ver ADR-003"* al explicar por qué un pago de resumen no cuenta como gasto; no debe volver a explicar la regla con sus propias palabras. Duplicar contenido que ya vive en `docs/` crea dos copias que pueden divergir con el tiempo — exactamente el problema de §2 aplicado a documentación en vez de a datos. `docs/` sigue siendo la única fuente de verdad de lo consolidado; la memoria es la fuente de lo que todavía está en proceso de consolidarse.

## 5. Qué nunca debe recordar

* **Ninguna copia de campos financieros** (`Description`, `Amount`, `Currency`, `CategoryId` actual, etc.) — solo la referencia al movimiento (§3). Si una tool necesita mostrar esos valores junto con una nota de memoria, los pide en vivo a `IMovementLookupService`/`IMovementsQueryService` (los mismos servicios que ya usan `GetMovement`/`SearchMovements`), no los lee de memoria.
* **Ningún valor "correcto" de clasificación como hecho asentado.** Una investigación puede concluir *"pensamos que debería ser Categoría=Transporte"* — eso es una interpretación con fecha, no un nuevo valor de verdad. Corregir el dato real sigue siendo, siempre, una reclasificación humana vía `ClassifyMovementCommand` (§10).
* **Reglas de negocio ya formalizadas en una ADR** — se referencian, no se copian (§4).
* **Credenciales, connection strings, tokens, o cualquier dato de configuración de infraestructura** — la memoria es memoria de dominio y de investigación, no un lugar para secretos operativos.
* **Nada fuera del dominio financiero de este proyecto.** El MCP es un sistema personal de un solo usuario (ver Architecture.md) — no hay riesgo hoy de mezclar datos de terceros, pero el principio queda fijado para cuando la memoria empiece a acumular contenido: solo contenido relevante a investigar y entender *este* sistema.

## 6. Dónde vive cada cosa

| | Vive en | Se escribe vía |
|---|---|---|
| Movimientos, clasificación, catálogos (`Transaction`, `BankStatement`, `ClassifiedMovement`, `Category`, `Counterparty`, `FinancialAccount`) | Base de FinancialMcp (Postgres, `AppDbContext`, ya existente) | `FinancialMcp.Api` (`ClassifyMovementCommand` y el resto de los endpoints ya existentes) — el MCP nunca escribe acá directamente, ver §10 |
| Convenciones y reglas de negocio consolidadas | `docs/` (Architecture.md, ADRs) | Un PR humano, como hasta ahora — la memoria no genera ADRs por su cuenta |
| Investigaciones, observaciones, decisiones informales | Memoria del MCP (tablas nuevas, mismo motor Postgres que ya usa `AppDbContext` — no una base ni un motor de persistencia nuevo, ver §8) | Tools de escritura del propio MCP, acotadas a estas tablas (§10) |

La memoria vive en el mismo Postgres por la misma razón que ya justificó no separar `FinancialSystem.McpServer` en su propia infraestructura: reutilizar lo que ya existe (conexión, migraciones vía `DatabaseMigrationExtensions`, `IApplicationDbContext`) en vez de introducir un motor de persistencia nuevo (SQLite local, un vector store, lo que sea) sin necesidad concreta que lo justifique — exactamente el criterio YAGNI que ya vino aplicando cada PR de la Fase 1. Las tablas de memoria son nuevas y separadas de las financieras, sin FK explícita hacia ellas — el mismo patrón sin-FK que ya usa `ClassifiedMovementItem` para referenciar `Transaction`/`BankStatement` (`SourceEntityType` + `SourceId`), reutilizado acá en vez de inventar una convención de referencia nueva.

## 7. Cómo se evita que la memoria contradiga los datos reales

Tres reglas, todas derivadas de §3:

1. **La memoria nunca es la fuente que responde "¿cómo está clasificado esto ahora?"** — esa pregunta la responde siempre `GetMovement`/`ExplainMovement` contra la base real. Una tool de memoria (Fase 2) que muestre una investigación sobre un movimiento debe mostrar la interpretación histórica *junto con* — no en lugar de — el estado real actual obtenido en vivo, dejando que el LLM note la discrepancia si la hay en vez de que el sistema intente resolverla automáticamente.
2. **Toda entrada de memoria tiene un tipo explícito** — Hipótesis, Observación, Decisión o Conclusión — y nunca se mezcla con el dato financiero real en la misma estructura. El objetivo funcional lo pide explícitamente ("siempre debe diferenciar datos reales / hipótesis / observaciones / decisiones humanas"): ese campo es la forma concreta de cumplirlo.
3. **Toda entrada de memoria tiene fecha y estado** (ej. Abierta / Cerrada / Descartada para una investigación) — nada en memoria se trata como válido indefinidamente por defecto.

## 8. Cómo se actualiza

Vía tools de escritura nuevas del propio MCP (Fase 2 en adelante), acotadas exclusivamente a las tablas de memoria — nunca a datos financieros. Esto es una excepción deliberada y acotada a la regla "toda escritura pasa por la API existente": esa regla gobierna datos financieros, que sí tienen una Api existente (`FinancialMcp.Api`) con reglas de negocio que no hay que duplicar. La memoria no es un dato financiero y no tiene ninguna Api existente que rutear artificialmente — inventar ese salto agregaría una capa sin ningún beneficio (la misma sobreingeniería que ADR-006 evitó en cada PR de la Fase 1). El patrón de implementación es el que ya usa todo el proyecto: contrato en Application, implementación en Infrastructure, registrado en `AddInfrastructure`, consumido por una tool delgada — el mismo patrón que `IMovementLookupService`, no uno nuevo.

Consecuencia directa de esto y de §5: **el MCP sigue sin escribir datos financieros nunca, en ninguna fase de esta ADR.** Ninguna tool de memoria, presente o futura, reclasifica un movimiento ni toca `ClassifiedMovement`. Corregir un dato financiero real, incluso cuando la corrección nace de una conclusión guardada en memoria, sigue siendo una acción humana a través de la Api existente.

## 9. Cómo se consulta

Tools de lectura nuevas (Fase 2 en adelante) que siguen la misma convención de identificación e integración que ya usan las tools de la Fase 1 — no una nueva. Dos formas naturales de consulta, ambas extensiones directas de tools ya construidas:

* **Por movimiento**: dada la misma identificación que ya usa `GetMovement`/`ExplainMovement` (`SourceEntityType` + `SourceId`), devolver la memoria asociada a ese movimiento — el complemento natural de `ExplainMovement`, para responder *"¿ya investigamos esto antes?"* antes de volver a investigar desde cero.
* **Por búsqueda libre**: encontrar investigaciones/observaciones por texto o por período, para retomar una conversación — *"seguimos con lo de VISA"* — sin que el usuario tenga que recordar el identificador exacto.

Mismo principio de salida que ya rige toda la Fase 1: texto estructurado y estable, no JSON crudo, sin lenguaje natural de relleno (ver el criterio ya aplicado en `ExplainMovement`/`FindMisclassifiedMovements`) — no uno nuevo para memoria.

## 10. Cómo se elimina

La memoria debe poder eliminarse siempre — nunca debe volverse una verdad implícita solo por acumularse con el tiempo. Esta ADR fija el principio, no el mecanismo exacto (diseñarlo en detalle hoy, sin una sola fila de memoria escrita todavía, sería especular sobre un problema que no existe aún):

* Debe existir borrado explícito de una entrada o investigación puntual, iniciado por el usuario.
* Vale la pena evaluar, al implementar la Fase 2, si observaciones nunca promovidas a investigación o decisión deberían expirar solas después de un tiempo — pero esa política concreta (cuánto tiempo, con qué aviso) se decide con datos reales de uso, no de antemano.
* Ninguna eliminación de memoria afecta jamás datos financieros — es, por diseño (§3), imposible que lo haga: la memoria no contiene el dato, solo la referencia y la interpretación.

## 11. Roadmap por fases

Estas fases son un track paralelo al roadmap de tools de ADR-006, no una renumeración de ese roadmap — cubren específicamente la evolución de memoria/IA, mientras que ADR-006 cubre qué tools de investigación y auditoría existen. La relación exacta con las fases de ADR-006 se detalla en §12.

**Fase 1 — Sin memoria.** La ya construida: Ping/Version/Health, SearchMovements, GetMovement, ExplainMovement, FindSuspiciousMovements, FindMisclassifiedMovements. Puramente de lectura, sin ningún estado propio del MCP. Vigente hoy.

**Fase 2 — Memoria persistente de investigaciones.** Tablas nuevas (§6), tools de escritura acotadas a ellas (§8), tools de consulta por movimiento y por texto (§9). Ejemplo del objetivo funcional: *"Analizamos el movimiento X y concluimos que estaba mal clasificado."*

**Fase 3 — Memoria funcional del proyecto.** Extiende la Fase 2 a convenciones, decisiones informales, reglas de negocio en proceso de consolidarse y excepciones conocidas (§4) — siempre por referencia a `docs/` cuando ya están formalizadas, nunca por copia (§4, §5).

**Fase 4 — Integración con Ollama.** Ollama como proveedor de IA para interpretar información ya obtenida vía tools — nunca un agente embebido dentro del MCP. El MCP sigue siendo, en esta fase también, exclusivamente un proveedor de tools; el razonamiento y la decisión de qué tool llamar siguen del lado del cliente MCP. Esto no es una decisión nueva de esta ADR: es exactamente el principio que ADR-006 ya fijó para su propia Fase 3 ("IA local"), reafirmado acá porque memoria + Ollama es precisamente el escenario donde más tienta embeber un loop de agente — y sigue sin corresponder.

**Fase 5 — Auditorías inteligentes.** Distinta de `FindSuspiciousMovements`/`FindMisclassifiedMovements` (ADR-006, ya implementadas): esas son reglas objetivas fijas, sin memoria ni IA. Esta fase usa memoria (Fases 2-3) e IA (Fase 4) para encontrar patrones nuevos, anomalías y reglas aprendidas del historial de investigaciones — siempre como sugerencia para que un humano evalúe, nunca como corrección automática. Depende de que Fases 2-4 ya existan y tengan volumen real de datos; no tiene sentido diseñar su detalle antes de eso.

## 12. Relación con ADR-006

| Esta ADR | ADR-006 |
|---|---|
| Fase 1 (sin memoria) | Fase 1 completa (Ping...FindMisclassifiedMovements) |
| Fases 2-3 (memoria) | Fase 4 ("Memoria") — esta ADR es, en concreto, esa fase expandida |
| Fase 4 (Ollama) | Fase 3 ("IA local") — mismo principio, reafirmado en el contexto de memoria |
| Fase 5 (auditorías inteligentes) | No tiene equivalente en ADR-006 — capacidad nueva, solo posible una vez que memoria e IA (Fases 2-4 de esta ADR) ya existen |

## 13. Consecuencias

* El MCP sigue sin escribir datos financieros en ninguna fase — eso no cambia ni siquiera cuando el MCP empiece a escribir memoria (Fase 2). Son dos escrituras de naturaleza distinta, con caminos distintos (§8).
* Ninguna tool de memoria puede diseñarse ni implementarse sin identificar sus movimientos referenciados con la misma convención `SourceEntityType`+`SourceId` que ya usan `GetMovement`/`ExplainMovement` — no se introduce una identificación nueva.
* `docs/` (ADRs, Architecture.md) sigue siendo la única fuente de verdad de convenciones y reglas consolidadas; la memoria del MCP nunca las duplica, solo las referencia.
* Ninguna fase de §11 se implementa por defecto al llegar a ella — cada una necesita su propio PR (o serie de PRs) evaluado con el mismo criterio YAGNI que ya aplicó cada PR de la Fase 1, y puede requerir precisar detalles que esta ADR deja abiertos a propósito (§10, mecanismo exacto de expiración; el modelo de datos exacto de cada fase).
* Si en el futuro aparece evidencia de que alguno de estos principios no funciona en la práctica, corresponde un ADR nuevo que reemplace explícitamente a esta, no una extensión silenciosa por acumulación — mismo criterio que ya fija ADR-001 para las 4 dimensiones de clasificación.
