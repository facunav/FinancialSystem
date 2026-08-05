# Indicador visual de cobertura de clasificación — Dashboard

Documento operativo del PATCH-020 (épica "Consistencia y confianza del producto").
Cubre exclusivamente la tarjeta nueva del Dashboard (`dashboard.html`) que consume
`GET /api/metrics/classification-coverage` (backend del PATCH-019, Patches 0071/0072,
sin cambios en este patch). El objetivo: que el usuario sepa de un vistazo qué tan
confiables son las métricas que está viendo, sin tener que ir a buscar el dato.

## Qué se agregó

Una tarjeta nueva ("Cobertura de clasificación"), en su propia sección
("Confiabilidad de los datos"), entre el resumen del mes (KPIs) y la sección de
Distribución y comparación. No se modificó ninguna tarjeta existente ni la
navegación del Dashboard.

Para el período que esté viendo el usuario (mismo `currentYear`/`currentMonth` que ya
gobierna el resto del Dashboard — la tarjeta se actualiza al navegar entre meses),
muestra:

* Un badge de estado (Alta/Media/Baja) con color.
* El porcentaje de cobertura, tal cual lo devuelve el endpoint.
* Una barra de progreso con el mismo ancho que el porcentaje.
* El detalle: cantidad de movimientos clasificados, pendientes y totales.

**Sin recálculo en el frontend**: los cuatro valores (`totalMovements`,
`classifiedMovements`, `pendingMovements`, `coveragePercentage`) se muestran tal cual
llegan del backend — el frontend solo decide en qué "balde" visual (alta/media/baja)
cae el porcentaje ya calculado, nunca recalcula el porcentaje en sí.

## Umbrales (centralizados)

Un único objeto en `dashboard.html`, `COVERAGE_THRESHOLDS`, es la única fuente de
estos dos números — ningún otro lugar del código los repite:

```js
const COVERAGE_THRESHOLDS = { HIGH: 80, MEDIUM: 50 };
```

* `>= 80%` → **Alta** (verde)
* `>= 50%` y `< 80%` → **Media** (ámbar)
* `< 50%` → **Baja** (rojo)

Para cambiar los umbrales alcanza con editar ese único objeto.

## Período sin movimientos

Si `totalMovements` es `0`, la tarjeta no muestra una barra en 0% (que se leería como
"cobertura baja" cuando en realidad no hay nada que cubrir) — muestra explícitamente
"Sin movimientos en este período — no hay cobertura para calcular.".

## Si el endpoint falla

`loadClassificationCoverage()` se llama de forma independiente del resto de la carga
del Dashboard (mismo criterio ya establecido por `loadPlanningSummary()` para la
tarjeta de Planificación Mensual): tiene su propio `try/catch`, nunca se propaga al
`try/catch` principal de `loadDashboard()`. Si la request falla, solo esta tarjeta
muestra "⚠ No se pudo cargar la cobertura de clasificación." — el resto del Dashboard
(KPIs, categorías, tendencia, comparación, Planificación Mensual) sigue funcionando
con total normalidad.

## Tests

Este repositorio no tiene infraestructura de testing para el frontend (`wwwroot/` son
páginas HTML autocontenidas, sin build step, sin framework de componentes ni test
runner de JS instalado — confirmado en `docs/PROJECT_STATUS.md`: *"frontend (7-8
páginas HTML sin infraestructura compartida)"*, y no hay `package.json` en el
repositorio). Siguiendo la instrucción explícita del patch de no crear infraestructura
de testing nueva solo para esta tarjeta, no se agregaron tests automatizados.

Verificación manual (ver también "Acciones manuales" del patch): abrir
`dashboard.html`, confirmar que la tarjeta muestra el estado correcto para un período
con cobertura alta/media/baja, para un período sin movimientos, y que una falla del
endpoint (ej. desconectando la red momentáneamente) no rompe el resto de la pantalla.

## Qué NO cambió

* El cálculo de cobertura ni el endpoint (`FinancialMetricsService`,
  `MetricsEndpoints`, `ClassificationCoverageDto`) — ver
  `docs/Architecture/CoberturaDeClasificacion.md`.
* Ninguna otra tarjeta del Dashboard (KPIs, categorías, tendencia, comparación,
  Planificación Mensual, placeholders).
* La navegación del Dashboard — no se agregó ningún link ni sección nueva de
  navegación, solo una tarjeta de contenido.
* Auditoría, Planning, Importaciones, Investigaciones, Clasificación, el modelo de
  dominio y la autenticación de la UI.
