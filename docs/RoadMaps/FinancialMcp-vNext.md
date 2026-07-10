# FinancialMcp — vNext

> **Reemplaza la versión anterior de este archivo** (commit `324eadf`, "Se agrego nuevo md"), que describía un modelo de conciliación (`ReconciledExpense`, `ReconciliationOrchestrator`, `ProcessedExpense`) retirado del código durante el refactor v2.0 (commit `e38ace2`) y explícitamente **no** reintroducido por el ADR de Review & Classification Engine v2 (hoy en `docs/Archive/ReviewClassificationEnginev2ADR.md`). Ese contenido quedó desalineado con el código real — ninguna de esas clases existe hoy en el repositorio. Esta versión reemplaza esa por una alineada con el estado real de `origin/master`.
>
> Este documento cumple, de acá en adelante, el rol que tenía `FinancialMcp-Roadmap.md` (eliminado del repositorio antes de esta ronda de documentación): **es la fuente de verdad del proyecto**. Antes de implementar una funcionalidad nueva, leerlo entero. Cualquier decisión de arquitectura, modelo de datos o flujo debe alinearse con lo que dice acá.

---

## 1. Estado actual del sistema

**Completado — Review & Classification Engine v2 (Épicas A–D).** El motor de revisión y clasificación de movimientos financieros fue reconstruido de punta a punta: carga de movimientos crudos, motor de sugerencias de matching, comandos de clasificación manual y confirmación de match, y UI actualizada al nuevo contrato. Detalle completo, con ADRs de diseño y backlog original, en `docs/Archive/ReviewClassificationEnginev2ADR.md` y `docs/Archive/ReviewClassificationBacklog.md`.

Piezas activas en el código hoy:

* `ClassifiedMovement` / `ClassifiedMovementItem` — única fuente de verdad para métricas y MCP.
* `IMovementLoader` / `IMatchScorer` (+ 4 `IMatchingRule`) / `ISuspicionDetector` / `IReviewEngine` — motor de sugerencias, sin persistencia intermedia.
* `ClassifyMovementCommand`, `ConfirmMatchCommand`, `DiscardLegacyCandidatesCommand`, `RestoreLegacyCandidatesCommand`, `GetUnclassifiedMovementsQuery` — los 5 casos de uso de escritura/lectura, todos con endpoint bajo `/api/movement-review/*`.
* `group-reconciliation.html` — UI de clasificación, ya apuntando al contrato nuevo (D1) y con las 4 dimensiones de clasificación completas (D2).
* `FinancialMetricsService` + 4 herramientas MCP (`GetMonthlySummary`, `GetExpensesByCategory`, `GetMonthlyTrend`, `CompareWithPreviousMonth`) — sin cambios, funcionando sobre `ClassifiedMovement`.

**En planificación — Épicas I–N (este documento).** Nada de lo que describen las secciones 7-9 está implementado todavía. Antes de escribir código para cualquiera de ellas, ver `docs/Epics/` para el detalle de la que corresponda.

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
   Importación            ← Épica I (planificada)
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
   Dashboard               ← Épica L visibilidad de cobertura (planificada)
        │
        ▼
   MCP                     (ya implementado, funciona automáticamente)
```

* **Sources → Importación:** hoy tres pipelines independientes (banco XLS, tarjeta PDF, Excel legacy), con niveles de confiabilidad distintos — ver §6 y `docs/Epics/EpicaI-Importacion.md`.
* **Importación → Normalización:** `ITransactionNormalizer` limpia descripción, resuelve moneda y normaliza fecha antes de persistir. Sin cambios planificados.
* **Normalización → Review:** `IMovementLoader` adapta `Transaction`/`BankStatement`/`LegacyImportedExpense` a `FinancialMovement`, excluyendo lo ya clasificado. `IReviewEngine` arma sugerencias de matching y detecta sospechosos.
	Sin cambios planificados al motor en sí — Épica K solo cambia la UI que lo consume.
* **Review → Classification:** el usuario clasifica manualmente (`ClassifyMovementCommand`) o confirma una sugerencia (`ConfirmMatchCommand`), escribiendo `ClassifiedMovement` con las 4 dimensiones (ADR-001). Épica K simplifica cómo se llega a esa decisión (Contraparte con valores por defecto, Épica N), no el modelo en sí.
* **Classification → Dashboard:** `FinancialMetricsService` agrega por `FinancialImpact`/`Category`. Épica L agrega visibilidad de cuánto del período **no** está clasificado todavía, algo que hoy no existe.
* **Dashboard → MCP:** sin cambios — las herramientas MCP ya leen `ClassifiedMovement` automáticamente.

---

## 5. Qué NO se debe cambiar

Cosas que ya están bien diseñadas y no deben tocarse en las épicas siguientes salvo que aparezca evidencia concreta de un problema:

* El modelo de clasificación de 4 dimensiones (`Category`, `FinancialImpact`, `MovementType`, `Counterparty`) — ver ADR-001.
* `ClassifiedMovement`/`ClassifiedMovementItem` como única fuente de verdad para métricas y MCP.
* El patrón de idempotencia de `BbvaBankStatementImporter`/`LegacyImportedExpense` (`ExternalId` + índice único + consulta previa) — es el patrón a **copiar** hacia tarjeta (Épica I), no a rediseñar.
* La arquitectura Command/Handler + endpoints delgados de Review & Classification Engine v2.
* El motor de sugerencias (`IMatchScorer`, `ISuspicionDetector`, `IReviewEngine`) — Épica K cambia la UI que lo consume, no el motor.
* Excel como mecanismo de migración histórica — no se elimina del sistema, se saca del centro de la UX (ADR-002).

---

## 6. Problemas reales existentes

Detectados por revisión directa del código (no hipótesis) durante la evaluación funcional posterior al cierre de Review & Classification Engine v2:

1. **La importación de tarjeta (PDF) no es idempotente.** Reimportar el mismo resumen duplica cada movimiento — no hay `ExternalId`, no hay índice único, el único "dedup" existente es un `HashSet` en memoria que solo compara líneas dentro del mismo archivo que se está procesando, nunca contra la base de datos. Detalle en `docs/Epics/EpicaI-Importacion.md`.
2. **El diagnóstico de líneas descartadas/fallidas se calcula pero se descarta.** `PdfStatementParserBase` cuenta líneas ignoradas y fallidas internamente, pero el método público que expone `IFileParser` devuelve esos contadores hardcodeados en `0`/`[]` — la información nunca llega a `FileParseResult`, solo a un log que además usa niveles (`Trace`) que la configuración por defecto filtra.
3. **Riesgo de ruteo incorrecto entre parsers de PDF.** `FileParserFactory` prueba los parsers en orden de registro en DI y usa el primero cuyo fingerprint matchea. El fingerprint de `BbvaVisaStatementParser` (`\bBBVA\b`) es lo bastante amplio para capturar también un extracto BBVA Mastercard, que se registra después — candidato concreto para el síntoma de "líneas del PDF que no se guardan".
4. **`FinancialAccount` existe (Épica J) pero nada la asigna automáticamente.** `BankStatement.FinancialAccountId`/`Transaction.FinancialAccountId` son FK opcionales que ningún importador completa — quedan `null` hasta que un usuario las asigna a mano desde `movements.html` (`PUT /api/{bank-statements|transactions}/{id}/financial-account`). Para banco el dato para matchear ya existe (`BankStatement.BankName`+`AccountNumber`, extraídos del import) pero no se cruza contra `FinancialAccount`; para tarjeta (`Transaction`) no hay ningún campo identificador de origen todavía — hace falta extender el parser de PDF antes de poder resolver esto automáticamente. Pendiente de un PR futuro de Épica J.
5. **La UI de clasificación sigue organizada alrededor del matching contra Excel** como flujo principal, cuando Excel es solo un mecanismo de migración temporal (ADR-002). Ver `docs/UX/ClassificationUX.md`.
6. **No hay visibilidad de cobertura de clasificación.** El dashboard puede calcular un resumen del mes sobre una fracción minoritaria de los movimientos reales sin ninguna advertencia. El badge de pendientes en el nav (`navPending`) existe en el HTML pero ningún script lo completa.
7. **La distinción entre consumo de tarjeta y pago de resumen ya está resuelta en el dominio** (`FinancialImpact.DebtPayment`, ver ADR-003) **pero no está guiada en la UI** — es fácil clasificar por error un pago de resumen como gasto porque nada en el formulario actual orienta hacia la opción correcta.

---

## 7. Roadmap por épicas

Continúa la numeración de letra usada en Review & Classification Engine v2 (que llegó hasta D).

| Épica | Objetivo | Estado |
|---|---|---|
| **I** — Confiabilidad de importación | Idempotencia real y trazabilidad de errores para tarjeta, al nivel que ya tienen banco/Excel. | 📋 Planificada — ver `docs/Epics/EpicaI-Importacion.md` |
| **J** — Modelo de Cuentas Financieras | Introducir `FinancialAccount` (Bank/Card/Investment/Cash) como entidad explícita. | 📋 Planificada |
| **K** — Nueva UX de clasificación | Reemplazar `group-reconciliation.html` por una pantalla centrada en clasificar, con Excel como acción secundaria. | 📋 Planificada — ver `docs/UX/ClassificationUX.md` |
| **L** — Visibilidad de cobertura | Indicador de cuánto del período está clasificado vs. pendiente, en dashboard y nav. | 📋 Planificada |
| **M** — Cuentas de inversión | Adelanto acotado de Fase 4 (README): habilitar `FinancialAccount.Type=Investment` y transferencias hacia/desde ella. El modelo completo de movimientos internos de inversión (dividendos, compra/venta de activos) queda fuera de este roadmap y requiere su propio documento. | 📋 Planificada |
| **N** — Simplificación del formulario de clasificación | Derivar `FinancialImpact` por defecto para los `MovementType` no ambiguos, sin eliminar el campo. | 📋 Planificada |

Detalle PR-por-PR de cada épica: `docs/Epics/` (por ahora solo existe el de la Épica I; las siguientes se documentan a medida que se empiezan).

---

## 8. Dependencias entre épicas

```
I (Importación)  ──┐
                    │
J (Cuentas)  ───────┼──▶ M (Inversiones)   [M depende de J]
                    │
K (UX)  ◀───────────┘   [K reutiliza los mismos endpoints de C1-C4, no depende de I/J]
   │
   └──▶ la pre-carga de valores por defecto por Contraparte (sin PR asignado
        todavía — no confundir con el PR K4 ya implementado, que es el de
        sugerencias del motor `ReviewEngine` en `movements.html`, ver
        `docs/UX/ClassificationUX.md`, sección "2. Movimientos") resuelve en
        la práctica el problema descripto en ADR-003 — sin ella, ADR-003
        sigue siendo correcto en el dominio pero no se percibe así al usar
        el producto.

L (Cobertura)  — independiente, puede ir en paralelo con cualquiera.

N (Simplificación de formulario) — depende de K (mismo formulario que reemplaza K2).
```

No hay dependencia dura entre I y J/K — pueden desarrollarse en paralelo si hace falta. J es prerrequisito de M. N es un ajuste sobre el formulario que entrega K, por lo que debe ir después.

---

## 9. Orden recomendado de implementación

1. **Épica I** — primero. Es la que tiene bugs concretos y activos (duplicados reales, líneas perdidas reales) — corregirla no depende de ninguna otra épica y desbloquea confianza en los datos antes de construir UI nueva sobre ellos.
2. **Épica J** — segundo. Prerrequisito de M, y de bajo riesgo (entidad nueva, sin tocar lo existente hasta el último PR).
3. **Épica K** — tercero. Es el cambio de mayor superficie visible; conviene hacerlo con la importación ya confiable (I) para no construir una UX nueva sobre datos con duplicados.
4. **Épica L** — en paralelo con K o inmediatamente después — es pequeña y no depende de K para el endpoint, aunque su UI más simple es agregar el badge una vez exista la pantalla de K.
5. **Épica N** — inmediatamente después de K (ajusta el mismo formulario).
6. **Épica M** — al final — depende de J y es la de mayor incertidumbre de producto (todavía no tiene un documento de diseño propio).

---

*Última actualización: revisión funcional posterior al cierre de Review & Classification Engine v2. Fuente: `docs/Architecture/Architecture.md`, `docs/Epics/EpicaI-Importacion.md`, `docs/UX/ClassificationUX.md`, `docs/Decisions/ADR-001` a `ADR-005`.*
