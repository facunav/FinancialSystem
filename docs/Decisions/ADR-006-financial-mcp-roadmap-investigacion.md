# ADR-006 — Financial MCP: de proveedor de métricas a compañero de investigación del sistema

**Estado:** Aceptado (roadmap; solo la Fase 1 previa —`FinancialTools`, 4 herramientas financieras— está implementada hoy).

## Contexto

`FinancialSystem.McpServer` existe hoy como host MCP funcional (SDK `ModelContextProtocol` 1.3.0, transporte stdio, con el mismo `AddApplication()`+`AddInfrastructure()` que `FinancialMcp.Api` y `FinancialSystem.Worker`). Expone una sola clase de herramientas, `FinancialTools`, con 4 tools de solo lectura sobre `IFinancialMetricsService` (`GetMonthlySummary`, `GetExpensesByCategory`, `GetMonthlyTrend`, `CompareWithPreviousMonth`), orientadas a responder preguntas financieras agregadas.

Durante el desarrollo de Review & Classification Engine v2 y de la instrumentación temporal agregada para depurar el corrimiento de `EffectiveDate` entre meses (ver `ClassifyMovementHandler`, `FinancialMetricsService`), quedó en evidencia que el mayor valor del MCP no está en repetir preguntas financieras que el dashboard ya responde, sino en poder **inspeccionar el estado interno del sistema**: por qué un movimiento quedó clasificado así, qué `EffectiveDate` terminó persistido, por qué algo no aparece en un período, qué reglas aplicó el motor de sugerencias.

## Problema

Hoy, investigar ese tipo de pregunta requiere agregar logging temporal a mano en código de producción, redeployar, leer logs, y después revertir la instrumentación — no existe una forma reutilizable de hacer estas preguntas desde afuera del sistema. Al mismo tiempo, el MCP puede crecer sin control si cada capacidad deseable (auditoría, memoria de investigaciones, asistencia con IA local) se diseña e implementa junta, sin fases ni límites — el riesgo es terminar con herramientas grandes, de múltiples responsabilidades, difíciles de probar por separado.

## Decisión tomada

El Financial MCP cambia de objetivo: pasa de ser un proveedor de métricas financieras a ser el **compañero de investigación del sistema**, priorizando la inspección del estado interno por sobre la automatización. Principios que gobiernan toda esta evolución, sin excepción:

* El razonamiento sigue del lado del cliente MCP (Claude Desktop, Claude Code, ChatGPT, etc.) — el servidor no es un agente autónomo, no corre su propio loop de decisiones.
* Las tools son pequeñas, de responsabilidad única, y mayormente de solo lectura.
* El MCP no modifica datos financieros directamente — toda escritura sigue pasando por `FinancialMcp.Api`.
* Se reutilizan servicios y reglas ya existentes (`IMovementsQueryService`, `IReviewEngine`, `ISuspicionDetector`, `IClassificationSuggestionService`) antes de crear lógica nueva.
* La complejidad se agrega solo cuando hay una necesidad concreta ya identificada — YAGNI aplica a cada fase.

### Roadmap por fases

**Fase 1 — Investigación básica.** Objetivo: poder inspeccionar completamente el estado del sistema. Tools: `Ping`, `Version`, `Health`, `SearchMovements`, `GetMovement`, `ExplainMovement`. Todas de solo lectura, sin lógica de IA.

**Fase 1.5 — Conocimiento del proyecto.** Objetivo: que el LLM entienda el dominio del proyecto sin que haya que re-explicarle la arquitectura en cada conversación. Tools: `SearchDocs`, `ReadAdr`, `ExplainConcept`, `GetArchitecture`. Reutilizan la documentación ya existente en `docs/`.

**Fase 2 — Auditoría.** Objetivo: encontrar inconsistencias automáticamente. Tools: `FindSuspiciousMovements`, `FindDuplicates`, `FindUnclassified`, exponiendo capacidades ya existentes (`ISuspicionDetector`) donde corresponda. No se agregan heurísticas nuevas si ya existen en el proyecto.

**Fase 3 — IA local.** Objetivo: agregar herramientas puntuales que usen Ollama para asistir al usuario. Tools: `AnalyzeMovement`, `AnalyzeMonth`, `SuggestCategory`, `SuggestCounterparty`. Ollama se usa únicamente desde tools específicas — el MCP no implementa un loop de agente.

**Fase 4 — Memoria.** Objetivo: incorporar memoria persistente de investigaciones (hipótesis planteadas, bugs encontrados, decisiones tomadas, investigaciones abiertas y cerradas). No forma parte del MVP. Se diseña mediante una ADR independiente cuando exista una necesidad real — este documento no fija su modelo de datos ni su mecanismo de escritura.

## Consecuencias

* `FinancialTools` (las 4 tools financieras actuales) no cambia — esta ADR no reemplaza ese contrato, lo complementa con un objetivo nuevo y distinto.
* Cada fase se implementa y se prueba por separado; no se empieza una fase sin haber cerrado (o descartado explícitamente) la anterior.
* Ninguna tool de Fase 1, 1.5 o 2 depende de Ollama ni de estado persistente propio del MCP — pueden implementarse y usarse sin que existan las fases 3 y 4.
* La Fase 4 (memoria) queda deliberadamente fuera de alcance hasta que su propia ADR la defina — este documento no debe usarse como aprobación implícita de su diseño de datos ni de su mecanismo de escritura.
* Todo PR de tools nuevas debe poder ubicarse en una de estas fases; una tool que no encaje en ninguna es señal de que hace falta ampliar esta ADR antes de implementarla, no de forzarla en la fase más parecida.
