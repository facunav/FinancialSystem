# PR-UI1 — Análisis de arquitectura de UI (previo a cualquier código)

Commit base: `origin/master` (`3729dda`). Documento de solo análisis: no se
modificó ningún archivo del repositorio. Leí completos, en esta sesión, los 5
archivos reales de `src/FinancialMcp.Api/wwwroot/`: `dashboard.html` (1365
líneas), `movements.html` (1395), `accounts.html` (581), `imports.html`
(447), `counterparties.html` (560), más `index.html` (8 líneas, redirect
puro). No hay ningún otro archivo compartido de `wwwroot/` — no existe
`shared/`, `static/`, ni ningún `.css`/`.js` externo: **las 5 páginas son
100% autocontenidas hoy**, cada una con su propio `<style>` y `<script>`
inline. Toda cifra de duplicación citada abajo viene de comparar el contenido
real de estos archivos (`diff`, `grep`), no de estimación.

---

## 1. Cómo debería organizarse la navegación completa

```
Principal
  Dashboard
  Movimientos
  Importaciones

Catálogos
  Cuentas financieras
  Categorías        (pantalla todavía no existe)
  Contrapartes

Análisis     — Gastos fijos, Presupuestos            (sin cambios, "pronto")
Patrimonio   — Patrimonio, Inversiones, Objetivos     (sin cambios, "pronto")
IA           — Copilot                                (sin cambios, "pronto")
```

**Justificación**: agrupo por frecuencia e intención de uso, no por tipo de
dato. Dashboard/Movimientos/Importaciones son las tres pantallas que un
usuario visita en el flujo operativo del día a día — Importaciones incluida
porque es la confirmación natural de "¿el archivo que acabo de soltar en la
carpeta se procesó bien?", inmediatamente después de lo cual el usuario va a
Movimientos a clasificar lo que entró. Cuentas/Categorías/Contrapartes son
catálogos de configuración: se completan con alta frecuencia al principio y
después se tocan solo ocasionalmente (agregar una contraparte nueva, corregir
un default) — comparten intención de uso entre sí, no con Importaciones (que
es visibilidad de solo lectura sobre una corrida, no administración de un
catálogo editable). Los tres grupos "pronto" quedan intactos: representan
épicas que no arrancaron, y diseñar su estructura ahora sería exactamente lo
que este análisis pidió evitar.

No propongo ningún nivel de menú adicional (submenús, breadcrumbs, tabs de
segundo nivel): con 8 pantallas reales o casi-reales en total, dos grupos
"activos" (Principal, Catálogos) más tres reservados ("pronto") ya cubren el
horizonte de las próximas 2-3 épicas sin necesitar más jerarquía.

---

## 2. Sidebar: reutilizar, extraer, copiar, u otra alternativa

Hoy solo `dashboard.html` tiene sidebar real (`.sidebar`, 220px fijo, con
`.nav-group-label` + `<a>` por ítem). Las otras 4 páginas usan un patrón
completamente distinto: `<header class="topbar">` con un `<h1>` de título y
un único link de texto `← Dashboard`. Evalúo las 4 alternativas de forma
concreta, con ventajas y desventajas reales, no genéricas:

### A. Copiar el layout (formalizar lo que ya se hace hoy)

Cada página pega el HTML/CSS del sidebar tal cual, como ya hice al construir
`counterparties.html` copiando el patrón de `accounts.html`.

- **Ventajas**: cero infraestructura nueva; cada archivo sigue siendo
  100% autocontenido — se puede abrir un solo `.html`, entenderlo
  completo y editarlo sin tocar nada más, que es exactamente el motivo por
  el que este proyecto pudo iterar rápido PR a PR hasta ahora; cero riesgo
  de que tocar una página rompa otra.
- **Desventajas**: la duplicación medida en la sección 3 sigue existiendo y
  crece con cada pantalla nueva (Categorías sería la sexta copia del mismo
  bloque de ~200 líneas). Ya hay evidencia de que copiar a mano diverge sin
  que nadie lo note: `dashboard.html` no tiene la función `esc()` que las
  otras 4 páginas sí tienen, y por eso interpola
  `cat.categoryDisplayName`/`v.categoryDisplayName` directo en `innerHTML`
  sin escapar (ver sección 4) — exactamente el tipo de bug que aparece
  cuando el mismo concepto vive copiado 5 veces en vez de una.

### B. Extraer un layout compartido (archivos estáticos incluidos por `<link>`/`<script src>`)

Un `wwwroot/shared/tokens.css` (los 16 `--variables` que ya son idénticas en
4 de 5 archivos), un `wwwroot/shared/components.css` (topbar/panel/tabla/
botón/modal/toast/status-badge), y un `wwwroot/shared/nav.js` que inyecta el
sidebar con la entrada activa correcta. Cada página pasa a tener 2-3 líneas
de `<link>`/`<script src>` en vez de repetir el bloque.

- **Ventajas**: una sola fuente de verdad — corregir el bug de `esc()`,
  agregar una entrada al menú, o cambiar un color se hace en un lugar y se
  propaga a las 5 páginas. Reduce cada archivo a lo que es realmente
  específico de esa pantalla (en `accounts.html`, por ejemplo, eso es
  ~60 líneas de CSS de 260, según la comparación de la sección 3).
- **Desventajas**: es la primera pieza de infraestructura compartida del
  proyecto — inyectar el sidebar por JS después de que el HTML ya cargó
  puede producir un parpadeo/reflow si no se cuida el orden de carga; rompe
  parcialmente la propiedad de "cada archivo se lee aislado" (ahora hace
  falta abrir `nav.js` para entender qué aparece en el menú de cualquier
  página); y es una superficie nueva que, sin disciplina, puede empezar a
  acumular responsabilidades que no le corresponden (la tentación de
  convertirla en un mini-framework casero).
- Es la alternativa que recomiendo — desarrollo por qué en la sección 6.

### C. Otra alternativa: layouts de servidor (Razor Pages / `_Layout.cshtml`)

El backend ya es ASP.NET Core — técnicamente podría migrarse `wwwroot/*.html`
a Razor Pages con un layout compartido real, resuelto en el servidor sin
ningún JS de inyección ni parpadeo.

- **Ventaja real**: es la única opción que resuelve "una sola fuente de
  verdad" sin ningún costo de carga en el cliente — el HTML ya llega armado.
- **Desventaja real**: es, con diferencia, el cambio de mayor magnitud de
  las cuatro opciones. Hoy no existe ningún paso de build ni renderizado de
  servidor para el frontend — es HTML estático servido tal cual desde
  `wwwroot`. Adoptar Razor Pages para resolver un problema de layout de 5
  pantallas es desproporcionado, y aunque Razor no es un framework de
  frontend, sí introduce un ciclo de vida de request y un tipo de archivo
  nuevos que hoy el proyecto no tiene en ningún lado — contradice el
  espíritu de "sin frameworks" aunque no la letra.

**No recomiendo A ni C.** A porque ya demostró que diverge sin que nadie lo
note (el bug de `dashboard.html`). C porque es una migración de arquitectura
completa para resolver un problema que es, en el fondo, de organización de
UI, no de backend.

---

## 3. Duplicaciones detectadas, con evidencia medida

### 3.1 CSS — tokens de diseño

El bloque `:root { --bg: ...; --surface: ...; ... }` de 16 líneas es
**byte-a-byte idéntico** en `movements.html`, `accounts.html`,
`imports.html` y `counterparties.html` (confirmado con `diff`, cero líneas
de diferencia entre los cuatro). `dashboard.html` tiene un bloque de 27
líneas que **incluye esas mismas 16** más 11 tokens adicionales
(`--surface3`, `--border2`, `--green-dim`, `--red-dim`, `--blue*`,
`--purple*`, `--radius*`, `--shadow`) que necesita para sus gráficos y KPIs.

### 3.2 CSS — componentes base

Comparando el `<style>` completo de `accounts.html` (260 líneas) contra el de
`counterparties.html` (217 líneas) con `diff`, **solo 58 líneas difieren** —
es decir, alrededor de 200 líneas de CSS (topbar, `.content`, `.panel`,
`.panel-head`, `button`/`.sm`/`.primary`/`.danger`, `table`/`th`/`td`,
`.empty`/`.loading`, `.status-badge`, `.toast`, todo el bloque de
`.modal-overlay`/`.modal`/`.modal-head`/`.modal-body`/`.modal-foot`/
`.close-x`) están repetidas palabra por palabra entre esos dos archivos. Las
únicas diferencias reales son las esperables: `accounts.html` tiene
`.filter-bar` (búsqueda + checkbox) y `td.mono`/`td.truncate`, que
`counterparties.html` no usa por decisión explícita de alcance (PR-O8).

`imports.html` (219 líneas de CSS) e incluso `movements.html` (412, el más
grande por sus componentes propios de sugerencias/lote) comparten ese mismo
núcleo de topbar/panel/tabla/botón/modal — confirmado línea por línea contra
`accounts.html` para el bloque topbar→tabla (60 líneas de diferencia sobre
~80 comparadas, con las diferencias concentradas en el CSS específico de
sugerencias/chips que `movements.html` agrega encima del mismo núcleo).

Total de CSS en las 5 páginas: 1837 líneas. Una fracción sustancial de las
1108 líneas de `movements.html + accounts.html + imports.html +
counterparties.html` es ese mismo núcleo repetido 4 veces.

### 3.3 JavaScript — helpers de red y utilidades

| Función | Dónde aparece | ¿Idéntica? |
|---|---|---|
| `getJson` | Las 5 páginas | 4 de 5 idénticas (`movements`/`accounts`/`imports`/`counterparties`, byte a byte). `dashboard.html` tiene una versión **distinta y más débil** (usa `r.text()` en vez de parsear `.detail`/`.title` de un `ProblemDetails`) — ver sección 4. |
| `postJson` | `movements`/`accounts`/`counterparties` | Idéntica en las 3 (confirmado con `diff`) |
| `putJson` | `movements`/`accounts`/`counterparties` | Idéntica en las 3 |
| `deleteJson` | `accounts`/`counterparties` | Idéntica en las 2 (`movements.html` no la necesita — no borra nada) |
| `handleWriteResponse` | `movements`/`accounts`/`counterparties` | Lógica idéntica en las 3; solo difiere el comentario que describe qué endpoint responde qué (copiado y ajustado a mano en cada archivo — evidencia directa del patrón copy-paste-y-editar-una-línea) |
| `esc` | `movements`/`accounts`/`imports`/`counterparties` | Idéntica, literalmente la misma línea en los 4 archivos. **Ausente en `dashboard.html`.** |
| `showToast` | `movements`/`accounts`/`counterparties` | Idéntica en las 3 |

### 3.4 Topbars

4 de 5 páginas repiten el mismo patrón exacto: `<header class="topbar"><h1>
{Título}</h1><a href="/dashboard.html">← Dashboard</a></header>`, con CSS
idéntico (`.topbar`, `.topbar h1`, `.topbar a`, `.topbar a:hover`). Solo
cambia el texto del `<h1>`. `dashboard.html` tiene un topbar estructuralmente
distinto (con navegación de período, no comparable).

### 3.5 Modales

`accounts.html` y `counterparties.html` comparten una implementación de
modal de alta/edición casi idéntica (mismo overlay, mismo header con botón
`✕`, mismo footer con Cancelar/Guardar). `imports.html` tiene un modal
distinto en propósito (detalle de solo lectura, sin footer de guardado) pero
reutiliza exactamente el mismo esqueleto CSS (`.modal-overlay`, `.modal`,
`.modal-head`, `.modal-body`, `.modal-foot`, `.close-x`) letra por letra.
`movements.html` también parte del mismo esqueleto, con contenido de
formulario propio encima.

---

## 4. Deuda técnica que aparece porque las páginas crecieron por separado

1. **`dashboard.html` interpola HTML sin escapar.** `renderCategories`
   (línea 1100) y `renderComparison` (línea 1141) insertan
   `cat.categoryDisplayName`/`v.categoryDisplayName` directo en `innerHTML`
   sin pasar por `esc()` — porque `dashboard.html` es la única de las 5
   páginas que nunca definió esa función. Las otras 4 la usan de forma
   consistente en cada interpolación de texto dinámico. Hoy el impacto real
   es bajo (el nombre de categoría sale de un catálogo interno, no de un
   input público), pero es una inconsistencia de disciplina real entre
   páginas que nació exactamente por no compartir código: si `esc()`
   viviera en un solo lugar, esta omisión no habría podido pasar
   desapercibida.

2. **`getJson` divergió en calidad de manejo de errores.** La versión de
   `dashboard.html` es estrictamente peor que la de las otras 4: usa
   `r.text()` y arma un mensaje `"{status}: {text}"` crudo, mientras que
   `movements`/`accounts`/`imports`/`counterparties` parsean el cuerpo como
   JSON y extraen `.detail`/`.title` de un `ProblemDetails` cuando existe.
   Un error de `/api/metrics/*` se ve, hoy, peor formateado en Dashboard que
   el mismo tipo de error en cualquier otra pantalla — inconsistencia visible
   para el usuario, no solo interna.

3. **Comentarios de "mismos valores que" desactualizados.** `imports.html`
   dice en su comentario de cabecera *"Design tokens (mismos valores que
   dashboard.html)"* — pero comparé ambos bloques `:root` con `diff` y
   `dashboard.html` tiene 11 tokens adicionales que `imports.html` no
   define (`--surface3`, `--blue*`, `--purple*`, `--radius*`, `--shadow`,
   etc.). El comentario es, en rigor, impreciso — la cadena de comentarios
   "mismos valores que X" que atraviesa los 4 archivos (`movements` → cita
   `accounts`/`imports`; `accounts` → cita `imports`; `imports` → cita
   `dashboard`) documenta una intención de consistencia que nadie
   verificó de punta a punta. Es exactamente el síntoma de no tener una
   única fuente de verdad: los comentarios son la única forma que existe
   hoy de rastrear la relación entre archivos, y ya se desactualizaron.

4. **El mismo bug, corregido 3 veces por separado si apareciera hoy.** Si se
   encontrara un problema en `handleWriteResponse` (por ejemplo, un caso
   donde el backend devuelve un array de errores de validación en vez de un
   string u objeto simple), habría que corregirlo en `movements.html`,
   `accounts.html` y `counterparties.html` por separado, con el riesgo real
   —ya demostrado por el punto 1 y 2— de que alguna copia quede sin
   actualizar.

---

## 5. Cosas que ya no tienen sentido

- **Pantallas huérfanas**: `accounts.html`, `imports.html` y
  `counterparties.html` no tienen ningún link entrante en toda la
  aplicación — solo alcanzables por URL directa (ya relevado en el análisis
  de navegación anterior; sigue siendo así, sin cambios, en el código
  actual).
- **`navPending` sin implementar**: el `<span class="badge"
  id="navPending">—</span>` existe en el sidebar de `dashboard.html` pero
  ningún `<script>` de ese archivo lo completa (confirmado: la única
  aparición de `navPending` en las 5 páginas es la del propio `<span>`).
  Promete un contador de pendientes que nunca se cumple.
- **Links rotos en sentido literal: ninguno encontrado.** Revisé cada
  `href="..."` de las 5 páginas — todas apuntan a archivos que existen
  (`/dashboard.html`, `/movements.html`). El problema no es que algo apunte
  a la nada; es que casi nada apunta a las 3 pantallas reales que existen.
- **`group-reconciliation.html`**: ya no existe como archivo (eliminado en
  una PR anterior), pero sigue mencionado en dos comentarios
  (`dashboard.html` línea 761, `movements.html` línea 371) explicando por
  qué se retiró. Son comentarios históricos legítimos, no una pantalla
  huérfana — no requieren acción, los señalo solo para que quede claro que
  no encontré ninguna referencia activa (ni `<a href>`, ni `fetch`) hacia un
  archivo inexistente.
- **CSS efectivamente muerto**: no encontré selectores CSS sin ningún
  elemento que los use dentro de cada archivo individual (cada bloque
  `<style>` que revisé tiene su contraparte en el HTML de la misma página).
  El "CSS muerto" real de este proyecto no es CSS sin uso — es CSS *duplicado
  con uso*, que es el problema de la sección 3, no de reglas huérfanas.
- **Redundancia de acceso a Movimientos**: alcanzable desde la sidebar y
  desde el botón `.btn-action.primary` del topbar de `dashboard.html` al
  mismo tiempo — no rompe nada, pero es la contracara exacta del problema de
  las pantallas huérfanas: se pensó dos veces cómo llegar a Movimientos y
  ninguna vez cómo llegar a Cuentas/Contrapartes/Importaciones.

---

## 6. Arquitectura simple propuesta

Sin React, sin SPA, sin bundler, sin build step — HTML servido tal cual desde
`wwwroot`, tal como está hoy, con tres archivos nuevos que **no reemplazan
nada, solo centralizan lo que ya es idéntico**:

```
wwwroot/
  shared/
    tokens.css        ← los 16 --variables ya idénticos en 4/5 páginas
    components.css     ← topbar/panel/tabla/botón/modal/toast/status-badge
    app.js              ← getJson/postJson/putJson/deleteJson/
                          handleWriteResponse/esc/showToast + la función
                          que inyecta el sidebar con la entrada activa
  dashboard.html
  movements.html
  imports.html
  accounts.html
  counterparties.html
  categories.html      (futura)
```

Cada página pasa a tener, arriba de su propio `<style>`/`<script>`
específico:

```html
<link rel="stylesheet" href="/shared/tokens.css">
<link rel="stylesheet" href="/shared/components.css">
...
<script src="/shared/app.js" data-active="counterparties"></script>
```

`app.js` lee `data-active` del propio `<script>` tag (o de un atributo en
`<body>`) para saber qué entrada del sidebar marcar como activa, sin que
cada página tenga que repetir la lista completa de links. `dashboard.html`
sigue teniendo sus propios estilos y tokens extendidos (KPIs, donut, gráfico
de tendencia) en su propio `<style>`, que no tiene sentido mover a
`components.css` porque no los usa ninguna otra página — la regla para
decidir qué va a `shared/` es simple: **si dos o más páginas ya lo repiten
igual, va a `shared/`; si es de una sola página, se queda donde está.**

No hay ningún mecanismo de "componentes" ni de templating — es HTML/CSS/JS
plano incluido por referencia, el mecanismo más simple que el navegador ya
entiende sin nada adicional. Es deliberadamente menos sofisticado que un
sistema de componentes: para 5-8 pantallas, un archivo compartido de CSS y
uno de JS alcanzan, y agregar más estructura sería sobre-ingeniería para el
tamaño real de este proyecto.

---

## 7. Hoja de ruta

Cada PR tiene un único objetivo y es mergeable por sí solo, siguiendo el
mismo criterio ya usado en toda esta serie.

### UI1 — Extraer `tokens.css` y `components.css`

Mover el bloque de `:root` (ya idéntico en 4/5 archivos) y el núcleo de
CSS ya duplicado (topbar/panel/tabla/botón/modal/toast/status-badge) a
`wwwroot/shared/`, referenciado por `<link>` desde las 5 páginas. Cero
cambio de comportamiento ni de aspecto visual — es una extracción pura.
Primero en la hoja de ruta porque es el cambio de menor riesgo posible
(mover CSS ya idéntico no puede introducir una regresión de lógica) y
porque los siguientes pasos (UI2, UI3) se apoyan en que exista un lugar
común donde agregar código nuevo compartido.

### UI2 — Extraer `app.js` (helpers de red + `esc`)

Mover `getJson`/`postJson`/`putJson`/`deleteJson`/`handleWriteResponse`/
`esc`/`showToast` a `shared/app.js`. Como consecuencia directa de unificar
—no como un fix aislado separado— esto cierra los dos hallazgos concretos
de la sección 4: `dashboard.html` pasa a tener el mismo `getJson` (con
manejo de `ProblemDetails`) y la misma `esc()` que las otras 4, así que sus
dos interpolaciones sin escapar (`renderCategories`/`renderComparison`)
quedan resueltas por el simple hecho de que ahora `esc()` existe y está
disponible ahí. Va segundo porque es, igual que UI1, una extracción de
código ya idéntico — bajo riesgo, sin decisiones de diseño nuevas.

### UI3 — Compartir el componente de sidebar

`shared/app.js` gana la función que inyecta el `.sidebar` de
`dashboard.html` (HTML + lógica de marcar la entrada activa). Las 4 páginas
secundarias reemplazan su `<header class="topbar">` aislado por este
componente compartido. Es el PR que resuelve el problema central detectado
en el análisis de navegación anterior: las pantallas huérfanas dejan de
serlo porque ahora comparten el mismo menú que `dashboard.html`. Va tercero,
después de UI1/UI2, porque depende de que ya exista el lugar compartido
(`shared/`) donde vivir, y es el primer PR de esta serie que sí cambia
comportamiento visible (aparece un sidebar donde antes había un link de
texto) — separarlo de la extracción pura de UI1/UI2 hace que sea más fácil
de revisar y, si hiciera falta, revertir sin afectar los pasos anteriores.

### UI4 — Reorganizar el contenido del menú

Agregar las entradas reales de Importaciones/Cuentas/Contrapartes al sidebar
compartido, agrupadas según la estructura de la sección 1 (Principal /
Catálogos). Separado de UI3 a propósito: UI3 es "todas las páginas
comparten el componente" (mecánico), UI4 es "qué dice ese componente"
(una decisión de contenido y agrupación) — mezclarlos en un solo PR haría
más difícil revisar cada uno por separado si alguno necesitara ajustarse.

### UI5 — Completar el badge de pendientes

Con el sidebar ya centralizado en `shared/app.js`, implementar el contador
de `navPending` en un solo lugar (hoy sería el quinto lugar si se hiciera
antes de UI3/UI4, con el mismo riesgo de duplicación que motivó todo este
documento). Va último porque es una funcionalidad nueva y pequeña, no una
extracción — tiene sentido resolverla recién cuando el sidebar ya es una
sola pieza de código, no cinco copias.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ningún patch.
Todo el trabajo fue lectura completa de los 5 archivos de `wwwroot/` y
comparaciones exactas (`diff`, `grep`, conteo de líneas) entre ellos para
sostener cada afirmación de duplicación con evidencia verificable, no con
estimación.
