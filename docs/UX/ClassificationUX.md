# UX objetivo — Clasificación y revisión

> Documento vivo. Describe las pantallas objetivo hacia las que evoluciona la UI actual (`dashboard.html`, `group-reconciliation.html`) y qué pasa con cada pieza de la UI existente. No implica que estas pantallas ya existan — hoy solo existen `dashboard.html` (con navegación a secciones "pronto") y `group-reconciliation.html` (2 columnas + balance bar + modales, ver `src/FinancialMcp.Api/wwwroot/`). Referencia funcional: `docs/RoadMaps/FinancialMcp-vNext.md` §7 (Épica K).

---

## 1. Pantallas objetivo

### 1. Dashboard

Punto de entrada. Ya existe como `dashboard.html`. Hoy tiene navegación con placeholders deshabilitados ("pronto": Movimientos, Gastos fijos, Presupuestos, Patrimonio, Inversiones, Copilot) y un badge `navPending` declarado pero nunca poblado por ningún script. Objetivo: que cada entrada de navegación apunte a una pantalla real de esta lista, y que el badge muestre la cantidad de movimientos pendientes de clasificar (dato que ya puede obtenerse de `GetUnclassifiedMovementsQuery`).

### 2. Movimientos pendientes

Pantalla nueva (Épica K). Lista de `FinancialMovement` de banco/tarjeta (Transaction/BankStatement), resultado de `GET /api/movements` (PR K1) — un endpoint propio que depende directamente de `IMovementLoader`, sin pasar por `IReviewEngine`/`GetUnclassifiedMovementsQuery`: no calcula sugerencias de matching ni sospechosos, solo lista y filtra (por cuenta, texto, período). `GetUnclassifiedMovementsQuery` sigue siendo exclusivo de `group-reconciliation.html` (Migración desde Excel), que sí necesita las sugerencias del motor para el cruce N↔M. Reemplaza la lectura mental de "columna Banco/Tarjeta" de `group-reconciliation.html` como vista principal de trabajo diario — pero no es una pantalla de reconciliación 1:1, es una cola de pendientes con selección y filtro, pensada para clasificar rápido, no para conciliar montos entre dos fuentes.

### 3. Clasificación

Pantalla/modal nueva (Épica K), disparada desde "Movimientos pendientes". Es la evolución directa del modal de clasificación ya existente en `group-reconciliation.html` (`ClassifyMovementCommand`), pero como flujo primario en vez de acción secundaria dentro de una pantalla de reconciliación. Debe completar las 4 dimensiones (`Category`, `FinancialImpact`, `MovementType`, `Counterparty`) y, cuando se elige `Counterparty`, pre-cargar sus valores por defecto (`DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact` — mecanismo ya modelado en el dominio, sin wiring en ninguna UI hoy; PR K4).

### 4. Importaciones

Pantalla nueva (Épica I, PR I6). Consume `GET /api/imports/history` (Épica I, PR I5) sobre `ImportBatch`. Muestra corridas de importación por fuente (banco/tarjeta/Excel) con insertados/duplicados/fallidos y el detalle de líneas ignoradas — hoy esa información no es visible en ninguna pantalla, se pierde en logs de proceso.

### 5. Cuentas

Pantalla nueva (Épica J). CRUD de `FinancialAccount` (`Bank`/`Card`/`Investment`/`Cash`). Hoy no existe ningún concepto de "cuenta" en la UI — `BankStatement`/`Transaction` no se agrupan por cuenta de origen. Esta pantalla es la base sobre la que después se apoya la vista de inversiones (Épica M).

---

## 2. Qué pasa con `group-reconciliation.html`

No se borra en la Épica K. Se mantiene como pantalla secundaria de **conciliación banco vs. Excel** — un caso de uso real y distinto de "clasificar un movimiento": comparar dos fuentes que deberían coincidir en monto y marcar diferencias. Deja de ser la pantalla principal de trabajo diario cuando existan "Movimientos pendientes" y "Clasificación". Se revisita en la Épica N (simplificación del formulario) para decidir si su modal de clasificación se unifica con el nuevo, o si se elimina la duplicación de lógica entre ambos.

## 3. Qué partes se reutilizan

* El patrón de **selección + filtro + action bar** (`refSelAll`/`refClear`/`refFilter`/`action-bar` con contador de seleccionados) de `group-reconciliation.html` — aplica igual de bien a "Movimientos pendientes".
* El **modal de clasificación** (`ClassifyMovementCommand` ya integrado) — se traslada como flujo primario, no se reescribe desde cero.
* El **modal "Marcar como revisado" en modo batch** (`batchList`, `btnBatchReview`) — el patrón de acción en lote sobre una selección es reutilizable para cualquier acción masiva futura.
* `FinancialMetricsService` y los endpoints de `MetricsEndpoints` — el Dashboard no necesita nueva lógica de agregación, ya existe.

## 4. Qué conceptos desaparecen

* La lectura de "columna Banco/Tarjeta vs. columna Excel/Manual" como **el** flujo principal de trabajo — pasa a ser específico de conciliación, no de clasificación.
* La **balance bar** (`Banco seleccionado` / `Excel seleccionado` / `Diferencia`) como elemento central de la pantalla principal — es específica del caso "hacer coincidir dos montos", no aplica a clasificar un movimiento individual sin contraparte de reconciliación.
* La idea de que el Excel es una fuente de datos "viva" a mostrar junto al banco en la pantalla principal — según ADR-002, Excel es mecanismo histórico de migración, no un flujo activo permanente.

## 5. Qué acciones quedan como secundarias

* **Confirmar Match** (`btnConfirm`, `ConfirmMatchCommand`) — sigue existiendo, pero como acción dentro de "conciliación" (`group-reconciliation.html`), no como parte del flujo de clasificación individual.
* **Descartar candidato legacy** (`btnDiscard`, `DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`) — acción secundaria de mantenimiento sobre datos importados de Excel, no una acción que un usuario nuevo necesite ver en su flujo diario.
* **Marcar revisados en lote** — se mantiene como utilidad, pero no es el objetivo principal de "Movimientos pendientes" (el objetivo principal ahí es clasificar, no revisar).
