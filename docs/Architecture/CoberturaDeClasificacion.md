# Cobertura de clasificación — GET /api/metrics/classification-coverage

Documento operativo del Patch 0068 (PATCH-019, épica "Consistencia y confianza del
producto" — Épica L "Visibilidad de cobertura" de `docs/RoadMaps/FinancialMcp-vNext.md`).
Cubre exclusivamente esta métrica y su endpoint. Backend únicamente: este patch no
integra el indicador al Dashboard ni a ninguna pantalla (esa integración queda para
un patch futuro, cuando se retome la Épica L en el frontend).

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

Este es literalmente el mismo criterio que `IMovementsQueryService` (consumido hoy por
la pantalla Movimientos y por `AuditEndpoints`) usa para distinguir pendientes de
clasificados: `MovementView.Status` no-nulo significa clasificado; nulo significa
pendiente.

## Cómo se calcula

```
GET /api/metrics/classification-coverage?year=2026&month=6
GET /api/metrics/classification-coverage?from=2026-01-01&to=2026-06-30
```

(Mismo estilo de parámetros que `GET /api/metrics/summary`.)

1. Se obtiene la lista completa de movimientos del período vía
   `IMovementsQueryService.GetAsync` (misma fuente que la pantalla Movimientos) — una
   sola consulta, sin recalcular nada por separado.
2. `TotalMovements` = cantidad total de movimientos devueltos (pendientes + clasificados).
3. `ClassifiedMovements` = cantidad de esos movimientos con `Status` no nulo.
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
  "coveragePercentage": 71.4
}
```

## Qué NO cambió

* Ningún endpoint de métricas existente (`/summary`, `/by-category`, `/monthly-trend`,
  `/compare`) — mismas firmas, mismo comportamiento.
* El modelo de clasificación, `ClassifiedMovement`, ni la lógica de
  `IMovementsQueryService`.
* El grupo `/api/metrics` sigue sin `RequireAuthorization()` (nunca lo tuvo — fuera de
  alcance de este patch, que no toca autenticación).
* Dashboard/frontend: el endpoint existe y devuelve datos correctos, pero ninguna
  pantalla lo consume todavía.
