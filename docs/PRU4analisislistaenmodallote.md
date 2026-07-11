# PR-U4 — Análisis: restaurar el listado de movimientos en el modal de lote

Análisis puro, sin código, sin patch, sin modificaciones al repositorio. Base:
`origin/master` en `9bef72c` (PR-U3 mergeado, confirmado por `git fetch` + `git log`
al inicio de este análisis — nada asumido de la conversación previa, todo releído del
archivo real).

---

## 1. Estado actual

**Cómo se construye hoy el modal de lote** (`openBatchModal`, único punto de entrada,
disparado por `btnBatchClassify`):

```js
function openBatchModal() {
    const n = state.selectedIds.size;
    if (n === 0) return;
    state.selectedMovement = null;
    state.modalMode = 'batch';

    const selected = state.movements.filter(m => state.selectedIds.has(m.sourceId));
    const total = selected.reduce((sum, m) => sum + m.amount, 0);
    document.getElementById('classifyModalTitle').textContent = `Clasificar ${n} movimiento${n !== 1 ? 's' : ''}`;
    document.getElementById('classifySubtitle').textContent =
        `Se aplicará la misma clasificación a los ${n} movimientos seleccionados · Total $${fmt(total)}`;
    // ... resetea los 5 campos del formulario a sus valores por defecto ...
}
```

**Dato clave, confirmado leyendo la función completa:** `selected` ya es un array de
los objetos `movement` completos (con `date`, `description`, `amount`, todo lo que
tiene cualquier fila de la tabla) — se construye solo para calcular `total`, y después
se descarta. **No hace falta ningún dato nuevo ni ninguna llamada adicional**: todo lo
necesario para mostrar la lista ya está en memoria en el momento exacto en que se
arma el modal.

**Qué información faltaría:** ninguna. Es el mismo hallazgo que ya se repitió en
PR-U2 (el dato ya viajaba, solo faltaba renderizarlo) — acá es más directo todavía,
porque ni siquiera hace falta leer el DTO de nuevo, la variable `selected` ya existe
en la función que hay que tocar.

**Qué renderizar:** el modal hoy solo tiene el subtítulo (conteo + total) y el
formulario de 5 campos (`cCategory`/`cImpact`/`cMovementType`/`cCounterparty`/
`cComment`) — no existe ningún elemento en el HTML del modal (`#classifyOverlay`) para
listar movimientos individuales. Habría que agregar un contenedor nuevo.

**`saveBatchClassify`:** no necesita ningún cambio — ya opera sobre `state.selectedIds`
directamente, no sobre lo que se muestre en el modal. Confirmado que la lista sería
puramente informativa, sin ningún efecto sobre el envío.

**Ningún riesgo de que la lista quede desactualizada mientras el modal está abierto:**
`.modal-overlay` es un overlay de pantalla completa (`position:fixed; inset:0; z-index:50`)
que bloquea la interacción con la tabla de atrás — la selección no puede cambiar
mientras el modal está abierto, así que un snapshot tomado al abrir el modal es
seguro para toda la duración de la sesión del modal.

---

## 2. Comparación con `group-reconciliation.html`

Recuperado del último commit antes de su eliminación (`6774da4^`, PR-L4). El modal
"Marcar como revisado" (equivalente histórico de este modal de clasificación) tenía
exactamente esta pieza, disparada solo en modo lote:

```js
document.getElementById('batchListWrap').style.display = 'block';
document.getElementById('batchList').innerHTML =
    items.map(m => `<div>${fmtDate(m.date)} · ${esc(m.description)} · $${fmt(Math.abs(m.amount))}</div>`).join('');
```

con este CSS:
```css
.batch-list {
    font-size: 12px;
    color: var(--dim);
    background: var(--surface2);
    border: 1px solid var(--border);
    border-radius: 4px;
    padding: 8px 10px;
    max-height: 90px;
    overflow-y: auto;
    line-height: 1.7
}
```

y este HTML, dentro del mismo `.modal-field` que ya usaba cualquier otro campo:
```html
<div class="modal-field">
    <label>Movimientos seleccionados</label>
    <div class="batch-list" id="batchList"></div>
</div>
```

**Qué mostraba exactamente:** fecha + descripción + importe, una línea de texto plano
por movimiento, dentro de una caja con su **propio scroll interno acotado a 90px de
alto** — independiente del scroll del `.modal-body` general.

**Comportamiento que vale la pena recuperar:** exactamente eso — los 3 datos
(fecha/descripción/importe) y, sobre todo, el patrón técnico del scroll acotado
propio (`max-height` + `overflow-y: auto` en el contenedor de la lista, no en el
modal entero). Es la pieza que hace que esto escale sin romper nada (ver sección 4).

**Comportamiento que NO debería volver:** todo lo que dependía del modelo de matching
N↔M ya eliminado — el modal viejo tenía dos modos (`reviewMode = 'single'|'batch'`)
ligados a un backend de conciliación contra Excel legacy que ya no existe. Acá no
aplica nada de eso: el modal actual ya tiene su propia distinción `modalMode =
'classify'|'batch'` sobre el motor de clasificación vigente, sin relación con aquel.
Tampoco debería volver ningún control interactivo dentro de la lista (el original no
tenía ninguno — era texto plano, sin checkboxes propios) ni ninguna referencia a
`ConfirmMatchCommand`/balance/candidatos, que son parte del modelo retirado.

---

## 3. Alternativas de UX

| Alternativa | Ventajas | Desventajas | Impacto visual | Complejidad |
|---|---|---|---|---|
| **A. Lista de texto plano** (una línea por movimiento: fecha · descripción · importe, como la pantalla vieja) | Mínimo código; reutiliza `fmt`/`fmtDate`/`esc` tal cual; patrón ya probado en producción (aunque en una pantalla hoy eliminada) | Sin alineación tabular — los importes no quedan alineados a la derecha, algo más difícil de comparar entre líneas de un vistazo | Bajo, compacto | Mínima |
| **B. Tabla compacta** (mini `<table>` Fecha/Descripción/Importe) | Importes alineados, más fácil comparar montos de un vistazo | El modal tiene ancho fijo de 460px — una tabla de 3 columnas ahí deja muy poco espacio para la descripción, forzando truncamiento agresivo o quiebre de layout; requiere CSS de tabla nuevo que hoy no existe en ningún modal de esta pantalla | Moderado-alto — estructura nueva en un espacio angosto | Media |
| **C. Solo descripción + importe** (sin fecha) | Línea más corta, menos riesgo de desborde | Pierde la fecha, que puede ser justo el dato que delata una selección accidental (ej. un cargo del mismo comercio en una fecha que no correspondía al lote) | Bajo | Mínima |
| **D. Solo descripción + fecha** (sin importe) | Útil si el objetivo fuera solo "cuáles son" | El importe individual es el dato que más ayuda a detectar un monto atípico colado en la selección — omitirlo va en contra del objetivo explícito ("reducir errores antes de confirmar") | Bajo | Mínima |
| **E. Fecha + descripción truncada + importe** (los 3 datos, como la pantalla vieja, con la descripción truncada) | Máxima capacidad de detectar cualquier tipo de error (fecha rara, comercio equivocado, monto atípico) — cubre las 3 preguntas que alguien se haría antes de confirmar un lote; el truncamiento reduce el único riesgo real de la alternativa A (una descripción muy larga desbordando la línea) reutilizando `.truncate`, que ya existe en este mismo archivo | Línea marginalmente más compleja de armar que A | El mayor de las alternativas de texto, pero acotado por el scroll interno — no crece el modal | Baja |

**Descarto B** (tabla) por el ancho fijo del modal y por no tener ningún precedente de
tabla-dentro-de-modal en esta pantalla. **Descarto C y D** por perder, cada una, uno
de los dos datos que más ayudan a detectar justo el tipo de error que este PR busca
prevenir. **Recomiendo E** — es la A de la pantalla vieja, con un ajuste concreto
(truncar la descripción) que ya tiene solución lista en el propio archivo.

---

## 4. Escalabilidad

Con el patrón de scroll acotado (`.batch-list`-equivalente, `max-height` +
`overflow-y: auto`, propio del contenedor de la lista y no del modal entero):

- **5 movimientos:** la caja queda mayormente vacía, sin necesidad de scroll.
- **20 movimientos:** algunas líneas requieren scroll dentro de la caja acotada — sin
  ningún efecto sobre el resto del modal.
- **100 movimientos:** ~100 líneas de texto (a ~20px de alto por línea con
  `line-height:1.7` y `font-size:12px`, unos 2000px de contenido) dentro de una caja
  de altura fija — se resuelve con scroll interno, sin crecer el modal. Insertar 100
  `<div>` simples en el DOM no es un costo de performance real a esta escala (muy por
  debajo de cualquier umbral perceptible).

**El único escenario que "rompería" algo:** implementar la lista SIN acotar su altura
(sin el `max-height`+`overflow-y` propio) — en ese caso el `.modal-body` (que ya tiene
su propio `overflow-y:auto`, confirmado en el CSS actual) scrollearía todo junto, y
con 100 movimientos el formulario (Categoría, Impacto, etc.) quedaría empujado fuera
de la vista inicial, obligando a scrollear antes de poder completarlo. No es un modal
"roto" técnicamente (`.modal` ya tiene `max-height:88vh` como techo duro), pero sí una
fricción real de UX — evitable adoptando el mismo patrón que ya usaba la pantalla
vieja.

---

## 5. Código — reutilización y hallazgos reales

**Helpers directamente reutilizables, sin cambios:** `fmt` (formato de importe),
`fmtDate` (formato de fecha), `esc` (escape HTML) — los tres ya existen y ya se usan
en el resto del archivo para exactamente este tipo de dato.

**CSS reutilizable:** `.modal-field` + `<label>` como contenedor (mismo patrón que
cualquier otro campo del modal — la pantalla vieja ya lo hacía así, y sigue siendo
válido acá sin cambios). `.truncate` existe para la descripción, aunque su
`max-width: 320px` actual está pensado para la celda de la tabla principal, no para
el interior más angosto del modal (460px de ancho, menos padding) — necesitaría un
ajuste de ancho específico para este contexto, a decidir en la implementación.

**Lo único que no existe y habría que incorporar:** una regla CSS nueva equivalente a
`.batch-list` (contenedor con `max-height`+`overflow-y` propio) — sus valores pueden
copiarse tal cual de la pantalla vieja, todos sobre tokens ya existentes
(`--dim`/`--surface2`/`--border`), sin ningún color nuevo.

**Render duplicado:** no encontré ninguno — no existe hoy ninguna otra función que
renderice una lista de movimientos en este formato compacto (texto plano), así que no
hay nada que unificar ni extraer.

**Comentario que quedaría incompleto (no incorrecto) tras implementar esto:** el
doc-comment de `openBatchModal` dice "Muestra el total como chequeo rápido antes de
aplicar la misma clasificación a todas" — seguiría siendo cierto, pero le faltaría
mencionar que además se lista el detalle. Mismo patrón ya visto en PR-U2/PR-U3:
adición esperable de un PR aditivo, no una corrección de algo mal escrito.

---

## 6. Riesgos reales

- **Scroll/altura del modal:** ya cubierto en la sección 4 — resuelto con el mismo
  patrón de scroll acotado que ya demostró funcionar en la pantalla vieja; sin ese
  patrón, el riesgo es de fricción (formulario empujado fuera de vista), no de modal
  roto.
- **Performance:** no hay ninguno real a la escala de este sistema (decenas a un
  centenar de movimientos por selección) — insertar ese volumen de `<div>` de texto
  no es medible.
- **Accesibilidad:** riesgo menor. La lista es de solo lectura, sin controles
  interactivos dentro (ningún checkbox ni botón propio, igual que la versión vieja) —
  no hay trampa de foco (focus trap) que evitar ni orden de tabulación que romper.
- **Selección accidental — mitigado, no eliminado:** mostrar la lista es estrictamente
  mejor que la situación actual (cero visibilidad del detalle), pero con selecciones
  muy grandes (100 ítems) el usuario puede no revisar cada línea del scroll aunque
  esté disponible — la mejora reduce el riesgo real, no lo garantiza al 100%. Vale
  decirlo con honestidad en vez de vender esto como una solución completa.

---

## Recomendación y alcance exacto de PR-U4

**Implementar la alternativa E:** un contenedor nuevo (mismo patrón `.modal-field` +
`<label>Movimientos seleccionados</label>`) con una lista de texto plano —
fecha · descripción (truncada) · importe, una línea por movimiento — dentro de una
caja con scroll interno acotado (`max-height`+`overflow-y` propios, valores
recuperados tal cual de la pantalla vieja, sobre tokens ya existentes). Poblado
directamente desde `selected` (la variable que `openBatchModal` ya calcula hoy, sin
ningún dato ni consulta nueva), visible únicamente en modo `batch` — en modo
`classify` (individual) no debería aparecer, igual que la pantalla vieja no la
mostraba fuera del modo lote.

**Archivos que tocaría:** únicamente `src/FinancialMcp.Api/wwwroot/movements.html`
(mismo archivo que S5/U1/U2/U3 — sin necesidad de tocar el backend en ningún punto:
todo el dato ya está en memoria del lado del cliente).

**Archivos que NO deberían modificarse:** cualquier archivo de backend
(`ClassificationSuggestionService.cs`, DTOs, endpoints — el dato ya viaja completo en
`MovementListItemDto`, no hace falta nada nuevo); `saveBatchClassify` (no necesita
ningún cambio, sigue operando sobre `state.selectedIds`); `renderSuggestionChips`/
`renderClassificationChips`/`renderActionsCell`/`quickAcceptValues`/`classifyMovement`
(sin relación con el modal de lote); `openClassifyModal` (modo individual, no debería
ganar esta lista).

---

Sobre la observación de arquitectura: este PR sigue exactamente esa proporción — es
una mejora de UX pura, no toca el motor de sugerencias en ningún punto, y como quedó
confirmado en la sección 1, ni siquiera necesita datos adicionales del backend. El
motor queda congelado tal como se decidió; esta es una mejora que el propio motor ya
habilitó (todo el dato necesario ya viaja) sin necesitar tocarlo de nuevo.

**Confirmación explícita:** durante este análisis no modifiqué ningún archivo del
repositorio, no ejecuté `git add`, no hice ningún commit ni push. Todo el código
citado se leyó exclusivamente vía `git show origin/master:<path>` (estado actual) y
`git show 6774da4^:<path>` (último commit con `group-reconciliation.html` antes de su
eliminación en PR-L4).
