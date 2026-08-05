# Épica — Planificación Mensual

> Estado: 📋 diseño funcional cerrado, sin letra de épica asignada todavía (ver nota abajo), sin historias técnicas. Este documento fija el objetivo, los principios, el alcance y el modelo conceptual del módulo — sigue el mismo protocolo que `docs/Epics/EpicaI-Importacion.md` y `docs/Epics/EpicaO-ImportacionManual.md`, pero se detiene un paso antes: no define contrato de API, modelo de base de datos ni plan de PRs. Esa siguiente etapa queda para un documento posterior, una vez que este se dé por aprobado.

**Nota sobre numeración.** `docs/RoadMaps/FinancialMcp-vNext.md` numera épicas hasta la **O**. Pero `docs/Archive/RoadmapMVP.md`, `docs/Archive/MVPDefinitivo.md` (archivados en PATCH-025) y `docs/Architecture/PRU1analisisexperienciaclasificacion.md` ya hacen referencia a Épicas **S**, **U** y **UI** (con PRs propios, ej. `PR-S6`, `PR-U1`) sin que `vNext.md` las liste. La numeración de letras está desincronizada entre documentos del propio repositorio — esto es anterior a este módulo y no algo que introduzca esta épica. Asignarle una letra a este documento es una decisión administrativa que depende de resolver esa desincronización primero, no una decisión de diseño; se deja pendiente para no bloquear la revisión del contenido.

---

## 1. Contexto

FinancialMcp hoy registra, importa, clasifica y audita movimientos financieros reales: todo lo que existe en el sistema —`ClassifiedMovement`, el motor de sugerencias de clasificación, el Dashboard, la Auditoría— describe **hechos que ya ocurrieron**. No existe ningún módulo orientado a organizar lo que todavía **no ocurrió**.

El nav de `dashboard.html` ya reserva un lugar para esto: los ítems "Gastos fijos" y "Presupuestos" existen en el menú marcados como `soon`, sin pantalla real detrás. Este módulo ocupa ese espacio funcional — no como un sistema de presupuestos ni como gastos fijos con recurrencia modelada, sino como algo más chico y más directo: una checklist mensual de pagos esperados.

## 2. Objetivo

Ayudar al usuario a responder, día a día durante el mes, estas cuatro preguntas y ninguna otra:

* ¿Qué tengo que pagar este mes?
* ¿Qué ya pagué?
* ¿Qué me falta pagar?
* ¿Cuánto dinero necesito para terminar el mes?

No es un sistema de presupuestos, no es una agenda, no es un calendario, no es otra pantalla de clasificación.

## 3. Filosofía

**Planificación representa una intención. Movimientos representa lo que realmente ocurrió.** De este principio se derivan reglas concretas:

1. **La clasificación financiera vive exclusivamente en Movimientos.** Categoría, Contraparte, `MovementType`, `FinancialImpact` — las 4 dimensiones de `ClassifiedMovement` (ADR-001) — no se duplican ni se reinterpretan acá. Planificación no clasifica nada.
2. **Planificación nunca modifica Movimientos.** No hay ninguna operación de este módulo que escriba sobre `Transaction`, `BankStatement` o `ClassifiedMovement`.
3. **Movimientos nunca modifica Planificación automáticamente.** Importar o clasificar un movimiento real no altera ningún ítem planificado. La única conexión prevista (sección 9) es una sugerencia que el usuario puede aceptar o ignorar, nunca una escritura automática.
4. **El sistema únicamente facilita la carga de información; nunca decide por el usuario.** Qué agregar, qué eliminar, cuánto pagar, cuándo marcar un pago como realizado: siempre son acciones explícitas del usuario, nunca inferencias del sistema.
5. **Detectar un patrón no es lo mismo que inferir una intención.** El punto 4 podría parecer en tensión con la función de sugerencias (sección 8) — no lo está: el sistema puede observar que "Internet" aparece en el historial de Movimientos los últimos 12 meses y **ofrecerlo**, pero en ningún momento asume que el usuario **va a pagarlo** este mes. La oferta es datos; la decisión de agregarlo, ignorarlo o cuánto poner, sigue siendo enteramente del usuario. Esta distinción es la que mantiene la sección 8 compatible con este principio, y conviene dejarla explícita para que una futura ampliación de "sugerencias" no cruce la línea hacia autocompletar el mes.

## 4. Responsabilidad del módulo

| Puede hacer | Por qué es parte del alcance |
|---|---|
| Crear una planificación mensual | Es la unidad básica del módulo — un mes es autocontenido. |
| Copiar una planificación existente | Resuelve el caso más común ("todos los meses pago lo mismo") sin ningún análisis de historial. |
| Agregar / eliminar pagos | Control total del usuario sobre la lista, sin restricciones. |
| Editar importes esperados | El usuario es siempre quien conoce el monto real de la factura — el sistema nunca lo estima. |
| Marcar pagos como realizados | Es el corazón de la checklist: responde "qué ya pagué / qué me falta". |
| Mostrar el estado general del mes | Resumen agregado simple (sección 7) — sin él, el módulo no contesta la pregunta de flujo de caja. |
| Ayudar mediante sugerencias provenientes del historial de Movimientos | Aprovecha datos que el sistema ya tiene, evitando que el usuario tipee de memoria conceptos recurrentes — siempre como propuesta, nunca como inserción automática (sección 8). |

### No es responsabilidad del módulo

Clasificar movimientos · importar movimientos bancarios · administrar categorías · administrar contrapartes · administrar cuentas · administrar inversiones · administrar presupuestos · administrar objetivos financieros · estimar importes · generar predicciones · generar recordatorios · enviar notificaciones · tomar decisiones automáticas.

Cada uno de estos puntos ya tiene (o deliberadamente no tiene) un dueño en el sistema: clasificación y cuentas son de Movimientos/Auditoría; presupuestos, objetivos e inversiones son ítems `soon` distintos en el propio nav de `dashboard.html`, fuera del alcance de este módulo; estimaciones, predicciones y notificaciones se descartan por decisión explícita de producto (sección 10), no por limitación técnica.

## 5. Modelo conceptual

Dos conceptos, sin relación con ninguna entidad de clasificación existente.

**PlanningMonth** — representa un único mes de planificación.

* `Period` — el mes al que corresponde esta planificación.
* `ExpectedIncome` (opcional) — un valor que el usuario ingresa manualmente. Nunca se calcula ni se sugiere a partir del historial.

**PlanningItem** — representa una obligación de pago dentro de un mes. No representa un movimiento bancario, una categoría, una factura ni una contraparte — representa únicamente algo que el usuario quiere recordar pagar ese mes.

* `Title` — texto libre.
* `ExpectedAmount` — cargado manualmente por el usuario; copiar el valor del mes anterior está permitido porque sigue siendo un dato que el propio usuario ingresó alguna vez, no una estimación del sistema.
* `DueDate` — dato puramente descriptivo (sección 7).
* `IsPaid` / `PaidAt` — estado de la checklist.

Ninguna de las dos entidades tiene relación (ni opcional) con `Category`, `Counterparty`, `MovementType`, `FinancialImpact`, `FinancialAccount`, `Transaction`, `BankStatement` o `ClassifiedMovement`. Esto es deliberado, no un olvido: es lo que garantiza que Planificación pueda existir, cambiar o incluso eliminarse sin ningún impacto sobre Movimientos.

## 6. Flujos funcionales

### 6.1 — Creación de un mes

Tres casos, sin asistente de varios pasos salvo cuando hace falta decidir algo real:

* **Caso normal** (existe el mes calendario inmediatamente anterior): dos opciones, "Copiar mes anterior" (recomendada) o "Empezar vacío".
* **Primer uso** (no existe ningún mes previo, de ningún tipo): el sistema crea el primer mes vacío directamente, sin mostrar ningún diálogo — no hay ninguna opción real de copiar, así que preguntar sería fricción sin sentido.
* **Con huecos** (el mes inmediatamente anterior no existe, pero sí hay meses más antiguos): el sistema no asume cuál copiar — pregunta explícitamente entre "Copiar la última planificación disponible" y "Empezar vacío". Esta es la aplicación directa del principio 4: ante una ambigüedad real, se pregunta en vez de inferir.

### 6.2 — Copiar una planificación

Al copiar: se copian todos los `PlanningItem` del mes origen, en el mismo orden, todos vuelven a estado pendiente, `ExpectedAmount` y `DueDate` se copian como referencia editable. A partir de ese momento los dos meses son completamente independientes — eliminar un ítem en el mes nuevo no afecta al mes origen, y viceversa.

### 6.3 — Agregar desde historial

Acción independiente dentro de un mes ya creado (no forma parte del asistente de creación, sección 6.1) — puede usarse en cualquier momento, incluso a mitad de mes. Al presionarla, el sistema analiza el historial de `ClassifiedMovement` y propone conceptos recurrentes (ej. "Internet — presente en los últimos 12 meses"). Cada sugerencia se puede agregar o ignorar. Las sugerencias nunca se insertan automáticamente, nunca se persisten, siempre se calculan al abrir la pantalla — son un resultado de lectura, no un estado del sistema.

No hay ninguna lógica para evitar duplicados entre lo sugerido y lo ya cargado ese mes, ni entre dos ítems agregados por error. Es una decisión de producto explícita (sección 10.2), no un descuido.

### 6.4 — Marcar un pago como realizado

Acción manual, siempre del usuario. Al marcarlo, se registra `PaidAt`. No dispara ninguna otra consecuencia (no reclasifica nada, no busca un movimiento correspondiente en Movimientos — eso queda fuera del MVP, sección 9).

### 6.5 — Resumen superior

Se muestran únicamente cinco valores, todos calculados con sumas y restas directas sobre los `PlanningItem` y el `PlanningMonth` del mes activo — nunca con datos del historial de Movimientos:

* **Esperado cobrar** — `ExpectedIncome`, si existe.
* **Total planificado** — suma de `ExpectedAmount` de todos los ítems del mes.
* **Pagado** — suma de `ExpectedAmount` de los ítems con `IsPaid = true`.
* **Pendiente** — Total planificado − Pagado.
* **Disponible** — `ExpectedIncome` − Total planificado, solo si `ExpectedIncome` existe. Si el resultado es negativo, se muestra el número negativo tal cual, sin ningún color de alerta ni mensaje de advertencia — el sistema informa, el usuario decide.

## 7. Regla de evolución del módulo

Estas reglas no son específicas del MVP — deben sostenerse en cualquier ampliación futura de este módulo:

1. **El resumen superior nunca crece más allá de sumas y restas directas** sobre `PlanningMonth`/`PlanningItem`. Cualquier cálculo que use el historial de Movimientos (promedios, tendencias, proyecciones) pertenece exclusivamente a la función de sugerencias de la sección 6.3 — que es de lectura, no persistida, y siempre accionada por el usuario — nunca al resumen.
2. **`DueDate` es y sigue siendo un dato descriptivo.** Como máximo ordena la lista visualmente. Ninguna futura iteración debe agregarle alertas, colores de urgencia, recordatorios o notificaciones — si eso llega a pedirse, es una funcionalidad distinta, no una extensión natural de este campo.
3. **Ninguna dimensión de clasificación se agrega a `PlanningItem`.** Ni siquiera como campo opcional. Si en el futuro hace falta relacionar un pago planificado con una Categoría o Contraparte, eso pertenece a la sección 9 (integración con Movimientos), resuelta como una relación de lectura en el momento del match — nunca como un campo persistido en Planificación.
4. **Toda ampliación de "sugerencias" preserva la distinción del principio 5** (sección 3): puede detectar patrones más ricos (tendencia, tarjetas que aumentan), pero nunca puede pasar de proponer a insertar, ni de proponer un concepto a proponer un monto.

## 8. Relación con el resto del sistema

| Módulo | Relación |
|---|---|
| **Dashboard** | Nuevo ítem de navegación que ocupa el lugar hoy reservado ("Gastos fijos" / "Presupuestos", ambos `soon` en `dashboard.html`). Sin otra integración en el MVP. |
| **Movimientos** | Dependencia de **lectura, unidireccional y opcional**: la función "Agregar desde historial" (sección 6.3) lee `ClassifiedMovement` para proponer conceptos recurrentes. Movimientos no sabe que Planificación existe — nunca lee ni depende de `PlanningMonth`/`PlanningItem`. Vale la pena decirlo así de preciso: la independencia entre ambos módulos no es total en ambos sentidos, es total **desde Movimientos hacia Planificación**, y de solo-lectura **desde Planificación hacia Movimientos**. |
| **Auditoría** | Sin integración. `PlanningItem` no es un movimiento clasificado, no entra al motor de auditoría ni a sus hallazgos. |
| **Reportes** | No existe hoy una sección de Reportes independiente en el sistema (lo único parecido es el reporte de Auditoría, que es otra cosa). No hay nada que integrar todavía. |

## 9. Integración futura — fuera del MVP

Cuando se importa un movimiento real cuya descripción se parece a un `PlanningItem` pendiente (ejemplo: se importa "PAGO INTERNET" y existe un ítem "Internet" sin pagar), el sistema podría ofrecer: *"¿Querés marcar este pago como realizado?"* — siempre como sugerencia, nunca como acción automática, coherente con el principio 4.

Esto **no** forma parte de este documento ni del MVP. Se deja registrado acá únicamente para confirmar que el modelo de la sección 5 no lo bloquea: el matching se resolvería en el momento (comparando texto, mismo criterio ya usado hoy por `ClassificationSuggestionService` para normalizar descripciones), sin necesitar ningún campo nuevo en `PlanningItem` ni ninguna relación persistida hacia Movimientos.

## 10. Fuera de alcance por decisión de producto

Ninguno de estos puntos es una limitación técnica — son decisiones explícitas para mantener el módulo chico:

* **Presupuestos por categoría, sobres de dinero, reglas contables** — el módulo no clasifica ni distribuye dinero, solo lista pagos esperados.
* **Calendario, agenda, recordatorios, notificaciones** — `DueDate` es un dato plano, nunca un disparador (regla 2, sección 7).
* **Estimación de importes, promedios, predicciones, IA** — el usuario carga el monto siempre; el sistema como máximo copia un valor que el propio usuario ya escribió (sección 6.2) o cuenta presencia histórica para sugerir (sección 6.3), nunca infiere una cifra.
* **Planificación anual** — el mes es la única unidad; no hay agregación entre meses en la UI de este módulo.
* **Prevención de duplicados / matching inteligente al agregar desde historial** — aceptado como limitación consciente (sección 10.2 abajo); la simplicidad del módulo tiene prioridad sobre la automatización.

## 11. Revisión crítica y decisiones abiertas

Puntos verificados explícitamente al redactar este documento, para que quede registrado por qué no se resolvieron distinto:

**10.1 — "Independencia total" es una simplificación.** Como se detalla en la sección 8, la dependencia real es unidireccional (Planificación lee Movimientos para sugerir; Movimientos no conoce Planificación). No es una inconsistencia del diseño, pero el enunciado original ("ambos módulos son independientes") es impreciso si se lee literalmente — este documento lo corrige explícitamente en la sección 8 para que no genere confusión más adelante.

**10.2 — Duplicados aceptados afectan al resumen, no solo a la lista visual.** Si el usuario agrega dos veces el mismo concepto (a mano, o vía "Agregar desde historial" sobre algo que ya estaba con otro texto), el "Total planificado" y el "Disponible" quedan inflados/deflactados en consecuencia — son los números centrales que responden la pregunta de flujo de caja. Es una decisión ya tomada y mantenida a propósito (la alternativa es incorporar matching, que el diseño rechaza explícitamente) — se deja documentado el costo real, no oculto detrás de "es solo una fila de más".

**10.3 — El texto libre de `Title` limita la precisión de cualquier matching futuro** (tanto en "Agregar desde historial" como en la integración de la sección 9), porque no hay vínculo con `Counterparty`. Aceptado como parte de la misma decisión de simplicidad — si en el futuro esto genera fricción real, la corrección pertenece al diseño de la sección 9, no a este documento.

**10.4 — Letra de épica sin asignar**, ver nota al inicio del documento — hallazgo de proceso, no de diseño: la numeración de épicas del repositorio está desincronizada entre `docs/RoadMaps/FinancialMcp-vNext.md` y los documentos más recientes en `docs/Architecture/`. Este documento no intenta resolver esa desincronización.

Ningún punto de esta sección requiere cambiar el modelo conceptual (sección 5) ni el alcance (sección 4) — son aclaraciones y costos aceptados, no defectos pendientes de corrección.
