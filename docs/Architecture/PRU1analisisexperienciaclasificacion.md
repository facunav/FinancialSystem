# Épica U — Análisis de la experiencia de clasificación de movimientos

Análisis puro, sin código, sin patch, sin modificaciones al repositorio. Base:
`origin/master` en `763b64d` (PR-S12 mergeado, confirmado por `git fetch` + `git log`
al inicio de este análisis — nada asumido de conversaciones anteriores).

Código leído (todo vía `git show origin/master:<path>` o `git show <commit>:<path>`):

- `src/FinancialMcp.Api/wwwroot/movements.html` completo (1151 líneas) — la única
  pantalla del flujo actual.
- `src/FinancialMcp.Api/wwwroot/group-reconciliation.html`, recuperado del último
  commit antes de su eliminación (`6774da4^`, PR-L4) — 1418 líneas, para comparar
  contra el punto 11/12.
- `src/FinancialMcp.Api/DTOs/MovementsDtos.cs`.
- `src/FinancialMcp.Api/Endpoints/MovementReviewEndpoints.cs` (manejo de errores de
  `/api/movement-review/classify`).

---

## El flujo actual, recorrido completo

**Pantallas: 1.** Todo el flujo (listar, filtrar, asignar cuenta, clasificar
individual, clasificar en lote) vive en `movements.html`, con un único modal
reutilizado para clasificación individual y en lote. Esto ya es bueno — lo señalo
primero porque es la base de todo lo que sigue: el problema de esta pantalla no es
"cuántas pantallas", es "cuántas interacciones por movimiento dentro de esta única
pantalla".

**Clics para clasificar un movimiento, caso ideal** (el motor de sugerencias ya
propuso todo correctamente, con confianza alta): abrir el modal (clic en "Clasificar")
→ verificar los valores precargados a simple vista → clic en "Guardar clasificación".
**2 clics**, pero ningún camino existe para evitar el modal — incluso cuando las 4
dimensiones ya vienen sugeridas con `SuggestionConfidence.High`, hay que abrir y
cerrar el modal igual que si no hubiera ninguna sugerencia.

**Clics para clasificar un movimiento sin sugerencias útiles**: abrir modal (1) +
Categoría (abrir el `<select>` + elegir, 2) + Impacto financiero si el default
"Gasto real" no aplica (2) + Tipo de movimiento si el default "Compra" no aplica (2) +
Contraparte opcional (2) + Guardar (1) → entre 4 y 10 clics según cuántos campos haya
que tocar.

**Clics para clasificar en lote**: un clic de checkbox por movimiento seleccionado (N)
+ "Clasificar seleccionados" (1) + completar los mismos 4 campos (hasta 8) + Guardar
(1) → `N + 10` en el peor caso, mejor que `N × 6` individual, pero **requiere que el
usuario ya sepa, mirando la lista, cuáles son idénticos** — no hay ningún agrupamiento
ni preselección automática por descripción o por sugerencia coincidente.

Con "cientos de movimientos" (el escenario que vos mismo planteás en la pregunta 8),
esta aritmética importa: 300 movimientos × un promedio conservador de 4 clics sin
ningún agrupamiento son 1200 clics. Es el problema central que hay que atacar.

---

## 1-2. Clics y pantallas

Ya cubierto arriba. Resumen: **1 pantalla** (correcto, no es el problema) vs. **2 a 10
clics por movimiento** dependiendo de cuánto ayude la sugerencia — y ni siquiera el
caso de máxima confianza evita el viaje completo al modal.

## 3. Qué información realmente necesita ver el usuario

En la lista, sin abrir nada: Fecha, Descripción, Importe — imprescindibles, ya están.
Estado (Pendiente/Revisado/Confirmado) — imprescindible para saber qué falta. Los
chips de sugerencia (PR-S5) — valiosos, ya están, son la única pista hoy disponible
sin abrir el modal.

**Hallazgo concreto: un movimiento ya clasificado no muestra en ningún lado de la
fila qué categoría/tipo/impacto/contraparte se le asignó.** Confirmé esto leyendo
`renderMovementRow`: una vez que `m.status !== 'Pending'`, `m.suggestions` llega
vacío del backend (por contrato, `MovementView.Suggestions` nunca tiene valor en
movimientos ya clasificados) — así que la única celda que podría haber mostrado algo
(`renderSuggestionChips`) queda en blanco, y no existe ninguna otra columna que
muestre `categoryId`/`counterpartyId`/`movementType`/`financialImpact` para filas
clasificadas. La única forma de ver qué se eligió es abrir "Cambiar categoría" y leer
los `<select>` ya precargados. Si estás revisando un lote recién clasificado para
confirmar que salió bien, hoy tenés que reabrir cada movimiento uno por uno.

## 4. Qué información sobra

**Columna "Alerta"**: ocupa una columna entera de ancho fijo para un dato que la
mayoría de las filas no tiene (`—`). Ya existe precedente en este mismo archivo de
resolver exactamente este problema (PR-S5 movió las sugerencias de una columna propia
a un renglón secundario dentro de la celda Descripción) — la alerta podría vivir del
mismo modo, sin una columna dedicada casi siempre vacía.

**Columna "Cuenta financiera"**: es una tarea distinta de clasificar (asignación
contable, no categorización) mezclada en la misma fila. No digo que sobre el dato —
sobra tenerlo siempre visible y editable en cada fila cuando la tarea del momento es
"clasificar 300 movimientos", no "revisar a qué cuenta pertenece cada uno". Candidata
a quedar oculta por defecto y visible solo bajo un toggle o en una vista aparte (ver
roadmap).

No encuentro que sobre nada más — Fecha/Descripción/Importe/Estado/Acción son
mínimos indiscutibles.

## 5. Qué acciones se repiten demasiado

Abrir el modal, mirar los mismos 4 campos, cerrar, repetir — para descripciones
idénticas o casi idénticas (el motor de sugerencias existe justamente porque estas se
repiten: suscripciones, supermercado, sueldo). El batch existente mitiga esto solo si
el usuario hace el trabajo manual de identificar y tildar cada fila igual — no hay
ninguna acción que diga "esto ya se repitió antes, agrupalo".

Seleccionar cuenta financiera fila por fila cuando la mayoría de los movimientos de un
mismo período probablemente van a la misma cuenta (no hay "aplicar a todos los
visibles" para ese campo, a diferencia de la categoría que sí tiene su propio flujo
batch).

## 6. Qué acciones podrían hacerse con un solo clic

**La más justificada: "Aceptar sugerencia" directamente desde la fila, sin abrir el
modal**, para sugerencias de confianza `High` (o, más ajustado, solo cuando las 4
dimensiones tienen sugerencia con `High` a la vez). El dato ya existe
(`m.suggestions`, `SuggestionConfidence`), el backend ya calcula exactamente esta
distinción desde PR-S10 — hoy se usa solo para decidir qué chip mostrar, nunca para
ofrecer una acción. Es la mejora de mayor impacto medible: convierte "abrir modal +
verificar + guardar" (2 clics + una lectura) en un único clic con el mismo resultado.

**"Aplicar a todos los que tengan esta misma sugerencia"**: un botón que, al lado de
un chip de sugerencia, seleccione automáticamente todos los pendientes visibles con
exactamente la misma sugerencia — reemplaza la selección manual de N checkboxes
idénticos por 1 clic + revisar + confirmar.

## 7. Qué tareas podrían hacerse por teclado

**Cerrar el modal con Escape — existía en `group-reconciliation.html`
(`document.addEventListener('keydown', ...)` en dos lugares) y no está en
`movements.html`.** Confirmé por grep que hoy no hay ningún listener de teclado en
todo el archivo actual — es, literalmente, la única funcionalidad de teclado que
existía en la pantalla vieja y no sobrevivió a la migración.

**Guardar el modal con Enter** cuando el foco está en un campo del formulario — no
existe hoy en ninguna de las dos versiones, pero es una adición natural y de bajo
riesgo dado que ya existe el patrón de un solo botón de guardar.

**Encadenar automáticamente al siguiente pendiente** después de guardar — hoy, al
guardar, el modal se cierra y hay que volver a hacer clic en "Clasificar" de la fila
siguiente. Para alguien clasificando en serie, que el modal se reabra directamente
sobre el próximo pendiente (o que Enter tras guardar avance al siguiente) evitaría
volver al mouse entre cada movimiento.

Ninguna de las dos pantallas (ni la actual ni la vieja) tuvo nunca navegación de filas
por flechas — no lo propongo por falta de evidencia de que haga falta, ni la pantalla
vieja lo tenía.

## 8. Mejoras visuales para clasificar cientos de movimientos más rápido

**Diferenciar visualmente los chips de sugerencia por `SuggestionConfidence`.**
Hallazgo concreto: desde PR-S10 el backend produce Low/Medium/High con semántica real
(Low = historial dividido o sin mayoría clara, High = unánime o mayoría calificada),
pero el CSS de `.suggestion-chip` es una sola clase, un solo color verde, sin
distinción — la única forma de enterarse de la confianza real es pasar el mouse y leer
el `title`. Con cientos de filas, nadie va a hacer eso fila por fila. Un color/borde
distinto por nivel (ya existe el patrón de esta paleta: `--green`/`--amber`/`--red`
usados en `.status-badge`) permitiría distinguir de un vistazo "esto es casi seguro"
de "esto es una suposición débil" sin leer nada.

**Un indicador de progreso del período** (cuántos de los N movimientos del filtro
actual ya están clasificados) — hoy `countLabel` solo muestra el total, no cuántos
faltan. Sensación de avance real cuando se están procesando cientos.

**Agrupar visualmente movimientos con la misma descripción normalizada** (ya existe
esa misma clave de agrupación en el backend, `ClassificationSuggestionService.Normalize`,
reutilizable como criterio de agrupación visual) — convierte una lista plana de 300
filas en, por ejemplo, 40 grupos, cada uno clasificable de una vez.

No propongo un rediseño visual "moderno" sin justificación — el resto del diseño
(paleta oscura, tipografía, densidad de tabla) ya funciona bien para una tarea de
revisión rápida de muchas filas; los tres puntos de arriba son mejoras puntuales
sobre fricciones reales, no un cambio estético general.

## 9. Qué cosas deberían verse directamente en la lista sin abrir el modal

Ya cubierto en el punto 3: la clasificación asignada a un movimiento ya clasificado
(hoy invisible sin reabrir el modal), y la confianza de cada sugerencia (hoy solo
accesible vía `title`, cubierto en el punto 8).

## 10. Qué cosas sí deberían permanecer dentro del modal

El formulario completo de clasificación en sí — 4 campos + comentario libre es
información suficientemente detallada como para merecer su propio espacio dedicado,
no una edición inline en la fila (algo que la propia pantalla ya decidió
correctamente para "Cuenta financiera", que si es inline). El comentario libre en
particular (`textarea`) no tiene sentido como columna de tabla. La confirmación de
lote (ver ítems seleccionados antes de aplicar la misma clasificación a todos) también
pertenece al modal — es una decisión de una sola vez, no algo que se consulte
repetidamente por fila.

## 11. Qué funcionalidades de `group-reconciliation.html` realmente se perdieron y siguen siendo útiles

- **Escape cierra el modal** — señalado en el punto 7, la única pérdida real y
  directamente aplicable hoy.
- **Lista de movimientos seleccionados dentro del modal de lote**
  (`batchListWrap`/`batchList`, visible en el modal de "Marcar como revisado"): antes
  de aplicar una clasificación a varios movimientos a la vez, el usuario veía la
  lista real de qué iba a modificar. Hoy `openBatchModal` solo muestra un conteo y un
  total (`Se aplicará la misma clasificación a los N movimientos seleccionados ·
  Total $X`) — sin poder repasar cuáles son. Con lotes grandes, esto es una pérdida
  real de seguridad ante un error de selección (tildar una fila de más sin darse
  cuenta).

## 12. Qué funcionalidades nunca deberían volver

Todo lo que dependía del backend de matching Legacy retirado en Épica L, confirmado
que ya no tiene ningún sustento de datos: la columna "Excel / Manual" completa
(candidatos), la barra de balance banco↔Excel (`updateBalance`, `balRef`/`balCand`/
`balDiff`), "Confirmar Match" (`ConfirmMatchCommand`, ya eliminado), "Descartar"
candidatos. Esto no es una opinión de diseño — es una consecuencia directa de que la
fuente de datos que alimentaba ese flujo (`LegacyImportedExpense`) fue eliminada por
completo en Épica L, con migración incluida. Reintroducir cualquier parte de esa UI
implicaría reconstruir un backend que se retiró deliberadamente por falta de uso real.

---

## Hoja de ruta — basada en productividad, no en arquitectura

**PR-U1 — Diferenciar chips de sugerencia por confianza (color/borde por Low/Medium/High)**
- *Objetivo*: que la confianza ya calculada por el backend (PR-S10) sea visible sin
  hover.
- *Beneficio para el usuario*: decidir de un vistazo qué sugerencias aceptar
  "a ciegas" y cuáles revisar con más cuidado, sobre todo en listas largas.
- *Riesgo*: mínimo — solo CSS y una clase condicional en `renderSuggestionChips`, cero
  cambios de datos ni de backend.
- *Complejidad*: baja.
- *Dependencia*: ninguna — es la base visual sobre la que se apoyan PR-U2 y PR-U4.

**PR-U2 — Aceptar sugerencia con un clic desde la fila (solo confianza High en las 4 dimensiones)**
- *Objetivo*: eliminar el viaje al modal para el caso donde el motor ya tiene certeza
  completa.
- *Beneficio para el usuario*: el cambio de mayor impacto medible — de 2 clics + una
  lectura a 1 clic, exactamente en el caso que Épica S entera se construyó para
  resolver.
- *Riesgo*: medio — hay que decidir con cuidado el umbral ("las 4 dimensiones en
  High" vs. "alguna en High"), y comunicar claramente que fue una aceptación
  automática (para poder deshacerla via "Cambiar categoría" como cualquier otra
  reclasificación).
- *Complejidad*: media — reutiliza el mismo endpoint `/api/movement-review/classify`
  que ya usa el modal, sin comando nuevo.
- *Dependencia*: se beneficia de PR-U1 (poder ver qué es High antes de que exista el
  botón), pero no lo requiere estrictamente.

**PR-U3 — Escape cierra el modal, Enter lo guarda**
- *Objetivo*: recuperar la única funcionalidad de teclado que tenía
  `group-reconciliation.html` y no sobrevivió, más un atajo natural de guardado.
- *Beneficio para el usuario*: menos viajes al mouse en sesiones largas de
  clasificación.
- *Riesgo*: bajo — hay que tener cuidado de no capturar Enter dentro del `textarea`
  del comentario (donde Enter debe insertar una línea nueva, no guardar).
- *Complejidad*: baja.
- *Dependencia*: ninguna.

**PR-U4 — Mostrar la clasificación asignada en la fila para movimientos ya clasificados**
- *Objetivo*: cerrar el hallazgo de la sección 3/9 — que un movimiento clasificado no
  muestre en ningún lado qué se le asignó.
- *Beneficio para el usuario*: poder auditar un lote recién clasificado sin reabrir
  cada fila.
- *Riesgo*: bajo — el dato ya viaja en `MovementListItemDto`
  (`CategoryId`/`CounterpartyId`/`MovementType`/`FinancialImpact`), es pura
  presentación.
- *Complejidad*: baja-media (requiere resolver `CategoryId`/`CounterpartyId` contra
  `state.categories`/`state.counterparties`, mismo patrón ya usado en
  `suggestionValueLabel`).
- *Dependencia*: ninguna.

**PR-U5 — Lista de movimientos dentro del modal de clasificación en lote**
- *Objetivo*: recuperar la visibilidad de "qué voy a modificar" que tenía
  `group-reconciliation.html` (`batchList`) y no tiene `openBatchModal` hoy.
- *Beneficio para el usuario*: seguridad ante selecciones accidentales antes de
  aplicar una clasificación a varios movimientos a la vez.
- *Riesgo*: bajo.
- *Complejidad*: baja — los datos ya están en `state.selectedIds`/`state.movements`.
- *Dependencia*: ninguna.

**PR-U6 — Agrupar/preseleccionar por descripción o sugerencia coincidente**
- *Objetivo*: atacar la sección 5/6 — reemplazar la selección manual de N checkboxes
  idénticos por una acción de agrupamiento.
- *Beneficio para el usuario*: el mayor ahorro de clics para el caso de "cientos de
  movimientos" con alta repetición (suscripciones, supermercado).
- *Riesgo*: medio-alto — es el cambio más profundo de UI de toda la lista (posible
  reestructuración visual de la tabla en grupos), y el que más fácil se presta a
  "modernizar por las dudas" si no se ancla estrictamente al criterio de agrupación
  que ya usa el backend (`Normalize`).
- *Complejidad*: alta.
- *Dependencia*: se beneficia mucho de PR-U2 (agrupar y aceptar en un clic combinados
  es el combo de mayor impacto), conviene hacerlo después, no antes.

**PR-U7 — Ocultar "Cuenta financiera" y "Alerta" detrás de un toggle/columna colapsable**
- *Objetivo*: reducir el ruido visual de la sección 4 sin perder el dato.
- *Beneficio para el usuario*: más foco en la tarea de clasificar cuando esa es la
  intención del momento.
- *Riesgo*: medio — hay que validar que ocultar "Cuenta financiera" no rompa flujos
  donde asignarla es justamente la tarea principal de la sesión.
- *Complejidad*: media.
- *Dependencia*: ninguna, pero conviene dejarlo último — es el único ítem del roadmap
  que no tiene una justificación tan urgente como los anteriores (es "menos ruido",
  no "menos clics").

---

## Si este fuera mi producto...

**El primer cambio sería PR-U2 (aceptar sugerencia con un clic desde la fila, para
confianza High en las 4 dimensiones).** No PR-U1, aunque sea más simple y sea su
prerrequisito visual natural — porque el objetivo original de todo este proyecto,
según tu propio contexto, era "un flujo mucho más simple para clasificar
movimientos bancarios", y hoy, después de doce PRs completos construyendo un motor de
sugerencias con historial, defaults, confianza calculada con mayoría calificada y
validación de entidades activas, **ese trabajo entero todavía no le ahorra ni un solo
clic al usuario** — sigue siendo obligatorio abrir el modal para cada movimiento, sin
importar cuánta certeza tenga el motor. Es la brecha más grande entre lo que ya se
construyó y lo que el usuario realmente experimenta today. PR-U1 (diferenciar
confianza visualmente) es una mejora real, pero es preparación; PR-U2 es el momento en
el que separado ese trabajo de meses empieza a devolver tiempo real, todos los días,
clasificando mis propios movimientos.
