# PR-S6 — Análisis del siguiente paso del motor de sugerencias

> Documento de análisis puro. Sin código, sin patch, sin cambios al repositorio. Basado en lectura directa de `origin/master` en `8f2764a` (PR-S5.1 mergeado). Cada afirmación está anclada a un archivo real.

---

## 1. ¿Qué siguiente heurística agregarías primero?

Evalué cada candidato contra el código real (qué datos existen hoy, qué tan cerca están del patrón ya probado) y contra el principio que guio todo Épica S hasta acá: reglas determinísticas, sin IA todavía.

| Candidato | Valor | Riesgo/Costo | Nota |
|---|---|---|---|
| **Enriquecimiento vía `Counterparty.Default*`** | Muy alto | Muy bajo | Ver detalle abajo — no es una heurística nueva sobre historial, es conectar un mecanismo ya modelado y curado. |
| **Normalización de descripción más agresiva** | Alto | Bajo-medio | Extiende el patrón exacto que ya funciona (PR-S3), mismo query, misma forma. Riesgo: falsos positivos si se recorta demasiado. |
| **Monto + periodicidad aproximada** | Alto | Alto | La señal más potente para gastos fijos recurrentes (alquiler, suscripciones), pero la más cara de calcular bien — ya identificada como tal en el análisis de PR-S1. |
| **Categoría histórica generalizada** (más allá de descripción exacta) | Medio | Alto | Depende de resolver primero una Contraparte por otra vía (huevo-gallina) — no es independiente de los anteriores. |
| **Cuenta bancaria** | N/A | N/A | Ver hallazgo — **no aplica al modelo actual**, no es una dimensión de este motor. |
| **Tipo de movimiento / Impacto financiero como señales nuevas** | — | — | Ya están cubiertas por la heurística de PR-S3 (`BuildSuggestions` ya las produce). No hay nada nuevo que agregar acá. |
| **Frecuencia histórica** | Alto | Alto | Mismo caso que "monto + periodicidad" — son la misma familia de señal. |
| **Recencia** | — | — | Ya está parcialmente incorporada (`MostRecent` como desempate). Fortalecerla es un ajuste de la heurística existente, no una nueva. |

### Enriquecimiento vía `Counterparty.Default*` — por qué lo pongo primero

Leyendo `Counterparty.cs` (Domain/Entities): la entidad ya tiene `DefaultCategoryId`, `DefaultMovementType`, `DefaultFinancialImpact` — campos explícitamente pensados para esto ("Cuando el usuario selecciona esta contraparte al clasificar un movimiento, la UI propone estos valores"). Confirmé que **hoy no los usa nadie**: ni `ClassificationSuggestionService`, ni `openClassifyModal` en `movements.html` (su precarga es `movement.categoryId ?? suggestionValue(...) ?? ''`, sin ninguna referencia a `Counterparty.Default*`). Es un mecanismo completo, modelado desde antes de la Épica S, con dato curado explícitamente por el usuario (no inferido de un historial ruidoso), y con cero consumidores en todo el sistema — el mismo hallazgo apareció en el análisis de PR-S1 y sigue sin resolverse.

La forma concreta (sin escribir código, solo la idea): si la heurística de PR-S3 ya encuentra una sugerencia de Contraparte para un movimiento (via descripción exacta), un paso adicional puede resolver esa `Counterparty` y, si tiene `Default*` seteados, agregar/reforzar sugerencias de Categoría/Tipo/Impacto a partir de ahí — una fuente adicional de señal, más confiable que un empate de historial porque es una decisión explícita y ya tomada por el usuario al configurar la contraparte.

**Por qué no elijo la normalización de descripción como primera:** es un excelente segundo paso (más cobertura sobre la MISMA heurística), pero el enriquecimiento por Contraparte tiene menor riesgo de falsos positivos (no hay ambigüedad: si el usuario configuró `Default*`, es una instrucción explícita) y cierra una brecha de producto ya señalada dos veces sin acción. Lo dejo como PR-S7 en el roadmap (sección 8).

### Hallazgo: "cuenta bancaria" no es una dimensión de este motor hoy

Revisé `ClassifiedMovementConfiguration.cs`: `ClassifiedMovement` **no tiene columna `FinancialAccountId`**. La cuenta financiera se asigna a `Transaction`/`BankStatement` directamente, por un endpoint separado (`PUT /api/{bank-statements|transactions}/{id}/financial-account`), sin relación con `ClassifyMovementCommand` ni con `SuggestionDimension` (que solo tiene `Category`/`MovementType`/`FinancialImpact`/`Counterparty`). Sugerir una cuenta no encaja en el modelo de clasificación actual — requeriría una dimensión nueva y un mecanismo de aplicación distinto (no es un campo del modal de clasificar). Lo marco como hallazgo, no como candidato viable dentro de este motor tal como está diseñado hoy.

---

## 2. ¿Cómo debería convivir más de una regla?

El contrato ya impone una restricción fuerte que simplifica esta pregunta: `ClassificationSuggestionSet.Suggestions` es, por diseño, "a lo sumo una por dimensión" (doc-comment de `ClassificationSuggestionSet.cs`). Sin importar cuántas reglas corran internamente, la salida final tiene que colapsar a eso.

Analizando las alternativas que listaste:

- **Primera que encuentra (short-circuit):** simple, pero pierde información — dos reglas de confianza media que coinciden en el mismo valor son, combinadas, más convincentes que ganar por default de orden de ejecución. Además reintroduce el mismo problema de "orden implícito no garantizado" que ya identificamos y corregimos del lado del frontend en PR-S5.1 — no tiene sentido resolverlo en el cliente y reintroducirlo en el servidor.
- **Todas generan sugerencias, sin fusión:** rompe la invariante documentada del contrato (podría devolver 2 sugerencias de Categoría) — es exactamente el escenario contra el que PR-S5.1 ya blindó al frontend, precisamente *porque* nada del lado del servidor lo impedía.
- **Pipeline secuencial mutable** (cada regla ve y modifica el resultado de la anterior): acopla el resultado al orden de registro de forma opaca — difícil de razonar, difícil de testear cada regla aislada.
- **Reglas independientes + fusión centralizada (mi recomendación):** cada regla determinística es una función pura `movimientos → sugerencias crudas`, sin saber nada de las demás. Un paso final (no cada regla) fusiona el resultado combinado aplicando el mismo criterio que ya diseñamos para el frontend en PR-S5.1: mayor `SuggestionConfidence` gana; en empate, orden de evaluación de las reglas actúa como prioridad implícita y documentada.

**Ventaja concreta de este enfoque:** reglas testeables en aislamiento, se agregan o quitan sin tocar las demás, y el criterio de fusión vive en un solo lugar auditable — no repartido implícitamente entre N reglas que "saben" cuándo ceder el paso a otra.

---

## 3. ¿Cómo evitar conflictos? (Categoría A vs Categoría B)

Es la misma pregunta que el punto 2 aplicada a un caso concreto: cuando dos reglas *distintas* (no dos filas de historial, como hoy) proponen valores *distintos* para la misma dimensión, el criterio de resolución tiene que ser explícito, no accidental.

Recomendación concreta: portar al backend el mismo criterio que ya implementamos en el frontend (`normalizeSuggestions`, PR-S5.1) — mayor confianza gana, empate por orden. Esto **no vuelve redundante** la deduplicación del frontend: son dos capas de defensa distintas (el backend garantiza el contrato en su propio límite; el frontend se protege de cualquier implementación de backend, presente o futura, que no lo respete perfectamente). Mantener ambas es defensa en profundidad, no duplicación dañina.

Punto adicional que no estaba en tu lista: cuando dos reglas empatan en confianza pero difieren en valor, el orden en que se **registran/evalúan** las reglas termina actuando como prioridad de desempate. Eso significa que el mecanismo de composición (sea cual sea su forma final) necesita que ese orden sea explícito y documentado desde el día uno — no un detalle incidental de en qué lista aparecen.

---

## 4. ¿Cómo aprovechar `SuggestionConfidence`? ¿Sigue siendo suficiente?

**Hallazgo concreto:** revisé `ClassificationSuggestionService.AddDimensionSuggestion` — hoy solo asigna `SuggestionConfidence.High` o `.Medium`. **`Low` existe en el enum pero ningún código lo produce todavía.** No es un error: el propio doc-comment de `SuggestionConfidence.cs` ya anticipa que es una escala compartida por implementaciones futuras, no algo que la primera heurística necesitara agotar.

**¿Sigue siendo suficiente con varias reglas?** Como escala de *presentación* al usuario, sí — 3 niveles legibles en un chip siguen siendo razonables; más granularidad sería ruido visual sin valor real. Como *mecanismo de desempate* entre reglas de distinta naturaleza (punto 2/3), también sigue siendo suficiente, **siempre que cada regla nueva documente explícitamente qué condición produce cada nivel para ella** — mismo estilo que ya usa `AddDimensionSuggestion` hoy (unánime = High, dividido = Medium). El riesgo no es el tamaño de la escala, es que dos reglas usen "High" con criterios de certeza no comparables entre sí — eso es disciplina de diseño al escribir cada regla, no algo que resolver ampliando el tipo. No tocaría el enum ahora — reabrir esa discusión sin una segunda regla real sería la misma abstracción prematura que PR-S1/PR-S2 ya evitaron deliberadamente al elegir ordinal sobre un score continuo.

---

## 5. ¿Conviene introducir `IClassificationSuggestionRule` ya?

Leyendo el código actual: hoy hay **una sola implementación** de `IClassificationSuggestionService`, con una sola heurística, y `BuildSuggestions`/`AddDimensionSuggestion` son métodos privados estáticos — no existe ningún punto de extensión intermedio hoy, ni falta: nadie lo necesita todavía.

**Mi respuesta: es demasiado pronto.** Introducir una interfaz de "regla" ahora, sin una segunda implementación real que valide su forma, es exactamente el tipo de abstracción prematura que este proyecto evitó explícitamente en cada paso previo — el ejemplo más directo es el propio diseño de `ClassificationSuggestion` en PR-S1, done a propósito "sin una segunda implementación real que la valide". No hay evidencia todavía de que todas las reglas futuras vayan a tener la misma forma: la heurística de monto+periodicidad (candidata #3 en la sección 1) probablemente necesite trabajar sobre el lote agregado, no por movimiento aislado — la misma pregunta de diseño que ya se resolvió para `IClassificationSuggestionService` en sí (`SuggestAsync` por lote, no por movimiento, documentado explícitamente en su doc-comment). Adivinar esa forma ahora, para una interfaz interna, sería repetir el mismo error sin la ventaja de tener el caso real en la mano.

**Recomendación concreta:** implementar la segunda heurística real (PR-S7, ver roadmap) directamente dentro de `ClassificationSuggestionService`, como un método privado hermano de `BuildSuggestions` — no como una regla plugueable. Recién con dos implementaciones reales conviviendo, extraer la interfaz que de verdad les sirva a ambas, con evidencia en mano en vez de una suposición.

---

## 6. Rendimiento

Hoy `ClassificationSuggestionService.SuggestAsync` hace **una sola consulta** por invocación (que a su vez es una por request a `GET /api/movements`): trae todo `_db.ClassifiedMovements` proyectado (sin tracking) y agrupa/filtra en memoria. Confirmé en `ClassifiedMovementConfiguration.cs` que no hay índice sobre `Description` — decisión ya tomada conscientemente en PR-S3 dado el volumen esperado (sistema personal, un solo usuario).

**¿Sigue siendo válido con varias reglas?** Depende de si cada regla nueva **reutiliza la misma carga** ya hecha, o dispara su propia consulta paralela:
- El enriquecimiento por `Counterparty.Default*` (candidato #1) agrega una consulta *chica* adicional (a `Counterparties`, filtrada por los ids ya encontrados en el historial) — sigue siendo "una consulta grande + una consulta chica", no N consultas.
- La normalización más agresiva (candidato #2) puede reutilizar exactamente el mismo `history` ya cargado, solo cambiando cómo se indexa/agrupa en memoria — costo marginal es CPU, no I/O nuevo.
- Monto + periodicidad (candidato #3) también puede trabajar sobre el mismo dataset ya en memoria, aunque con más trabajo de CPU por explorar combinaciones — sigue sin requerir I/O adicional al de hoy.

El riesgo real no es "más reglas" — es que la tabla `ClassifiedMovements` crezca lo suficiente (con los años, un usuario real acumulando historial) para que cargarla completa deje de ser barato. Eso **ya estaba identificado y aceptado explícitamente** en el propio comentario de `ClassificationSuggestionService.cs`: *"Si el volumen... crece lo suficiente... un PR futuro puede agregar una columna normalizada indexada... decisión a tomar con datos reales, no de antemano."*

**¿Cambiaría algo ahora? ¿O esperaría?** Esperaría. El patrón actual sigue siendo válido para una o dos reglas más, con la única condición de que cada regla nueva comparta la carga existente en vez de agregar su propia consulta completa — eso sí es algo a vigilar activamente al diseñar la próxima regla, no un cambio de arquitectura a hacer preventivamente. Recomendaría medir volumen real (mismo criterio que ya se usó en PR-L4.5/PR-S1.5) antes de invertir en índices — no hay evidencia hoy de que haga falta.

---

## 7. Revisión de código relacionado con Suggestions

Repasé `Application/Suggestions/*`, `Infrastructure/Suggestions/ClassificationSuggestionService.cs`, `MovementsQueryService.cs`, los DTOs, y `movements.html` (esto último ya había sido revisado a fondo en la propia entrega de PR-S5.1, sin hallazgos nuevos desde entonces).

- **`SuggestionConfidence.Low` nunca se produce hoy** (ver sección 4) — no es un bug, pero vale que quien escriba la próxima regla sepa que está reservado, sin precedente de uso real todavía.
- **No encontré código muerto, comentarios desactualizados, ni CSS/JS redundante** en todo lo relacionado con Suggestions — es código reciente (PR-S1 a PR-S5.1) y ya fue revisado exhaustivamente en la entrega de PR-S5.1. Está limpio.
- **Un lugar que *no* es un problema hoy pero va a pedir atención en la próxima regla:** `ClassificationSuggestionService.SuggestAsync` mezcla, en un único método, tres responsabilidades — cargar historial, indexar por descripción normalizada, y construir sugerencias por movimiento. Está bien así porque hay una sola regla. El día que llegue una segunda regla que reutilice la carga pero indexe distinto (por ejemplo, por `CounterpartyId` en vez de por descripción), este método no tiene hoy ningún punto de corte natural para insertar eso sin reescribirlo. No lo tocaría ahora — sería refactorizar sin la segunda regla real en la mano (mismo argumento que la sección 5) — pero es el primer lugar concreto a mirar cuando llegue.

**Hallazgos fuera de alcance** (no relacionados directamente con Suggestions, mencionados sin mezclarlos con la propuesta):
- `FinancialMovement.Category`/`MovementCategory` (`Domain/Review/FinancialMovement.cs`) sigue siendo un campo que ningún código popula — ya identificado en el análisis de PR-S1, sigue así, no es parte del código que introdujo la Épica S.
- `Counterparty.DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact` sin wiring en ninguna UI — mencionado también en PR-S1. A diferencia del punto anterior, este SÍ lo incorporo activamente a la propuesta (sección 1 y 8), porque es exactamente la oportunidad que este análisis debía identificar.

---

## 8. Roadmap propuesto

Cada PR con un único objetivo, sin PRs gigantes, siguiendo la misma disciplina que ya sostuvo toda la Épica L y S:

- **PR-S6** — este documento. Sin código.
- **PR-S7** — segunda heurística real: enriquecimiento de sugerencias vía `Counterparty.Default*`, cascada desde una sugerencia de Contraparte ya encontrada por la heurística de PR-S3. Vive dentro de `ClassificationSuggestionService`, sin interfaz nueva (ver sección 5). Sin cambios de contrato ni de frontend — `movements.html` ya consume y deduplica cualquier sugerencia adicional gracias a PR-S5.1.
- **PR-S8** — portar al backend el criterio de fusión por dimensión (mayor confianza, empate por orden) que hoy solo vive en el frontend (`normalizeSuggestions`), para que la invariante documentada del contrato se sostenga también del lado del servidor. Se vuelve necesario recién con dos reglas reales conviviendo — PR-S7 lo habilita y lo justifica con evidencia concreta.
- **PR-S9** — normalización de descripción más agresiva (recortar sufijos numéricos/códigos de operación), como segunda extensión de la heurística original — con más contexto real de cuánta cobertura falta después de PR-S7.
- **PR-S9.5** (opcional) — medir volumen real de `ClassifiedMovements`, mismo criterio que PR-L4.5/PR-S1.5, antes de invertir en la heurística de monto+periodicidad (la más cara identificada en la sección 1).
- **PR-S10** (futuro, no inmediato) — heurística de monto + periodicidad aproximada, para gastos fijos recurrentes.
- **PR-S11+** (futuro) — recién con 3+ reglas reales en producción, evaluar si extraer `IClassificationSuggestionRule` está justificado por evidencia concreta (no antes, ver sección 5).

---

## Recomendación final

**PR-S6 debería ser: implementar el enriquecimiento de sugerencias vía `Counterparty.Default*`** (lo que el roadmap de arriba llama PR-S7 en la numeración continua — lo marco acá como la recomendación concreta para el próximo PR de código).

Razones, en orden de peso:
1. Menor riesgo técnico de todos los candidatos: no toca el algoritmo de comparación existente, solo agrega una consulta chica adicional y una cascada de valores ya curados por el usuario — no inferidos de un historial potencialmente ruidoso.
2. Cierra una brecha de producto ya señalada dos veces (PR-S1 y este análisis) sin ninguna acción tomada — dato completamente modelado, sin un solo consumidor en todo el sistema.
3. Es la primera vez que va a existir una segunda fuente real de sugerencias — el caso de prueba concreto que la sección 5 dice que hace falta antes de considerar `IClassificationSuggestionRule`. Sirve como piloto para validar (o refutar) el criterio de fusión de la sección 2/3 antes de comprometerse a ninguna abstracción.
4. No requiere cambios de contrato (`ClassificationSuggestion`/`ClassificationSuggestionSet`/`SuggestionDimension`/`SuggestionConfidence` ya alcanzan) ni de frontend (`movements.html` ya lo consume correctamente gracias a PR-S5.1).
