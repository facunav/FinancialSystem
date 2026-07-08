# ADR-001 — Mantener las 4 dimensiones de clasificación

**Estado:** Aceptado (decisión ya implementada, este ADR documenta retroactivamente por qué se mantiene hacia adelante).

## Contexto

`ClassifiedMovement` clasifica cada movimiento financiero con 4 dimensiones independientes: `Category` (¿para qué se usó el dinero?), `FinancialImpact` (¿cómo afecta mi patrimonio? — `Expense`/`Income`/`InternalMovement`/`DebtPayment`), `MovementType` (¿qué clase de operación fue? — `Purchase`/`Transfer`/`Payment`/`Receipt`/`Fee`/`Interest`/`Refund`/`Adjustment`/`Other`) y `Counterparty` (¿con quién o qué se relaciona?, opcional). Es el modelo que quedó fijado al completar Review & Classification Engine v2 (Épicas A-D) y sobre el que corre todo el MCP (`FinancialMetricsService`, `FinancialTools`).

## Problema

Al planificar las épicas I-N (importación, cuentas financieras, nueva UX, inversiones) existe el riesgo de proponer una quinta dimensión, o de colapsar dimensiones existentes, cada vez que aparece un caso nuevo (por ejemplo: "¿la cuenta de origen es una dimensión de clasificación?", "¿inversión es un `MovementType` nuevo?"). Sin una decisión explícita, cada época corre el riesgo de reabrir el modelo de datos.

## Decisión tomada

Las 4 dimensiones se mantienen fijas. Ningún caso nuevo detectado durante la revisión funcional (cuentas financieras, importación, inversiones) requiere una quinta dimensión de clasificación:
* "De qué cuenta vino el movimiento" es un atributo de **origen/trazabilidad** (`FinancialAccount`, Épica J), no de clasificación — no cambia el significado financiero del movimiento.
* Los movimientos internos de una cuenta de inversión (dividendos, compra/venta de activos) **no son `ClassifiedMovement`** — viven en un dominio separado (`InvestmentAccount`, Épica M) con su propia semántica, precisamente para no forzar una quinta dimensión sobre el modelo existente.
* El caso "pago de resumen de tarjeta" ya tiene dimensión propia (`FinancialImpact.DebtPayment`, ver ADR-003) — no requiere una nueva.

## Consecuencias

* Toda funcionalidad nueva debe explicarse en términos de las 4 dimensiones existentes o de entidades adyacentes (`FinancialAccount`, `ImportBatch`, `InvestmentAccount`) — no se agregan campos de clasificación nuevos a `ClassifiedMovement` sin un ADR que reemplace este.
* `FinancialMetricsService` y los 4 `FinancialTools` del MCP siguen siendo válidos sin cambios de contrato mientras se implementan las Épicas I-N.
* Si en el futuro aparece un caso que genuinamente no encaja en las 4 dimensiones, corresponde abrir un nuevo ADR que reemplace explícitamente a este, no extender el modelo por acumulación silenciosa.
