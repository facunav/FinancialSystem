# PR-S8 — Análisis de una normalización más inteligente de descripciones

Análisis puro, sin código, sin patch, sin modificaciones al repositorio — según lo pedido.
Base: `origin/master` en `80ae7b6` (PR-S7 mergeado, motor de sugerencias con dos heurísticas:
histórica exacta + enriquecimiento por `Counterparty.Default*`).

Código leído para este análisis:

- `src/FinancialSystem.Infrastructure/Suggestions/ClassificationSuggestionService.cs` (`Normalize`, y todo el flujo que la usa).
- `src/FinancialSystem.Application/Suggestions/IClassificationSuggestionService.cs` (contrato y doc-comments).
- `src/FinancialSystem.Application/Parsing/BbvaTransactionLineParser.cs` (tarjeta BBVA Visa).
- `src/FinancialSystem.Application/Parsing/MastercardTransactionLineParser.cs` (tarjeta Mastercard).
- `src/FinancialSystem.Application/Parsing/Helpers/CurrencyDetector.cs`.
- `src/FinancialSystem.Infrastructure/Imports/BankStatements/BbvaBankStatementParser.cs` (débito bancario BBVA).
- `src/FinancialSystem.Infrastructure/Imports/Normalization/TransactionNormalizer.cs` (normalizador de import, distinto y no reutilizado por el motor de sugerencias).

---

## 1. Limitaciones de la normalización actual

`ClassificationSuggestionService.Normalize` (sin cambios desde PR-S3) hace exactamente tres cosas:

```csharp
var trimmed = description.Trim();
// colapsa corridas de whitespace a un solo espacio (bucle manual, no regex)
return collapsed.ToString().ToUpperInvariant();
```

Es una clave de comparación **exacta**: dos descripciones producen la misma sugerencia solo si,
después de trim + colapso de espacios + mayúsculas, son *carácter por carácter idénticas*. Eso
es deliberado (documentado como "ingenua a propósito" desde PR-S3) pero tiene un costo medible
contra los datos reales que efectivamente produce este sistema:

- **No absorbe montos variables embebidos en la descripción.** El parser de BBVA Visa
  (`BbvaTransactionLineParser`) deja el monto en dólares *dentro* del texto para transacciones
  USD: `"PLAYSTATION USD 4,99"`, `"NETFLIX.COM 0HnCsFb8GUSD 11,14"`. El mismo comercio recurrente
  factura montos distintos mes a mes → la clave normalizada nunca coincide entre sí, y el
  historial de clasificaciones de ese comercio queda fragmentado en N claves distintas en vez de
  una sola. Esto **no es hipotético**: es el comportamiento documentado y probado del parser real
  (`CurrencyDetector.TryExtractUsdAmount` existe precisamente porque el monto USD vive en la
  descripción).
- **No absorbe contadores de cuotas.** El parser de Mastercard normaliza el *formato* de cuotas
  pero no las elimina: `"GARBARINO C1/3"` y `"GARBARINO C2/3"` son, después de `Normalize`, dos
  claves distintas para el mismo comercio y la misma compra en cuotas. Mismo problema que el
  punto anterior: fragmenta el historial de un comercio real en vez de agruparlo.
- **Inconsistencia entre parsers para el mismo tipo de dato.** BBVA Visa deja `"USD 4,99"` dentro
  de `Description`; Mastercard lo *elimina* de la descripción antes de persistir
  (`"Limpiar la marca USD de la descripción para que quede limpia"`, `MastercardTransactionLineParser.NormalizeDescription`
  vía `UsdInDescriptionPattern.Replace`). Es decir: el mismo problema (monto USD embebido) ya
  tiene precedente de solución determinística dentro de este mismo codebase, solo que a nivel de
  un parser y no del otro, y no a nivel del motor de sugerencias.
- **Números de cupón/autorización: no son un problema real hoy.** Vale aclarar esto porque el
  enunciado del PR los menciona como candidato. En ambos parsers de tarjeta (BBVA y Mastercard)
  el número de cupón se captura en un grupo de regex *separado* y se guarda en
  `Transaction.CouponNumber`, nunca dentro de `Description`. No hay nada que limpiar ahí —
  cualquier regla dirigida a "quitar números de autorización de la descripción" estaría
  resolviendo un problema que no existe en los datos reales de este sistema.
- **Prefijos/códigos de sucursal o canal: tampoco hay evidencia.** En el extracto bancario BBVA
  (`BbvaBankStatementParser`), el código de canal/detalle ("100 - BANCA ONLINE", "733 - ") vive en
  una columna separada (`Detail`), no concatenado en `Concept`. No encontré ningún parser actual
  que deje códigos de sucursal embebidos en la descripción.
- **Sensible a puntuación/símbolos no semánticos.** No hay evidencia concreta de datos reales
  hoy (no vi casos como `"MC DONALD'S"` vs `"MCDONALDS"` en los formatos parseados), pero es un
  riesgo estructural: cualquier variación de puntuación entre dos apariciones del mismo comercio
  (un punto, un apóstrofo, un asterisco de más) rompe la igualdad exacta.
- **No es exclusiva del motor de sugerencias.** Existe un normalizador completamente distinto,
  `ITransactionNormalizer.CleanDescription` (usado en tiempo de importación, antes de persistir),
  que también solo colapsa espacios y trunca a 512 caracteres — confirma que el problema de
  "limpiar mejor la descripción" nunca se resolvió en ninguna capa, ni en import ni en
  sugerencias.

En síntesis: la limitación real y medible no es "la normalización es poco agresiva en general" —
es que **dos patrones concretos y ya observados en el código de parsing (monto USD embebido,
contador de cuotas embebido) fragmentan el historial de exactamente los comercios recurrentes que
el motor de sugerencias más necesita reconocer** (comercios que se repiten mes a mes son
justamente los que más beneficio traen de una sugerencia histórica).

## 2. Qué reglas determinísticas agregaría

Ordenadas por evidencia real en el código de parsing (no por especulación):

1. **Quitar el monto USD embebido de la clave de comparación.** Mismo patrón que ya usa
   `CurrencyDetector` (`\bUSD\s*monto`) — reutilizar ese conocimiento del formato, no reinventarlo.
   Ejemplo: `"PLAYSTATION USD 4,99"` y `"PLAYSTATION USD 9,99"` deberían normalizar a la misma
   clave.
2. **Quitar el contador de cuotas embebido.** Patrón `C\d+/\d+` (mismo formato que ya normaliza
   `MastercardTransactionLineParser.NormalizeDescription`, solo que ahí se normaliza el *formato*
   y acá se debería eliminar directamente para la clave de comparación). Ejemplo: `"GARBARINO
   C1/3"` y `"GARBARINO C2/3"` deberían normalizar a la misma clave.
3. **Colapso de espacios (ya existe)** — sin cambios, ya cubierto.
4. **Mayúsculas (ya existe)** — sin cambios, ya cubierto.

Explícitamente **no** propongo (por falta de evidencia en los datos reales de este sistema, no
por descarte teórico):

- Limpieza de números de cupón/autorización — ya resuelto upstream, en `Description` no aparecen.
- Limpieza de códigos de sucursal/canal — ya resuelto upstream (columna separada), no aparecen.
- Limpieza de prefijos bancarios genéricos — no encontré ningún caso real en los parsers
  existentes (BBVA Visa, BBVA Mastercard, BBVA débito).
- Normalización general de símbolos/puntuación — sin casos reales documentados hoy; ver riesgos
  en la pregunta 3.

## 3. Qué reglas son seguras vs. cuáles arriesgan falsos positivos

**Seguras (agregaría en el próximo PR):**

- *Quitar monto USD embebido*: el patrón es inequívoco (`USD` + número con formato de moneda
  argentina), ya está probado en producción por `CurrencyDetector` y por
  `MastercardTransactionLineParser` (que literalmente hace este mismo strip, solo que a nivel de
  parser en vez de a nivel de clave de comparación). Cero ambigüedad: un monto en dólares nunca
  es parte del nombre de un comercio.
- *Quitar contador de cuotas*: patrón igualmente inequívoco (`C` + dígitos + `/` + dígitos, en un
  formato ya validado por el parser de Mastercard). Un contador de cuota nunca es parte del
  nombre de un comercio.

Ambas reglas comparten la misma propiedad que las hace seguras: **el propio código de parsing ya
demuestra, con datos reales, que ese fragmento de texto es un dato variable y no parte de la
identidad del comercio** — no es una suposición mía, es un hecho ya codificado en otro lugar del
sistema.

**Riesgosas — NO las propondría sin evidencia adicional:**

- *Quitar dígitos finales genéricos* (cualquier número al final de la descripción, no solo
  cuotas/USD): riesgo alto de falso positivo. Un número al final podría ser parte legítima del
  nombre de un comercio (`"FARMACIA 24"`, `"CANAL 13"`, `"RUTA 8 SA"`) o distinguir sucursales
  con configuraciones realmente distintas de categoría/contraparte. Colapsar todos esos casos a
  una sola clave podría *unir* comercios que el usuario clasifica distinto a propósito.
- *Eliminación genérica de símbolos/puntuación*: mismo riesgo — sin casos reales que lo motiven,
  cualquier regla así es especulativa y puede fusionar nombres de comercios que no deberían
  fusionarse (ej. abreviaturas ambiguas).
- *Cualquier regla "por si acaso" sin un ejemplo real de datos que la necesite*: el criterio que
  ya viene aplicándose en este proyecto desde PR-L4.5/PR-S6 (medir antes de optimizar, no
  construir sobre supuestos) aplica igual acá — una regla no evidenciada en datos reales es, por
  definición, una fuente de falsos positivos sin beneficio demostrado.

Regla general para decidir "seguro vs. riesgoso": **¿el patrón que se está eliminando puede,
alguna vez, ser parte de la identidad de un comercio real?** Si la respuesta es "no, nunca" (como
un monto en dólares o un contador de cuotas), es seguro. Si la respuesta es "podría, en algunos
casos" (como dígitos genéricos o símbolos), es riesgoso y no debería agregarse sin evidencia
concreta de los datos reales del usuario.

## 4. Cómo organizar las reglas

Estructura recomendada, consistente con la postura ya fijada en PR-S6/PR-S7 de no introducir
abstracciones de "regla" (`IClassificationSuggestionRule` fue explícitamente rechazado para las
heurísticas de sugerencias, y el mismo argumento aplica acá con más razón todavía: son dos pasos
de limpieza de texto, no dos estrategias de negocio):

- **Un solo método `Normalize`, con los nuevos pasos como métodos privados `static` adicionales,
  llamados en secuencia dentro de `Normalize`** — mismo patrón ya usado en el propio
  `ClassificationSuggestionService` (`AddDimensionSuggestion`, `MergeDimension`, etc. son todos
  métodos privados estáticos, no una jerarquía de tipos).
- Cada paso nuevo (quitar USD embebido, quitar cuota embebida) como su propio método privado
  `static string StripEmbeddedUsdAmount(string)` / `static string StripInstallmentCounter(string)`,
  con su propio regex `[GeneratedRegex]` o `Regex` estático compilado (mismo patrón que ya usan
  los parsers de BBVA/Mastercard) — no un `Regex.Replace` inline en cada llamada.
- Orden de aplicación: quitar USD embebido y cuota embebida **antes** de colapsar espacios (para
  que el espacio que dejan al remover el fragmento se absorba en el mismo paso de colapso ya
  existente, en vez de necesitar un segundo paso de limpieza).
- **No** crear una carpeta `Normalization/` ni una interfaz — no hay ninguna señal de que el
  motor de sugerencias vaya a necesitar más de dos o tres pasos de limpieza determinística en el
  corto plazo, y ya existe precedente en este codebase (`ITransactionNormalizer`, en la capa de
  import) de que cuando de verdad se necesita una abstracción reutilizable para normalización, se
  crea aparte — pero ese normalizador tiene un propósito distinto (limpieza de datos para
  persistencia) del de acá (clave de comparación para matching), y esa separación ya fue una
  decisión deliberada de diseño anterior a este PR. Mezclarlos sería scope creep sobre una
  decisión ya tomada.
- Mantener `Normalize` como el único punto de entrada usado por `SuggestAsync` — no exponerlo
  públicamente ni cambiar su firma.

## 5. Impacto en el rendimiento

`Normalize` ya se ejecuta hoy sobre cada descripción entrante y sobre **todo** el historial de
`ClassifiedMovement` cargado en memoria (no hay índice sobre `Description`, confirmado en PR-S6).
Agregar dos pasos más de regex (compilados, `O(longitud del texto)` cada uno) no cambia el orden
de complejidad general del método — sigue siendo lineal en el tamaño de la descripción, ejecutado
una vez por movimiento entrante y una vez por fila de historial cargada, exactamente como hoy.

El costo dominante de `SuggestAsync` sigue siendo la única consulta a `ClassifiedMovements` (todo
el historial, sin filtrar) — el trabajo de CPU en `Normalize` (incluso con las reglas nuevas) es
insignificante comparado con el round-trip a la base de datos, igual que ya se concluyó en el
análisis de PR-S6. No hay razón para medir esto por separado ni para preocuparse por su impacto:
dos regex compilados adicionales sobre strings cortos (descripciones de movimientos bancarios,
típicamente <100 caracteres) no es un costo perceptible frente a una consulta SQL.

## 6. Riesgos para usuarios reales

El riesgo asimétrico a tener en cuenta: un **falso negativo** (la normalización no une dos
descripciones que en realidad son el mismo comercio) simplemente deja al usuario sin sugerencia —
mismo comportamiento que tiene hoy, no es una regresión, el usuario sigue clasificando a mano como
ya lo hace. Un **falso positivo** (la normalización une dos descripciones que en realidad son
comercios distintos) es más grave: el motor propondría activamente una categoría/tipo/impacto
incorrectos para un movimiento, con la misma superficie de confianza (chip visible, mismo texto
de "razón") que una sugerencia correcta — silenciosamente erosiona la confianza del usuario en el
sistema si acepta una sugerencia mala sin revisar, que es justamente el comportamiento que la UI
de sugerencias (PR-S5) fue diseñada para facilitar (aceptar rápido, con poca fricción).

Por eso las dos reglas propuestas en la pregunta 2 fueron elegidas específicamente por tener
**cero ambigüedad demostrable** (un monto en dólares o un contador de cuotas nunca son parte del
nombre de un comercio) — y por eso, en la pregunta 3, descarté explícitamente cualquier regla sin
evidencia de datos reales: el costo de un falso positivo acá es mayor que el beneficio de cubrir
un caso hipotético.

Riesgo operativo adicional: cualquier cambio en `Normalize` afecta retroactivamente el agrupamiento
de *todo* el historial ya persistido (no hay migración de datos, es una función pura aplicada en
cada lectura) — esto es seguro porque `ClassifiedMovement.Description` nunca se modifica, solo se
reinterpreta la clave de agrupación en memoria en cada consulta. No hay riesgo de corrupción de
datos, solo el riesgo (ya cubierto arriba) de que la nueva agrupación sea semánticamente
incorrecta si la regla no es lo suficientemente conservadora.

## 7. PRs pequeños propuestos

**PR-S9 (recomendado como próximo paso concreto):** agregar exactamente las dos reglas
determinísticas seguras identificadas en la pregunta 2 — strip de monto USD embebido y strip de
contador de cuotas embebido — como pasos adicionales dentro de `Normalize`, con tests unitarios
que cubran los casos reales ya documentados en los comentarios de
`BbvaTransactionLineParser`/`MastercardTransactionLineParser` (`"PLAYSTATION USD 4,99"` vs
`"PLAYSTATION USD 9,99"` → misma clave; `"GARBARINO C1/3"` vs `"GARBARINO C2/3"` → misma clave;
casos sin USD/cuotas deben quedar exactamente igual que hoy, byte a byte, para no introducir
ninguna regresión en el matching existente). Sin cambios de contrato, sin cambios de
`SuggestionConfidence` ni de las heurísticas 1 y 2 — es puramente una mejora de la función
`Normalize` que ambas heurísticas ya usan indirectamente (la heurística 2 depende de que la
heurística 1 haya encontrado una Contraparte, así que una mejor normalización beneficia a ambas
sin tocarlas).

Fuera de alcance de PR-S9, para PRs futuros **solo si aparece evidencia real** en los datos del
usuario (no antes):

- Reglas de limpieza de símbolos/puntuación — solo si se observan casos reales de la misma
  descripción con puntuación inconsistente.
- Reglas de sucursal/canal — solo si en el futuro se agrega soporte a un banco/tarjeta cuyo
  formato sí las embeba en la descripción (hoy ninguno de los tres parsers existentes lo hace).
- Cualquier forma de fuzzy matching / distancia de edición — explícitamente fuera de alcance por
  el enunciado de este PR y por decisión ya tomada desde PR-S1/PR-S3 (el motor es determinístico a
  propósito).

---

## Recomendación concreta

El próximo PR debería ser **PR-S9: agregar dos reglas determinísticas a `Normalize`** (strip de
monto USD embebido y strip de contador de cuotas embebido), implementadas como métodos privados
estáticos adicionales dentro de `ClassificationSuggestionService`, sin ninguna abstracción nueva,
sin tocar el contrato público, sin tocar las heurísticas 1 y 2, con tests que fijen tanto los
casos nuevos (USD y cuotas colapsando a la misma clave) como el comportamiento actual sin
regresión. Es el único punto de esta lista con evidencia concreta en el código de parsing real de
este sistema — todo lo demás (símbolos, sucursales, prefijos bancarios) queda deliberadamente
pendiente hasta que aparezca esa misma clase de evidencia.
