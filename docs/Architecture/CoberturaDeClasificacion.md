# Cobertura de clasificación — GET /api/metrics/classification-coverage

Documento operativo del PATCH-019 (épica "Consistencia y confianza del producto" —
Épica L "Visibilidad de cobertura" de `docs/RoadMaps/FinancialMcp-vNext.md`). Cubre
exclusivamente esta métrica y su endpoint. Backend únicamente: no integra el indicador
al Dashboard ni a ninguna pantalla (esa integración queda para un patch futuro, cuando
se retome la Épica L en el frontend).

Implementado en dos pasos:
* **Patch 0071**: endpoint, modelo y criterio de clasificación originales.
* **Patch 0072**: agrega `PendingMovements` como campo explícito de la respuesta y
  reemplaza el cálculo (que traía a memoria el período completo vía
  `IMovementsQueryService.GetAsync`) por tres consultas `COUNT` directas contra la
  base — mismo resultado, sin materializar movimientos.

## Qué representa el porcentaje

Responde una pregunta que ninguna métrica existente contestaba: **de todos los
movimientos reales (banco + tarjeta) que ocurrieron en un período, ¿qué porcentaje ya
fue clasificado?** — a diferencia de `GetPeriodSummaryAsync`, que solo describe lo YA
clasificado sin decir nada sobre cuánto queda pendiente (riesgo #4 de
`docs/PROJECT_STATUS.md`: "el Dashboard puede mostrar un resumen calculado sobre una
fracción minoritaria de los movimientos reales del período, sin ningún indicador de
cobertura").

## Qué condiciones debe cumplir un movimiento para ser considerado clasificado

**Exactamente el mismo criterio que ya usa el resto del sistema — no se introduce una
definición alternativa:**

Un movimiento de banco o tarjeta está clasificado si y solo si existe un
`ClassifiedMovement`/`ClassifiedMovementItem` que lo referencia. `ClassifiedMovement`
exige `MovementType`, `FinancialImpact` y `CategoryId` obligatorios por diseño de
dominio (ver `ClassifiedMovement.cs`, sección "CLASIFICACIÓN OBLIGATORIA") — no existen
estados intermedios ni un "parcialmente clasificado": si la fila existe, las
dimensiones obligatorias ya están completas. `CounterpartyId` es opcional y no afecta
si un movimiento cuenta como clasificado (tampoco lo hace en ningún otro lugar del
sistema).

Este es el mismo criterio que usan `MovementLoader` (pendientes: `BankStatement`/
`Transaction` del período sin `ClassifiedMovementItem` que los referencie) y
`MovementsQueryService` (clasificados: `MovementView.Status` no nulo) para la pantalla
Movimientos — ninguno de los dos cambió.

## Cómo se calcula

```
GET /api/metrics/classification-coverage?year=2026&month=6
GET /api/metrics/classification-coverage?from=2026-01-01&to=2026-06-30
```

(Mismo estilo de parámetros que `GET /api/metrics/summary`.)

`FinancialMetricsService.GetClassificationCoverageAsync` resuelve todo con 3 consultas
`COUNT` secuenciales contra la base (secuenciales porque comparten el mismo
`DbContext`, que no admite operaciones concurrentes sobre la misma instancia) — en
ningún momento se carga un movimiento completo a memoria:

1. `PendingMovements` = `BankStatement`s del período sin `ClassifiedMovementItem` que
   los referencie, más `Transaction`s del período en la misma condición (2 `COUNT`).
2. `ClassifiedMovements` = `ClassifiedMovementItem`s (de banco o tarjeta) cuyo
   `OriginalDate` cae en el período (1 `COUNT`).
3. `TotalMovements` = `ClassifiedMovements` + `PendingMovements`.
4. `CoveragePercentage` = `ClassifiedMovements / TotalMovements * 100`, redondeado a 1
   decimal (mismo criterio de redondeo que `SavingsRate`/`PercentageOfTotal` en el
   resto del módulo de métricas). `0` si `TotalMovements` es `0` (período sin
   movimientos) — nunca una división por cero.

Determinístico: para el mismo período y el mismo estado de la base de datos, siempre
devuelve el mismo resultado — no depende de la hora de ejecución ni de ningún estado
externo.

## Respuesta

```json
{
  "from": "2026-06-01",
  "to": "2026-06-30",
  "totalMovements": 42,
  "classifiedMovements": 30,
  "pendingMovements": 12,
  "coveragePercentage": 71.4
}
```

## Qué NO cambió

* Ningún endpoint de métricas existente (`/summary`, `/by-category`, `/monthly-trend`,
  `/compare`) — mismas firmas, mismo comportamiento.
* El modelo de clasificación, `ClassifiedMovement`, ni la lógica de
  `IMovementsQueryService`/`MovementLoader` (siguen siendo la fuente de verdad para la
  pantalla Movimientos y para Auditoría — este endpoint ya no depende de
  `IMovementsQueryService`, pero no lo modifica).
* El grupo `/api/metrics` sigue sin `RequireAuthorization()` (nunca lo tuvo — fuera de
  alcance de este patch, que no toca autenticación).
* Dashboard/frontend: el endpoint existe y devuelve datos correctos, pero ninguna
  pantalla lo consume todavía.
