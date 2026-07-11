# ADR-004 — Introducir `FinancialAccount` antes del módulo de inversiones

**Estado:** Aceptado (planificación; ninguna de las dos entidades está implementada).

## Contexto

Hoy ninguna entidad persistida (`Transaction`, `BankStatement`) tiene noción explícita de "cuenta financiera de origen" — no hay forma de responder, a nivel de datos, "¿de qué cuenta o tarjeta vino este movimiento?". El roadmap incluye dos épocas relacionadas con cuentas: Épica J (`FinancialAccount` — cuenta financiera genérica: `Bank`/`Card`/`Investment`/`Cash`) y Épica M (`InvestmentAccount` — cuenta de inversión con saldo/valuación y movimientos internos propios). `Counterparty.CounterpartyType` ya tiene el valor `Investment = 9` reservado para este caso.

## Problema

Es posible plantear ambas épocas en paralelo o incluso invertir el orden (construir inversiones primero, usando `Counterparty` con tipo `Investment` como sustituto de cuenta). Eso generaría un módulo de inversiones apoyado en una entidad (`Counterparty`) que no está diseñada para representar saldo, valuación ni movimientos internos — sería necesario re-modelarlo apenas se implemente `FinancialAccount`, duplicando trabajo.

## Decisión tomada

`FinancialAccount` (Épica J) se implementa antes que `InvestmentAccount` (Épica M). `InvestmentAccount` se modela como una extensión de `FinancialAccount` (`Type = Investment`), no como una entidad independiente ni como una extensión de `Counterparty`. El orden recomendado en `docs/RoadMaps/FinancialMcp-vNext.md` §9 refleja esto explícitamente (J antes que M).

## Consecuencias

* La Épica M no puede empezar hasta que `FinancialAccount` exista — es una dependencia dura, no una preferencia de orden.
* `Counterparty.CounterpartyType.Investment` sigue usándose para el caso "el movimiento es una transferencia hacia/desde una cuenta de inversión externa" (una transferencia bancaria común, `FinancialImpact = InternalMovement`), no para modelar la cuenta de inversión en sí — esa distinción evita que `Counterparty` cargue con responsabilidades que le corresponden a `FinancialAccount`/`InvestmentAccount`.
* Los movimientos internos de una cuenta de inversión (dividendos, compra/venta de activos) no son `ClassifiedMovement` (ver ADR-001) — viven exclusivamente bajo `InvestmentAccount`, que solo puede construirse una vez que `FinancialAccount` da la base común de tipo de cuenta.
