# PR-S12 — Análisis: ¿otras fuentes de sugerencias pueden producir valores inválidos?

Análisis puro, sin código, sin patch, sin modificaciones al repositorio. Base:
`origin/master` en `1a929ec` (PR-S11 mergeado, confirmado por `git fetch` + `git log`
al inicio de este análisis).

Código leído (todo vía `git show origin/master:<path>`):

- `ClassificationSuggestionService.cs` (estado completo post-PR-S11).
- `CounterpartyEndpoints.cs`, `CategoryEndpoints.cs` (rutas de escritura de
  `DefaultCategoryId`/`IsDeactivated`).
- `ClassificationSuggestionServiceBuildSuggestionsTests.cs`,
  `ClassificationSuggestionServiceNormalizeTests.cs` (cobertura actual completa).

---

## 1. `EnrichWithCounterpartyDefaultsAsync` — mismo defecto de PR-S11, sin corregir acá

Confirmado leyendo el método completo: la consulta que arma `CounterpartyDefaultsRow`
trae `c.DefaultCategoryId` tal cual, sin ningún join contra `Category` para verificar
si esa categoría existe o está activa:

```csharp
var defaultsById = await _db.Counterparties
    .AsNoTracking()
    .Where(c => suggestedCounterpartyIds.Contains(c.Id))
    .Select(c => new CounterpartyDefaultsRow(
        c.Id, c.Name, c.DefaultCategoryId, c.DefaultMovementType, c.DefaultFinancialImpact))
    .ToDictionaryAsync(c => c.Id, cancellationToken);
```

Y en `EnrichSuggestions`, ese `DefaultCategoryId` se usa directamente:

```csharp
if (defaults.DefaultCategoryId is { } categoryId)
    MergeDimension(merged, SuggestionDimension.Category, categoryId, reason);
```

**Referencias inexistentes son posibles**: confirmé en `CounterpartyEndpoints.cs`
(`Create` y `Update`) que `DefaultCategoryId = request.DefaultCategoryId` se asigna sin
ninguna validación de que el `Guid` corresponda a una `Category` real — no hay
`db.Categories.AnyAsync(...)` en ningún lado de ese flujo. En la práctica esto es poco
probable si el cliente siempre usa el `<select>` del frontend (que solo ofrece IDs
reales), pero el backend no lo garantiza.

**Categorías desactivadas sí pueden aparecer, y este es el caso real y confirmado**:
`CategoryEndpoints.Deactivate` (`DELETE /api/categories/{id}`) marca
`category.IsDeactivated = true` sin tocar ningún `Counterparty.DefaultCategoryId` que
la referencie — no hay cascada, ni siquiera una validación que impida desactivar una
categoría todavía referenciada como default. Es exactamente el mismo patrón que
`ClassifiedMovement.CategoryId` antes de PR-S11: una referencia que era válida al
configurarse, y queda obsoleta después de una acción del usuario completamente normal
(desactivar una categoría vieja).

**Valores inválidos sí pueden llegar al frontend**: como `EnrichSuggestions` no filtra
nada, un `DefaultCategoryId` desactivado se empaqueta en un `ClassificationSuggestion`
con `SuggestionConfidence.High` (siempre — `MergeDimension` construye el candidato de
heurística 2 con `SuggestionConfidence.High` fijo) y sigue el mismo camino que ya
diagnosticó el análisis de PR-S11: `suggestionValueLabel` en `movements.html` no
encuentra el id en `state.categories` (que ya filtra desactivadas) y muestra el GUID
crudo en el chip; el `<select>` del modal queda en blanco al precargar ese valor.

**Es, si acaso, más grave que el bug que corrigió PR-S11**: ahí la confianza baja
(Medium/Low) al menos podía darle una pista al usuario de que el dato era dudoso. Acá
la sugerencia siempre llega con `High` — máxima confianza aparente sobre un valor
potencialmente inválido.

`DefaultMovementType`/`DefaultFinancialImpact` no tienen este problema: son enums, no
referencias a un catálogo con estado activo/inactivo — no hay equivalente de
`IsDeactivated` para ellos, y el frontend los traduce contra `<option>` estáticas del
propio modal (`optionLabel`), no contra un catálogo dinámico filtrable. Revisé esto
explícitamente para no asumir — el problema es específico de `DefaultCategoryId`.

## 2. ¿La misma validación debería existir en todas las fuentes, o cada una tiene reglas propias?

El **principio** es el mismo en las dos fuentes que existen hoy: "nunca sugerir un
valor que el catálogo activo no puede mostrar" — no hay ninguna razón de negocio para
que la heurística histórica sea más estricta que el enriquecimiento por defaults; sería
inconsistente que el motor blindee un camino y deje el otro abierto, como pasa hoy.

El **mecanismo** de aplicarlo sí difiere un poco por dónde vive cada fuente:
- Heurística 1 (histórica): filtra filas de historial antes de contar frecuencias
  (`BuildSuggestions`, ya resuelto en PR-S11).
- Heurística 2 (defaults): tendría que resolverse en la misma consulta batched que ya
  existe en `EnrichWithCounterpartyDefaultsAsync`, extendiéndola con un join a
  `Category` (mismo patrón exacto de PR-S11, aplicado a un segundo punto de entrada) —
  no hay necesidad de una regla distinta, solo de aplicar la misma en el lugar que
  falta.

No encuentro justificación para que cada fuente tenga su propia política de validación
— es la misma garantía ("todo valor sugerido debe existir en el catálogo activo"),
aplicada en dos lugares distintos porque hoy hay dos fuentes distintas de valores.

## 3. `SuggestionConfidence`/`MergeDimension`: ¿siguen siendo correctos?

`MergeDimension` en sí mismo sigue siendo correcto y no necesita cambios: su contrato
es "comparar dos `SuggestionConfidence` y quedarse con la mayor, empate gana la
primera" — es agnóstico a de dónde viene cada sugerencia, y esa comparación ordinal
sigue siendo válida sea cual sea el origen (historial activo, defaults, o una fuente
futura). El problema de la sección 1 **no es un defecto de `MergeDimension`** — es que
heurística 2 le entrega un candidato marcado `High` sin haber validado antes que ese
candidato sea válido. `MergeDimension` no tiene (ni debería tener) forma de saber si un
`Guid` es válido; solo compara confianzas.

Esto deja un principio arquitectónico explícito, útil para cualquier fuente futura
(IA, embeddings, reglas configurables): **la responsabilidad de validar que un valor
sugerido exista y esté activo es de cada fuente individual, antes de emitir la
sugerencia — nunca de `MergeDimension` ni de la fusión posterior.** Hoy solo la
heurística 1 cumple esa responsabilidad (desde PR-S11); la heurística 2 todavía no.
Cualquier fuente nueva que se agregue debería adoptar la misma disciplina desde el
día uno, no depender de que el merge la proteja — el merge no puede protegerla.

Con historial activo + defaults corregidos (post fix propuesto), el escenario de tres
fuentes (agregando una futura) seguiría funcionando sin cambios en `MergeDimension`:
cada fuente emite su candidato ya validado, con su propia confianza honesta, y el
merge sigue siendo solo una comparación ordinal.

## 4. ¿`ClassificationSuggestionService` acumula demasiada lógica después de PR-S11?

PR-S11 en sí agregó poca lógica nueva (dos `.Where()` adicionales en `BuildSuggestions`,
dos columnas más en la consulta existente) — no encuentro evidencia de que este PR
puntual haya empeorado la mezcla de responsabilidades más allá de lo ya señalado en el
análisis anterior (bloque de normalización de texto vs. bloque de resolución de
historial, con la extracción ya propuesta y todavía pendiente).

Sí encuentro una **señal a vigilar hacia adelante**, no una excusa para abstraer ahora:
si se corrige el hallazgo de la sección 1 replicando el mismo chequeo de
`IsDeactivated` dentro de `EnrichWithCounterpartyDefaultsAsync`, van a existir dos
lugares del mismo archivo resolviendo "¿esta entidad está activa?" de forma
independiente (uno en la consulta de `SuggestAsync`, otro en la consulta de
`EnrichWithCounterpartyDefaultsAsync`). Con dos ocurrencias todavía no hay evidencia
real de que compartir esa lógica sea necesario — recién sería una señal concreta si
aparece una tercera. Lo señalo para que quien implemente esa corrección lo tenga en
cuenta, no para pedir una abstracción hoy.

## 5. Tests actuales y qué falta cubrir

Cobertura existente confirmada (los dos archivos de test del proyecto):
- `ClassificationSuggestionServiceNormalizeTests.cs` — cubre `Normalize()` (PR-S9).
- `ClassificationSuggestionServiceBuildSuggestionsTests.cs` — cubre `BuildSuggestions()`
  completo: mayoría/confianza (PR-S10), exclusión de desactivados (PR-S11).

**Hallazgo concreto: heurística 2 tiene cobertura de tests cero.** Ni
`EnrichWithCounterpartyDefaultsAsync`, ni `EnrichSuggestions`, ni `MergeDimension`
tienen un solo test, desde que se implementaron en PR-S7 — ninguno de los PR
siguientes (S9, S10, S11) los tocó, así que ninguno agregó tests para ellos tampoco.
Es la combinación menos deseable posible con el hallazgo de la sección 1: lógica con un
bug real y confirmado, y sin ningún test que lo hubiera detectado.

Escenarios concretos que faltan, listados para cuando se implemente la corrección
(mismo patrón ya usado: exponer `EnrichSuggestions`/`MergeDimension` como `internal`
para test directo, sin tocar el contrato público — `EnrichWithCounterpartyDefaultsAsync`
en sí es más difícil de testear unitariamente porque hace `await` sobre
`IApplicationDbContext`; alcanza con cubrir `EnrichSuggestions`, que es la lógica pura):

- `MergeDimension`: el candidato reemplaza cuando la sugerencia existente tiene menor
  confianza (Medium/Low → High).
- `MergeDimension`: la sugerencia existente se mantiene cuando ya es High (empate,
  gana la primera — la histórica, no el default).
- `EnrichSuggestions`: contraparte sin ningún default configurado → sugerencias sin
  cambios.
- `EnrichSuggestions`: contraparte con defaults parciales (solo `DefaultCategoryId`,
  por ejemplo) → solo esa dimensión se agrega/reemplaza, las demás quedan como las
  dejó la heurística histórica.
- `EnrichSuggestions`: ningún movimiento tiene sugerencia de Counterparty → devuelve
  el set sin cambios (camino de salida temprana).
- El caso nuevo que motivaría la corrección de la sección 1: `DefaultCategoryId`
  apuntando a una categoría desactivada → no debe agregar/reemplazar la sugerencia de
  Category.

## 6. Roadmap propuesto

Continúo la numeración desde PR-S11 (el roadmap del análisis anterior había propuesto
S12=extraer normalización y S13=señal de Currency condicional — quedan vigentes, pero
el hallazgo de este análisis es más urgente por ser un defecto real ya confirmado,
igual que lo fue PR-S11, así que reordeno: pasa a ocupar el próximo lugar).

**PR-S12 — Extender la exclusión de entidades desactivadas a `EnrichWithCounterpartyDefaultsAsync`**
- *Objetivo*: que `DefaultCategoryId` no produzca una sugerencia de Category cuando esa
  categoría está desactivada (o no existe) — mismo principio de PR-S11, aplicado al
  segundo punto de entrada de sugerencias.
- *Alcance*: extender la consulta ya existente de `Counterparties` en
  `EnrichWithCounterpartyDefaultsAsync` con un join a `Category` (mismo patrón de
  PR-S11: navegación ya configurada, sin consulta nueva), y que `EnrichSuggestions` no
  llame a `MergeDimension` para Category si la categoría resuelta está desactivada o no
  existe. Incluye, en el mismo PR, la cobertura de tests completa para
  `EnrichSuggestions`/`MergeDimension` listada en la sección 5 — no tiene sentido
  corregir este comportamiento sin dejar cubierta la lógica que se está tocando, y hoy
  no hay ningún test existente que sirva de red de seguridad.
- *Riesgo*: bajo — mismo patrón ya implementado y verificado en PR-S11.
- *Por qué primero*: corrige un defecto real y confirmado, más severo que el de
  PR-S11 en la práctica porque siempre se manifiesta con `SuggestionConfidence.High`.

**PR-S13 — Extraer la normalización de descripción a su propia clase** (ya propuesto
en el análisis anterior, sin cambios: extracción mecánica y de bajo riesgo del bloque
`Normalize`/regex, sin nueva abstracción, sin cambio de comportamiento).

**PR-S14 (condicional a evidencia) — `Currency` como señal secundaria de matching**
(ya propuesto en el análisis anterior: solo si aparecen casos reales de la misma
descripción con clasificaciones distintas según moneda).

No propongo nada nuevo en materia de abstracción de "reglas" ni de una tercera
heurística — seguimos con las mismas dos, y el hallazgo de este análisis es un defecto
de una fuente ya existente, no una fuente nueva.

---

## Recomendación final

**El próximo PR debería ser PR-S12 (extender la exclusión de entidades desactivadas a
`EnrichWithCounterpartyDefaultsAsync`, con su cobertura de tests).** Es un defecto real,
confirmado leyendo el código completo (no una hipótesis): `DefaultCategoryId` se usa
sin ninguna validación de existencia o de `IsDeactivated`, con el agravante de que la
sugerencia resultante siempre lleva `SuggestionConfidence.High`. Es la misma clase de
problema que PR-S11 ya solucionó para la heurística histórica, con el mismo patrón de
solución disponible (extender una consulta ya existente, sin N+1) y de menor riesgo que
las alternativas del roadmap anterior — a diferencia de la extracción de normalización
(puramente estructural, sin urgencia) o la señal de `Currency` (todavía sin evidencia
de que haga falta), acá hay un bug concreto y una ruta de arreglo ya probada.
