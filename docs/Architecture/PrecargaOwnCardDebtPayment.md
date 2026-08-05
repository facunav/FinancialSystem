# Precarga de Impacto financiero para contrapartes OwnCard — `movements.html`

Documento operativo del PATCH-022. Implementa el punto (a) de "Consecuencias" de
`docs/Decisions/ADR-003-separar-consumo-pago-tarjeta.md` — ver ese documento para el
contexto completo (por qué `DebtPayment` + `OwnCard` existen) y la sección
"Actualización" para el desvío deliberado respecto del texto original del ADR.

## Comportamiento

En el modal de clasificación manual de `movements.html` (individual y en lote): al
elegir en el selector "Contraparte" una contraparte cuyo `type` es `OwnCard`, el campo
"Impacto financiero" se precarga inmediatamente con `DebtPayment` ("Pago de deuda").
No espera a guardar — es una actualización síncrona del `<select>`, sin request al
backend. El usuario puede cambiarlo libremente después: sigue siendo el mismo
`<select>` de siempre, sin bloqueo ni confirmación. Mientras el valor precargado por
esta razón sigue vigente, una leyenda ("Sugerido porque la contraparte es una tarjeta
propia — podés cambiarlo.") lo deja explícito; se oculta apenas el usuario toca el
campo a mano.

## Mecanismo reutilizado — sin nada nuevo del lado del backend

* `Counterparty.Type`/`CounterpartyType.OwnCard`: sin cambios, ya existían.
* `GET /api/counterparties`: sin cambios — `CounterpartyDto` ya incluía `type` desde
  siempre; el catálogo que `movements.html` carga en `state.counterparties`
  (`loadCounterparties()`, sin cambios) ya trae ese dato.
* El disparador es el evento `change` del `<select id="cCounterparty">`, el mismo que
  ya usaba el listener de sugerencia de período financiero (Épica K) — se agregó un
  segundo listener sobre ese mismo evento, no un mecanismo paralelo. Se registra antes
  que ese listener existente a propósito: evita una condición de carrera donde el fetch
  asincrónico de la sugerencia de período (que solo debe aplicar a `Income`) quedara
  aplicado sobre un movimiento que la precarga de esta patch acababa de marcar como
  `DebtPayment`.

## Por qué no vía `Counterparty.DefaultFinancialImpact`

El texto original del ADR-003 sugería usar `DefaultFinancialImpact` como vehículo. Se
evaluó y se descartó para esta implementación: requeriría que cada contraparte
`OwnCard` tenga ese campo configurado a mano vía el CRUD completo de contrapartes — el
alta rápida ("+ Nueva") desde el propio modal de clasificación en `movements.html` solo
pide el nombre, así que en la práctica casi ninguna contraparte `OwnCard` real llegaría
a tener `DefaultFinancialImpact` seteado, y la precarga no se activaría nunca. Anclar
la precarga directamente a `Type == OwnCard` es una realización más confiable de la
misma intención del ADR, sin depender de un paso de configuración manual adicional, y
sin introducir ningún campo, tabla o endpoint nuevo — sigue usando exclusivamente el
mismo `Type`/`CounterpartyType.OwnCard` que el ADR ya había confirmado como el
mecanismo estándar. `DefaultFinancialImpact` no se tocó: sigue funcionando igual que
antes para el motor de sugerencias automáticas (`ClassificationSuggestionService`), sin
ninguna relación con este cambio.

## Alcance

Solo se dispara durante la edición manual (modal de clasificación, individual y en
lote) de `movements.html`. No afecta: importación, el motor de clasificación
automática ni sus sugerencias (`ClassificationSuggestionService`, sin cambios),
auditoría (`AuditReportService`, sin cambios), planificación, ni ningún endpoint.

## Tests

**Backend**: este patch no modifica ningún archivo de backend — no agrega, cambia ni
depende de ningún endpoint, DTO o campo nuevo (`GET /api/counterparties` ya devolvía
`type` desde antes de este patch). No hay comportamiento de backend nuevo que
justifique un test nuevo.

**Frontend**: el repositorio no tiene infraestructura de testing para el frontend
(`wwwroot/` son páginas HTML autocontenidas, sin build step, sin framework de
componentes ni test runner de JS instalado — confirmado en `docs/PROJECT_STATUS.md`:
*"frontend (7-8 páginas HTML sin infraestructura compartida)"*, sin `package.json` en
el repositorio). Siguiendo la instrucción explícita del patch de no crear
infraestructura de testing nueva solo para este comportamiento, no se agregaron tests
automatizados de frontend.

Verificación manual (ver también "Acciones manuales" del patch): en `movements.html`,
abrir el modal de clasificación de un movimiento, crear o elegir una contraparte con
`Type = OwnCard` (vía `counterparties.html`) y confirmar la precarga inmediata, la
leyenda visible, la posibilidad de cambiarla, y que otros tipos de contraparte no
disparan ningún cambio.

## Qué NO cambió

* El modelo de dominio (`Counterparty`, `CounterpartyType`, `FinancialImpact`).
* `ClassificationSuggestionService` (motor de sugerencias automáticas) ni
  `AuditReportService` (auditoría) — ambos ya leían `DefaultFinancialImpact` desde
  antes, sin relación con este cambio.
* `CounterpartyEndpoints` — ningún endpoint se modificó.
* El resto de `movements.html`: el listener de sugerencia de período financiero
  (Épica K) sigue con la misma lógica, solo se le agregó una condición implícita
  (recibe un `#cImpact` ya actualizado si la contraparte es `OwnCard`).
* Importación, Planning, Auditoría, Dashboard, autenticación.
