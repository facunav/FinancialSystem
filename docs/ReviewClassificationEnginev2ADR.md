# Review & Classification Engine v2 — Documento de Arquitectura

> Este documento es la base de diseño de la próxima iniciativa del proyecto FinancialMcp. No contiene código ni implementación — es el plan a validar antes de empezar a construir. Se apoya en `FinancialMcp-Roadmap.md` (fuente de verdad del proyecto) y en la auditoría de código realizada sobre el estado actual del repositorio.

---

## 1. Objetivo

Reconstruir el motor de **Revisión y Clasificación de movimientos financieros**, la funcionalidad central del sistema que fue eliminada durante el refactor v2.0 (commit `e38ace2`, "Refactor parcial") sin reemplazo equivalente.

Hoy no existe ningún código que cree un registro en `ClassifiedMovement` — la única tabla que consumen Métricas y el MCP. El objetivo de esta iniciativa es cerrar exactamente esa brecha, alineado al modelo de clasificación de 4 dimensiones (Tipo de Movimiento, Impacto Financiero, Categoría, Contraparte) definido en el Roadmap, no al modelo viejo de "conciliación" que existía antes.

---

## 2. Alcance

**Incluido:**

- Adaptar los movimientos crudos ya importados (`Transaction`, `BankStatement`, `LegacyImportedExpense`) a un modelo neutro de trabajo (`FinancialMovement`, ya definido en `Domain/Review/FinancialMovement.cs`).
- Motor de sugerencias de matching (scoring configurable) entre movimientos bancarios/tarjeta y candidatos legacy, produciendo el contrato ya definido en `Domain/Review/ReviewResult.cs` (`MatchedPair`, `UnmatchedMovement`, `SuspiciousGroup`).
- Comandos para clasificar manualmente (`Reviewed`) o confirmar una sugerencia (`Confirmed`), escribiendo `ClassifiedMovement` + `ClassifiedMovementItem` con las 4 dimensiones.
- Endpoints Api que reemplazan a los antiguos `/api/reconciliation/*` (borrados, sin sucesor).
- Actualización de `group-reconciliation.html` (o su reemplazo) para consumir el nuevo contrato y reemplazar el viejo campo "Motivo" por Tipo de Movimiento + Contraparte + Comentario libre.

**Explícitamente fuera de alcance** (pertenecen a otras fases del Roadmap):

- Pipeline de importación (`FileImportRouter`, handlers) — ya funciona, no se toca.
- Módulo de Gastos Fijos (Roadmap Fase 2).
- Presupuestos, flujo de caja, patrimonio neto (Roadmap Fases 2-4).
- Aprendizaje automático de reglas de clasificación por historial (Roadmap lo marca como "futuro", Fase 5).
- Nuevas herramientas MCP (`GetTopCounterparties`, etc.) — el MCP empieza a devolver datos reales automáticamente en cuanto esta iniciativa pueble `ClassifiedMovement`; no requiere cambios de código en `FinancialTools.cs`.

**Zona gris a decidir** (ver sección 18): sugerencia automática de clasificación cuando la Contraparte ya es conocida — el Roadmap la marca como pendiente v2.0 y la entidad `Counterparty` ya tiene los campos (`DefaultCategoryId`, `DefaultMovementType`, `DefaultFinancialImpact`) para soportarla, pero puede entrar en esta iniciativa o posponerse.

---

## 3. Responsabilidades por capa

Sigue la Clean Architecture ya establecida en el proyecto (Domain → Application → Infrastructure → Api), sin introducir capas nuevas.

| Capa | Responsabilidad | Ya existe / a crear |
|---|---|---|
| Domain | Modelos neutros del proceso de revisión (`FinancialMovement`, `ReviewResult` y su familia) y el resultado persistido (`ClassifiedMovement`, `ClassifiedMovementItem`) | Ya existe — reutilizar tal cual, no redefinir |
| Application | Contratos (`IMovementLoader`, `IMatchScorer`, `IMatchingRule`, `ISuspicionDetector`, `IReviewEngine`), comandos, queries, opciones de configuración | A crear |
| Infrastructure | Implementaciones concretas: adaptador EF de las 3 fuentes, reglas de scoring, orquestador | A crear |
| Api | Endpoints delgados que delegan a los comandos/queries de Application, sin lógica de negocio | A crear |

---

## 4. Flujo completo

```
1. IMPORTAR (fuera de alcance, ya funciona)
   Transaction / BankStatement / LegacyImportedExpense se pueblan vía FileImportRouter.

2. CARGAR PERÍODO A REVISAR
   IMovementLoader lee Transaction + BankStatement (Reference) y LegacyImportedExpense
   no descartado (Candidate) del período pedido, y los adapta a FinancialMovement.
   Excluye movimientos que ya tienen un ClassifiedMovementItem (ya clasificados).

3. GENERAR SUGERENCIAS (en memoria, no persistido)
   IReviewEngine aplica IMatchScorer (reglas de monto/fecha/descripción/método de pago)
   sobre los FinancialMovement cargados y arma un ReviewResult:
     - Matched: pares/grupos con score >= umbral
     - Unmatched: movimientos sin candidato suficiente
     - Suspicious: posibles duplicados/splits (ISuspicionDetector)

4. EL USUARIO DECIDE (UI de revisión)
   a) Acepta una sugerencia (1↔1, N↔1, 1↔N, N↔M)
      -> POST confirmar-match { items, categoryId, movementType, financialImpact, counterpartyId? }
      -> handler crea ClassifiedMovement (Status=Confirmed) + Items (Role=Reference/Candidate)
   b) Clasifica manualmente sin candidato
      -> POST clasificar { sourceEntityType, sourceId, categoryId, movementType, financialImpact, counterpartyId?, comment? }
      -> handler crea ClassifiedMovement (Status=Reviewed) + Item (Role=Reference)
   c) Descarta un candidato legacy que no corresponde a ningún movimiento real
      -> POST descartar-candidatos { ids } -> LegacyImportedExpense.IsDiscarded = true

5. MÉTRICAS Y MCP (fuera de alcance, ya funcionan)
   FinancialMetricsService y FinancialTools.cs leen ClassifiedMovements sin cambios.
   Empiezan a devolver datos reales apenas el paso 4 escribe la primera fila.
```

---

## 5. Componentes

Contratos nuevos en `FinancialSystem.Application` (namespace propuesto: `FinancialSystem.Application.Review`, evitando el nombre `Reconciliation` retirado en PR1-3):

- `IMovementLoader` — cambia el período `(DateOnly from, DateOnly to)` y devuelve `IReadOnlyList<FinancialMovement>` (Reference + Candidate), excluyendo lo ya clasificado.
- `IMatchScorer` — recibe un `FinancialMovement` de referencia y uno candidato, devuelve `MatchScore`.
- `IMatchingRule` — una regla individual (monto/fecha/descripción/método de pago); `IMatchScorer` las compone.
- `ISuspicionDetector` — recibe la lista completa de un lado y devuelve `IReadOnlyList<SuspiciousGroup>`.
- `IReviewEngine` — orquesta los anteriores y arma el `ReviewResult` completo para un período.

Estos nombres son conceptualmente equivalentes a los que existían en el motor viejo (`IMatchScorer`, `IMatchingRule`, `ISuspicionDetector`, `IReconciliationEngine` → ahora `IReviewEngine`) — se reutiliza el diseño de interfaces, no el código borrado, y se renombra lo que aludía a "Reconciliation".

## 6. Handlers

En `FinancialSystem.Application.Review.Commands` / `.Queries` (siguiendo el mismo patrón `Command`/`Handler` que ya usaba el motor viejo, sin repositorio intermedio — ver decisión en sección 17):

- `ClassifyMovementCommand` / `ClassifyMovementHandler` — clasificación manual (Reviewed).
- `ConfirmMatchCommand` / `ConfirmMatchHandler` — confirmación de una sugerencia (Confirmed), soporta grupos N↔M.
- `DiscardLegacyCandidatesCommand` / `DiscardLegacyCandidatesHandler` — marca `IsDiscarded`.
- `GetUnclassifiedMovementsQuery` / `GetUnclassifiedMovementsHandler` — expone el resultado de `IReviewEngine` para un período.

## 7. Servicios

En `FinancialSystem.Infrastructure.Review`:

- `MovementLoader : IMovementLoader` — consulta `IApplicationDbContext.Transactions`/`BankStatements`/`LegacyImportedExpenses`, hace `LEFT JOIN` contra `ClassifiedMovementItem` (por `SourceEntityType`+`SourceId`) para excluir lo ya clasificado, y proyecta a `FinancialMovement`.
- `MatchScorer : IMatchScorer` + reglas concretas (`AmountRule`, `DateRule`, `DescriptionRule`, `PaymentMethodRule`).
- `SuspicionDetector : ISuspicionDetector`.
- `ReviewEngine : IReviewEngine` — orquesta los anteriores.
- `ReviewEngineOptions` — reemplaza a la vieja `ReconciliationOptions` (eliminada como config huérfana en PR11). Se registra en DI y se bindea a una sección de configuración **desde el mismo PR que la introduce**, para no repetir el problema de PR11.

## 8. Endpoints

Grupo nuevo (nombre a confirmar, ver sección 18), propuesta: `/api/movement-review`.

| Método | Ruta | Reemplaza a (borrado) |
|---|---|---|
| GET | `/api/movement-review/unclassified?from&to` | `GET /api/reconciliation/unmatched-movements` |
| POST | `/api/movement-review/classify` | `POST /api/reconciliation/review` |
| POST | `/api/movement-review/confirm-match` | `POST /api/reconciliation/confirm-group` |
| POST | `/api/movement-review/discard-candidates` | `POST /api/reconciliation/discard-candidates` |
| POST | `/api/movement-review/restore-candidates` | `POST /api/reconciliation/restore-candidates` |

DTOs nuevos en `src/FinancialMcp.Api/DTOs/MovementReviewDtos.cs` (nombre libre otra vez tras PR4, sin espacio en el archivo esta vez) y endpoints en `src/FinancialMcp.Api/Endpoints/MovementReviewEndpoints.cs`, registrados en `Program.cs` junto a los existentes.

## 9. Interacción con la UI

`src/FinancialMcp.Api/wwwroot/group-reconciliation.html` deja de llamar a `/api/reconciliation/*` (hoy rotos) y pasa a llamar a `/api/movement-review/*`. Cambios de contenido, no de estructura:

- Se quita el combo "Motivo" (ya no existe `ReviewReason` en el dominio).
- Se agrega selector de Tipo de Movimiento (`MovementType`) y autocompletado de Contraparte (`/api/counterparties`, ya existe). La pre-carga automática de `DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact` al seleccionar una contraparte queda condicionada a la decisión #2 de la sección 18: si se pospone, el selector de Contraparte se agrega igual, pero sin autocompletado de valores sugeridos.
- Se mantiene el campo de comentario libre (ya existía como `ReviewNotes`, hoy `Comment` en `ClassifiedMovement`).
- Se recomienda parchear el archivo existente incrementalmente (grilla, selección múltiple y barra de acciones ya están construidas) en vez de reescribirlo desde cero — ver decisión en sección 18.

## 10. Interacción con el Worker

Ninguna requerida. `ImportsFolderWatcherHostedService` sigue poblando las tablas crudas sin cambios; `TransactionInsightsWorker` sigue operando sobre `Transactions` sin cambios. El cómputo de sugerencias se resuelve sincrónicamente dentro del request Api (ver decisión en sección 17) — no hay necesidad de un job en background para el volumen de datos de un usuario personal.

## 11. Interacción con el MCP

Ninguna requerida en el código. `FinancialTools.cs` ya delega a `IFinancialMetricsService`, que ya lee `ClassifiedMovements`. En cuanto esta iniciativa escriba la primera fila, las 4 herramientas MCP existentes (`GetMonthlySummary`, `GetExpensesByCategory`, `GetMonthlyTrend`, `CompareWithPreviousMonth`) empiezan a devolver datos reales sin ningún cambio adicional. Nuevas herramientas relacionadas con Contraparte quedan fuera de esta iniciativa (Roadmap, sección MCP, "Herramientas futuras").

## 12. Eventos

**No se introduce infraestructura de eventos.** El proyecto no tiene hoy ningún bus de eventos, MediatR notifications, ni mecanismo pub/sub — toda la comunicación entre capas es por inyección de dependencias directa. Agregar eventos para esta iniciativa sería una capa de complejidad sin consumidor real (no hay ningún otro módulo que necesite reaccionar a "se clasificó un movimiento" hoy). Si en el futuro Gastos Fijos necesita enterarse de una clasificación para marcar un pago automáticamente, se evalúa en esa iniciativa, no en esta.

## 13. Modelo de datos

**No se necesitan tablas nuevas.** `ClassifiedMovement` y `ClassifiedMovementItem` (con `MovementRole`, `SourceEntityType`) ya cubren completamente lo que este motor necesita persistir — fueron diseñadas para esto en PR1-3, solo faltaba el motor que las poblara.

Las sugerencias de matching **no se persisten** — se recalculan en cada request. Esto ya está documentado explícitamente en el código actual: el comentario de `ClassifiedMovement.cs` dice *"Las sugerencias de matching viven en MatchSuggestion (tabla de staging, no persistida hoy)"*, y `ReviewResult.cs` es un `record` in-memory, no una entidad EF. Esta iniciativa mantiene esa decisión (ver sección 17), no la revierte.

## 14. Estrategia de implementación incremental

Cada paso compila y es verificable por separado, sin dejar el sistema en un estado peor que el actual en ningún punto intermedio:

1. **Config** — introducir `ReviewEngineOptions`, registrarla en DI y bindearla, sin ningún consumidor todavía (PR aislado, cero riesgo).
2. **Lectura pura** — `IMovementLoader` + implementación. Solo lee, no expone nada a la Api todavía. Verificable con datos reales de la base.
3. **Scoring** — `IMatchScorer` + reglas, operando sobre lo que carga el paso 2. Sigue sin escribir nada.
4. **Orquestación** — `IReviewEngine` arma el `ReviewResult` completo. Se expone por primera vez un endpoint de solo lectura (`GET unclassified`) para validar visualmente que las sugerencias tienen sentido, antes de poder confirmar nada.
5. **Primer camino de escritura** — comando de clasificación manual (`Reviewed`). Es el camino más simple porque no depende del motor de sugerencias — ya permite poblar `ClassifiedMovement` de punta a punta.
6. **Segundo camino de escritura** — comando de confirmación de match (`Confirmed`), que sí depende del motor de sugerencias del paso 4.
7. **UI** — actualizar `group-reconciliation.html` para consumir 4-6.
8. **(Opcional, a confirmar)** sugerencia automática por Contraparte conocida.

## 15. División en épicas y PRs pequeños

**Épica A — Fundamentos de lectura (sin escritura, sin endpoint)**
- PR A1: `ReviewEngineOptions` + registro en DI
- PR A2: `IMovementLoader` + `MovementLoader` (adapta las 3 fuentes a `FinancialMovement`, excluye ya-clasificados)

**Épica B — Motor de sugerencias (sigue sin escritura ni endpoint)**
- PR B1: `IMatchScorer` + reglas de monto y fecha
- PR B2: reglas de descripción y método de pago, scorer compuesto completo
- PR B3: `ISuspicionDetector` (duplicados, splits)
- PR B4: `IReviewEngine` (orquestador, arma `ReviewResult`)

**Épica C — Primer endpoint y primera escritura**
- PR C1: `GET /api/movement-review/unclassified` (expone la Épica B, solo lectura)
- PR C2: comando + endpoint `classify` (Reviewed) — primer camino de escritura end-to-end
- PR C3: comando + endpoint `confirm-match` (Confirmed) — cierra el círculo completo
- PR C4: comandos + endpoints `discard-candidates` / `restore-candidates`

**Épica D — UI**
- PR D1: apuntar la grilla existente a los nuevos endpoints (sin cambiar el modelo de clasificación todavía)
- PR D2: reemplazar el combo "Motivo" por Tipo de Movimiento + Contraparte (con autocompletado de valores sugeridos) + Comentario libre

Cada PR de esta lista se implementará y verificará individualmente cuando se decida avanzar, con el mismo protocolo ya usado en la etapa anterior (verificar el hallazgo/contexto antes de tocar código, un commit por PR, patch exportado, sin push).

## 16. Riesgos técnicos

- **Scoring configurable a mano es propenso a bugs sutiles** (falsos positivos/negativos). Mitigación: casos de prueba explícitos por regla individual antes de componerlas (Épica B dividida regla por regla, no de una sola vez).
- **Recalcular sugerencias en cada request sin cota de rango de fechas.** El costo del scoring crece con el producto de movimientos de referencia y candidatos dentro de la ventana pedida, y hoy nada impide pedir un rango arbitrariamente grande (ej. varios años) en `GET unclassified`. No es un problema de "optimizar prematuramente": es la ausencia de un límite en el contrato del endpoint, que es barato de definir ahora y caro de agregar después sin romper compatibilidad. Ver decisión #6 en la sección 18.
- **Reintroducir un config huérfano** (el mismo problema de PR11) si `ReviewEngineOptions` no se registra y consume desde el primer PR que la introduce.
- **Ventana de UI rota más larga de lo necesario**: entre que se exponen los nuevos endpoints (Épica C) y se actualiza la UI (Épica D), `group-reconciliation.html` seguiría apuntando a rutas viejas. Mitigación: secuenciar D inmediatamente después de C, sin otras iniciativas en el medio.
- **Naming**: repetir el error `CounterParty`/`Counterparty` (ya corregido en PR8) si no se usa consistentemente "Counterparty" desde el primer commit de esta iniciativa.

## 17. Decisiones de arquitectura (ya tomadas en este documento, con su razón)

- **Sin patrón Repository.** Se sigue la convención ya establecida post-refactor: los handlers usan `IApplicationDbContext` directamente (como ya hacen `CategoryEndpoints`/`CounterpartyEndpoints`), no se reintroduce `IProcessedExpenseRepository`/`IManualExpenseRepository` como existían en el motor viejo.
- **Sugerencias efímeras, no persistidas.** Coincide con lo que ya documentan `ClassifiedMovement.cs` y `ReviewResult.cs` en el código actual — no es una decisión nueva, es continuar la ya tomada.
- **Sin bus de eventos.** No hay infraestructura de eventos en el proyecto ni un consumidor real hoy (sección 12).
- **Sin cambios al Worker ni al MCP.** Ambos siguen funcionando igual; el MCP empieza a devolver datos reales automáticamente.
- **Cómputo de sugerencias sincrónico dentro del request Api**, no en background — no se justifica la complejidad de un job para el volumen actual.

## 18. Decisiones que requieren confirmación del usuario

1. **Nombre de la ruta pública nueva.** Propuse `/api/movement-review`; alternativas razonables: `/api/classification`, o incluso reutilizar `/api/reconciliation` con significado nuevo (no recomendado, generaría confusión con la documentación/histórico). Necesito que confirmes o elijas otra.
2. **¿La sugerencia automática por Contraparte conocida entra en esta iniciativa o se pospone?** El Roadmap la marca como pendiente v2.0 y el modelo de datos ya la soporta (`Counterparty.DefaultCategoryId/DefaultMovementType/DefaultFinancialImpact`), pero es una funcionalidad extra sobre el flujo mínimo de clasificación.
3. **¿Persistir las sugerencias de matching (tabla de staging) o mantenerlas efímeras?** Recomiendo efímeras (continuar la decisión ya documentada en el código), pero es una decisión de producto/performance que vale la pena confirmar explícitamente antes de empezar.
4. **¿Se rehace `group-reconciliation.html` desde cero o se parchea incrementalmente?** Recomiendo parchear (menor riesgo, la grilla y selección múltiple ya están construidas), pero es una decisión de UX/alcance de UI que corresponde confirmar.
5. **¿Los pesos y umbrales del motor de scoring se mantienen iguales a los que tenía la vieja `ReconciliationOptions`** (umbrales 0.85/0.60/0.35, pesos monto/fecha/descripción/método de pago 45/25/20/10), **o se re-evalúan desde cero?** No hay nada en el código actual que documente si esos valores funcionaban bien o mal en la práctica.
6. **¿Qué límite máximo de rango de fechas acepta `GET /api/movement-review/unclassified`?** El costo del motor de sugerencias crece con el producto de movimientos de referencia y candidatos en la ventana pedida, recalculado en cada request sin caché (sección 16). Necesito un valor concreto (por ejemplo, un máximo en días) antes de implementar el endpoint — no hay nada en el Roadmap ni en el código actual que sugiera uno.

---

## 19. Cambios respecto de la versión anterior

1. **Sección 9 (Interacción con la UI):** se corrigió una contradicción interna — el texto daba por hecho el autocompletado de valores por defecto de Contraparte, mientras que la sección 2 y la decisión #2 de la sección 18 marcan esa misma funcionalidad como no confirmada todavía. Se reescribió para condicionar el autocompletado a esa decisión, en vez de asumirla.
2. **Secciones 16 y 18:** el riesgo de recalcular sugerencias sin ningún límite de rango de fechas estaba mencionado pero sin ninguna cota real ("no optimizar prematuramente"). Se reforzó la redacción del riesgo y se agregó como decisión #6 en la sección 18, ya que el valor del límite es una decisión de producto que no puede inferirse del código ni del Roadmap.

---

*Este documento no incluye código ni cambios al repositorio. Es la base de diseño a validar antes de iniciar la implementación de "Review & Classification Engine v2".*
