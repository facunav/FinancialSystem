# UX objetivo — Clasificación y revisión

> Documento vivo. Describe las pantallas objetivo hacia las que evolucionó la UI (`dashboard.html`, `movements.html`) y qué pasó con cada pieza de la UI anterior. Hoy existen `dashboard.html` y `movements.html`; `group-reconciliation.html` (2 columnas + balance bar + modales, conciliación banco vs. Excel) se eliminó por completo en PR-L4 — ver sección 2. Referencia funcional: `docs/RoadMaps/FinancialMcp-vNext.md` §7 (Épica K).

---

## 1. Pantallas objetivo

### 1. Dashboard

Punto de entrada. Ya existe como `dashboard.html`. Hoy tiene navegación con placeholders deshabilitados ("pronto": Gastos fijos, Presupuestos, Patrimonio, Inversiones, Copilot) y un badge `navPending` declarado pero nunca poblado por ningún script. Objetivo: que el badge muestre la cantidad de movimientos pendientes de clasificar. `GetUnclassifiedMovementsQuery`, que hasta PR-L4 hubiera sido la fuente natural de ese dato, se retiró (sin consumidor real); el dato equivalente puede obtenerse igual filtrando el resultado de `IMovementsQueryService.GetAsync` por `Status == null`, no hace falta reintroducir esa query.

### 2. Movimientos

Pantalla nueva (Épica K), implementada en `movements.html`. Lista movimientos de banco/tarjeta (Transaction/BankStatement), tanto pendientes como ya clasificados (PR K3) — resultado de `GET /api/movements`, un endpoint que depende de `IMovementsQueryService` (Application). Para los pendientes, el servicio orquesta `IReviewEngine.GenerateAsync` una sola vez por request y reutiliza su resultado tanto para el listado de pendientes como para los grupos sospechosos (K6, posible duplicado/split). Para los ya clasificados sigue leyendo `ClassifiedMovement`/`ClassifiedMovementItem` directo, sin pasar por el motor.

**PR-L4:** hasta acá esta pantalla también incluía una sugerencia de matching por fila (mejor candidato Excel legacy encontrado por el motor, con nivel de confianza — PR K4) y, para sugerencias de confianza alta, un botón "Confirmar" de un clic que reutilizaba `POST /api/movement-review/confirm-match` (`ConfirmMatchCommand`, PR K5). Ese mecanismo completo se retiró: no tenía consumidor real distinto de `group-reconciliation.html`, que a su vez ya no tenía entrada de navegación desde PR-L3a. `IMovementLoader` ya no carga `LegacyImportedExpense`, así que no hay ninguna segunda fuente contra la que sugerir un match. Sigue sin haber selección cruzada, columnas duplicadas, checkboxes de matching ni balance en esta pantalla — ahora porque el concepto completo de matching contra una segunda fuente no existe en el sistema, no solo porque esta pantalla lo evitara.

**Cuenta financiera (columna, desde K1)**: hoy es un `<select>` editable por fila, y eso refleja fielmente el estado real del dominio, no un descuido de UI. `FinancialAccountId` en `BankStatement`/`Transaction` nunca lo completa ningún importador (`BbvaBankStatementImporter`, el importador de tarjeta) — queda `null` hasta que alguien lo asigna manualmente vía `PUT /api/{bank-statements|transactions}/{id}/financial-account`, que es exactamente lo que hace este `<select>`. Es, hoy, el único mecanismo que existe para vincular un movimiento con una cuenta — confirmado revisando los tres importadores y los doc-comments de `FinancialAccount.cs`, `BankStatement.cs` y `Transaction.cs`, que ya documentan esto explícitamente ("la asignación es manual por ahora; no hay wiring automático desde el pipeline de importación"). Resolver esto automáticamente a partir de metadata de importación (banco, tarjeta, tipo de cuenta, identificador) es alcance de la Épica J, no de K — `BankStatement` ya captura `BankName`+`AccountNumber` en el import y podría cruzarse contra `FinancialAccount`, pero `Transaction` (tarjeta) hoy no tiene ningún campo identificador de origen, así que ese cruce requeriría antes extender el parser de PDF. Hasta que Épica J resuelva esto, el `<select>` en Movimientos se mantiene editable — volverlo de solo lectura sin la resolución automática dejaría a los movimientos de banco/tarjeta sin ninguna forma de asignarse a una cuenta.

### 3. Clasificación

Modal en `movements.html`, disparado desde "Movimientos". Único modo desde PR-L4: clasificar/reclasificar vía `ClassifyMovementCommand` (individual o en lote, PR-L2). Debe completar las 4 dimensiones (`Category`, `FinancialImpact`, `MovementType`, `Counterparty`) y, cuando se elige `Counterparty`, pre-cargar sus valores por defecto (`DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact` — mecanismo ya modelado en el dominio, sin wiring en ninguna UI hoy). Desde K3, este modal también permite reclasificar un movimiento ya clasificado (precarga sus valores actuales); `ClassifyMovementHandler` actualiza el `ClassifiedMovement` existente en vez de duplicarlo, siempre que no forme parte de un grupo de más de un `ClassifiedMovementItem` — ese caso devuelve error (`AlreadyPartOfMatchGroup`, HTTP 409) y no tiene ninguna pantalla que lo resuelva hoy (ver sección 2).

**PR-L4:** hasta acá este modal tenía un segundo modo ("Confirmar sugerencia", desde K5), disparado por un botón junto a una sugerencia de confianza alta, que enviaba `POST /api/movement-review/confirm-match` con dos items (`Role=Reference`/`Role=Candidate`) — mismo comando y handler que ya usaba `group-reconciliation.html`. Ese modo se retiró junto con `ConfirmMatchCommand`/`ConfirmMatchHandler`. Los grupos `ClassifiedMovementItem` de más de un item que ese comando creó en el pasado siguen existiendo y siguen protegidos por `AlreadyPartOfMatchGroup` — la protección no dependía de que el comando siguiera activo, solo de que el grupo ya exista en base.

### 4. Importaciones

Pantalla nueva (Épica I, PR I6). Consume `GET /api/imports/history` (Épica I, PR I5) sobre `ImportBatch`. Muestra corridas de importación por fuente (banco/tarjeta) con insertados/duplicados/fallidos y el detalle de líneas ignoradas — hoy esa información no es visible en ninguna pantalla, se pierde en logs de proceso.

### 5. Cuentas

Pantalla nueva (Épica J). CRUD de `FinancialAccount` (`Bank`/`Card`/`Investment`/`Cash`). Hoy no existe ningún concepto de "cuenta" en la UI — `BankStatement`/`Transaction` no se agrupan por cuenta de origen. Esta pantalla es la base sobre la que después se apoya la vista de inversiones (Épica M).

---

## 2. Qué pasó con `group-reconciliation.html`

**Historial:** durante PR-L1 a PR-L3a se mantuvo como pantalla secundaria de **conciliación banco vs. Excel** ("comparar dos fuentes que deberían coincidir en monto y marcar diferencias"), retirada de la navegación visible en PR-L3a pero todavía accesible por URL directa, a la espera de que se terminara de limpiar el backend Legacy que sostenía.

**PR-L4:** se eliminó por completo (archivo y todo el backend que dependía — `IMatchScorer`, 4 `IMatchingRule`, `ConfirmMatchCommand`/`DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`/`GetUnclassifiedMovementsQuery`). La razón: para cuando se hizo el análisis de PR-L4, la pantalla no tenía entrada de navegación desde PR-L3a y ningún flujo real de conciliación activa la usaba — mantenerla habría dejado una pantalla físicamente presente pero funcionalmente muerta, inconsistente con el resto del sistema. La entidad `LegacyImportedExpense` en sí se eliminó en PR-L5 (informado por el conteo de datos de PR-L4.5). No queda ninguna pantalla de conciliación banco vs. Excel en el sistema, ni ninguna tabla de origen para ese flujo.

## 3. Qué partes se reutilizaron

* El patrón de **selección + filtro + action bar** (`refSelAll`/`refClear`/`refFilter`/`action-bar` con contador de seleccionados) de `group-reconciliation.html` — se adaptó a la barra de selección en lote de "Movimientos" (PR-L2).
* El **modal de clasificación** (`ClassifyMovementCommand` ya integrado) — se trasladó como flujo primario, no se reescribió desde cero.
* El **modal "Marcar como revisado" en modo batch** (`batchList`, `btnBatchReview`) — el patrón de acción en lote sobre una selección se reutilizó como `doBatchReview`/clasificación en lote de "Movimientos" (PR-L2), disparando `ClassifyMovementCommand` N veces en secuencia en vez de un comando batch nuevo.
* `FinancialMetricsService` y los endpoints de `MetricsEndpoints` — el Dashboard no necesitó nueva lógica de agregación, ya existía.

## 4. Qué conceptos desaparecieron

* La lectura de "columna Banco/Tarjeta vs. columna Excel/Manual" como flujo de trabajo — no existe ninguna pantalla organizada así hoy.
* La **balance bar** (`Banco seleccionado` / `Excel seleccionado` / `Diferencia`) — eliminada junto con `group-reconciliation.html`.
* La idea de Excel como fuente "viva" a mostrar junto al banco — consistente con ADR-002 (Excel es mecanismo histórico de migración), llevada hasta el final en PR-L4: no queda ningún flujo de UI que trate a Excel como una segunda fuente activa.
* El motor de sugerencias de matching (`IMatchScorer`, 4 `IMatchingRule`) y la sugerencia por fila en "Movimientos" (K4/K5) — sin consumidor real fuera de `group-reconciliation.html`, se retiraron junto con ella en PR-L4.

## 5. Qué acciones se eliminaron (ya no son ni siquiera secundarias)

* **Confirmar Match** (`ConfirmMatchCommand`) — se retiró en PR-L4. No es una acción "secundaria" en ninguna pantalla: no existe ningún productor de confirmaciones hoy. `ClassificationStatus.Confirmed`/`ProcessingSource.ConfirmedFromSuggestion` siguen siendo valores válidos del dominio (filas históricas los tienen), simplemente sin nada que los genere actualmente — podrían volver a tener productor si se implementa un motor de recomendaciones real (historial, reglas, IA).
* **Descartar/restaurar candidato legacy** (`DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`) — se retiraron en PR-L4 junto con el resto del backend de matching.
* **Marcar revisados en lote** — no se eliminó: es lo que terminó siendo la clasificación en lote de "Movimientos" (PR-L2), ver sección 3.
