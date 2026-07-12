# Análisis de arquitectura — entidad Counterparty

Commit base: `origin/master` (`3000d2c`, mismo código de negocio que los dos
análisis anteriores — el único commit nuevo en el remoto agrega un `.md` de
documentación, no toca `src/`). Documento de solo análisis: no se modificó,
creó ni escribió ningún archivo del repositorio para producirlo.

Releí en esta sesión, específicamente para este documento:
`Counterparty.cs`, `CounterpartyConfiguration.cs`, `CounterpartyEndpoints.cs`
completo, `CatalogDtos.cs`, `ClassificationSuggestionService.cs`, y ejecuté
búsquedas dirigidas (`grep`) sobre **todo** `src/` y `tests/` en `origin/master`
para `CounterpartyType` y para `Counterparty` en general, incluida la suite de
tests de PR-S10/S11/S12 (`tests/FinancialSystem.Infrastructure.Tests/Suggestions/`).

---

## 1. Estado actual de Counterparty

### 1.1 Entidad (`src/FinancialSystem.Domain/Entities/Counterparty.cs`)

```
Id                        Guid
Name                      string           — requerido
Type                      CounterpartyType — requerido
Notes                     string?          — opcional, libre
DefaultCategoryId         Guid?            — opcional, FK a Category
DefaultMovementType       MovementType?    — opcional
DefaultFinancialImpact    FinancialImpact? — opcional
IsDeactivated             bool
CreatedAt / UpdatedAt     DateTime
```

`CounterpartyType` (10 valores, definido en el mismo archivo):
`Person, Business, Company, Bank, Service, Government, OwnAccount, OwnCard,
Investment, Other`.

### 1.2 Configuración EF (`CounterpartyConfiguration.cs`)

- Tabla física `CounterParties` (nombre pinneado explícitamente, distinto al
  nombre del tipo C#, "para preservar el esquema ya migrado").
- `Type` → `HasConversion<int>().IsRequired()`.
- **Dos índices dedicados a `Type`**: uno simple (`HasIndex(x => x.Type)`) y uno
  compuesto (`HasIndex(x => new { x.Type, x.Name })`). Esto es información nueva
  respecto al análisis anterior — no solo hay un campo obligatorio sin
  consumidor, hay **infraestructura de base de datos construida específicamente
  para poder filtrar/ordenar por ese campo**, sin que ningún código la use.

### 1.3 Endpoints (`CounterpartyEndpoints.cs`) — CRUD completo

- `GET /api/counterparties?includeDeactivated=&search=` — filtra por
  `IsDeactivated` y por substring de `Name`. **No acepta ni expone ningún
  filtro por `Type`**, pese a los dos índices del punto 1.2.
- `GET /api/counterparties/{id}`
- `POST /api/counterparties` — exige `Name` y `Type` (`Enum.TryParse<CounterpartyType>`,
  `BadRequest` si falta o es inválido, línea 75). Acepta `DefaultCategoryId`/
  `DefaultMovementType`/`DefaultFinancialImpact` opcionales.
- `PUT /api/counterparties/{id}` — actualiza `Name`, `Notes`, los 3 `Default*`.
  **No actualiza `Type`** (no está en el `UpdateCounterpartyRequest` que el
  handler lee) — es decir, hoy ni siquiera se puede corregir el `Type` de una
  contraparte ya creada desde la API que existe.
- `DELETE /api/counterparties/{id}` — desactiva, no elimina físicamente.

### 1.4 DTOs (`CatalogDtos.cs`)

`CounterpartyDto` expone `Type` como string de solo lectura (`c.Type.ToString()`)
junto con `Name`, `DefaultCategoryId`, `DefaultCategoryName`, `DefaultMovementType`,
`DefaultFinancialImpact`, `IsDeactivated`. Ningún consumidor de este DTO (no hay
ninguno hoy, porque no hay pantalla — ver análisis anterior) lee ese campo.

### 1.5 `ClassificationSuggestionService.cs` (PR-S7/S11/S12)

La heurística 2 (enriquecimiento por `Counterparty.Default*`) usa el record
interno `CounterpartyDefaultsRow(Id, Name, DefaultCategoryId,
DefaultCategoryIsActive, DefaultMovementType, DefaultFinancialImpact)` — **no
incluye `Type` en absoluto**, ni en la proyección de la consulta
(`EnrichWithCounterpartyDefaultsAsync`) ni en la lógica de fusión
(`EnrichSuggestions`/`MergeDimension`). El motor de sugerencias, que es el
consumidor más sofisticado que existe de `Counterparty`, no sabe que `Type`
existe.

### 1.6 `ClassifyMovementCommand`/`ClassifyMovementHandler`

Valida que `CounterpartyId` exista (`_db.Counterparties.AnyAsync(c => c.Id ==
counterpartyId)`) — no lee ni valida `Type` en ningún punto.

### 1.7 Tests (`tests/FinancialSystem.Infrastructure.Tests/Suggestions/`)

Los tres archivos de test de PR-S10/S11/S12
(`ClassificationSuggestionServiceBuildSuggestionsTests.cs`,
`ClassificationSuggestionServiceEnrichSuggestionsTests.cs`,
`ClassificationSuggestionServiceNormalizeTests.cs`) construyen filas de prueba
con `Guid`s de contraparte y valores de `Default*` — **ninguno de los tres
instancia, lee ni asigna un `CounterpartyType`**, porque el tipo de dato que
ejercitan (`CounterpartyDefaultsRow`) no lo tiene como campo.

### 1.8 Frontend (`movements.html`)

Ya documentado en el análisis anterior: el único uso de `Counterparty` en toda
la UI es un `<select id="cCounterparty">` de solo lectura sobre el catálogo
existente, dentro del modal de clasificación. No hay ninguna referencia a `Type`
en ningún archivo `.html` del proyecto.

---

## 2. Problemas encontrados

### 2.1 `CounterpartyType` — cero consumidores, confirmado exhaustivamente

Resultado de `grep -rn "CounterpartyType" --include="*.cs"` sobre todo el
repositorio (`src/` y `tests/`): **tres coincidencias, las tres son la propia
definición y su único punto de validación de entrada**:

1. `Counterparty.cs:36` — `public CounterpartyType Type { get; set; }` (la
   declaración de la propiedad).
2. `Counterparty.cs:77` — `public enum CounterpartyType` (la definición del
   enum).
3. `CounterpartyEndpoints.cs:75` — `Enum.TryParse<CounterpartyType>(request.Type,
   ...)` (la única línea de todo el código que *lee* el valor recibido, y solo
   para validar que sea un string parseable — nunca para tomar una decisión
   distinta según cuál de los 10 valores sea).

No hay una cuarta coincidencia en ningún lado: ni en `ClassificationSuggestionService.cs`,
ni en `ClassifyMovementHandler.cs`, ni en `MovementsQueryService.cs`, ni en los
tests, ni en el frontend, ni en ninguna regla de negocio de ningún otro archivo.
**Confirmo explícitamente, con evidencia exhaustiva y no solo con la búsqueda
puntual del análisis anterior: no hay ningún consumidor.**

Sí hay, en cambio, evidencia de que el campo *anticipaba* un uso futuro
específico. Dos de los diez valores tienen doc-comments que apuntan a un caso de
uso concreto y reconocible:

> `OwnAccount = 7` — "Cuenta propia del mismo usuario en otro banco/entidad."
> `OwnCard = 8` — "Tarjeta de crédito propia (para registrar pagos de resumen)."

Esto sugiere que la intención original era usar `Type` para distinguir "esto es
una contraparte externa real" de "esto es, en rigor, otra cuenta/tarjeta mía" —
relevante para el caso, ya identificado con datos reales en el primer análisis
de esta serie, de `"PAGO DE TARJETA MASTERCARD Nro:..."` /
`"PAGO DE TARJETA VISA Nro:..."` (`MovementType.Payment` +
`FinancialImpact.DebtPayment`). Es un uso legítimo en el papel — pero ese mismo
análisis ya identificó que ese caso se resuelve con una regla determinística de
prefijo de texto (el `Concepto` del banco ya dice literalmete "PAGO DE TARJETA
..."), **sin necesitar ninguna clasificación de tipo de contraparte**. El único
uso futuro concreto y reconstruible a partir del propio código es, con la
información disponible hoy, redundante con una solución más barata que ya
identificamos por otro camino.

Los ocho valores restantes (`Person, Business, Company, Bank, Service,
Government, Investment, Other`) no tienen ningún indicio, ni en código ni en
comentarios, de para qué decisión de producto servirían. Mi lectura es que
responden a una taxonomía genérica de "tipos de entidad" pensada por analogía
con otros sistemas contables/CRM, no derivada de una necesidad concreta de
*este* producto.

### 2.2 Infraestructura de base de datos construida para un filtro que no existe

Los dos índices sobre `Type` (simple y compuesto con `Name`) representan costo
real, aunque pequeño, de mantenimiento en cada escritura — y ninguna consulta en
todo el código los usaría nunca, porque no hay ningún filtro por `Type` en
ningún endpoint. Es la misma señal que el campo en sí: trabajo de
implementación invertido en una dimensión que el producto, hasta ahora, no
necesitó.

### 2.3 Inconsistencia entre `Create` y `Update`: `Type` no es editable

`POST` exige `Type`; `PUT` no lo acepta en absoluto (`UpdateCounterpartyRequest`
no lo incluye, confirmado leyendo el handler completo). Si alguien completa mal
ese campo al crear — cosa fácil, dado que ninguna decisión posterior depende de
su valor y por lo tanto no hay ninguna señal de que importe elegir bien — hoy no
hay forma de corregirlo salvo escribiendo directo en la base. Es un síntoma más,
no la causa: nadie terminó de construir el camino de edición porque nada
consume el dato para justificar la inversión.

### 2.4 No hay forma estructurada de vincular una descripción cruda a una Contraparte más allá del historial exacto

No es un problema de `Type`, pero surge de mirar toda la entidad en conjunto: no
existe ningún campo de "alias" o patrón de descripción conocido asociado a una
Contraparte. Hoy, que un movimiento con descripción `"MERPAGO*LACOCA"` termine
vinculado a la Contraparte "La Coca" depende enteramente de que el usuario lo
elija a mano la primera vez, y de que el motor de sugerencias (heurística 1)
reconozca la *misma* descripción exacta en el futuro. Si el mismo comercio real
aparece alguna vez con una descripción ligeramente distinta (otro sufijo de
cupón, otra pasarela), no hay ningún mecanismo — ni de dato ni de código — que
los una. Lo señalo como una limitación real, pero **no la recomiendo resolver
ahora**: agregar un campo de alias/patrones es exactamente el tipo de
"responsabilidad nueva" que la sección 5 de este documento argumenta que
`Counterparty` no debería sumar en este momento del producto.

---

## 3. Campos realmente necesarios

Pensando desde el producto, no desde lo ya implementado, el conjunto mínimo
para que `Counterparty` cumpla su función real (dar nombre estable a "con quién
se relaciona un movimiento" y acumular default de clasificación) es:

1. **`Name`** — obligatorio. Es el único dato sin el cual la entidad no tiene
   sentido: es lo que el usuario ve, busca y elige.
2. **`DefaultCategoryId` / `DefaultMovementType` / `DefaultFinancialImpact`** —
   opcionales, se completan progresivamente (ver análisis anterior, sección 5:
   deberían poblarse desde el momento de clasificar, no desde un formulario de
   alta). Confirmado en el punto 1.5 que son, hoy, el único conjunto que el
   motor de sugerencias realmente consume.
3. **`IsDeactivated` + `CreatedAt`/`UpdatedAt`** — no son "datos" que el usuario
   carga, son metadatos técnicos de ciclo de vida; se mantienen sin cambios.
4. **`Notes`** — opcional, de bajo costo (nunca obligatorio, nunca bloquea nada,
   nunca lo lee ningún otro componente): vale la pena mantenerlo tal cual está
   porque no impone ninguna fricción y sí puede ser útil para el usuario como
   recordatorio propio ("sucursal del shopping", ejemplo real que ya trae el
   doc-comment de la entidad).

Con estos campos, `Counterparty` responde exactamente a las dos preguntas que
el dominio necesita que responda: "¿quién es?" (`Name`) y "¿qué sugiero cuando
aparece de nuevo?" (los 3 `Default*`). No falta nada más para eso.

---

## 4. Campos innecesarios o cuestionables

**`CounterpartyType`, tal como existe hoy (obligatorio, 10 valores, sin
consumidor)** — es el hallazgo central de este documento, ya desarrollado en la
sección 2. No es un campo "levemente cuestionable": es un campo obligatorio,
con costo de fricción real en cada alta (el usuario tiene que decidir entre 10
opciones sin ninguna guía de cuál importa) y costo de mantenimiento real (dos
índices), a cambio de cero funcionalidad hoy y, con la evidencia disponible, un
único caso de uso futuro plausible que ya tiene una solución mejor identificada
por otro camino (punto 2.1).

No encontré ningún otro campo de la entidad que sea cuestionable en el mismo
sentido. `Notes` es opcional y de costo cero; los 3 `Default*` tienen consumidor
real y verificado (PR-S7/S11/S12); `IsDeactivated`/auditoría son necesarios para
cualquier entidad con CRUD real.

---

## 5. Evolución futura: ¿debería Counterparty seguir creciendo, o mantenerse simple?

Debería mantenerse simple, y lo justifico por contraste con las otras dos
entidades maestras del mismo nivel, que sí tienen razones de dominio concretas
para ser más ricas que `Counterparty`:

- **`FinancialAccount`** necesita `Type`, `AccountNumber`, `Currency` porque
  esos datos **afectan el parsing y la reconciliación** — son la base para el
  wiring automático de cuenta financiera que el análisis anterior identificó
  como la mejora de mayor impacto de todo el producto. Cada campo ahí tiene un
  consumidor futuro concreto y ya identificado.
- **`Category`** tiene `ParentId` reservado para jerarquía — una extensión con
  justificación de dominio clara (subcategorías), aunque todavía sin
  implementar.
- **`Counterparty`**, en cambio, no tiene ninguna dimensión de negocio
  pendiente de ese tipo. Su valor no viene de tener más campos: viene de tener
  **más movimientos correctamente vinculados a la misma contraparte** a lo
  largo del tiempo. Con miles de movimientos, lo que hace que `Counterparty`
  sea útil no es que sepa más cosas sobre sí misma — es que el 95% de las veces
  que aparece "MERPAGO*LACOCA" el sistema ya sepa que es la misma contraparte
  de siempre. Ese es un problema de **calidad de vinculación** (qué tan bien se
  reconoce que una descripción nueva corresponde a una contraparte ya
  existente), no un problema de **esquema** (qué campos tiene la entidad).

Agregar responsabilidades nuevas a `Counterparty` (alias, patrones de
descripción, cuentas asociadas, límites de gasto, lo que sea) antes de haber
resuelto que la gente pueda siquiera crear una contraparte hoy (análisis
anterior, punto 4) sería repetir exactamente el patrón que motivó este
documento: invertir en sofisticación de una capa antes de que la capa anterior
esté completa. La entidad debería permanecer con el conjunto mínimo de la
sección 3 hasta que exista evidencia de uso real (no hipotética) de que falta
algo — el mismo criterio que ya rige, con buenos resultados, el motor de
sugerencias ("sin evidencia de futuras reglas compartiendo forma común, extraer
una abstracción ahora sería prematuro", `ClassificationSuggestionService.cs`).

---

## 6. CRUD futuro: qué debería mostrar, qué acciones, qué no debería estar

### Debería mostrar

- Listado: `Name`, resumen legible de los 3 `Default*` (ej. "Salud · Compra ·
  Gasto", o "Sin configurar" si están vacíos), `Estado` (activa/desactivada) —
  mismo criterio visual que `accounts.html` ya usa para `FinancialAccount`.
- Un dato adicional, derivado y de solo lectura, que **no requiere ningún
  campo nuevo en el dominio**: cantidad de movimientos clasificados vinculados
  a esa contraparte (`COUNT(*) FROM ClassifiedMovement WHERE CounterpartyId =
  ...`). Es información real y útil para las tareas de mantenimiento que esta
  pantalla vendría a habilitar (decidir cuál de dos contrapartes casi
  duplicadas desactivar, por ejemplo — normalmente la que tiene 0 o 1
  movimiento).
- Formulario de alta/edición: `Name` (único campo obligatorio), los 3
  `Default*` (editables, para corregir lo que el flujo de "recordar como
  default" del análisis anterior vaya guardando), `Notes` (opcional).

### Acciones

- Crear (mismo formulario mínimo que el alta en contexto propuesta en el
  análisis anterior — un solo formulario, dos puntos de entrada, sin duplicar
  lógica).
- Editar (incluye poder corregir los `Default*`).
- Desactivar / reactivar (ya existe en el backend, solo falta la pantalla).
- Buscar por nombre (ya existe en el backend).

### Qué no debería estar

- **Ningún filtro ni columna de `Type`** — no hay ninguna decisión de producto
  que dependa de él hoy (sección 2.1); construir UI alrededor de un dato sin
  consumidor sería repetir el mismo problema que motivó este análisis, un nivel
  más arriba (en la UI en vez de en el dominio).
- **Ninguna acción de "fusionar contrapartes duplicadas"** — es una necesidad
  real y previsible (el mismo comercio real con dos nombres ligeramente
  distintos, dato observable recién cuando haya uso real), pero es
  significativamente más compleja que un CRUD simple (requiere decidir qué pasa
  con el historial de `ClassifiedMovement` ya vinculado a la contraparte que se
  fusiona) — queda fuera de esta pantalla hasta que haya evidencia concreta de
  que hace falta, no como anticipación.
- **Ningún panel de analítica/gasto por contraparte** dentro de esta pantalla —
  esa responsabilidad ya es del dashboard, agrupando `ClassifiedMovement` por
  `CounterpartyId`; duplicarla acá mezclaría una pantalla de administración de
  catálogo con una de reporting, sin necesidad.

---

## Recomendación arquitectónica

**Retirar la obligatoriedad de `CounterpartyType` en el alta, y no exponerlo en
absoluto en el formulario de alta en contexto ni en el CRUD nuevo por ahora.**
No recomiendo eliminar el campo de la base de datos ni del dominio en esta
instancia — es un cambio de mayor alcance (migración, y decidir qué hacer con
los dos índices) que no aporta nada adicional frente a simplemente dejar de
exigirlo y dejar de mostrarlo. La recomendación concreta, en orden de qué tan
seguro estoy de cada parte:

1. **Alto grado de certeza:** dejar de exigir `Type` en `POST
   /api/counterparties` (aceptar ausente, con un valor por defecto interno
   como `Other`) y no incluirlo en ningún formulario de UI, presente o futuro.
   Esto es reversible sin costo y elimina toda la fricción hoy, sin tocar el
   modelo de datos.
2. **Grado de certeza medio:** una vez que exista uso real y, con él, evidencia
   de si alguna vez hace falta distinguir "esto es en rigor una cuenta/tarjeta
   propia" (el único caso con justificación de dominio identificado en 2.1),
   evaluar si conviene resolverlo con la regla de prefijo textual (ya
   identificada y más barata) en lugar de reactivar `CounterpartyType`. No lo
   resuelvo en este documento porque depende de datos de uso que todavía no
   existen — lo dejo señalado, no decidido.
3. **No recomiendo** eliminar la columna/enum de la base ni del dominio ahora:
   no hay urgencia (un campo no leído no genera bugs, solo fricción de alta si
   se lo sigue exigiendo, y el punto 1 de esta recomendación ya elimina esa
   fricción sin necesidad de migración).

---

## Impacto sobre la futura UI

Con esta recomendación aplicada, la pantalla de administración de Contrapartes
y el alta en contexto (ambas ya descriptas en el análisis anterior como PR-O3 y
PR-O5) se simplifican de forma directa y medible:

- El formulario de alta en contexto queda con **un solo campo obligatorio**
  (`Name`) en vez de dos, eliminando la única decisión no trivial (elegir entre
  10 valores de `Type` sin ningún criterio para hacerlo) que hoy exigiría ese
  flujo si se implementara tal como está modelado el backend.
- La pantalla de administración (CRUD completo) pierde una columna, un filtro y
  un índice de base de datos que nunca se iban a usar — un formulario más corto
  y una tabla más legible, sin ninguna pérdida de funcionalidad real.
- No cambia el alcance de los PR-O3/PR-O5 ya descriptos en el análisis anterior
  más que en el detalle de qué campos expone cada formulario — no introduce
  trabajo nuevo, lo reduce.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no ejecuté
`git add`, no hice ningún commit ni push. Todo el trabajo fue lectura de código
en `origin/master` (vía `git show`) y búsquedas de solo lectura (`grep`) sobre
`src/` y `tests/` para confirmar, de forma exhaustiva, la ausencia de
consumidores de `CounterpartyType` antes de afirmarlo. No propuse ningún PR, tal
como se pidió — esto es únicamente el análisis para decidir si el modelo
necesita simplificarse antes de construir cualquier pantalla nueva.
