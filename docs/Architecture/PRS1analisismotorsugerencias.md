# PR-S1 — Análisis de arquitectura: motor de sugerencias de clasificación

> Documento de análisis puro. No incluye código, no es un patch, no modifica nada del repositorio. Basado en lectura directa de `origin/master` en `9a43c24` (PR-L5 mergeado). Cada afirmación está anclada a un archivo real — donde hago una suposición la marco explícitamente como tal.

---

## Resumen ejecutivo

Después de PR-L5 el sistema quedó con una separación limpia: `IReviewEngine` orquesta *carga* (`IMovementLoader`) y *detección de sospechosos dentro de una sola lista* (`ISuspicionDetector`) — ninguna de las dos responsabilidades compara un movimiento contra una fuente externa. Esa es exactamente la propiedad que el nuevo motor de sugerencias debe conservar: **nunca compara un movimiento contra otro movimiento**. Compara un movimiento contra *un resumen agregado de decisiones pasadas*.

Mi recomendación de diseño en una frase: un **`IClassificationSuggestionService`** nuevo, con contrato propio (`ClassificationSuggestion`, no reutiliza nada de `MovementSuggestion`), consumido directamente por `MovementsQueryService` como un tercer colaborador (junto a `IApplicationDbContext` e `IReviewEngine`) — **no** orquestado por `IReviewEngine`. Razones completas en la sección 5.

---

## 1. Estado actual (post PR-L5)

### 1.1 `IReviewEngine` — qué responsabilidades tiene realmente

Archivo: `src/FinancialSystem.Application/Review/IReviewEngine.cs`, implementación en `src/FinancialSystem.Infrastructure/Review/ReviewEngine.cs`.

```
Task<ReviewResult> GenerateAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
```

La implementación actual es exactamente dos pasos, sin ramas ni condicionales:

```csharp
var movements = await _movementLoader.LoadAsync(from, to, cancellationToken);
var suspicious = _suspicionDetector.Detect(movements);
```

Responsabilidades reales, hoy:
1. **Cargar** movimientos pendientes de banco/tarjeta de un período (delegado 100% a `IMovementLoader`).
2. **Detectar grupos sospechosos** dentro de esa misma lista (delegado 100% a `ISuspicionDetector` — posibles duplicados y splits, nunca cruza contra una segunda fuente).

Propiedades importantes para el diseño del motor de sugerencias:

- **Es por período** (`from`/`to`), no por movimiento individual. No hay ningún método tipo `GetSuggestionFor(Guid sourceId)`.
- **Solo ve movimientos pendientes.** `IMovementLoader.LoadAsync` (`src/FinancialSystem.Infrastructure/Review/MovementLoader.cs`) excluye explícitamente cualquier `BankStatement`/`Transaction` que ya tenga un `ClassifiedMovementItem` (`ClassifiedSourceIds`). Esto importa: **cualquier motor de sugerencias solo necesita opinar sobre movimientos que `IMovementLoader` ya devuelve** — nunca sobre movimientos ya clasificados.
- **Es stateless por llamada** — no cachea nada entre invocaciones, cada `GenerateAsync` vuelve a consultar la base.
- **Comparte `IApplicationDbContext`** con quien lo invoque. `MovementsQueryService` ya documenta esto explícitamente (ver 1.2): dos llamadas que dependen del mismo `DbContext` deben ser secuenciales, no `Task.WhenAll`.
- El propio código ya deja una marca explícita de que es un punto de extensión, sin comprometerse a una forma:

  > `IReviewEngine.cs`: *"PUNTO DE EXTENSIÓN: este es el lugar donde debería integrarse un futuro motor de recomendaciones (historial de clasificaciones, reglas, IA) — como un componente más orquestado acá, siguiendo el mismo patrón de composición que ya usa `ISuspicionDetector`."*
  >
  > `ReviewResult.cs`: *"Su resultado NO debería modelarse como un nuevo `MatchedPair`/candidato a emparejar [...] Diseñar esa forma ahora, sin una segunda implementación real que la valide, sería la abstracción prematura que este proyecto evita."*

  Esta nota ya fue escrita por mí durante PR-L4/L5, sin comprometerme a la integración — la sección 5 de este documento es, precisamente, la decisión que esa nota dejó pendiente.

### 1.2 `MovementsQueryService` — qué responsabilidades tiene

Archivo: `src/FinancialSystem.Application/Movements/IMovementsQueryService.cs` (contrato) y `src/FinancialSystem.Infrastructure/Movements/MovementsQueryService.cs` (implementación).

```
Task<IReadOnlyList<MovementView>> GetAsync(DateOnly from, DateOnly to, Guid? financialAccountId, string? search, CancellationToken ct)
```

Es el **único** consumidor actual de `IReviewEngine`. Combina dos fuentes, secuencialmente (comentario explícito en el código sobre por qué no puede ser paralelo):

1. **Pendientes** — una sola llamada a `_reviewEngine.GenerateAsync(from, to)`, reutilizando `result.Movements` para el listado y `result.Suspicious` para construir el diccionario de `Warning` por `SourceId` (`BuildWarningsBySourceId`).
2. **Clasificados** — query directa a `ClassifiedMovementItems` (filtrado a `BankStatement`/`Transaction`, ya no existe `LegacyImport` como fuente real) + join a `ClassifiedMovement`, más hasta 2 queries en bloque para resolver `FinancialAccountId` (nunca N+1).

Resultado: `MovementView`, un record neutro (no es `FinancialMovement`, no es `ClassifiedMovement`) que combina ambos mundos. Hoy tiene `Warning` (K6, solo para pendientes) pero **no tiene ningún campo de sugerencia** — se quitó en PR-L4 (`MovementSuggestion` fue eliminado del todo, ver `IMovementsQueryService.cs`, comentario en el doc-comment de `MovementView`).

Esto es clave para la sección 6: `MovementsQueryService` es exactamente el lugar donde hoy se decide "qué le muestro a la pantalla Movimientos por cada fila" — cualquier campo de sugerencia visible en `movements.html` tiene que pasar por acá, igual que `Warning` ya lo hace.

### 1.3 Qué información posee `ClassifiedMovement`

Archivo: `src/FinancialSystem.Domain/Review/ClassifiedMovement.cs` + `ClassifiedMovementItem.cs`.

`ClassifiedMovement` es, literalmente, **la tabla de decisiones históricas del usuario** — cada fila es una clasificación ya verificada, nunca un estado intermedio. Campos relevantes para un motor de sugerencias:

| Campo | Tipo | Relevancia para sugerencias |
|---|---|---|
| `Description` | `string` (max 500) | Texto original del movimiento — base para similitud textual |
| `EffectiveDate` | `DateTime` | Fecha — útil para detectar periodicidad/frecuencia |
| `TotalAmount` | `decimal` | Monto absoluto — útil para agrupar gastos recurrentes de monto fijo (suscripciones, alquiler) |
| `CategoryId` | `Guid` (requerido) | La "respuesta correcta" más importante a sugerir |
| `MovementType` | enum (requerido) | Segunda dimensión a sugerir |
| `FinancialImpact` | enum (requerido) | Tercera dimensión a sugerir |
| `CounterpartyId` | `Guid?` (opcional) | Cuarta dimensión — ya tiene su propio mecanismo de sugerencia parcial (ver 1.4) |
| `Status`/`ProcessingSource` | enums | Trazabilidad de cómo se llegó a la clasificación — **no** afecta el contenido de una sugerencia futura |

Índices existentes (`ClassifiedMovementConfiguration.cs`): `EffectiveDate`, `CategoryId`, `CounterpartyId`, `FinancialImpact`, `MovementType`, compuesto `(EffectiveDate, CategoryId)`, compuesto `(CounterpartyId, FinancialImpact)`. **No hay índice sobre `Description`.** Esto es un dato duro para la sección 3: cualquier estrategia de similitud textual sobre `Description` en SQL sería hoy un full scan — o se resuelve en memoria sobre un subconjunto acotado, o requiere un índice nuevo (`pg_trgm`, `tsvector`) que hoy no existe.

`ClassifiedMovementItem` aporta el **snapshot inmutable** (`OriginalDescription`, `OriginalAmount`, `OriginalDate`, `OriginalCurrency`) del movimiento crudo tal como estaba al momento de clasificar — coincide en semántica con lo que carga `FinancialMovement`, pero es la copia congelada, no la fuente viva.

Dos campos muertos que NO hay que confundir con algo reutilizable: `MatchScore` (`double?`) y `AmountDelta` (`decimal?`) — eran trazabilidad del viejo motor de matching (`ConfirmMatchHandler`, retirado en PR-L4), ya no tienen productor, y **no deben interpretarse como un precedente de diseño** para el nuevo motor. Son deuda dejada intacta a propósito (ver informe de PR-L4) porque tocarlos no es parte de esta épica.

### 1.4 Qué información puede reutilizarse como historial

Tres fuentes de historial, con calidad muy distinta:

1. **`ClassifiedMovement` (+ `ClassifiedMovementItem`)** — la fuente principal y de mayor volumen. Cada fila es (descripción cruda, monto, fecha) → (categoría, tipo, impacto, contraparte). Esto es exactamente un dataset de entrenamiento/consulta para "¿qué categoría le puso el usuario la última vez a algo parecido a esto?".
2. **`Counterparty.DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact`** (`src/FinancialSystem.Domain/Entities/Counterparty.cs`) — esto **ya es**, funcionalmente, un mecanismo de sugerencia, pero manual y declarativo: el usuario configura de antemano "si elegís esta contraparte, te sugiero estos valores". El propio doc-comment de la entidad ya lo anticipa:

   > *"APRENDIZAJE FUTURO: A futuro, el sistema podrá sugerir la contraparte automáticamente basándose en la similitud de descripción del movimiento con descripciones históricas ya clasificadas."*

   Este mecanismo **hoy no tiene wiring en ninguna UI** (confirmado también en `docs/UX/ClassificationUX.md` — "mecanismo ya modelado en el dominio, sin wiring en ninguna UI hoy"). Es información estructurada que el nuevo motor debería *leer* (si conozco la contraparte sugerida, ya tengo categoría/tipo/impacto candidatos gratis), pero es un mecanismo distinto y ya existente — no hay que reinventarlo ni hacer que el nuevo motor le pise el rol.
3. **Nada del lado de `FinancialMovement`/`MovementCategory`.** Encontré que `FinancialMovement.Category` (`MovementCategory` enum: `Food`, `Transport`, `Health`, etc. — `src/FinancialSystem.Domain/Review/FinancialMovement.cs`) es un campo que **nunca se popula**: revisé `MovementLoader.ToFinancialMovement` (ambos overloads, `BankStatement` y `Transaction`) y ninguno de los dos asigna `Category` — queda siempre en su default, `Unknown`. El propio doc-comment ya lo admite: *"Es una pre-clasificación en memoria para ayudar al matching"* (matching que ya no existe). **Es código muerto pre-existente, en la misma categoría que `CommonHelper.cs`/`MatchScore` — fuera de alcance de este análisis, no lo tomes como fuente de historial ni lo actives como side-effect de este PR.**

Un dato estructural importante para toda la sección 3: **el sistema no tiene noción de usuario/tenant** (confirmé por grep que no existe `UserId`/`TenantId`/`OwnerId` en ningún lado del dominio, ni autenticación en `Program.cs`). Es un sistema personal de un solo usuario. "Historial del usuario" es, literalmente, toda la tabla `ClassifiedMovements` — no hace falta ni tiene sentido diseñar ningún tipo de particionado por usuario.

---

## 2. Dónde debería vivir el nuevo motor

### Naming y ubicación de la interfaz

Tres opciones sobre la mesa (las nombradas por vos) más la que recomiendo:

**`IClassificationSuggestionEngine`** — sigue el patrón `IReviewEngine`/`ISuspicionDetector` (sustantivo + "Engine"/"Detector" para un componente que ejecuta un proceso de análisis). Encaja bien conceptualmente, pero "Engine" en este código ya está asociado a "orquesta un `GenerateAsync` por período completo" (`IReviewEngine`). El nuevo motor, como se justifica en la sección 5, opera distinto (por movimiento, no por período) — reusar el sufijo "Engine" para algo con una forma de invocación distinta puede confundir a quien lea el código sin este documento.

**`IClassificationRecommendationService`** — "Recommendation" es más preciso semánticamente para lo que hace (recomendar un valor, no revisar/auditar), pero es más largo y "Service" es un sufijo ya usado en el codebase para servicios de lectura combinada (`IMovementsQueryService`, `IFinancialAccountQueryService`, `IImportHistoryQueryService`) — todos con forma "combino/leo cosas de varias fuentes", no "genero una predicción". Usarlo acá diluye esa convención.

**`ISuggestionProvider`** — demasiado genérico. "Provider" en .NET normalmente denota una implementación intercambiable de una infraestructura (`IServiceProvider`, `IPdfTextExtractor`... aunque ese último es más específico). No comunica qué se sugiere ni de qué depende.

**Mi recomendación: `IClassificationSuggestionService`.** Combina lo mejor de las tres:
- "Classification" dice qué se sugiere (las 4 dimensiones de clasificación, no "cualquier cosa").
- "Suggestion" evita la palabra "Recommendation" — deliberado, para que quede clara la distancia semántica con el viejo "Suggestion" del matching legacy (ver sección 4) y para que no se lea como "Engine" (que ya tiene una connotación de "orquesta un período completo" en este código).
- "Service" es consistente con `IMovementsQueryService` — ambos son servicios de **lectura combinada, sin efectos secundarios, invocados desde Application/Infrastructure**, ni comandos ni queries CQRS-style (que en este código sólo existen para *escritura*: `ClassifyMovementCommand`).

**Ubicación en capas** (seguiendo la convención ya establecida en todo Épica K/L):
- `src/FinancialSystem.Application/Suggestions/IClassificationSuggestionService.cs` — el contrato, junto al DTO `ClassificationSuggestion` (sección 4). Carpeta nueva `Suggestions/`, hermana de `Review/` y `Movements/`, **no** dentro de `Review/` — justificación: `Review/` hoy es exclusivamente "cargar movimientos + detectar sospechosos dentro de una lista", un concepto ya cerrado y con su propio ciclo (PR-L1 a PR-L5 lo dejaron mínimo a propósito). Meter sugerencias ahí mezclaría dos responsabilidades que esta misma épica se esforzó en separar.
- `src/FinancialSystem.Infrastructure/Suggestions/ClassificationSuggestionService.cs` — la implementación real (consulta a `IApplicationDbContext`), simétrica a como `Infrastructure/Review/` contiene `MovementLoader`/`SuspicionDetector`/`ReviewEngine`.

No es una interfaz gigante con "otra arquitectura mejor" que valga la pena inventar: el patrón `IInterface` en `Application` + `class` en `Infrastructure`, registrado en `AddInfrastructure` (`Infrastructure/DependencyInjection.cs`), es exactamente el que ya usa cada pieza de este sistema (`IMovementLoader`, `ISuspicionDetector`, `IFinancialMetricsService`, etc.) — no hay necesidad de una forma distinta.

---

## 3. Qué información debería utilizar

Analizando cada candidato contra el estado real de los datos (sección 1.3):

- **Descripción** — la señal más fuerte y la más barata de obtener (ya viene en `FinancialMovement.Description` y en `ClassifiedMovement.Description`/`ClassifiedMovementItem.OriginalDescription`). Problema real: no hay índice, así que cualquier comparación tiene que acotarse (ver más abajo). Además las descripciones bancarias suelen tener sufijos variables (números de operación, sucursal) — una comparación de igualdad exacta va a tener muy poco recall; hace falta alguna forma de normalización o comparación difusa, no necesariamente compleja (podría ser tan simple como comparar sobre una versión normalizada — mismo criterio que ya usa `ITransactionNormalizer` para limpiar descripciones en el import, que sería razonable reutilizar como referencia de "cómo ya normalizamos texto acá", sin necesariamente ser el mismo componente).
- **Contraparte** — señal fuerte cuando existe (`ClassifiedMovement.CounterpartyId`), pero es opcional y hoy minoritaria (no tengo el dato real de cobertura — **sería razonable que PR-S1.5, análogo a lo que fue PR-L4.5 para Legacy, midiera cuántas filas de `ClassifiedMovement` tienen `CounterpartyId != null` antes de apostar el diseño a esta señal**). Además ya tiene su propio mecanismo de sugerencia (`Counterparty.Default*`, sección 1.4) — el nuevo motor debería *consumir* esa señal, no duplicarla.
- **Categorías anteriores** — es el output, no un input independiente; es la variable que se está prediciendo. Sí es útil como señal indirecta cuando se agrupa por otra dimensión (ej: "de las últimas 10 veces que clasifiqué algo con esta contraparte, 9 fueron Categoría X").
- **Montos** — señal débil sola (dos gastos de $5000 pueden ser cualquier cosa), pero fuerte quintiled) en combinación con periodicidad (mismo monto, mismo día del mes → altísima probabilidad de ser el mismo concepto recurrente, ej. alquiler, suscripción). Ya existe un antecedente directo de "comparar montos con tolerancia" en `SuspicionDetector` (`DuplicateAmountTolerance`, `ReviewEngineOptions`) — **antecedente de patrón de configuración a copiar (una tolerancia configurable), no de código a reutilizar** (el propósito es opuesto: `SuspicionDetector` busca coincidencias sospechosas *dentro del mismo período*, el motor nuevo busca similitud *contra historial de otros períodos*).
- **Fechas** — poco valor aisladas; valor real solo combinadas con frecuencia/periodicidad (ver abajo).
- **Frecuencia / patrones históricos** — la señal más potente para el caso de uso real de este sistema (gastos recurrentes: alquiler, suscripciones, servicios) pero la más cara de calcular bien (requiere agrupar por descripción normalizada + ventana de fechas, no es una single-column query). Recomiendo **no implementarla en la primera versión** — ver roadmap, sección 8.

### Cómo desacoplar el algoritmo de los consumidores

El contrato (`IClassificationSuggestionService`) debe exponer **una operación por movimiento** (`SuggestAsync(FinancialMovement movement, CancellationToken ct)` o similar — sin comprometerme a la firma exacta en este documento, eso es diseño de PR-S2) que devuelva `ClassificationSuggestion?` (nullable — no siempre hay señal suficiente). El *cómo* se calcula (SQL agregando por descripción normalizada, algo más elaborado, o eventualmente un modelo externo) queda completamente encapsulado en la implementación de `Infrastructure`.

Esto es exactamente el mismo desacople que ya existe entre `ISuspicionDetector` (contrato: "dame grupos sospechosos de esta lista") y su implementación actual (`SuspicionDetector`, algoritmo O(N²) con tolerancias configurables) — nada impide que mañana se reemplace el algoritmo interno sin tocar ningún consumidor, porque el contrato nunca expuso el *cómo*. El mismo principio aplica acá: los consumidores (`MovementsQueryService`, eventualmente un endpoint dedicado) nunca deberían saber si la sugerencia salió de una query SQL agregando por texto, de una tabla de reglas, o de una llamada a un LLM.

---

## 4. Contrato de datos — `ClassificationSuggestion`

**Por qué no reutilizar `MovementSuggestion` (Legacy, ya eliminado):** ese tipo (ver commit de PR-L4) representaba *"el mejor movimiento candidato encontrado en otra fuente, con su score de confianza y una acción de confirmar que fusionaba dos movimientos"*. Es conceptualmente un **candidato a emparejar**, no una recomendación de clasificación. Nunca tuvo campos como categoría o tipo sugeridos — no hay nada estructuralmente reutilizable, solo el nombre, que además ya no debería reusarse (para evitar exactamente la confusión de "¿es lo mismo que antes?" que este PR busca evitar).

Diseño propuesto desde cero — pienso en dos niveles: una recomendación por dimensión, y un contenedor por movimiento.

**Nivel 1 — una recomendación por dimensión, independiente de las demás.** Justificación: las 4 dimensiones de clasificación son independientes entre sí en el dominio (ya lo dice `ClassifiedMovement.cs`: "4 dimensiones independientes") — no hay ninguna garantía de que la misma señal permita sugerir las 4 con la misma confianza. Un movimiento puede tener altísima certeza de contraparte (siempre es "Netflix") pero categoría ambigua la primera vez, o viceversa. Modelar esto como un objeto monolítico con las 4 sugerencias obligatorias forzaría a inventar un nivel de confianza global artificial. Conceptualmente (sin comprometerme a sintaxis de código):

- Campo que identifica **qué dimensión** se sugiere (Category / MovementType / FinancialImpact / Counterparty — nunca "cualquier otro dato clasificable" sin tipar, eso rompería el desacople de la sección 3; si mañana se agrega una quinta dimensión clasificable al dominio, se agrega un valor a este mismo enum, no un campo nuevo suelto).
- El **valor sugerido** en sí (el `Guid`/enum correspondiente a esa dimensión).
- Un **nivel de confianza** — no necesariamente un score numérico continuo desde el día uno (eso ya sería empezar a diseñar el algoritmo, que la sección 3 dice que no corresponde comprometer ahora); alcanza con algo tan simple como una escala ordinal (alta/media/baja) que cualquier implementación futura, desde un `GROUP BY` simple hasta un modelo de ML, pueda mapear a esa misma escala.
- Un **motivo legible** (string corto, para mostrar en la UI *por qué* se sugiere esto — ej. "Clasificaste así 8 de las últimas 9 veces con esta contraparte"). Esto es importante para la confianza del usuario en la sugerencia — no es un "nice to have", es lo que distingue una sugerencia útil de una caja negra.

**Nivel 2 — el contenedor por movimiento**, que agrupa 0 a N recomendaciones de dimensión para un mismo `SourceId`/`SourceEntityType` (misma identidad que ya usa `FinancialMovement.SourceId` — no un nuevo esquema de identidad).

Explícitamente **no** incluye: ningún segundo movimiento, ningún score de "distancia" entre dos movimientos, ninguna acción de "confirmar match". Es información de solo lectura sobre un único movimiento — el usuario sigue clasificando a través del mismo `ClassifyMovementCommand` que ya existe (ver sección 5), la sugerencia solo pre-completa valores en el mismo formulario.

---

## 5. Integración con `IReviewEngine`

Esta es la decisión de diseño más importante del documento. Dos opciones reales, con argumentos concretos para cada una.

### Opción A — `IReviewEngine` orquesta también el nuevo motor

Agregar `ISuggestions` (o como se llame) como tercera dependencia inyectada en `ReviewEngine`, junto a `IMovementLoader`/`ISuspicionDetector`, y agregar un campo `Suggestions` a `ReviewResult`.

**A favor:** es literalmente lo que el comentario que dejé en PR-L4/L5 sugiere textualmente ("como un componente más orquestado acá, siguiendo el mismo patrón de composición que ya usa `ISuspicionDetector`"). Un solo punto de entrada por período sigue siendo consistente con cómo `MovementsQueryService` ya arma "pendientes + sospechosos" con una sola llamada.

**En contra — y por qué pesa más:**
1. **Cambia el contrato de `IReviewEngine` para todo el mundo, incluso para quien no necesita sugerencias.** Hoy `IReviewEngine` es minimalista a propósito (`GenerateAsync(from, to)` → movimientos + sospechosos) — eso es precisamente lo que costó 5 PRs conseguir. Agregarle una responsabilidad más lo devuelve a ser un "totutti" que mezcla conceptos con ciclos de vida distintos.
2. **Descalce de granularidad real.** `IReviewEngine` procesa **todo el período de una sola vez** (`Movements` es una lista completa). Una sugerencia de clasificación, en cambio, tiene sentido pedirla **por movimiento individual**, en el momento en que el usuario abre el modal de clasificación (`openClassifyModal` en `movements.html`) — no necesariamente para los 200 movimientos pendientes del mes de una sola vez. Calcular sugerencias para movimientos que el usuario ni siquiera va a mirar en esa sesión es trabajo desperdiciado (cada sugerencia, según la sección 3, implica al menos una consulta agregando sobre `ClassifiedMovements` — no es gratis).
3. **`ISuspicionDetector` compara la lista contra sí misma; el nuevo motor compara cada movimiento contra una tabla externa (`ClassifiedMovements`) que no tiene relación con el período `from/to` que pidió el caller.** Son operaciones de naturaleza distinta aunque las dos "analicen movimientos" — meterlas en el mismo orquestador solo porque ambas "analizan" es agrupar por sustantivo, no por responsabilidad real (el mismo error que ya se corrigió al sacar el matching de `IReviewEngine`).
4. Acopla el ciclo de vida de release de las dos features: cualquier cambio al motor de sugerencias (que va a iterar rápido, con reglas → IA → embeddings según la sección 7) toca un archivo (`ReviewResult.cs`) que hoy es estable y usado por *todo* lo que consume `IReviewEngine`.

### Opción B — Punto de entrada propio (`IClassificationSuggestionService`, invocado directamente por quien lo necesite)

`MovementsQueryService` pasaría a depender de tres colaboradores en vez de dos: `IApplicationDbContext`, `IReviewEngine`, `IClassificationSuggestionService` — igual patrón de composición explícita que ya usa hoy (inyección de interfaces, sin herencia ni decoradores).

**A favor:**
- Preserva `IReviewEngine` minimal — sigue siendo exactamente "cargar + detectar sospechosos", que es lo que costó consolidar.
- Permite invocar sugerencias **por movimiento**, no por período completo — encaja mejor con el flujo real de UI (el usuario clasifica de a un movimiento, o en lote explícito vía PR-L2, nunca "todos los pendientes del mes a la vez").
- Cada motor evoluciona a su propio ritmo sin arrastrar al otro. `ISuspicionDetector` es determinístico y barato (O(N²) en memoria); el motor de sugerencias, según la sección 7, va a crecer hacia reglas configurables e IA — ciclos de cambio completamente distintos.
- Sigue exactamente el patrón de composición que ya funcionó para separar `IMovementsQueryService` de `IReviewEngine` en primer lugar (K6): un servicio de más alto nivel combina varios colaboradores de más bajo nivel, sin que ninguno de ellos necesite saber del otro.

**En contra:** hay que decidir dos veces "cuándo pedir esto" en vez de una — pero eso ya es lo que hace `MovementsQueryService` hoy entre pendientes y clasificados, no es una complejidad nueva.

### Recomendación

**Opción B.** El comentario que dejé en PR-L4 diciendo "como un componente más orquestado acá" fue una nota de intención tomada *antes* de tener el análisis real (exactamente el tipo de suposición que la limpieza de Legacy enseñó a no dar por sentada sin verificar contra el código). Con el motor ya diseñado en detalle (secciones 3 y 4), la asimetría de granularidad (período completo vs. por movimiento) y de ciclo de vida (estable vs. iterando rápido hacia IA) pesa más que la comodidad de "un solo orquestador". Corrijo esa nota acá: `IReviewEngine` no debería crecer para absorber esto.

---

## 6. Impacto sobre Movements

Sin escribir código, en orden de la cadena de datos:

### `MovementsQueryService`
- Nueva dependencia inyectada: `IClassificationSuggestionService` (constructor, igual patrón que las dos que ya tiene).
- En `LoadPendingWithWarningsAsync` (o un método hermano nuevo), después de obtener `result.Movements` filtrados a banco/tarjeta, para cada uno pedir la sugerencia — **con cautela de performance**: si la implementación real hace una query por movimiento, un período con 200 pendientes dispara 200 queries. Esto es un riesgo real a resolver en el diseño de PR-S2/S3 (probablemente la interfaz debería aceptar la lista completa de movimientos del período, `SuggestManyAsync(IReadOnlyList<FinancialMovement>)`, y resolver internamente en una sola consulta agregada — análogo a cómo `MovementLoader` ya resuelve cuentas asignadas "en bloque" y no por fila, ver comentario en `MovementsQueryService.LoadClassifiedAsync`). Dejo esto marcado como decisión de PR-S2, no de este documento.
- **Nunca** se le pide sugerencia a movimientos ya clasificados (`LoadClassifiedAsync`) — no tiene sentido, ya tienen una clasificación real.

### `MovementView`
- Nuevo campo opcional, análogo a `Warning` en forma (`ClassificationSuggestion? Suggestion` o el nombre final que se decida) — nullable, presente solo en pendientes, ausente en clasificados (mismo patrón exacto que ya sigue `Warning` hoy, incluyendo el comentario que ya existe explicando por qué es así).
- `MovementListItemDto` (`src/FinancialMcp.Api/DTOs/MovementsDtos.cs`) necesitaría el DTO espejo (`ClassificationSuggestionDto` o similar) — mismo patrón que `MovementWarningDto`.

### `movements.html`
- Una columna nueva (o una ampliación de una celda existente — a decidir en UX, no en este documento de arquitectura) siguiendo **el mismo patrón que `renderWarningCell`**: información de solo lectura con un badge, sin acción propia obligatoria.
- La diferencia real con `Warning`: una sugerencia sí puede tener una acción asociada razonable — "aplicar esta sugerencia" pre-completando el modal de clasificación (`openClassifyModal`) con los valores sugeridos en vez de vacíos. Esto **no** requiere un comando nuevo ni un endpoint nuevo: sigue siendo `ClassifyMovementCommand` vía `POST /api/movement-review/classify`, exactamente como ya hace la clasificación manual — la sugerencia solo cambia qué valores vienen pre-cargados en el formulario al abrirlo, igual que ya hace `Counterparty.Default*` hoy (mecanismo ya existente, ver 1.4) cuando el usuario elige una contraparte. Es la app de siempre. **Esto es exactamente el motivo por el que el contrato de la sección 4 no necesita ninguna acción de "confirmar" — la acción ya existe, se llama clasificar.**
- `toMovementViewModel` necesitaría mapear el nuevo campo del DTO, siguiendo el mismo patrón que ya usa para `warning`.

Ningún cambio a `ClassifyMovementCommand`, `ClassifyMovementHandler`, ni al endpoint `/classify` — la sugerencia vive enteramente en la capa de lectura, nunca en la de escritura.

---

## 7. Escalabilidad futura

El contrato de la sección 4 (recomendación por dimensión + contenedor por movimiento, confianza ordinal, motivo legible) fue diseñado para no romperse ante ninguno de estos escenarios:

- **Reglas configurables** — una implementación de `IClassificationSuggestionService` que lea una tabla de reglas explícitas (ej. "si `Description` contiene X → sugerir categoría Y") en vez de agregar sobre `ClassifiedMovements`. El contrato no cambia, solo la implementación en `Infrastructure`.
- **IA / LLM** — el sistema **ya tiene** un precedente arquitectónico directo para esto: `IFinancialInsightsService`/`IOpenAIFinancialInsightsService` (`src/FinancialSystem.Application/Insights/`, implementado en `src/FinancialSystem.Infrastructure/Insights/`), con `OllamaOptions`/`OpenAIOptions` ya resueltos vía `IOptions<T>` + `HttpClient` tipado, y ya usado hoy por `TransactionInsightsWorker`. **No es el mismo feature** (ese genera un resumen narrativo en background, no una sugerencia de clasificación por movimiento, y no debería confundirse ni fusionarse con este motor) pero es la prueba de que el patrón "wrapper de un proveedor LLM detrás de una interfaz propia, con opciones configurables" ya es código real y probado en este repo, no una idea nueva a validar desde cero. Una implementación futura de `IClassificationSuggestionService` basada en LLM podría copiar ese mismo patrón de configuración/HttpClient sin inventar nada.
- **Embeddings** — una implementación que compare vectores de descripciones en vez de texto agregado por igualdad/similitud simple. El contrato de salida (una sugerencia con confianza y motivo) no necesita saber cómo se calculó esa confianza internamente.
- **ML / modelos locales** — mismo argumento: el contrato es agnóstico al mecanismo. La única superficie pública es "dame una sugerencia (o ninguna) para este movimiento, con qué confianza y por qué" — eso no cambia si el cálculo pasa de un `GROUP BY` a un modelo entrenado localmente.

El punto de diseño que **hace** posible todo esto sin romper el contrato público es justamente la recomendación explícita de la sección 3: nunca exponer el "cómo" en la interfaz — solo `Movement in, ClassificationSuggestion? out`. Cualquier cambio de algoritmo es un cambio de implementación en `Infrastructure`, invisible para `Application`/`Api`/`movements.html`.

---

## 8. Roadmap propuesto — secuencia de PRs pequeños

Siguiendo el mismo criterio que ya se usó en toda la Épica L (cada PR compila, es revisable solo, y dejó una nota explícita del siguiente paso):

- **PR-S1** *(este documento)* — análisis de arquitectura. Sin código.
- **PR-S1.5** *(opcional, recomendado si no hay certeza sobre el volumen real de datos)* — análoga a PR-L4.5: medir en la base real cuántas filas tiene `ClassifiedMovements`, qué fracción tiene `CounterpartyId` no nulo, y qué distribución de `Description` únicas existe. Esto informa si vale la pena empezar directamente por frecuencia/periodicidad (sección 3) o si con el volumen actual ni siquiera hay suficiente historial para que una sugerencia por descripción tenga sentido — igual que PR-L4.5 evitó diseñar una migración destructiva sin conocer los datos reales.
- **PR-S2** — contrato (`IClassificationSuggestionService`, `ClassificationSuggestion`) + implementación mínima en `Infrastructure` (una sola señal: coincidencia de `Description` normalizada, sin frecuencia todavía) + registro en DI. Sin ningún consumidor todavía — deja el sistema compilando exactamente igual que antes, el motor existe pero nadie lo llama (mismo criterio que ya se siguió, por ejemplo, al introducir `ISuspicionDetector` antes de que `MovementsQueryService` lo consumiera).
- **PR-S3** — integra el motor en `MovementsQueryService`/`MovementView`/DTOs (sección 6, primera mitad) — sin tocar `movements.html` todavía. El backend queda completo y verificable vía API antes de tocar UI.
- **PR-S4** — UI en `movements.html`: columna/badge de sugerencia + "aplicar sugerencia" pre-cargando el modal de `ClassifyMovementCommand` existente (sección 6, segunda mitad). Ningún comando ni endpoint nuevo.
- **PR-S5** *(futuro, no inmediato)* — segunda señal: frecuencia/periodicidad (la señal más potente identificada en la sección 3, pero la más cara — deliberadamente después de tener la primera versión simple funcionando y medible).
- **PR-S6+** *(futuro, no inmediato, sección 7)* — reglas configurables y/o integración LLM, cada una como implementación alternativa/adicional de `IClassificationSuggestionService`, sin tocar el contrato.

Cada PR de esta lista dejaría el sistema compilando y es revisable de forma independiente, siguiendo exactamente la disciplina que ya se sostuvo durante PR-L1 a PR-L5.
