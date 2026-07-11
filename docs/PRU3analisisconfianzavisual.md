# PR-U3 — Análisis: diferenciar visualmente la confianza de las sugerencias

Análisis puro, sin código, sin patch, sin modificaciones al repositorio. Base:
`origin/master` en `bbfcef7` (PR-U2 mergeado, confirmado por `git fetch` + `git log`
al inicio de este análisis — nada asumido de la conversación previa, todo releído
del archivo real).

---

## 1. Estado actual — rastreado línea por línea

**Dónde llega `SuggestionConfidence`:** `MovementListItemDto.Suggestions` (backend) →
`toMovementViewModel` (línea ~607-612) copia `confidence: s.confidence` sin tocarlo →
vive intacto en `state.movements[].suggestions[].confidence` como string
`'Low'|'Medium'|'High'`.

**Dónde se usa hoy** (los únicos dos lugares en todo el archivo que leen
`s.confidence` o `m.suggestions[...].confidence`):
- `normalizeSuggestions` (línea 579-598): usa `SUGGESTION_CONFIDENCE_RANK` (línea 561,
  `{ Low: 1, Medium: 2, High: 3 }`) solo para decidir cuál sugerencia conservar si hay
  más de una para la misma dimensión (deduplicación) — nunca para presentación.
- `quickAcceptValues` (línea 901-915, PR-U1): compara `s.confidence !== 'High'` para
  decidir si aparece el botón "Aceptar sugerencia" — una decisión binaria (aparece/no
  aparece el botón), no una representación visual graduada.

**Dónde se pierde:** en `renderSuggestionChips` (línea 804-812). Cada sugerencia se
renderiza con una única clase CSS fija, sin condicional alguna sobre `s.confidence`:
```js
return `<span class="suggestion-chip" title="${esc(s.reason)}">${esc(dimLabel)}: ${esc(valueLabel)}</span>`;
```
`s.confidence` nunca se lee acá. El único rastro indirecto es el texto de `s.reason`
(el `title`), que describe la situación en prosa ("mayoría amplia...", "sin mayoría
clara...") pero **nunca dice literalmente "alta/media/baja"** — confirmado leyendo
`BuildReason` en `ClassificationSuggestionService.cs` (PR-S10): ninguna de las 3
plantillas de texto contiene la palabra "confianza" ni el nombre del nivel.

**Dónde se renderizan los chips:** `renderMovementRow` → `renderSuggestionChips(m)` /
`renderClassificationChips(m)` (PR-U2), ambas devuelven un `<div class="suggestion-chips">`
con `<span>` hijos, insertado dentro de `<td class="desc-cell">`, debajo del
`<div class="truncate">` de la descripción.

**Helpers que intervienen:** `SUGGESTION_DIMENSION_LABELS` (nombre de la dimensión),
`resolveDimensionLabel`/`suggestionValueLabel`/`optionLabel` (nombre del valor),
ninguno toca confianza.

**CSS que controla el aspecto:** `.suggestion-chips` (contenedor, layout puro, sin
color) y `.suggestion-chip` (línea 199-211): una única regla, sin variantes, con
`border: 1px solid var(--green); color: #b0eec8; background: var(--green-bg)` fijo
para toda sugerencia sin importar su confianza real.

**Hallazgo que cambia el marco de la solución:** esos tres valores exactos
(`#b0eec8` / `var(--green-bg)` / `var(--green)`) son **idénticos, carácter por
carácter**, a los de `.status-badge.status-confirmed` (línea 263). Es decir: el
`.suggestion-chip` de hoy ya es, sin saberlo, el tratamiento visual de "confianza
alta" — solo que se aplica indiscriminadamente a los tres niveles.

---

## 2. Alternativas de UX

| # | Alternativa | Ventajas | Desventajas | Accesibilidad | Complejidad | Consistencia |
|---|---|---|---|---|---|---|
| 1 | Color/borde por 3 clases, reusando tokens de `.status-badge` (Low=neutro, Medium=ámbar, High=verde) | Cero valores de color nuevos; el caso High ya coincide byte a byte con el chip actual; combos ya usados y visibles en esta misma pantalla | Solo color no basta por sí solo (ver riesgos) | Insuficiente en soledad, necesita un segundo canal (ver más abajo) | Baja | Alta — reusa literalmente lo ya usado |
| 2 | Ícono por nivel (ej. símbolos o puntos de intensidad) | No depende del color; funciona en escala de grises | Con "✓" ya significando "confirmado" (PR-U2) y "⚠" ya significando "alerta" (K6), un ícono nuevo por nivel de confianza agrega un tercer vocabulario simbólico — riesgo real de colisión de significado, no solo de ruido | Buena si el símbolo tiene forma distinguible, no solo color | Baja-media | Media — introduce simbología nueva |
| 3 | Etiqueta textual explícita ("(alta)"/"(media)"/"(baja)" en el propio chip) | Máxima claridad, cero ambigüedad | `.suggestion-chip` ya trunca a `max-width:150px` — agregar texto reduce el espacio disponible para lo que más importa (dimensión + valor), truncando antes esa información | La mejor de todas en lectura literal | Baja | Baja — ningún otro elemento usa calificador textual así |
| 4 | Opacidad decreciente por nivel | Cero CSS de color nuevo | Reduce la legibilidad del propio texto justo en el caso (Low) que más atención necesita — empeora el contraste en vez de mejorarlo, va contra el propio objetivo | Mala — degrada contraste | Mínima | Sin precedente en esta pantalla |
| 5 | Subrayado o borde más grueso para destacar High | CSS mínimo | El subrayado en web se lee como "enlace" — semánticamente engañoso en un chip no clickeable; el borde grueso desentona con el `1px solid` consistente de todos los demás badges | El subrayado sí ayuda a daltónicos, pero por el problema semántico no lo recomiendo | Mínima | Baja |
| 6 | Combinación: alternativa 1 (color/borde, 3 tokens reusados) + reforzar el `title` existente con la palabra literal del nivel ("Confianza alta: ...") | Resuelve el "a simple vista" (color) y el "sin depender de color" (texto) a la vez, sin agregar ningún elemento visible nuevo — el `title` ya existe, solo se le antepone una palabra | Ninguna real — es aditivo sobre un mecanismo que ya existe | Buena — cumple con no depender solo del color, con costo visual cero | Baja | Alta |

**Descarto 2, 3, 4 y 5** por los motivos de la tabla. **Recomiendo la 6**, que es la 1
reforzada con el único canal no visual que no cuesta "ruido" porque ya existe
(`title`).

---

## 3. Consistencia con Quick Accept (PR-U1)

Sí — deberían reforzarse mutuamente, no permanecer independientes. Hoy, un usuario no
tiene ninguna forma de predecir, mirando la fila, si va a aparecer el botón "Aceptar
sugerencia" hasta que ya lo ve aparecer (o no) junto a "Clasificar". Con color por
nivel, "las 4 dimensiones se ven verdes" se vuelve una señal aprendible que predice
"esta fila va a tener el botón" — antes incluso de mirar `.act-cell`. Es, de hecho, el
primer paso que le faltaba a PR-U1 para ser reconocible de un vistazo, no solo al
llegar al botón.

**Riesgo concreto a comunicar, no a resolver con más código:** una fila puede tener,
por ejemplo, 3 chips verdes (High) y 1 ámbar (Medium) — no califica para Quick Accept
(que exige las 4 en High), pero visualmente "se ve casi toda verde". Un usuario podría
esperar el botón y no encontrarlo. No es un defecto a corregir con lógica adicional —
es información real (3 de 4 dimensiones son confiables, la cuarta no) que el color ya
comunica correctamente; el desajuste de expectativa se resuelve solo con que el patrón
se aprenda rápido ("el botón aparece únicamente cuando las 4 son verdes"), no
ocultando la diferencia.

---

## 4. Consistencia con PR-U2 (chips de clasificación confirmada)

Los chips de PR-U2 no tienen equivalente de confianza — representan un hecho ya
guardado (`categoryId`/`counterpartyId`/`movementType`/`financialImpact` reales), no
una predicción con distintos grados de certeza. No hay "clasificación de baja
confianza": está clasificado o no lo está. Por eso no corresponde aplicarles ninguna
escala — siguen bien como están, con su único diferenciador (paleta neutra + "✓").

Si conviene reforzar las sugerencias con algo más que color: sí, pero no con
iconografía nueva por nivel (ver alternativa 2, descartada) — el refuerzo correcto es
el textual en el `title` (alternativa 6), no un símbolo que compita visualmente con el
"✓" que PR-U2 ya fijó como "esto es un hecho confirmado". Introducir, por ejemplo, un
ícono de estrella o puntos junto al color sería agregar exactamente el ruido que
pediste evitar, sin necesidad real dado que el `title` ya resuelve el problema de
accesibilidad sin costo visual.

---

## 5. CSS — reutilización

**Totalmente reutilizable, sin ningún valor nuevo:**
- El caso High no necesita ninguna regla nueva — `.suggestion-chip` ya es,
  exactamente, ese tratamiento (confirmado en la sección 1: mismos 3 valores que
  `.status-badge.status-confirmed`).
- Medium: los 3 valores exactos de `.status-badge.status-reviewed`
  (`color: #f5dcb0; background: var(--amber-bg); border-color: var(--amber)`).
- Low: los 3 valores exactos de `.status-badge.status-pending`
  (`color: var(--dim); background: var(--surface2); border-color: var(--border)`).
- El contenedor `.suggestion-chips` (layout) no necesita ningún cambio.

**Lo único que necesariamente hay que incorporar:** dos reglas CSS modificadoras
nuevas (`.suggestion-chip.confidence-medium`, `.suggestion-chip.confidence-low`), que
son literalmente una copia de los 3 valores de arriba aplicados a `.suggestion-chip`
en vez de a `.status-badge` — cero valores hexadecimales o tokens inventados para
este PR.

---

## 6. Código — hallazgos reales, sin inventar deuda

- **Sin código muerto causado por este análisis.** No hay ninguna función ni
  variable que quede sin uso al diferenciar los chips.
- **Sin helpers duplicados que corregir.** `resolveDimensionLabel` ya es el único
  punto de resolución de valor, compartido entre sugerencias y clasificación — nada
  que extraer de nuevo.
- **Comentarios que quedarían incompletos (no incorrectos) tras implementar esto:**
  el bloque CSS de `.suggestion-chip` (línea 183-190) y el doc-comment de
  `renderSuggestionChips` no mencionan hoy ninguna diferenciación por confianza —
  seguirían siendo ciertos tal cual están, pero les faltaría una línea que explique
  el nuevo modificador de clase. Es una adición esperable de cualquier PR aditivo, no
  una corrección de algo mal escrito.
- **Sin render innecesario.** `renderSuggestionChips` ya calcula `s.confidence`
  disponible en el mismo objeto que ya recorre — agregar una clase condicional no
  agrega ninguna pasada extra sobre los datos ni ningún cálculo nuevo del lado del
  servidor.
- **Sin CSS redundante existente que limpiar.** No encontré reglas de `.suggestion-chip`
  que queden sin usar o dupliquen otras — la única "redundancia" es, en realidad, la
  reutilización deseada (los 3 valores de `.status-badge` se repiten literalmente,
  a propósito, no por descuido).

---

## 7. Riesgos reales (no especulativos)

- **Color como único canal (WCAG 1.4.1 "Use of Color"):** real si se implementa solo
  la alternativa 1 sin el refuerzo textual del `title` — la 6 lo resuelve sin costo
  visual adicional.
- **Daltonismo (deuteranopía/protanopía, rojo-verde):** el esquema propuesto no usa
  rojo en ningún nivel (Low es neutro/gris, no rojo) — evita el par rojo/verde que es
  el más problemático para las formas de daltonismo más comunes. Los tres fondos
  (`--surface2`, `--amber-bg`, `--green-bg`) tienen luminancia perceptiblemente
  distinta entre sí (gris medio / marrón oscuro / verde casi negro), lo que ayuda a
  distinguirlos incluso sin percepción de matiz — pero sigue sin ser una garantía
  formal sin el refuerzo textual.
- **Contraste:** los tres combos ya están en producción hoy mismo en esta misma
  pantalla (`.status-badge`) — si hubiera un problema real de contraste, ya existiría
  independientemente de este PR. Reusarlos no introduce ningún riesgo nuevo de
  contraste; inventar colores nuevos sí lo habría hecho.
- **Tablas muy largas / densidad visual:** riesgo real descartado por diseño — al
  reusar el mismo box-model de `.suggestion-chip` sin cambiar tamaño, padding ni
  cantidad de chips por fila, no hay cambio de altura de fila ni de densidad. El único
  riesgo genuino sería la alternativa 3 (texto más largo → más truncamiento), que ya
  quedó descartada.
- **Riesgo no listado en tu enunciado pero real, encontrado durante el análisis:** con
  4 chips en una fila mostrando 3 niveles de color distintos simultáneamente
  (ej. High/Medium/Low a la vez en las 4 dimensiones de un mismo movimiento), la fila
  puede volverse visualmente "más ruidosa" que hoy (donde todo es uniformemente
  verde). Es inherente al objetivo mismo del PR (mostrar variación real), no un efecto
  colateral evitable — lo señalo para que no sea una sorpresa al verlo implementado.

---

## Recomendación concreta

Implementar la alternativa 6: **reusar los 3 combos de color/borde ya existentes en
`.status-badge`** (Low=neutro, Medium=ámbar, High=verde — este último ya es
exactamente el `.suggestion-chip` actual, sin cambios) como clases modificadoras sobre
`.suggestion-chip`, y **anteponer la palabra del nivel al `title` existente** (ej.
`"Confianza alta: {reason}"`) para que la información no dependa solo del color. Cero
elementos visuales nuevos, cero ruido agregado, reutilización total de tokens y
box-model ya en producción en esta misma pantalla.

### Alcance exacto de PR-U3

**Archivo a tocar:** únicamente `src/FinancialMcp.Api/wwwroot/movements.html`
(mismo archivo que S5/U1/U2 — sigue sin haber motivo para tocar el backend).

**Cambios concretos:**
1. Dos reglas CSS nuevas: `.suggestion-chip.confidence-medium` y
   `.suggestion-chip.confidence-low`, copiando literalmente los 3 valores de
   `.status-badge.status-reviewed`/`.status-badge.status-pending` respectivamente.
2. Un helper chico que mapee `'Low'|'Medium'|'High'` a la clase modificadora
   correspondiente (cadena vacía para `'High'`, ya que el estilo base alcanza).
3. `renderSuggestionChips` agrega esa clase al `<span>` y antepone la palabra del
   nivel al `title` ya existente.

**Archivos que NO deberían modificarse:** `ClassificationSuggestionService.cs` ni
ningún archivo de backend/DTO/endpoint (el dato ya viaja completo);
`renderClassificationChips` (PR-U2, sin concepto de confianza aplicable);
`renderActionsCell`/`quickAcceptValues` (PR-U1, su lógica de negocio no cambia —
se benefician visualmente de forma indirecta, sin necesitar ningún cambio propio);
`SUGGESTION_CONFIDENCE_RANK` (sigue siendo para orden/deduplicación, no para estilos —
no corresponde fusionarlo con el nuevo mapeo a clase CSS, sirven a propósitos
distintos).

---

**Confirmación explícita:** durante este análisis no modifiqué ningún archivo del
repositorio, no ejecuté `git add`, no hice ningún commit ni push. Todo el código citado
se leyó exclusivamente vía `git show origin/master:<path>` sobre el estado real y
actual de `origin/master` (`bbfcef7`).
