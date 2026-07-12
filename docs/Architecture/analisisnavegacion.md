# Análisis de navegación — antes de agregar más pantallas

Commit base: `origin/master` (`7c32036`, PR-O8 mergeado — `counterparties.html`
ya existe). Documento de solo análisis: no se modificó ningún archivo del
repositorio para producirlo. Leí completos, en esta sesión, los 6 archivos de
`wwwroot/`: `dashboard.html` (1365 líneas, no lo había leído entero hasta
ahora), `movements.html`, `accounts.html`, `imports.html`,
`counterparties.html` e `index.html`.

---

## 1. Estado actual

### 1.1 Inventario real de pantallas

| Archivo | Qué es | Tiene sidebar propio | Cómo se llega hoy |
|---|---|---|---|
| `index.html` | Redirect ciego a `/dashboard.html` (`<meta http-equiv="refresh">`) | — | URL raíz |
| `dashboard.html` | KPIs del mes, gráficos, único shell con sidebar real | Sí (`.sidebar`, 220px fijo) | URL raíz → redirect, o directamente |
| `movements.html` | Flujo diario: listar/clasificar movimientos | No — topbar con "← Dashboard" | Sidebar de dashboard.html (único link real) + botón en el topbar de dashboard.html |
| `imports.html` | Historial de importaciones (solo lectura) | No — topbar con "← Dashboard" | **Ningún link en toda la app.** Solo URL directa. |
| `accounts.html` | CRUD de Cuentas financieras | No — topbar con "← Dashboard" | **Ningún link en toda la app.** Solo URL directa. |
| `counterparties.html` | CRUD de Contrapartes (PR-O8) | No — topbar con "← Dashboard" | **Ningún link en toda la app.** Solo URL directa. |

### 1.2 El único menú real que existe

`dashboard.html` tiene el único componente de navegación persistente de todo
el proyecto: una sidebar con 4 grupos.

- **Principal**: Dashboard (activo), Movimientos (con un badge de pendientes,
  `id="navPending"`).
- **Análisis**: Gastos fijos, Presupuestos — ambos con clase `.soon`,
  deshabilitados (`pointer-events: none`), badge "pronto".
- **Patrimonio**: Patrimonio, Inversiones, Objetivos — mismo estado `.soon`.
- **IA**: Copilot — mismo estado `.soon`.

Es decir: de 8 entradas totales en el único menú de la aplicación, **2 llevan
a algo real y 6 son placeholders deshabilitados para épicas futuras que
todavía no existen.** Ninguna de las 3 pantallas de administración que sí
existen hoy (Cuentas, Contrapartes, Importaciones) aparece en ese menú.

### 1.3 Una señal adicional, ya detectada por el propio proyecto

El badge `id="navPending"` del ítem "Movimientos" nunca se completa —
ningún `<script>` de `dashboard.html` lo escribe (confirmado con `grep`
sobre el archivo completo: la única aparición de `navPending` en todo el
código es la del propio `<span>`). Esto no es un hallazgo nuevo de este
documento: ya está registrado en `docs/RoadMaps/FinancialMcp-vNext.md`
("El badge de pendientes en el nav... existe en el HTML pero ningún script
lo completa"). Lo señalo acá porque es la misma clase de problema que las
pantallas huérfanas: partes de la navegación que se dejaron a medio
construir mientras el esfuerzo se concentraba en las pantallas de contenido.

---

## 2. Problemas de navegación

1. **Tres pantallas reales son inalcanzables sin conocer la URL de memoria.**
   `accounts.html`, `imports.html` y `counterparties.html` no tienen ni un
   solo link entrante desde ningún otro archivo del proyecto (verificado con
   `grep -n "\.html" src/FinancialMcp.Api/wwwroot/*.html` sobre las 6
   pantallas: la única referencia cruzada real en todo `wwwroot/` es
   `dashboard.html → movements.html`, más el link estático "← Dashboard" que
   las 4 páginas secundarias apuntan hacia arriba, nunca entre sí). Para un
   usuario nuevo, esas tres pantallas no existen.

2. **El patrón "← Dashboard" es el único hilo de navegación entre pantallas
   secundarias, y es unidireccional.** Desde `accounts.html` no hay forma de
   ir a `counterparties.html` sin pasar primero por Dashboard (que tampoco
   tiene un link hacia ninguna de las dos). Cada pantalla secundaria es, en
   los hechos, una isla con una sola puerta de salida.

3. **El único menú real está desproporcionado respecto de lo que existe.**
   6 de 8 entradas son placeholders para funcionalidad que no se construyó
   todavía (Gastos fijos, Presupuestos, Patrimonio, Inversiones, Objetivos,
   Copilot) — visualmente ocupan más espacio que las pantallas reales de
   administración, que directamente no están.

4. **Redundancia menor en el otro extremo**: Movimientos es alcanzable desde
   dos lugares distintos dentro de `dashboard.html` (la sidebar y el botón
   `.btn-action.primary` del topbar) — no es un problema grave, pero es una
   señal de que se pensó dos veces cómo llegar a Movimientos y ninguna vez
   cómo llegar a Cuentas/Contrapartes/Importaciones.

5. **El badge de pendientes sin implementar** (punto 1.3) — no es
   estrictamente un problema de navegación en el sentido de "no se puede
   llegar", pero sí es una promesa de la UI de navegación que no se cumple:
   el usuario ve un contador vacío (`—`) donde se prometía información útil
   para decidir si entrar a Movimientos.

**Conclusión de esta sección**: no encontré evidencia de que el problema sea
"demasiadas pantallas" — son 5 pantallas reales, cada una con una
responsabilidad clara y sin superposición de contenido entre sí (ver sección
5). El problema es exclusivamente de **cableado**: la navegación no se
construyó al mismo ritmo que el contenido.

---

## 3. Propuesta de estructura

Extiendo la sidebar ya existente en `dashboard.html` — no propongo un
componente de navegación nuevo, ni un segundo nivel de menú, ni breadcrumbs,
ni ninguna pieza que no exista ya en el proyecto. Dos grupos nuevos, mismo
patrón (`.nav-group-label` + lista de `<a>`) que ya usan "Análisis",
"Patrimonio" e "IA":

```
Principal
  Dashboard
  Movimientos          (badge de pendientes — cuando se implemente)
  Importaciones        ← nuevo

Catálogos              ← grupo nuevo
  Cuentas financieras
  Categorías            (cuando exista la pantalla — todavía no)
  Contrapartes

Análisis      (sin cambios — pronto)
Patrimonio    (sin cambios — pronto)
IA            (sin cambios — pronto)
```

Y, para que ese menú sea real navegación y no un segundo "hub" al que hay
que volver todo el tiempo: **las 4 pantallas secundarias deberían compartir
el mismo shell con sidebar que hoy solo tiene `dashboard.html`**, cada una
marcando su propia entrada como activa — reemplazando el actual topbar
minimalista de "← Dashboard" por el mismo `.sidebar` ya construido. Esto no
es un componente nuevo: es aplicar el HTML/CSS que `dashboard.html` ya tiene
a los otros 4 archivos.

No propongo tocar el contenido de ninguna pantalla existente, ni fusionar
ninguna, ni el trabajo de las 6 entradas "pronto" — están correctamente
alcanzadas para código que todavía no existe, y no es momento de
diseñarlas.

---

## 4. Justificación de cada sección

### Por qué "Principal" incluye Importaciones y no solo Dashboard/Movimientos

Importaciones es, por naturaleza, distinta de Cuentas/Categorías/
Contrapartes: no es un catálogo que el usuario configura una vez y edita
ocasionalmente — es **visibilidad sobre una corrida real** ("¿el archivo que
acabo de soltar en la carpeta se procesó bien?"), directamente encadenada al
flujo diario de Movimientos (los movimientos pendientes que aparecen ahí
vienen de una importación). Es más parecida, en frecuencia de uso y en
propósito, a Movimientos que a una pantalla de administración de catálogo —
por eso va en "Principal", no en "Catálogos".

### Por qué "Catálogos" agrupa Cuentas + Categorías + Contrapartes, y por qué NO las fusiono en una sola pantalla

Las tres entidades comparten exactamente la misma naturaleza de uso: se
configuran con poca frecuencia (alta esporádica, edición ocasional), tienen
la misma forma de interacción (listar, crear, editar, desactivar) y, de
hecho, ya comparten literalmente el mismo patrón de implementación —
`counterparties.html` es un mirror directo de `accounts.html`, mismos
tokens de diseño, misma estructura de modal, mismos helpers de red. Agruparlas
bajo una sola entrada de menú responde a esa afinidad real, no a una
convención genérica de "settings".

No las fusiono en una sola pantalla con pestañas por dos razones concretas:

1. Ya existe un patrón probado dos veces (`accounts.html` →
   `counterparties.html`) de página independiente por entidad — construir un
   selector de pestañas encima sería una pieza de UI nueva para resolver un
   problema (demasiados clics para llegar a cada catálogo) que la agrupación
   en el menú ya resuelve sin escribir ningún componente nuevo.
2. Cada catálogo tiene su propio ciclo de vida de PRs (Cuentas ya existe,
   Contrapartes recién se sumó, Categorías todavía no) — mantenerlas como
   archivos separados permite seguir entregando cada uno de forma
   completamente independiente, exactamente como se viene trabajando.

### Por qué no toco los 6 ítems "pronto"

Están correctamente alcanzados: representan épicas explícitamente futuras
(Gastos fijos, Presupuestos, Patrimonio, Inversiones, Objetivos, Copilot),
ya marcadas como deshabilitadas, sin ningún backend ni pantalla detrás
todavía. Tocarlos ahora sería exactamente lo que este análisis pidió
evitar: diseñar para funcionalidad que no existe. Los dejo tal cual están.

### Por qué recomiendo llevar el shell con sidebar a las 4 páginas secundarias, y no solo agregar links cruzados

Una alternativa más barata sería simplemente agregar un par de links extra
en los topbars actuales (por ejemplo, que `accounts.html` linkee a
`counterparties.html` y viceversa, sin tocar `dashboard.html`). La descarto
como solución final porque no resuelve el problema real: un usuario que
entra por primera vez a `movements.html` (el caso más común, ya que es la
pantalla de uso diario) seguiría sin ver que existen Cuentas/Categorías/
Contrapartes/Importaciones — solo lo descubriría si antes pasó por
Dashboard. Compartir la sidebar hace que la navegación completa esté
siempre visible sin importar por dónde entra el usuario, que es justamente
la definición de "navegación coherente" que pediste. Es más trabajo de
implementación que solo agregar 3-4 links sueltos, pero es el único de los
dos caminos que efectivamente deja de depender de que el usuario vuelva a
Dashboard para moverse por la app.

---

## 5. Qué pantallas eliminaría, fusionaría o movería

**Elimino: ninguna.** Repaso explícito de las dos preguntas del enunciado:

- **¿Necesitamos una pantalla de Cuentas separada de Importaciones?** Sí. Son
  conceptos distintos con datos distintos (una es catálogo editable, la otra
  es log de solo lectura) y fusionarlas mezclaría configuración con
  diagnóstico dentro de la misma pantalla, sin ningún ahorro real de clics —
  hoy ya son dos endpoints, dos modelos y dos responsabilidades separadas en
  el backend; fusionar solo la UI sin fusionar el dominio subyacente
  generaría una pantalla híbrida más confusa que las dos actuales por
  separado.
- **¿Categorías y Contrapartes merecen pantallas separadas, o deberían
  compartir una?** Merecen archivos separados (ver sección 4), pero
  **comparten sección de menú** junto con Cuentas — esa es la fusión real
  que sí recomiendo: no de pantallas, sino de *dónde aparecen en la
  navegación*.

**Fusiono: nada de contenido.** El único cambio de "fusión" es puramente de
menú (agrupar 3 entradas bajo un mismo rótulo "Catálogos"), no de pantallas
ni de endpoints.

**Muevo (agrego a la navegación, sin mover contenido de archivo):**

- Importaciones, Cuentas y Contrapartes pasan de "inalcanzables sin URL
  directa" a tener una entrada real en la sidebar (grupos "Principal" y
  "Catálogos" respectivamente).
- Las 4 páginas secundarias (Movimientos, Importaciones, Cuentas,
  Contrapartes) pasan de un topbar aislado con "← Dashboard" a compartir el
  mismo shell con sidebar que hoy solo tiene Dashboard, cada una con su
  propia entrada marcada como activa.

**Sin decidir todavía, señalado para cuando exista evidencia:** si
Importaciones alguna vez deja de ser un log de solo lectura y gana una
acción real (por ejemplo, poder reintentar un archivo rechazado — el
hallazgo del análisis de la Épica O sobre el ruteo por nombre de archivo
roto), podría valer la pena reconsiderar si sigue perteneciendo a
"Principal" o si en ese momento se acerca más a un catálogo. No lo resuelvo
acá porque depende de una funcionalidad que hoy no existe — exactamente el
tipo de decisión prematura que este documento pidió evitar.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push. Todo el trabajo fue
lectura completa de los 6 archivos de `wwwroot/` (incluyendo `dashboard.html`
por primera vez en su totalidad en esta sesión) y una búsqueda (`grep`) para
confirmar la ausencia de links cruzados entre pantallas y la ausencia de
cualquier script que complete `navPending`. No propuse código ni patches —
esto es únicamente el análisis para decidir la estructura de navegación
antes de sumar la próxima pantalla de catálogo.
