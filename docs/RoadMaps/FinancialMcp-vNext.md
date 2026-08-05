# FinancialMcp — vNext

> **Reemplaza la versión anterior de este archivo** (commit `324eadf`, "Se agrego nuevo md"), que describía un modelo de conciliación (`ReconciledExpense`, `ReconciliationOrchestrator`, `ProcessedExpense`) retirado del código durante el refactor v2.0 (commit `e38ace2`) y explícitamente **no** reintroducido por el ADR de Review & Classification Engine v2 (hoy en `docs/Archive/ReviewClassificationEnginev2ADR.md`). Ese contenido quedó desalineado con el código real — ninguna de esas clases existe hoy en el repositorio. Esta versión reemplaza esa por una alineada con el estado real de `origin/master`.
>
> Este documento cumple, de acá en adelante, el rol que tenía `FinancialMcp-Roadmap.md` (eliminado del repositorio antes de esta ronda de documentación): **es la fuente de verdad del proyecto**. Antes de implementar una funcionalidad nueva, leerlo entero. Cualquier decisión de arquitectura, modelo de datos o flujo debe alinearse con lo que dice acá.

---

## 1. Estado actual del sistema

**Completado — Review & Classification Engine v2 (Épicas A–D).** El motor de revisión y clasificación de movimientos financieros fue reconstruido de punta a punta: carga de movimientos crudos, motor de sugerencias de matching, comandos de clasificación manual y confirmación de match, y UI actualizada al nuevo contrato. Detalle completo, con ADRs de diseño y backlog original, en `docs/Archive/ReviewClassificationEnginev2ADR.md` y `docs/Archive/ReviewClassificationBacklog.md`.

Piezas activas en el código hoy:

* `ClassifiedMovement` / `ClassifiedMovementItem` — única fuente de verdad para métricas y MCP.
* `IMovementLoader` / `ISuspicionDetector` / `IReviewEngine` — carga movimientos de banco/tarjeta y detecta grupos sospechosos (posible duplicado/split), sin persistencia intermedia.
* `ClassifyMovementCommand` — único caso de uso de escritura, con endpoint bajo `/api/movement-review/classify`.
* `movements.html` — UI de clasificación (reemplazó a `group-reconciliation.html`, ver Épica K).
* `FinancialMetricsService` + 4 herramientas MCP (`GetMonthlySummary`, `GetExpensesByCategory`, `GetMonthlyTrend`, `CompareWithPreviousMonth`) — sin cambios, funcionando sobre `ClassifiedMovement`.

**Épica K (Nueva UX de clasificación) — completada.** PR-L1 a PR-L5 retiraron por completo el importador Excel legacy muerto, agregaron clasificación en lote a `movements.html`, retiraron `group-reconciliation.html` de la navegación y luego del código, eliminaron el backend de matching contra `LegacyImportedExpense` (`IMatchScorer`, 4 `IMatchingRule`, `ConfirmMatchCommand`/`DiscardLegacyCandidatesCommand`/`RestoreLegacyCandidatesCommand`/`GetUnclassifiedMovementsQuery`) que ya no tenía ningún consumidor real, y finalmente eliminaron `LegacyImportedExpense` (entidad + tabla), informado por el conteo de datos de PR-L4.5. `SourceEntityType.LegacyImport`/`MovementRole.Candidate` se conservan como valores de enum históricos (filas ya persistidas los usan), sin productor actual. Detalle en `docs/UX/ClassificationUX.md`.

**Actualización (PATCH-029):** verificado contra el código, además de la Épica K ya hay otras tres con trabajo entregado — **J** (`FinancialAccount` como entidad explícita, CRUD completo en `accounts.html`), **L** (endpoint de cobertura de clasificación + indicador visual en `dashboard.html` + badge `#navPending` completado) y **O** (subida manual de archivos desde `imports.html`, reutilizando el mismo motor que el Worker). Detalle real por épica en la tabla de la sección 7. Además del roadmap de este documento, el proyecto construyó dos módulos completos que no estaban planificados acá — **Centro de Auditoría** y **Planificación Mensual** — ver `docs/PROJECT_STATUS.md` sección 2 para su estado; no se incorporan a este roadmap porque no nacieron de ninguna de las épicas I-O.

**En planificación — Épicas I–O (este documento).** De las siete, **K, J (núcleo), L y O ya tienen trabajo entregado** (ver arriba); **I** tiene avances concretos (idempotencia y trazabilidad del pipeline catch-all) pero conserva su riesgo principal sin resolver (ítem I7, ver sección 6); **M** y **N** siguen sin empezar. Antes de escribir código para cualquiera de ellas, ver `docs/Epics/` para el detalle de la que corresponda.

---

## 2. Visión del producto

FinancialMcp **no es una herramienta de conciliación bancaria** ni un sistema contable tradicional. Es una base de conocimiento financiero personal centrada en **revisar y clasificar movimientos**, no en hacerlos calzar contra un registro externo.

El objetivo de largo plazo es un asistente financiero (Financial Copilot) capaz de responder preguntas como:

* ¿Cuánto gasté en farmacia este año?
* ¿Cuánto gasto en combustible por mes?
* ¿Qué categorías aumentaron más?
* ¿Cuánto necesito para sostener mi estilo de vida?

Combinando:

* Movimientos de cuenta bancaria.
* Movimientos de tarjeta de crédito.
* Registros históricos de Excel (solo como ayuda de migración — ver ADR-002).
* Gastos fijos (planificado).
* Comportamiento de gasto histórico.

### Filosofía central

Banco y tarjeta son la fuente de verdad financiera. El Excel personal **no** es parte de la visión de largo plazo del sistema — es exclusivamente un mecanismo de migración mientras se transiciona el historial (ADR-002). Los flujos nuevos (cuentas, gastos fijos, inversiones) se diseñan sin depender de datos de Excel.

---

## 3. Arquitectura objetivo

Clean Architecture, sin capas nuevas:

| Capa | Responsabilidad |
|---|---|
| Domain | Entidades y modelos neutros. Sin dependencias hacia afuera. |
| Application | Contratos, comandos, queries, opciones de configuración. Sin lógica de infraestructura. |
| Infrastructure | Implementaciones concretas (EF Core, parsers, motor de sugerencias). |
| Api | Endpoints delgados que delegan a Application. Sin lógica de negocio. |

Detalle completo de capas y entidades (actuales y futuras) en `docs/Architecture/Architecture.md`.

Decisiones de arquitectura ya tomadas y vigentes (heredadas de Review & Classification Engine v2, no se revisan acá):

* Sin patrón Repository — los handlers usan `IApplicationDbContext` directamente.
* Sin bus de eventos — no hay infraestructura de eventos en el proyecto ni consumidor real.
* Sugerencias de matching efímeras — se recalculan en cada request, no se persisten.
* Cómputo de sugerencias sincrónico dentro del request Api — no hay job en background.

---

## 4. Flujo completo

```
Sources (Banco / Tarjeta / Excel legacy)
        │
        ▼
   Importación            ← Épica I (parcial — idempotencia catch-all ya implementada, I7 pendiente)
        │
        ▼
   Normalización          (TransactionNormalizer, ya implementado)
        │
        ▼
   Review                 (IReviewEngine, ya implementado)
        │
        ▼
   Classification         (ClassifiedMovement, ya implementado)
        │
        ▼
   Dashboard               ← Épica L visibilidad de cobertura (implementada)
        │
        ▼
   MCP                     (ya implementado, funciona automáticamente)
```

* **Sources → Importación:** hoy tres pipelines independientes (banco XLS, tarjeta PDF, Excel legacy), con niveles de confiabilidad distintos — ver §6 y `docs/Epics/EpicaI-Importacion.md`.
* **Importación → Normalización:** `ITransactionNormalizer` limpia descripción, resuelve moneda y normaliza fecha antes de persistir. Sin cambios planificados.
* **Normalización → Review:** `IMovementLoader` adapta `Transaction`/`BankStatement` a `FinancialMovement`, excluyendo lo ya clasificado. `IReviewEngine` detecta grupos sospechosos (posible duplicado/split).
	PR-L4 (Épica K) retiró el motor de sugerencias de matching contra `LegacyImportedExpense` que hasta entonces vivía acá — `IMovementLoader` ya no carga esa fuente. `IReviewEngine`/`ReviewResult` quedan como punto de extensión para un futuro motor de recomendaciones (historial, reglas, IA), sin diseñarse todavía.
* **Review → Classification:** el usuario clasifica manualmente (`ClassifyMovementCommand`), escribiendo `ClassifiedMovement` con las 4 dimensiones (ADR-001). Épica K simplifica cómo se llega a esa decisión (Contraparte con valores por defecto, Épica N), no el modelo en sí. `ConfirmMatchCommand` (confirmar una sugerencia de matching) se retiró en PR-L4 junto con el motor que la producía — `ClassificationStatus.Confirmed`/`ProcessingSource.ConfirmedFromSuggestion` no se generan actualmente, pero el concepto de "confirmación" no se elimina del dominio: puede volver a tener productor cuando exista un motor de recomendaciones real.
* **Classification → Dashboard:** `FinancialMetricsService` agrega por `FinancialImpact`/`Category`. Épica L (terminada) agrega visibilidad de cuánto del período **no** está clasificado todavía — endpoint de cobertura + indicador visual en `dashboard.html` + badge `#navPending`.
* **Dashboard → MCP:** sin cambios — las herramientas MCP ya leen `ClassifiedMovement` automáticamente.

---

## 5. Qué NO se debe cambiar

Cosas que ya están bien diseñadas y no deben tocarse en las épicas siguientes salvo que aparezca evidencia concreta de un problema:

* El modelo de clasificación de 4 dimensiones (`Category`, `FinancialImpact`, `MovementType`, `Counterparty`) — ver ADR-001.
* `ClassifiedMovement`/`ClassifiedMovementItem` como única fuente de verdad para métricas y MCP.
* El patrón de idempotencia de `BbvaBankStatementImporter` (`ExternalId` + índice único + consulta previa) — **ya se copió** hacia el pipeline catch-all de tarjeta (`ImportFileProcessingSink`, Épica I), con el mismo criterio de consulta batch. No rediseñarlo; lo que queda de Épica I (I7, fingerprint Visa/Mastercard) es un problema de ruteo de parser, no de idempotencia.
* La arquitectura Command/Handler + endpoints delgados de Review & Classification Engine v2.
* `ISuspicionDetector`/`IReviewEngine` como orquestador de detección de sospechosos y punto de extensión para un futuro motor de recomendaciones.

**Corrección (PR-L4):** esta sección afirmaba que "el motor de sugerencias... Épica K cambia la UI que lo consume, no el motor" — resultó incorrecto. El análisis de PR-L4 encontró que ese motor (`IMatchScorer` + 4 `IMatchingRule`) no tenía ningún consumidor real fuera de `group-reconciliation.html`, que la propia Épica K retiró en PR-L3a/PR-L4. Se elimina en vez de mantenerse. Regla ajustada: "no cambiar sin evidencia concreta" sigue valiendo, pero la evidencia concreta ya apareció acá.

---

## 6. Problemas reales existentes

Detectados por revisión directa del código (no hipótesis) durante la evaluación funcional posterior al cierre de Review & Classification Engine v2:

1. ~~La importación de tarjeta (PDF) no es idempotente.~~ **Resuelto.** `ImportFileProcessingSink` consulta los `ExternalId` existentes contra la base (una sola query batch) antes de insertar, con el mismo patrón que `BbvaBankStatementImporter.PersistAsync`. Reimportar el mismo resumen ya no duplica movimientos ni pierde las filas nuevas de un archivo parcialmente repetido. Ver `docs/Architecture/EstadoMVP.md` §3, bug #2.
2. ~~El diagnóstico de líneas descartadas/fallidas se calcula pero se descarta.~~ **Resuelto.** `PdfStatementParserBase.ParseLines` devuelve `SkippedLines`/`Diagnostics` reales, que llegan completos a `FileParseResult` — ya no están hardcodeados en `0`/`[]`.
3. **Riesgo de ruteo incorrecto entre parsers de PDF (sigue activo).** `FileParserFactory` prueba los parsers en orden de registro en DI y usa el primero cuyo fingerprint matchea. El fingerprint de `BbvaVisaStatementParser` (`\bBBVA\b`) es lo bastante amplio para capturar también un extracto BBVA Mastercard, que se registra después — candidato concreto para el síntoma de "líneas del PDF que no se guardan". Es el ítem I7 pendiente de Épica I (sección 7).
4. **`FinancialAccount` existe (Épica J) y ya se asigna automáticamente cuando el archivo trae un número de cuenta resoluble sin ambigüedad — pero no siempre.** `BbvaBankStatementImporter.AssignFinancialAccountAsync` (banco) e `ImportFileProcessingSink.ResolveFinancialAccountIdAsync` (tarjeta débito/crédito, pipeline catch-all) cruzan el número de cuenta extraído del archivo contra `FinancialAccount.AccountNumber`; si hay exactamente un match activo, lo asignan solos. Si el archivo no trae número de cuenta, o hay más de una cuenta activa con el mismo número, queda `null` y se asigna a mano desde `movements.html` (`PUT /api/{bank-statements|transactions}/{id}/financial-account`) — ese es el caso restante, no la regla general.
5. ~~La UI de clasificación sigue organizada alrededor del matching contra Excel como flujo principal.~~ **Resuelto (PR-L1 a PR-L4).** `movements.html` clasifica banco/tarjeta directamente, sin ningún flujo de matching contra Excel; `group-reconciliation.html` y el backend que lo sostenía se retiraron del código. Ver `docs/UX/ClassificationUX.md`.
6. ~~No hay visibilidad de cobertura de clasificación.~~ **Resuelto (Épica L).** `GET` de cobertura de clasificación (`ClassificationCoverage`, con `PendingMovements` por conteo directo en base) + indicador visual de 3 estados en `dashboard.html` + badge `#navPending` completado desde `GET /api/movements` (cuenta `status === 'Pending'` del período actual).
7. ~~La distinción entre consumo de tarjeta y pago de resumen ya está resuelta en el dominio pero no está guiada en la UI.~~ **Resuelto (parcial).** Al seleccionar una contraparte `OwnCard` en el formulario de clasificación, `movements.html` precarga `FinancialImpact=DebtPayment` y muestra un hint (`#cImpactSuggestedHint`) — ver ADR-003, sección "Actualización". Sigue siendo una precarga puntual para ese caso, no una guía general del campo `FinancialImpact` para el resto de los `MovementType` (eso es Épica N, todavía pendiente).

---

## 7. Roadmap por épicas

Continúa la numeración de letra usada en Review & Classification Engine v2 (que llegó hasta D).

| Épica | Objetivo | Estado |
|---|---|---|
| **I** — Confiabilidad de importación | Idempotencia real y trazabilidad de errores para tarjeta, al nivel que ya tienen banco/Excel. | 🔄 Parcial — idempotencia (`ExternalId` + consulta batch) y diagnóstico de líneas descartadas ya entregados en el pipeline catch-all; falta I7 (fingerprint Visa/Mastercard, sección 6) y la fragilidad posicional del `ExternalId` de `BankStatement`. Ver `docs/Epics/EpicaI-Importacion.md` |
| **J** — Modelo de Cuentas Financieras | Introducir `FinancialAccount` (Bank/Card/Investment/Cash) como entidad explícita. | ✅ Terminada (núcleo) — entidad, CRUD completo (`accounts.html`) y asignación automática al importar cuando el número de cuenta resuelve sin ambigüedad (sección 6, ítem 4) |
| **K** — Nueva UX de clasificación | Reemplazar `group-reconciliation.html` por una pantalla centrada en clasificar. | ✅ Completada (PR-L1 a PR-L5). Ver `docs/UX/ClassificationUX.md` |
| **L** — Visibilidad de cobertura | Indicador de cuánto del período está clasificado vs. pendiente, en dashboard y nav. | ✅ Terminada — endpoint de cobertura, indicador visual en `dashboard.html` y badge `#navPending` completado (sección 6, ítem 6) |
| **M** — Cuentas de inversión | Adelanto acotado de Fase 4 (README): habilitar `FinancialAccount.Type=Investment` y transferencias hacia/desde ella. El modelo completo de movimientos internos de inversión (dividendos, compra/venta de activos) queda fuera de este roadmap y requiere su propio documento. | 📋 Planificada — no iniciada |
| **N** — Simplificación del formulario de clasificación | Derivar `FinancialImpact` por defecto para los `MovementType` no ambiguos, sin eliminar el campo. | 📋 Planificada — no iniciada. Existe una precarga puntual y más acotada para el caso `OwnCard`→`DebtPayment` (sección 6, ítem 7), que no reemplaza esta épica; `docs/Architecture/SimplificacionModeloClasificacion.md` es el insumo consolidado para cuando se retome. |
| **O** — Importación Manual e Historial | Botón "Importar" desde la UI, reutilizando el mismo motor de importación que ya usa el Worker (`IFileImportRouter` → Handlers → Parsers → Importers, sin duplicarlo) + detección automática de cuenta financiera por `AccountNumber`. Tiene una decisión de arquitectura pendiente de resolver antes del primer PR. | ✅ Terminada — subida manual desde `imports.html` sobre el mismo motor del Worker. La documentación propia de la épica quedó desactualizada (no la funcionalidad) — ver `docs/Epics/EpicaO-ImportacionManual.md` |

Detalle PR-por-PR de cada épica: `docs/Epics/` (por ahora existen las de las Épicas I y O; las siguientes se documentan a medida que se empiezan).

---

## 8. Dependencias entre épicas

```
I (Importación)  ──┐
                    │
J (Cuentas)  ───────┼──▶ M (Inversiones)   [M depende de J]
                    │
K (UX)  ◀───────────┘   [K reutiliza los mismos endpoints de C1-C4, no depende de I/J]
   │
   └──▶ la pre-carga de `FinancialImpact=DebtPayment` al seleccionar una
        contraparte `OwnCard` (ya implementada — no confundir con el PR K4,
        que es el de sugerencias del motor `ReviewEngine` en `movements.html`,
        ver `docs/UX/ClassificationUX.md`, sección "2. Movimientos") resuelve
        en la práctica, para ese caso puntual, el problema descripto en
        ADR-003. El resto de la guía de UX que Épica N contempla en general
        (derivar `FinancialImpact` por `MovementType`) sigue pendiente.

L (Cobertura)  — independiente, terminada (endpoint + indicador + badge).

N (Simplificación de formulario) — depende de K (mismo formulario que reemplaza K2).

O (Importación Manual)  — independiente en el diseño (reutiliza IFileImportRouter
   sin modificarlo), pero conviene hacerla después de I: importar manualmente
   sobre un pipeline con los bugs de idempotencia de I sin corregir reproduciría
   esos mismos bugs con más frecuencia (uso manual = más corridas que el watcher
   automático).
```

No hay dependencia dura entre I y J/K — pueden desarrollarse en paralelo si hace falta. J es prerrequisito de M. N es un ajuste sobre el formulario que entrega K, por lo que debe ir después. O no depende de J/K/L/M/N.

---

## 9. Orden recomendado de implementación

**Actualización (PATCH-029):** de este orden original, I (parcial), J, K, L y O ya tienen trabajo entregado — ver estado real por épica en la sección 7. Queda vigente el orden relativo entre lo que sigue pendiente:

1. ~~**Épica I**~~ — parcial. Idempotencia y trazabilidad del pipeline catch-all ya resueltas (sección 6); queda el fingerprint Visa/Mastercard (I7), que no depende de ninguna otra épica pendiente.
2. ~~**Épica J**~~ — núcleo terminado (entidad + CRUD + asignación automática cuando el número de cuenta resuelve sin ambigüedad).
3. ~~**Épica K**~~ — completada (PR-L1 a PR-L5).
4. ~~**Épica L**~~ — completada (endpoint + indicador de dashboard + badge de nav).
5. ~~**Épica O**~~ — completada (subida manual reutilizando el motor del Worker).
6. **Épica N** — siguiente pendiente. Ajusta el mismo formulario que entrega K, ya completada, así que no tiene que esperar nada más.
7. **Épica M** — al final — depende de J, que ya está terminada (núcleo), pero sigue siendo la de mayor incertidumbre de producto (todavía no tiene un documento de diseño propio).

---

*Última actualización: PATCH-029, verificación de estado real de las épicas I-O contra el código (`accounts.html`, `ImportFileProcessingSink`, `dashboard.html`, `imports.html`, `PdfStatementParserBase`) y `docs/PROJECT_STATUS.md`. Fuente: `docs/Architecture/Architecture.md`, `docs/Epics/EpicaI-Importacion.md`, `docs/UX/ClassificationUX.md`, `docs/Decisions/ADR-001` a `ADR-005`.*
