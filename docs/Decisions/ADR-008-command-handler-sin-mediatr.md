# ADR-008 — Command/Handler invocado directamente, sin MediatR

**Estado:** Aceptado (PATCH-045, Epic V). Formaliza una decisión que ya existía de hecho en el código desde el motor de revisión (`ClassifyMovementCommand`/`Handler`, PR-L4) y se repitió en cada módulo nuevo desde entonces (Planning, Investigations, Audit) — este documento no cambia nada del código, solo pone por escrito una decisión que hasta ahora solo se podía inferir leyendo `docs/Architecture/Architecture.md` §1 y `docs/PROJECT_STATUS.md` §8/§11.

## Contexto

`docs/PROJECT_STATUS.md` (§8 "Arquitectura", §11 "Deuda técnica") viene señalando, desde antes de este patch, dos hechos sobre el código real:

1. El proyecto usa clases llamadas "Command" y "Handler" en varios módulos, pero **no usa MediatR** — no hay `ISender`, no hay `IRequestHandler<TRequest, TResponse>`, no hay pipeline behaviors, no hay descubrimiento de handlers por reflection. Cada handler es una clase concreta, inyectada por constructor donde se necesita, e invocada con una llamada directa a `Handle(command, ct)`.
2. El patrón Command/Handler **no se aplica de forma uniforme**: aproximadamente la mitad de los módulos lo usan (ver inventario abajo) y la otra mitad tiene su lógica de negocio (validaciones, unicidad, normalización) escrita directamente en los archivos de `Endpoints/`, sin pasar por Application.

Ninguno de estos dos hechos era, hasta este patch, una decisión formal — eran una observación de estado. Esta ADR formaliza la decisión #1 (qué patrón usar) y determina cómo convive con la realidad de la decisión #2 (la migración pendiente), sin ejecutar esa migración todavía.

**Verificado contra el código para esta ADR** (no se asumió que el inventario de `PROJECT_STATUS.md` siguiera exacto):

*Módulos que ya usan Command/Handler (`Application/<Módulo>/Commands/`):*
- `Application/Review/Commands` — `ClassifyMovementCommand`/`Handler` (el original, PR-L4).
- `Application/Planning/Commands` — 8 handlers (`CreatePlanningMonthHandler`, `AddPlanningItemHandler`, `EditPlanningItemHandler`, `DeletePlanningItemHandler`, `MarkPlanningItemAsPaidHandler`, `UnmarkPlanningItemAsPaidHandler`, `UpdateExpectedIncomeHandler`, `CopyPlanningMonthHandler`).
- `Application/Investigations/Commands` — 4 handlers (`CreateInvestigationHandler`, `LinkMovementToInvestigationHandler`, `AddInvestigationFindingHandler`, `UpdateInvestigationStatusHandler`).
- `Application/Audit/Commands` — `ReviewMovementsCommand`/`Handler`.

*Módulos con lógica de negocio directamente en `Endpoints/` (sin Command/Handler ni servicio de Application propio), confirmado por inspección de cada archivo:*
- `CategoryEndpoints.cs`, `CounterpartyEndpoints.cs` — CRUD con validaciones de unicidad/desactivación inline.
- `FinancialAccountEndpoints.cs` — CRUD con validaciones inline.
- `BankStatementEndpoints.cs`, `TransactionEndpoints.cs` — consultas y ediciones puntuales inline.

*Lecturas de solo consulta (ni Command/Handler, ni lógica de negocio material — no forman parte de esta decisión porque no hay una escritura que orquestar):* `MovementsEndpoints.cs`/`MetricsEndpoints.cs` delegan a servicios de Application (`IMovementsQueryService`, `IFinancialMetricsService`) que ya son la forma correcta de una lectura sin CQRS de por medio — ver "Queries" más abajo.

## Decisión tomada

### ¿FinancialMcp usa CQRS?

**Sí, en el sentido conceptual del término** (separar el camino de escritura del camino de lectura), **no en el sentido de un framework o una biblioteca**. Las escrituras que ya migraron pasan por un `Command` (record inmutable) + un `Handler` (clase con un único método `Handle`) que valida, decide y persiste. Las lecturas no usan "Query" como clase — se resuelven con servicios de solo lectura inyectables (`IMovementsQueryService`, `IPlanningQueryService`, `IFinancialMetricsService`, `IMovementLookupService`) que reciben parámetros primitivos y devuelven DTOs/records, sin mutar nada. Esto ya es una separación de responsabilidades CQRS-style; no hace falta una clase `Query` explícita para que lo sea — `docs/Archive/PRS1analisismotorsugerencias.md` ya llegó a esta misma conclusión al diseñar `IMovementsQueryService` ("servicio de lectura combinada... ni comandos ni queries CQRS-style").

### ¿Usa MediatR?

**No, y no se va a adoptar** como parte de esta decisión.

### ¿Por qué no?

- **Tamaño del proyecto.** Es un sistema personal, de un solo usuario, con un equipo de desarrollo de facto de una persona (asistida por IA). MediatR resuelve problemas de proyectos con muchos handlers, muchos desarrolladores tocando el mismo código a la vez, y necesidad de pipeline behaviors compartidos (logging, validación, transacciones, autorización) aplicados uniformemente sin que cada handler los repita. Ninguna de esas presiones existe hoy acá.
- **La inyección directa ya da lo mismo que un mediador, sin la indirección.** Un endpoint o una tool MCP que necesita `ClassifyMovementHandler` lo declara en su constructor y listo — el contenedor de DI ya resuelve el grafo de dependencias. Pasar por un `ISender.Send(command)` genérico no agrega ninguna capacidad nueva acá: agrega una capa de indirección (reflection para encontrar el handler correcto en tiempo de ejecución) a cambio de nada, porque no hay pipeline behaviors que justifiquen esa indirección.
- **Menos una dependencia externa.** Cada paquete NuGet nuevo es superficie de mantenimiento (versiones, breaking changes, licencia) — no sumar uno sin un beneficio concreto es coherente con otras decisiones ya tomadas en el proyecto (sin patrón Repository, sin bus de eventos — ver `docs/Archive/ReviewClassificationEnginev2ADR.md` §17 y la cita de esa misma ADR sobre eventos, ambas por el mismo motivo: complejidad sin consumidor real hoy).
- **Explícito por sobre "mágico".** Con inyección directa, seguir el flujo de una request es "click en el constructor, click en `Handle`". Con un mediador, hay que saber además cómo resuelve el handler correcto para un `IRequest<T>` dado — una capa de indirección más para cualquiera (humano o IA) que llegue al código por primera vez.

### ¿Cuál es el patrón oficial del proyecto?

**Command/Handler invocado directamente, sin mediador — este es el patrón oficial para toda escritura nueva del proyecto**, reemplazando la ambigüedad anterior ("¿sigo el patrón de Planning o el de CategoryEndpoints?"). Concretamente:

- Un **Command** es un `record` inmutable en `Application/<Módulo>/Commands/`, con los datos de entrada ya validados en su forma (tipos correctos), no necesariamente válidos en su contenido — esa validación de negocio es trabajo del Handler.
- Un **Handler** es una clase (`internal sealed class` o `public sealed class`, según si necesita ser instanciada desde otro proyecto — ver convención ya establecida en cada módulo existente) con un único método público `Handle(TCommand command, CancellationToken ct = default)`, que devuelve un `TResult` (record con `IsSuccess`/motivo de fallo, patrón ya establecido en los 4 módulos existentes — ver `LinkMovementToInvestigationResult`, `CreatePlanningMonthResult`, etc., como referencia de forma).
- El Handler recibe sus dependencias (`IApplicationDbContext`, `IDateTimeProvider`, etc.) por constructor — sin patrón Repository (ADR ya vigente, ver `docs/Architecture/Architecture.md` §1, sección Application).
- El consumidor (un endpoint de `FinancialMcp.Api`, o una tool de `FinancialSystem.McpServer`) declara el Handler en su propio constructor y lo invoca directamente — nunca a través de un mediador ni de un `ISender`.
- Para lecturas, **no se introduce un objeto "Query"** — se sigue el patrón ya establecido de un servicio de solo lectura con un método async que recibe parámetros primitivos/DTOs de entrada y devuelve un DTO/record de salida, sin abrir un `Command`/`Handler` para algo que no escribe nada.

### Ventajas de este patrón (documentadas para que quede explícito por qué se elige, no solo qué se elige)

- Simple de leer y de seguir: no hay reflection, no hay registro implícito de handlers, no hay "magia".
- Fácil de testear: el patrón que ya usa toda la suite de tests del proyecto (`new XxxHandler(dependencias).Handle(command)`, ver `ClassifyMovementHandlerTests`, `PlanningHandlersTests`, `InvestigationsHandlerTests`) sigue funcionando sin cambios.
- Sin dependencia externa nueva, sin superficie de mantenimiento adicional.
- El tipo de retorno explícito (`TResult` con `IsSuccess`) hace los fallos de negocio visibles en la firma del método, sin depender de excepciones para control de flujo.

### Limitaciones de este patrón (documentadas para que una futura decisión de escalar el proyecto tenga el contraste por escrito)

- **Sin pipeline cross-cutting centralizado.** No hay un lugar único donde aplicar logging, validación o manejo de transacciones a todos los comandos — cada Handler es responsable de lo que necesita. Si el número de handlers crece mucho, o si aparece una necesidad real de comportamiento uniforme (ej. auditar quién ejecutó cada comando), este punto sería la primera razón concreta para reconsiderar esta decisión — no antes.
- **Sin catálogo automático de comandos.** MediatR permite descubrir todos los `IRequestHandler<T>` registrados por reflection; acá, el catálogo de comandos existentes es el propio código (o `docs/PROJECT_STATUS.md` §2, que lo resume). No hay forma de listarlos programáticamente.
- **Migración manual, módulo por módulo.** No hay un mecanismo que fuerce a un módulo nuevo a seguir el patrón — depende de que quien lo escribe (humano o IA) lo sepa y lo siga. Esta misma ADR es, en parte, la forma de que quede escrito en un lugar que cualquiera debería leer antes de escribir código nuevo.

## Cómo conviven los módulos antiguos con los nuevos durante la migración

Esta ADR **no ejecuta ninguna migración** — ni de código, ni de namespaces, ni de estructura de carpetas (fuera de alcance explícito de PATCH-045). Deja escrito el criterio para cuando esa migración se retome (ver `docs/PROJECT_STATUS.md` §13, ítem 6, "homogeneizar el patrón CQRS", todavía pendiente de ejecución):

1. **Código nuevo:** sigue el patrón Command/Handler descripto arriba, sin excepción, para cualquier escritura. Esto aplica desde la fecha de esta ADR en adelante.
2. **Código existente con lógica en Endpoints** (Categorías, Contrapartes, Cuentas Financieras, BankStatement, Transaction): permanece como está. No se toca en este patch ni se programa automáticamente para tocarse en un patch específico — queda como deuda conocida y visible (`docs/PROJECT_STATUS.md` §11), a migrar módulo por módulo cuando se retome el punto 6 de §13, en el orden que convenga en ese momento (no lo fija esta ADR).
3. **Ningún módulo mixto durante la migración deja de funcionar**: migrar un endpoint a Command/Handler es un cambio interno (mover la lógica de validación/persistencia a un Handler nuevo, dejar el endpoint como capa delgada) que no cambia el contrato HTTP expuesto — la migración de cada módulo, cuando se haga, debe preservar el comportamiento observable exactamente igual, siguiendo el mismo criterio que ya aplicaron los patches de refactorización de performance de esta misma Epic (PATCH-041 a PATCH-044): sin cambios funcionales, solo reorganización interna.
4. **No hay fecha ni orden obligatorio fijado para completar la migración** — es deuda técnica documentada, no un bloqueante para seguir agregando funcionalidad en otros módulos.

## Consecuencias

- Cualquier desarrollo nuevo (humano o de IA) tiene ahora una única referencia escrita de qué patrón seguir, en vez de tener que inferirlo comparando módulos entre sí.
- La pregunta "¿por qué no usamos MediatR si las clases se llaman Command/Handler?" queda respondida en un lugar permanente, en vez de repetirse como una observación suelta en `PROJECT_STATUS.md` cada vez que alguien la nota.
- La deuda de migración (mitad de los módulos sin Command/Handler) sigue existiendo — esta ADR la reconoce y la deja planificada en principio, pero no la resuelve. `docs/PROJECT_STATUS.md` sigue siendo la fuente de verdad de qué migró y qué no (§2, tabla de módulos).
- Si en el futuro el proyecto crece lo suficiente como para que las limitaciones de la sección anterior (sin pipeline cross-cutting, sin catálogo automático) se vuelvan un problema real y no teórico, esta ADR es el punto de partida para evaluar MediatR (o una alternativa) con una comparación real de costo/beneficio — no antes, y no como parte de este patch.
