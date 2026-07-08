# ADR-003 — Separar consumo de tarjeta y pago de resumen usando `FinancialImpact.DebtPayment`

**Estado:** Aceptado (modelo de dominio ya implementado; UX de guía todavía pendiente — ver Consecuencias).

## Contexto

Un mismo gasto con tarjeta de crédito aparece dos veces en las fuentes de datos: una vez como consumo individual en el resumen de tarjeta (`Transaction`, vía PDF) y otra vez como el pago total del resumen en el extracto bancario (`BankStatement`, vía XLS, débito por el monto total del resumen). Si ambos se clasifican como `FinancialImpact.Expense`, el gasto se cuenta dos veces en las métricas del MCP.

`FinancialImpact` ya define `DebtPayment = 4`: "Pago de una deuda ya registrada como gasto en otro momento. No debe contabilizarse como gasto adicional para evitar duplicación. Ejemplos: pago del resumen de tarjeta de crédito, cuota de préstamo." — y tanto `FinancialMetricsService` como `FinancialTools` filtran estrictamente por `FinancialImpact == Expense`, por lo que `DebtPayment` ya queda excluido de las métricas de gasto sin código adicional. `Counterparty.CounterpartyType` también ya incluye `OwnCard = 8` ("tarjeta de crédito propia, para registrar pagos de resumen").

## Problema

La revisión funcional detectó "doble conteo de gastos de tarjeta" como síntoma reportado por el usuario. La investigación contra el código mostró que **no es una brecha del modelo de dominio** — la solución de datos ya existe (`DebtPayment`, `OwnCard`) y ya funciona si se usa correctamente. El problema real es que nada en la UI guía al usuario a clasificar el pago del resumen bancario como `DebtPayment` con `Counterparty` de tipo `OwnCard`, en vez de como `Expense` — el mecanismo de sugerencia por defecto de `Counterparty` (`DefaultFinancialImpact`) existe en el dominio pero no tiene wiring en ninguna pantalla todavía.

## Decisión tomada

Se confirma `FinancialImpact.DebtPayment` + `Counterparty` de tipo `OwnCard` como el mecanismo estándar para separar consumo de tarjeta (clasificado como `Expense` en su propia categoría, vía `Transaction`) del pago del resumen (clasificado como `DebtPayment`, vía `BankStatement`). No se introduce un mecanismo de dominio nuevo (por ejemplo, un flag `isCardPayment` o una quinta dimensión) — el modelo ya alcanza, falta la guía de UX.

## Consecuencias

* No hay cambio de código de dominio pendiente por este ADR — es un ADR de confirmación, no de construcción.
* La UX de clasificación (Épica K, PR K4, ver `docs/UX/ClassificationUX.md`) debe: (a) al elegir una `Counterparty` de tipo `OwnCard`, pre-cargar `FinancialImpact = DebtPayment` usando `DefaultFinancialImpact`; (b) idealmente, detectar heurísticamente un débito bancario cuyo monto coincide con el total de un resumen de tarjeta del mismo período y sugerir la clasificación — esto último queda fuera de alcance de K4 y se anota como posible refinamiento futuro, no como requisito de esta ADR.
* Mientras la UX no guíe esta clasificación, el riesgo de doble conteo persiste por error humano, no por defecto del sistema — cualquier reporte futuro de "doble conteo" debe primero verificar cómo se clasificó el pago del resumen antes de asumir un bug de dominio.
