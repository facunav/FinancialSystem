# Análisis del motor de sugerencias después de PR-S10

Análisis puro, sin código, sin patch, sin modificaciones al repositorio. Base:
`origin/master` en `3df5342` (PR-S10 mergeado, confirmado por `git fetch` + `git log`
al inicio de este análisis — no se usó ningún supuesto de la conversación previa).

Código leído para este análisis (todo vía `git show origin/master:<path>`, nunca desde
el checkout local, que está desactualizado/con cambios ajenos sin relación):

- `ClassificationSuggestionService.cs` (estado completo post-PR-S10).
- `IClassificationSuggestionService.cs`, `ClassificationSuggestion.cs`,
  `ClassificationSuggestionSet.cs`, `SuggestionDimension.cs`, `SuggestionConfidence.cs`.
- `ClassifiedMovement.cs`, `ClassifiedMovementItem.cs`, `Category.cs`, `Counterparty.cs`,
  `FinancialMovement.cs` (Domain).
- `ClassifiedMovementConfiguration.cs` (índices).
- `MovementsQueryService.cs` (consumidor).
- `CategoryEndpoints.cs`, `CounterpartyEndpoints.cs`, `movements.html` (fragmentos
  relevantes a catálogos activos/desactivados y renderizado de sugerencias).
- `docs/Architecture/PRS6analisissiguienteheuristica.md` (roadmap original, sección 8).
- `docs/Architecture/Architecture.md` (fragmentos sobre el motor de sugerencias).
- Búsquedas `git grep` en todo `origin/master` para confirmar uso real (o ausencia de
  uso) de varios campos del dominio, en vez de asumir.

---

## 1. Nuevas heurísticas aprovechando información que ya existe

### 1.1 Hallazgo principal: `IsDeactivated` se ignora por completo — y esto ya rompe la UI hoy

`Category.IsDeactivated` y `Counterparty.IsDeactivated` existen, están indexados, y
`GET /api/categories` / `GET /api/counterparties` ya filtran `!IsDeactivated` por
defecto (confirmado en `CategoryEndpoints.cs:32` y `CounterpartyEndpoints.cs:39`) — son
justamente los catálogos que `movements.html` carga en `state.categories`/
`state.counterparties` para poblar los `<select>` del modal de clasificación y para
traducir los chips de sugerencia a nombre legible.

`ClassificationSuggestionService` (heurística 1, `BuildSuggestions`) nunca consulta
`Category` ni `Counterparty` — solo trabaja con los `Guid` crudos ya persistidos en
`ClassifiedMovement.CategoryId`/`CounterpartyId`. Si el historial de una descripción
apunta mayoritariamente a una categoría o contraparte que el usuario desactivó
después de clasificar esos movimientos, el motor la sigue proponiendo con total
confianza.

Efecto confirmado, no hipotético, trazando el flujo completo hasta el frontend:

- `suggestionValueLabel` (movements.html:790) resuelve el nombre así:
  `state.categories.find(c => c.id === s.value)?.displayName ?? s.value` — si el
  `Guid` no está en `state.categories` (porque está desactivada y el endpoint la
  excluyó), el chip muestra el **GUID crudo** en vez de un nombre.
- El precargado del modal (`document.getElementById('cCategory').value = ... ??
  suggestionValue(...)`) intenta asignar ese mismo `Guid` a un `<select>` cuyas
  `<option>` tampoco incluyen la categoría desactivada — la asignación no encuentra
  coincidencia y el campo queda **en blanco**, silenciosamente, sin ningún error
  visible.

Es decir: hoy es posible ver un chip que dice `Categoría: 3f9a2b1c-...` (un GUID en
lugar de un nombre) y, al abrir el modal, encontrar el campo Categoría vacío a pesar
del chip. Esto no es una heurística nueva a inventar — es un caso donde el motor
**debería dejar de proponer** un valor que el propio catálogo ya invalidó.

### 1.2 `Currency` — persistido en ambos lados, ignorado en la clave de comparación

`FinancialMovement.Currency` (el movimiento pendiente) y `ClassifiedMovement.Currency`
(el historial) existen los dos, pero `ClassifiedHistoryRow` no proyecta `Currency` y
`Normalize`/el agrupamiento por descripción no lo usan. El propio análisis de PR-S8
(y la normalización agregada en PR-S9) ya identificó, con evidencia real de
`BbvaTransactionLineParser`, que una misma descripción normalizada ("NETFLIX.COM",
tras quitar el monto embebido) puede corresponder a cargos en ARS y en USD del mismo
comercio. Hoy ambos casos se mezclan en el mismo grupo de historial sin distinción.

Es un campo real, ya persistido en los dos lados, genuinamente ignorado — encaja en lo
que pediste. Pero a diferencia del punto 1.1, acá no hay evidencia de que mezclarlos
sea realmente un problema: si el usuario clasifica igual un cargo de Netflix en ARS
que en USD (razonable — sigue siendo el mismo servicio), separar por moneda solo
fragmentaría el historial en grupos más chicos sin ganar nada, empeorando la confianza
calculada por PR-S10 sin necesidad. Lo marco como candidato real, no como algo urgente
(ver roadmap, PR-S13).

### 1.3 `Amount` / signo — señal débil, más útil como validación cruzada que como sugerencia propia

`FinancialMovement.Amount` (positivo = gasto/débito, negativo = ingreso/crédito, según
su propio doc-comment) podría contrastarse con el `FinancialImpact` sugerido. Pero la
relación no es limpia: `Expense`/`Income` sí correlacionan con el signo, pero
`InternalMovement` y `DebtPayment` no tienen un signo consistente (una transferencia
interna puede ser el lado saliente o entrante del mismo movimiento). No alcanza para
proponer un valor por sí solo — como mucho, serviría para **marcar una posible
contradicción** ("el historial sugiere Ingreso pero el monto es positivo") en una
iteración futura, no como heurística nueva de sugerencia. No lo prioricé en el
roadmap por esta ambigüedad.

### 1.4 Campos revisados y descartados, con evidencia concreta de por qué

- **`FinancialAccountId`**: existe y está indexado en `Transaction`/`BankStatement`, y
  `FinancialMovement.FinancialAccountId` ya lo trae al motor — pero **no se persiste en
  ningún lado de `ClassifiedMovement`** (confirmado leyendo la entidad completa: no
  existe el campo). No hay forma de correlacionar "esta cuenta históricamente implica
  esta categoría" sin agregar una columna nueva — fuera de alcance explícito de estas
  mejoras incrementales. Descartado, no por falta de valor sino por requerir una
  migración.
- **`FinancialMovement.Category`** (`MovementCategory`, enum Food/Transport/Health/...):
  parece una preclasificación heurística lista para usar, pero `git grep
  "MovementCategory"` en todo `origin/master` no devuelve **ningún** lugar que le
  asigne un valor distinto del default (`Unknown`) — es un campo completamente muerto,
  no datos reales. Descartado.
- **`Category.ParentId`**: reservado para jerarquía futura, su propio doc-comment dice
  "Hoy siempre es null". Sin datos reales detrás, no hay nada que aprovechar todavía.
- **`Counterparty.Type`** (Person/Business/Bank/OwnAccount/...): tentador para reglas
  tipo "si Type=OwnAccount, sugerir FinancialImpact=InternalMovement", pero eso
  duplicaría lo que `Counterparty.DefaultFinancialImpact` (PR-S7) ya resuelve de forma
  más flexible y configurable por el usuario — no agrega nada, solo una segunda fuente
  de verdad para el mismo dato. Descartado.

---

## 2. Combinar señales existentes

| Combinación | Ya implementada | Agrega valor | Evidencia |
|---|---|---|---|
| Historial (heurística 1) + `Counterparty.Default*` (heurística 2) | Sí (PR-S7) | Sí, ya funciona | — |
| Historial + `IsDeactivated` de Category/Counterparty | No | **Sí, alto** | Bug confirmado (1.1) |
| Historial + `Currency` | No | Posible, no confirmado | Ver 1.2 |
| Historial + signo de `Amount` | No | Bajo, ambiguo | Ver 1.3 |
| `Counterparty.Type` como regla propia | No | No — redundante | Ver 1.4 |
| Ponderar historial por recencia (decay continuo) en vez de conteo plano | No | No, sin evidencia | Ver abajo |

Sobre la última fila: PR-S10 ya usa recencia como desempate cuando dos valores tienen
la misma frecuencia — es tentador ir más lejos y ponderar cada clasificación histórica
por qué tan reciente es (dar más peso a lo reciente de forma continua, no binaria).
Pero esto agregaría una constante de decaimiento más para justificar sin ninguna
evidencia de que los hábitos de clasificación del usuario realmente cambien con el
tiempo para el mismo comercio — exactamente el tipo de complejidad no respaldada por
datos que PR-S8/PR-S9/PR-S10 evitaron consistentemente. No lo propongo.

---

## 3. Responsabilidades mezclándose dentro de `ClassificationSuggestionService`

Evidencia concreta (no especulación): el archivo hoy contiene dos bloques
completamente ortogonales, y ambos crecieron en PRs recientes:

- **Normalización de texto**: `Normalize`, `StripEmbeddedUsdAmount`,
  `StripInstallmentCounter`, 2 campos `Regex` estáticos — ~45 líneas. Cero
  conocimiento de `ClassificationSuggestion`/`SuggestionDimension`/confianza. Es una
  función pura de `string → string`. Creció en PR-S9.
- **Resolución de historial**: `BuildSuggestions`, `AddDimensionSuggestion`,
  `ResolveConfidence`, `BuildReason` — ~70 líneas. Cero conocimiento de cómo se limpia
  el texto de entrada. Creció (se reescribió) en PR-S10.

Estos dos bloques no se llaman entre sí más que en el punto donde `SuggestAsync`
normaliza antes de agrupar — no comparten estado ni lógica. Esto es evidencia real (dos
bloques que ya crecieron de forma independiente en PRs consecutivos, no una
predicción) de que separarlos en un archivo/clase propia (ej. un `DescriptionNormalizer`
estático interno en el mismo namespace `FinancialSystem.Infrastructure.Suggestions`)
es una extracción mecánica y de bajo riesgo, no una abstracción nueva — mismos
métodos, mismo comportamiento, solo otro archivo. Lo marco como candidato concreto
(ver roadmap, PR-S12).

Lo que **no** encuentro evidencia de separar todavía: las dos heurísticas en sí
(`BuildSuggestions` para la 1, `EnrichWithCounterpartyDefaultsAsync`/`EnrichSuggestions`/
`MergeDimension` para la 2). Siguen siendo dos métodos privados de tamaño razonable,
sin señales de necesitar vivir en archivos distintos, y la postura ya fijada en PR-S6
(sección 5)/PR-S7 — no introducir `IClassificationSuggestionRule` hasta tener 3+
heurísticas reales en producción — sigue siendo válida: seguimos en 2.

---

## 4. Performance

Confirmado sin cambios respecto al análisis de PR-S6/PR-S7: `SuggestAsync` ejecuta
como máximo 2 consultas por lote, sin importar cuántos movimientos tenga —
`ClassifiedMovements` completo (siempre) y `Counterparties` filtrado por
`WHERE Id IN (...)` (solo si al menos un movimiento tiene sugerencia de contraparte).
PR-S9 y PR-S10 no agregaron ninguna consulta — ambos operan enteramente sobre datos ya
cargados en memoria.

Para heurísticas futuras, el patrón a seguir ya está establecido y probado
(`EnrichWithCounterpartyDefaultsAsync`): juntar todos los IDs candidatos del lote
completo primero, una sola consulta `WHERE Id IN (...)`, nunca una consulta por
movimiento. La candidata de la sección 1.1 (`IsDeactivated`) encaja exactamente en
este patrón — los `CategoryId`/`CounterpartyId` a verificar ya están acotados a los que
el historial propuso para el lote. La candidata de `Currency` (1.2) no agregaría
ninguna consulta nueva (mismo `SELECT`, una columna más).

Sigue sin existir índice sobre `Description` (señalado desde PR-S1/PR-S6, sin cambios)
— se sigue cargando el historial completo en memoria. Esto sigue siendo razonable para
el volumen de un sistema personal de un solo usuario, pero el propio roadmap de PR-S6
proponía un PR-S9.5 de "medir volumen real antes de invertir en la heurística de monto
+ periodicidad" que nunca se ejecutó — sigue pendiente si en algún momento se quiere
invertir en algo computacionalmente más caro que lo actual.

---

## 5. Escalabilidad hacia IA / embeddings / búsqueda semántica / reglas

**Ya preparado**, sin cambios necesarios:

- `SuggestionConfidence` ordinal (Low/Medium/High, no un score continuo) — diseño
  deliberado desde PR-S1/PR-S2 para que cualquier algoritmo futuro (embeddings, un
  modelo de IA) mapee su propia noción de certeza a esta misma escala sin romper el
  contrato.
- `SuggestAsync` por lote — amortiza cualquier costo fijo de una llamada externa cara
  (ej. una sola consulta de embeddings para todo el lote, no una por movimiento).
- `ClassificationSuggestion.Value` como `object` — el contrato ya no presupone qué tipo
  de dato produce cada dimensión.
- El patrón de fusión por dimensión (`MergeDimension`: mayor confianza gana, empate
  mantiene la primera) ya combina dos fuentes heterogéneas hoy (heurística
  histórica + defaults de contraparte) — se extendería sin cambios de diseño a una
  tercera fuente (ej. IA) el día que exista, siguiendo el mismo criterio.

**No preparado todavía** — y está bien que no lo esté, por decisión ya tomada, no por
descuido:

- La clave de comparación es igualdad exacta de string normalizado. Una heurística de
  embeddings/búsqueda semántica necesitaría infraestructura que hoy no existe en
  absoluto: una columna o tabla de vectores, un proveedor de embeddings inyectado
  (no hay ninguna abstracción tipo `IEmbeddingProvider` hoy), y un primitivo de
  similitud (coseno u otro) en vez de `GroupBy` por igualdad. Nada de esto está
  construido — correctamente, según la propia recomendación de PR-S1 de no construirlo
  sin evidencia de que la comparación exacta sea insuficiente en la práctica.
- No hay ninguna capa de caché: cada request recarga y recalcula todo desde cero. Para
  una heurística de IA/embeddings esto sería prohibitivo sin precomputar y persistir
  los vectores — otra pieza de infraestructura real que no existe hoy.
- `IApplicationDbContext` es la única dependencia inyectada en
  `ClassificationSuggestionService`. Agregar una fuente de IA necesitaría una
  abstracción nueva inyectada (trivial de agregar cuando haga falta vía DI estándar,
  no un bloqueo estructural).

No propongo construir nada de esto ahora — según pediste, y consistente con la
disciplina ya sostenida en PR-S1/PR-S6: se construye cuando haya evidencia concreta de
que la comparación exacta se queda corta, no antes.

---

## 6. Código muerto / comentarios stale como consecuencia de PR-S9/PR-S10

Revisé específicamente qué dejaron atrás los dos últimos PRs:

- El método `MostRecent` que PR-S10 reemplazó se eliminó por completo — `git grep
  "MostRecent\b"` en el archivo no devuelve ninguna referencia colgante.
- Los doc-comments de `Normalize` (PR-S9) y de `AddDimensionSuggestion`/
  `BuildSuggestions` (PR-S10) describen exactamente el comportamiento actual, sin
  afirmaciones incorrectas.
- `docs/Architecture/Architecture.md` (línea 43) sigue diciendo "Dos heurísticas" —
  sigue siendo exacto, PR-S9/PR-S10 mejoraron la heurística 1 existente, no agregaron
  una tercera.

**No encontré código muerto ni comentarios stale causados por PR-S9 o PR-S10.** Ambos
PRs dejaron el archivo consistente.

Hallazgo fuera de alcance (mencionado aparte, no como parte de este punto, porque no
es consecuencia de los últimos PRs y no corresponde tocarlo acá): `Architecture.md`
línea 27 todavía describe `MovementsQueryService`/`MovementView.Suggestions` como
"todavía sin consumidor en `movements.html`" — quedó desactualizado desde PR-S5 (varios
PRs atrás), no por S9/S10. Lo señalo porque lo encontré durante la lectura, no porque
corresponda corregirlo en el marco de este análisis.

---

## 7. Roadmap propuesto

**PR-S11 — Excluir de las sugerencias categorías/contrapartes desactivadas**
- *Objetivo*: cuando el historial resuelve un `CategoryId`/`CounterpartyId` que hoy
  tiene `IsDeactivated = true`, no proponerlo tal cual — corrige el bug confirmado en
  la sección 1.1 (chip con GUID crudo, modal quedando en blanco).
- *Alcance*: cargar el conjunto de IDs activos de Category/Counterparty relevantes al
  lote (mismo patrón `WHERE Id IN (...)` de una sola consulta batched ya usado en
  `EnrichWithCounterpartyDefaultsAsync`), y filtrar antes de devolver la sugerencia
  final. Sin cambios de contrato/DTO/endpoint. Frontend sin cambios — ya maneja bien
  listas de sugerencias vacías o parciales.
- *Riesgo*: bajo. Es un filtro que solo puede dejar de sugerir algo que hoy se sugiere
  mal — no inventa ningún valor nuevo, no cambia el algoritmo de mayoría de PR-S10.
- *Por qué primero*: es la corrección de un defecto real y ya confirmado con evidencia
  de código, no una mejora incremental — más urgente que cualquier heurística nueva.

**PR-S12 — Extraer la normalización de descripción a su propia clase**
- *Objetivo*: separar el bloque de limpieza de texto (`Normalize` + 2 regex + helpers)
  del bloque de resolución de historial, con la evidencia de la sección 3.
- *Alcance*: mover el código existente tal cual a una clase estática interna nueva en
  el mismo namespace — cero cambio de comportamiento, cero cambio de firma pública.
  Ajustar únicamente los tests que hoy invocan `Normalize` vía `InternalsVisibleTo`
  para apuntar al nuevo tipo.
- *Riesgo*: mínimo — refactor mecánico, no algorítmico, con los tests existentes como
  red de seguridad para confirmar comportamiento idéntico.
- *Por qué en este orden*: es puramente estructural; conviene hacerlo antes de que
  cualquier PR de heurística nueva (como S13) agregue más código al archivo, para no
  mezclar refactor con feature en el mismo diff.

**PR-S13 (condicional a evidencia) — `Currency` como señal secundaria de matching**
- *Objetivo*: evitar que una misma descripción normalizada mezcle historial de
  distinta moneda cuando eso efectivamente produce clasificaciones distintas en la
  práctica (sección 1.2).
- *Alcance*: agregar `Currency` a la proyección `ClassifiedHistoryRow` (mismo query
  existente, una columna más, sin consulta nueva), usarlo como criterio de
  agrupación o desempate secundario.
- *Riesgo*: medio — puede fragmentar innecesariamente historial que hoy funciona bien,
  si en la práctica el usuario clasifica igual sin importar la moneda.
- *Por qué condicional y después*: a diferencia de S11/S12, esto depende de evidencia
  que hoy no está confirmada. Antes de escribir código, conviene revisar manualmente
  si existen casos reales en los datos del usuario de la misma descripción normalizada
  con clasificaciones distintas según la moneda — si no aparecen, este PR no se
  justifica y debería descartarse, no implementarse "por las dudas".

**Explícitamente no propuesto ahora, con motivo:**
- Heurística de monto + periodicidad para gastos fijos recurrentes (la más cara
  identificada en el análisis de PR-S6): sigue pendiente el checkpoint de medir volumen
  real de `ClassifiedMovements` que ese mismo análisis proponía antes de invertir ahí.
- `IClassificationSuggestionRule` o cualquier abstracción de reglas: seguimos en 2
  heurísticas reales, por debajo del umbral de "3+" que PR-S6/PR-S7 ya fijaron.
- Cualquier IA/embeddings/búsqueda semántica: la infraestructura que requeriría no
  existe hoy (sección 5), y no hay evidencia de que la comparación exacta actual sea
  insuficiente en la práctica.

---

## Recomendación final

**El próximo PR debería ser PR-S11 (excluir categorías/contrapartes desactivadas de
las sugerencias).** Es la única de las candidatas que corrige un defecto ya confirmado
con evidencia de código (no una hipótesis): tracé el flujo completo desde
`ClassificationSuggestionService` (que nunca consulta `IsDeactivated`) hasta
`movements.html` (`suggestionValueLabel` y el precargado del modal), confirmando que
hoy es posible ver un chip con un GUID crudo y un campo del modal vacío a pesar de
tener una sugerencia "de alta confianza". Es de bajo riesgo (un filtro, no una
heurística nueva), reutiliza un patrón de consulta ya probado
(`EnrichWithCounterpartyDefaultsAsync`), no requiere ningún cambio de contrato, y no
depende de ninguna medición o evidencia adicional pendiente — a diferencia de la
candidata de `Currency` (PR-S13), que primero necesita confirmarse con datos reales
antes de justificar el riesgo de fragmentar historial que hoy funciona bien.
