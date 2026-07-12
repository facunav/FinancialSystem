# Próxima épica: de "motor de sugerencias inteligente" a "aplicación usable"

Commit base: `origin/master` (mismo estado que el análisis anterior,
`analisis-arquitectura-post-U4.md`). Este documento parte de esa base y no la
repite en detalle salvo donde hace falta releer código para responder algo nuevo —
en esta sesión releí, además de lo ya citado, `CategoryConfiguration.cs`,
`DatabaseMigrationExtensions.cs` (seed), `imports.html`,
`ImportsFolderWatcherHostedService.cs`, `FileIngestionOptions.cs`,
`BbvaBankStatementImportHandler.cs`, `FileImportRouter.cs`,
`ImportFileProcessingSink.cs`, `ExcelWorkbookParser.cs`, `accounts.html` completo y
`CounterpartyEndpoints.cs` completo. Documento de solo análisis: no se modificó
ningún archivo del repositorio.

---

## 0. La pregunta que el usuario pidió cuestionar

La hipótesis de partida es: *"seguimos agregando inteligencia a un sistema que
todavía no permite cargar correctamente la información que necesita esa
inteligencia."* La sostengo, pero no por la razón que parecía obvia (falta de
pantallas CRUD). Hay un problema **anterior** a eso, que además es más grave: en
este momento, si un usuario nuevo deja caer sus dos archivos reales de banco
(exactamente los que usamos en el análisis anterior:
`Debito_29_05_2026_al_10_07_2026.xls`) en la carpeta que el sistema vigila para
importar, **el sistema no los procesa** — no porque falte un parser (el parser para
ese formato existe y funciona, ya lo verificamos con los dos archivos reales), sino
porque el nombre de archivo no matchea una lista fija de patrones que no tiene
ninguna relación con cómo BBVA realmente nombra sus exportaciones. Esto no es una
pregunta de UX de clasificación: es que **el flujo de un usuario nuevo se corta en
el primer paso, antes de que el motor de sugerencias, las pantallas de Contraparte
o cualquier otra cosa lleguen siquiera a importar.**

Esto reordena la prioridad de todo lo demás. Lo desarrollo en el punto 2.1.

---

## 1. Diagnóstico del estado actual

### 1.1 Cómo entra un archivo al sistema hoy (nadie lo sube desde la web)

Revisé el `wwwroot/` completo: `accounts.html`, `dashboard.html`, `imports.html`,
`index.html`, `movements.html`. **Ninguno tiene un `<input type="file">` ni ningún
mecanismo de carga.** `imports.html` es exclusivamente un visor de historial
(`GET /api/imports/history`, `GET /api/imports/{id}`) — no hay botón "importar".

La importación real ocurre en `hosts/FinancialSystem.Worker`:
`ImportsFolderWatcherHostedService` vigila una carpeta local
(`FileSystemWatcher`) y, apenas aparece un archivo con extensión vigilada
(`.pdf`, `.csv`, `.xlsx`, `.xls` — `FileIngestionOptions.WatchedExtensions`),
dispara `IFileImportRouter.RouteAsync`. Es decir: **el "primer paso" de este
producto, para cualquier usuario, es copiar un archivo a una carpeta del sistema
de archivos del servidor.** No hay feedback en tiempo real en ninguna pantalla web
de que eso ocurrió — solo si el usuario después entra a `imports.html` y refresca.

### 1.2 Qué pasa con los dos `.xls` reales de esta conversación, tal como están nombrados

`FileImportRouter` prueba los handlers en orden de registro; el primero que acepta
gana. Para `.xls`, el handler específico es `BbvaBankStatementImportHandler`, y su
`CanHandle` exige **dos condiciones**: extensión `.xls` **y** que el nombre de
archivo matchee uno de estos patrones (`FileIngestionOptions.cs`, con estos
defaults exactos):

```
["Caja*.xls", "*ahorros*.xls", "*corriente*.xls"]
```

Los dos archivos reales que subiste se llaman `Debito_29_05_2026_al_10_07_2026.xls`
y `Debito_30_03_2026_al_15_06_2026.xls`. Ninguno empieza con "Caja", ninguno
contiene "ahorros", ninguno contiene "corriente" — **ninguno matchea.**
`BbvaBankStatementImportHandler.CanHandle` devuelve `false` para los dos.

El siguiente (y último) handler en la cadena, `TransactionImportHandler`, es un
catch-all que acepta cualquier extensión vigilada — así que sí "agarra" el
archivo. Pero adentro (`ImportFileProcessingSink.HandleFileAsync`) delega a
`IFileParserFactory.TryGetParser`, y el único parser genérico de Excel registrado
es `ExcelWorkbookParser`, cuyo `SupportedExtensions` es **`[".xlsx"]` únicamente**
(usa `ClosedXML`, que no lee el formato binario legado `.xls`/BIFF de estos dos
archivos reales). No hay ningún parser registrado para `.xls` fuera del handler
específico de BBVA que ya rechazó el archivo por nombre.

Resultado real, verificado leyendo el código de punta a punta: el archivo **no se
pierde en silencio absoluto** (`ImportFileProcessingSink` sí devuelve
`ImportRunResult.Failure("No hay parser registrado para la extensión '.xls'.")`, y
`FileImportRouter` sí persiste un `ImportBatch` con ese diagnóstico) — pero:

- No hay ninguna notificación activa. El usuario tiene que saber que existe
  `imports.html` y entrar a revisarlo.
- El mensaje de diagnóstico ("no hay parser registrado para la extensión") es
  **engañoso**: sí existe un parser para `.xls` — `BbvaBankStatementParser`, que
  además ya demostramos que interpreta perfectamente estos dos archivos reales
  (columnas, cuenta, importes, todo). El verdadero motivo del rechazo (el nombre no
  matchea una lista fija que no tiene relación con cómo BBVA nombra sus
  exportaciones) no aparece en ningún lado.
- No hay ningún lugar en la UI que le diga al usuario "para que esto funcione,
  el archivo tiene que llamarse `Caja*.xls`, `*ahorros*.xls` o `*corriente*.xls`".

Este es, con los datos reales de esta conversación, el primer obstáculo real de
cualquier usuario nuevo — **antes** de llegar a clasificar un solo movimiento.

### 1.3 Qué SÍ está listo, aunque no se vea

- `Category` **no está vacía en una instalación nueva.** `DatabaseMigrationExtensions.SeedCategoriesAsync`
  inserta 11 categorías de sistema en cada arranque (idempotente, por `Name`):
  Alimentación, Salud, Transporte, Servicios, Seguros, Educación, Entretenimiento,
  Suscripciones, Transferencias, Ingresos, Otros. Esto es información que faltaba
  en el análisis anterior y cambia el marco de "base vacía" que pediste analizar:
  Categoría sí tiene una base razonable desde el día uno. Contraparte y
  FinancialAccount, en cambio, **no tienen ningún seed** — arrancan genuinamente
  vacías.
- El CRUD completo de `Counterparty` (`CounterpartyEndpoints.cs`) ya acepta
  `DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact` tanto en
  `POST` como en `PUT` — el backend no necesita ningún cambio para que una pantalla
  nueva configure esos tres campos.
- `accounts.html` (581 líneas, sin framework, un solo archivo) es un patrón de CRUD
  completo y probado: listado con filtro/búsqueda, modal de alta/edición,
  desactivar/reactivar, toasts de error. Es el molde que ya existe para levantar
  algo equivalente para `Category`/`Counterparty` sin inventar ningún patrón nuevo.

### 1.4 El motor de sugerencias ya está, en efecto, razonablemente maduro

Coincido con tu conclusión, y la sostengo con lo que ya se verificó en el análisis
anterior: dos heurísticas determinísticas (historial exacto por descripción
normalizada, con confianza Alta/Media/Baja por mayoría calificada 2/3; enriquecimiento
por `Counterparty.Default*`), sin fuzzy matching ni IA, ya cubren el terreno que
tiene sentido cubrir con la cantidad de datos reales que este usuario genera (cientos
de movimientos por mes, no decenas de miles). Agregar más sofisticación al motor
ahora mismo — similitud difusa, embeddings, aprendizaje de nuevas reglas — no
tiene con qué alimentarse: la Contraparte estructuralmente no se puede cargar, la
cuenta financiera nunca llega poblada, y una parte no trivial de compras en cuotas
ni siquiera normaliza su descripción correctamente (ver el hallazgo de "C.02/03"
del análisis anterior). Seguir invirtiendo ahí es, literalmente, afinar un motor
que no tiene combustible.

---

## 2. Problemas más importantes, ordenados por impacto real

1. **[Bloqueante] Importación de archivos reales rota por convención de nombre no
   documentada ni visible.** Desarrollado en 1.2. Esto no es "un caso raro": es
   exactamente el nombre real que BBVA le puso al archivo que este usuario
   descargó de su banco. Cualquier usuario nuevo real, no solo este, probablemente
   choca con esto en su primer intento.
2. **[Bloqueante de UX, no de datos] No hay ninguna forma de subir un archivo
   desde la web.** Aun si el nombre matcheara, el flujo real hoy es "copiar un
   archivo a una carpeta del servidor" — no hay ningún paso de producto ahí, es
   infraestructura expuesta directamente al usuario.
3. **[Alto] No hay pantalla para crear Contrapartes ni Categorías** (ya
   identificado en el análisis anterior, punto 4.1 de ese documento) — sigue siendo
   el segundo bloqueo más importante, inmediatamente después de lograr importar
   algo.
4. **[Alto] Cuenta financiera nunca se infiere en el import** (ya identificado,
   punto 1 del análisis anterior) — sigue vigente, no cambia con este documento.
5. **[Medio] El flujo de clasificación pide 4 campos sin distinguir cuáles ya
   tienen evidencia fuerte y cuáles no** — desarrollado en el punto 3 y 4 de este
   documento.
6. **[Medio] No existe ningún mecanismo para "prometer" un valor por defecto de
   Contraparte en el momento natural en que se decide** (se puede hacer por API,
   no hay affordance de UI) — desarrollado en el punto 4.

El orden importa: **1 y 2 son estrictamente anteriores a todo lo demás.** No tiene
sentido diseñar la pantalla perfecta de Contrapartes si el usuario todavía no logró
importar un solo movimiento real.

---

## 3. Flujo ideal para un usuario nuevo

Punto de partida real: base de datos recién migrada (11 categorías de sistema
sembradas, cero contrapartes, cero cuentas financieras, cero movimientos, cero
historial).

### Paso 0 — Importar el primer archivo

**Hoy:** copiar un archivo a una carpeta del servidor, con un nombre que además
tiene que adivinar (o no hay forma de importarlo, ver 1.2). Sin feedback.

**Ideal:** un usuario nuevo no debería tener que saber que existe una carpeta
vigilada ni cómo se llama. El paso mínimo viable no requiere una pantalla de
importación sofisticada — alcanza con:
- Que el ruteo de `.xls`/`.csv` deje de depender de coincidencia de nombre de
  archivo y pase a usar contenido, igual que ya hacen los PDF (fingerprints sobre
  el contenido de las primeras filas/líneas — la fila de título del XLS BBVA
  (`"Detalle de Movimientos de Cuenta: CA$ ..."`) ya es, en los hechos, un
  fingerprint de contenido perfectamente utilizable, más confiable que el nombre
  del archivo).
- Que `imports.html` dejara de ser solo un historial pasivo y mostrara, arriba de
  todo, un estado claro por archivo: importado / rechazado + motivo en lenguaje
  claro ("el archivo no tiene el formato esperado de ningún banco soportado"), en
  vez del mensaje interno actual.

Esto es lo que **debe** resolverse antes de cualquier otra cosa de este documento
— no por elegancia arquitectónica, sino porque es literalmente el primer paso y
hoy falla con datos reales.

### Paso 1 — Ver los movimientos importados

Con el import corregido, este paso ya funciona razonablemente bien:
`GET /api/movements` trae todo lo importado, pendiente y clasificado, con
sugerencias cuando las hay. No requiere cambios de fondo para un usuario nuevo (el
motor de sugerencias no tiene nada que sugerir todavía, porque no hay historial —
comportamiento esperado y correcto).

### Paso 2 — Clasificar el primer lote

Acá es donde hoy se le pide al usuario, por cada movimiento, algo que en muchos
casos ya sabe con certeza porque lo acaba de decidir para el movimiento anterior
idéntico (mismo comercio). El flujo ideal, movimiento por movimiento, en la primera
sesión de uso real:

1. **Cuenta financiera:** no debería preguntarse nunca, si el paso 0 quedó bien
   resuelto (la cuenta/tarjeta de origen es inequívoca desde el propio documento
   importado, ver análisis anterior punto 1). Cero clics.
2. **Categoría:** acá sí corresponde pedir al usuario, la primera vez que ve un
   comercio — no hay atajo legítimo. Pero debería poder **crear la Contraparte y
   asignarle esta categoría como default en el mismo paso**, no navegar a otra
   pantalla (desarrollado en el punto 4).
3. **Tipo de movimiento / Impacto financiero:** para el ~88% de los casos que ya
   identificamos como derivables del prefijo textual del banco (`"PAGO CON VISA
   DEBITO"` → Purchase+Expense, `"TRANSFERENCIA"` → Transfer, `"PAGO DE TARJETA..."`
   → Payment+DebtPayment, `"INTERESES GANADOS"` → Interest+Income, `"PAGO DE
   HABERES"` → Receipt+Income), deberían llegar **pre-completados con una regla
   determinística de prefijo**, no como "sugerencia a confirmar" sino como el valor
   por defecto ya seleccionado en el formulario — el usuario solo actúa si
   discrepa. Para el resto (~12%), se pide como hoy.
4. **Contraparte:** ver punto 4.

### Paso 3 — El resto de ese mismo comercio, en ese mismo lote o en meses futuros

Si en el paso 2 el usuario creó la Contraparte "Mercado Pago - La Coca" con
Categoría/Tipo/Impacto por defecto, **todas las líneas futuras de "MERPAGO*LACOCA"
deberían llegar completamente pre-clasificadas** (Quick Accept, PR-U1, ya reduce
esto a un clic — el paso que falta es que la Contraparte con sus defaults exista en
primer lugar, que es exactamente el punto 4).

### Resultado del flujo ideal

Contando decisiones humanas irreductibles para un usuario que importa su primer mes
real de movimientos (usando los datos reales de este caso: 442 filas de banco + 90
filas de tarjeta aprox.), el número de decisiones genuinas debería acercarse a "una
por comercio nuevo" (a partir de las cifras reales, del orden de 30-40 comercios
distintos, no 500+ decisiones) — no "una por fila". Hoy, sin ninguno de los cambios
de este documento, es una por fila, cuatro veces.

---

## 4. Administración de datos maestros: cuál necesita pantalla propia, cuál se administra en contexto

Analizo las tres entidades por separado, sin asumir que la respuesta es la misma
para las tres.

### 4.1 Categoría → pantalla propia, uso poco frecuente

Ya viene poblada por seed (11 categorías, punto 1.3). El caso de uso real de una
pantalla de Categorías **no** es "crearlas todas desde cero" — es:
- Agregar alguna categoría propia que el seed no cubre (ej. una categoría más
  granular que "Alimentación").
- Reordenar o renombrar el `DisplayName` sin tocar el `Name` técnico (el propio
  doc-comment de `Category.cs` ya explica por qué esto es seguro: la FK apunta al
  `Id`, no al nombre).

Esto es infrecuente por naturaleza (se configura una vez, se toca ocasionalmente) —
tiene sentido como pantalla propia, mirroring `accounts.html`, **sin necesidad de
creación en contexto**: crear una categoría nueva en medio de clasificar un
movimiento sería una interrupción rara vez justificada, porque el seed ya cubre la
mayoría de los casos reales.

### 4.2 Cuenta financiera → pantalla propia, pero además necesita alta cuasi-automática

Ya tiene pantalla (`accounts.html`) y CRUD completo. Lo que falta no es la
pantalla — es que, según el análisis anterior, el vínculo con el import nunca se
resuelve solo. Con el import corregido (punto 3, paso 0), lo ideal es: al detectar
por primera vez un `AccountNumber`/número de tarjeta que no matchea ninguna
`FinancialAccount` existente, el sistema podría **proponer la creación** (no
crearla en silencio: el `Name` visible — "BBVA Caja de Ahorro" vs. un número crudo
— sigue siendo una decisión de producto legítima del usuario) en vez de dejar el
`FinancialAccountId` en null indefinidamente. Es la única de las tres entidades
donde "en contexto, en el momento del import" tiene más sentido que "en medio de
clasificar un movimiento" — porque la cuenta se decide una vez por archivo
importado, no una vez por movimiento.

### 4.3 Contraparte → sin pantalla dedicada como flujo principal; creación en contexto

Acá la respuesta es distinta a las otras dos, y la razón es de dominio, no de
gusto: una Contraparte nueva casi siempre se descubre en el momento de clasificar
un movimiento — el usuario está mirando la descripción real ("MERPAGO*LACOCA"),
tiene el contexto completo (cuánto fue, cuándo, en qué categoría lo está por
poner), y **crearla ahí mismo evita perder ese contexto** al navegar a otra
pantalla y tener que volver a escribir el nombre a mano sin la referencia visible.

Esto no significa "no hacer una pantalla de Contrapartes" — significa que esa
pantalla (igual de necesaria que la de Categorías, para tareas de mantenimiento:
desactivar duplicados, corregir un default mal puesto, fusionar variantes del mismo
comercio) **no debería ser el flujo principal por el que una Contraparte nace.**
El flujo principal debería ser un alta liviana, inline, dentro del modal de
clasificación que ya existe en `movements.html` — sin navegación, con el nombre
del comercio pre-sugerido a partir de la descripción (idealmente ya limpia del
prefijo de pasarela: `MERPAGO*`, `DLO*`, `OPENPAY*`, `PEDIDOSYA*`, que aparecen en
el 100% de los comercios reales de delivery/pagos del extracto Visa analizado).

### 4.4 Sobre el enum `CounterpartyType` (Person/Business/Company/Bank/Service/
Government/OwnAccount/OwnCard/Investment/Other)

Verifiqué con `grep` sobre todo `src/`: **ningún código, en ningún lado, lee el
valor de `CounterpartyType` para tomar una decisión.** Existe la enumeración, se
exige como campo obligatorio al crear (`Create` devuelve `BadRequest` si falta o es
inválido), viaja en el DTO — pero no hay ninguna regla, ni de negocio ni de UI, que
dependa de su valor. Es, literalmente hoy, un campo obligatorio sin ningún
consumidor. Si el objetivo es una alta en contexto de fricción mínima (punto 4.3),
pedir que el usuario elija entre 9 categorías de contraparte para algo que el
sistema no usa para nada es fricción sin retorno — cuestionando el modelo tal como
pediste: **este campo debería dejar de ser obligatorio en el alta en contexto**
(puede seguir existiendo como campo opcional editable después, desde la pantalla de
mantenimiento, para cuando alguien le encuentre un uso real).

---

## 5. Contraparte × Categoría: dónde debería configurarse `Default*`

Cuatro opciones planteadas: pantalla de Contrapartes, durante la clasificación,
automáticamente después de varias clasificaciones, u otro flujo. Las evalúo, no
elijo por default la más obvia.

**Pantalla de Contrapartes, como flujo principal:** descartada. Configurar
"Farmacia Amancay → Categoría Salud" *antes* de haber clasificado nunca un
movimiento de esa contraparte obliga al usuario a decidir sin evidencia — está
adivinando qué va a querer en el futuro, en una pantalla separada del contexto real
(un movimiento concreto, con un importe concreto, en una fecha concreta).

**Automáticamente después de varias clasificaciones, sin intervención:**
descartada como mecanismo único, aunque parcialmente atractiva. El motor de
sugerencias por historial (heurística 1, exact-match de descripción) ya cubre gran
parte de este terreno **sin necesitar `Counterparty.Default*` en absoluto** —
cuando la descripción se repite igual, ya hay sugerencia de Alta confianza sin
haber configurado nada. El valor único de `Counterparty.Default*` (heurística 2)
aparece solo cuando aparece una descripción **nueva** vinculada a una contraparte
**ya conocida** (ej. un comercio que cobra bajo distintos cupones/IDs de operación
cada vez, o un mismo negocio con dos formas de aparecer en el extracto). Automatizar
esto por completo, sin que el usuario lo confirme nunca, arriesga fijar un default
incorrecto a partir de una coincidencia espuria (dos clasificaciones que fueron
igual por casualidad, no por regla).

**Durante la clasificación, como acción explícita pero de un clic — la
recomendada:** en el momento exacto en que el usuario ya eligió Categoría/Tipo/
Impacto para un movimiento con Contraparte asignada, y esos valores **difieren**
del default actual de esa Contraparte (o no tiene default todavía), ofrecer un
checkbox opcional, ya tildado por defecto la primera vez: *"Recordar Categoría
Salud / Compra / Gasto para Farmacia Amancay"*. Es la misma decisión que el usuario
ya tomó, convertida en persistente con una sola marca adicional — no una pantalla
nueva, no una decisión nueva, ningún contexto perdido. Coincide con el mismo
principio ya aplicado en Quick Accept (PR-U1): reducir clasificación repetida a la
menor cantidad de clics posible, sin agregar una superficie de decisión nueva.

**Conclusión: la pantalla de Contrapartes debe poder editar `Default*`
manualmente (para corregir un error o cambiar de opinión), pero no debe ser la
forma en que ese dato nace.** Nace en el momento de clasificar, como una
consecuencia de un clic, no como una tarea aparte.

---

## 6. Experiencia de clasificación: qué pedir, qué inferir, qué recordar, qué aprender

Aplicando el marco pedido (pedir / inferir / recordar / aprender) a los 5 campos
que hoy se muestran (descripción, importe no son decisiones — quedan afuera):

| Campo | Hoy | Debería ser |
|---|---|---|
| Cuenta financiera | Pedir (select vacío, por fila) | **Inferir** en el import, no en la clasificación (punto 3, paso 0) |
| Categoría | Pedir (con sugerencia por historial si existe) | **Pedir** la primera vez por comercio; **recordar** después vía Contraparte.DefaultCategoryId (punto 5) |
| Tipo de movimiento | Pedir (con sugerencia por historial) | **Inferir** por prefijo textual para el ~88% de los casos (punto 2.1 del análisis anterior); pedir solo el resto |
| Impacto financiero | Pedir (con sugerencia por historial) | **Inferir** para los patrones deterministas (pago de resumen, intereses, sueldo); **recordar** vía Contraparte para el resto habitual; pedir solo lo genuinamente ambiguo |
| Contraparte | Pedir, sin forma de crear una nueva | **Pedir** la primera vez (con alta en contexto, punto 4.3); **recordar** automáticamente después (misma descripción normalizada → misma contraparte, algo que hoy ni siquiera está implementado como regla explícita, solo emerge indirectamente si la descripción es idéntica) |

Ninguno de los 5 campos necesita "aprender" en el sentido de ajustar una regla con
el tiempo más allá de lo que las heurísticas de historial (PR-S3/S9/S10) ya hacen
determinísticamente. No hay necesidad de un mecanismo de aprendizaje nuevo — hay
necesidad de que la infraestructura de aprendizaje que ya existe (el motor de
sugerencias) reciba datos completos para trabajar, que es exactamente lo que hoy le
falta.

### Después de 100 movimientos clasificados, ¿qué debería dejar de preguntarse?

Con datos reales (442 filas de banco + ~90 de tarjeta en dos meses, de un solo
usuario), 100 movimientos clasificados cubren, con alta probabilidad, la mayoría de
los comercios recurrentes reales (delivery, supermercado, streaming, combustible —
los que se repiten semana a semana en el extracto real). En ese punto:
- Cuenta financiera: ya debería haber dejado de preguntarse desde el import
  #1 (no depende del volumen clasificado, depende de arreglar el punto 3).
- Tipo/Impacto para comercios ya vistos: deberían llegar pre-completados por
  sugerencia de Alta confianza, con Quick Accept reduciendo la confirmación a un
  clic — esto ya existe (PR-U1), el gap es que hoy tiene poco con qué trabajar.
- Categoría para comercios con Contraparte + Default configurado (punto 5):
  también pre-completada.

### Después de 1000, ¿qué debería desaparecer completamente de la UI?

Acá la pregunta correcta no es "qué campo desaparece" sino "qué **fila** deja de
necesitar el modal completo". Con suficiente historial, la mayoría de las filas de
un mes nuevo van a tener sugerencia de Alta confianza en las 4 dimensiones
simultáneamente. Lo que debería desaparecer no es un campo — es el paso manual
completo para esas filas: una acción de "confirmar todo lo de Alta confianza" a
nivel de **lote completo**, no fila por fila (una extensión natural de Quick
Accept, que hoy ya existe por fila — el roadmap lo retoma en el punto 8). El modal
completo de 4 campos debería seguir existiendo, pero reservado para la minoría
genuinamente nueva o ambigua — nunca desaparece del todo, porque siempre va a
aparecer un comercio nuevo o un movimiento atípico (un reintegro, una operación en
efectivo, un cambio de moneda — los casos reales de la sección 6.4 del análisis
anterior que no mapean limpio a ningún patrón).

---

## 7. Qué cuestiono explícitamente del modelo actual

Sin dejarme llevar por lo ya construido en PR-S*/PR-U*, esto es lo que, mirado de
nuevo desde "¿lo diseñaríamos así hoy, sabiendo lo que sabemos del uso real?", no
sostendría sin cambios:

1. **`CounterpartyType` obligatorio sin ningún consumidor** (punto 4.4) — quitarle
   la obligatoriedad en el alta es una simplificación de bajo riesgo con retorno
   inmediato en fricción.
2. **El gating de importación de `.xls` por nombre de archivo** (punto 1.2) es una
   decisión de diseño que probablemente tenía sentido cuando no existía el
   ruteo por contenido de los PDF (`IStatementParser.CanHandle` sobre fingerprints
   de texto) — hoy es una inconsistencia: los PDF se rutean por contenido, el XLS
   por nombre de archivo, y el nombre de archivo real de BBVA no tiene relación con
   los patrones configurados. No hay razón de dominio para mantener dos
   estrategias de ruteo distintas entre formatos del mismo banco.
3. **La ausencia total de UI de administración para dos de las tres entidades
   maestras (Category, Counterparty) mientras el backend está 100% listo** — no es
   un cuestionamiento del modelo de dominio (el modelo está bien), es la
   constatación de que se invirtió esfuerzo completo en 4 PR de motor de
   sugerencias (S7 a S12) construyendo enriquecimiento sobre un dato
   (`Counterparty.Default*`) que hoy es estructuralmente imposible de cargar salvo
   por API cruda. Con la información que tengo ahora (incluida la del análisis
   anterior), esto no lo atribuiría a una mala decisión de arquitectura en ningún
   PR puntual — el motor en sí está bien hecho — sino a haber priorizado
   profundidad en una capa (sugerencias) antes que completitud en la capa anterior
   (poder cargar los datos que esa capa consume). Es la misma conclusión a la que
   vos ya habías llegado por tu cuenta al pausar después de U4 — este análisis la
   confirma con evidencia de código, no la contradice.
4. **No cuestiono, en cambio, el modelo de 4 dimensiones independientes de
   `ClassifiedMovement`** (Categoría/Impacto/Tipo/Contraparte) — sigue siendo
   correcto, y nada de lo relevado en este documento sugiere colapsarlo en menos
   dimensiones. El problema nunca estuvo en cuántos campos tiene el modelo: estuvo
   en cuántos de esos campos el usuario tiene que llenar a mano cada vez.

---

## 8. Recomendación de la próxima épica

### Nombre propuesto

**Épica O — Cierre del circuito de datos maestros** (continúa la numeración de
letra usada en el roadmap del proyecto, que llegó hasta N).

### Objetivo

Que un usuario nuevo pueda ir de "carpeta vacía, base de datos recién migrada"
hasta "mes real completamente clasificado" sin encontrarse con ningún punto donde
el sistema le pida un dato que ya tiene en otro lado, o le impida cargar un dato
que necesita. Explícitamente **no** es una épica de motor de sugerencias — no
toca `ClassificationSuggestionService` salvo donde haga falta consumir datos
nuevos que antes no existían (Contrapartes reales, cuenta financiera resuelta).

### Alcance

Dentro:
1. Ruteo de `.xls` por contenido, no por nombre de archivo (elimina el bloqueo del
   punto 1.2).
2. Pantalla de administración de Contrapartes (mirror de `accounts.html`) +
   alta en contexto desde el modal de clasificación de `movements.html`.
3. Pantalla de administración de Categorías (mismo patrón, menor prioridad que
   Contrapartes porque ya viene poblada por seed).
4. Checkbox "recordar como default de esta contraparte" en el modal de
   clasificación (punto 5) — requiere backend mínimo: un solo endpoint adicional o
   reutilizar `PUT /api/counterparties/{id}` ya existente desde el frontend.
5. Wiring automático de cuenta financiera en el import (banco: cruzar
   `BankStatement.AccountNumber` contra `FinancialAccount.AccountNumber`; tarjeta:
   extraer el número de cuenta/tarjeta del texto del PDF, ya identificado como
   literal y determinístico en el análisis anterior).
6. Quitar la obligatoriedad de `CounterpartyType` en el alta en contexto (puede
   seguir siendo editable después).

Fuera de alcance (explícitamente, para no repetir el patrón de invertir en la capa
equivocada):
- Cualquier cambio al motor de sugerencias en sí (`ClassificationSuggestionService`)
  más allá de lo estrictamente necesario para que empiece a recibir datos que hoy
  no existen.
- Reglas de inferencia de Tipo/Impacto por prefijo textual (punto 2 del análisis
  anterior) — tiene valor real, pero depende menos de que el usuario pueda operar
  el sistema día a día, y puede ser una épica separada posterior sin bloquear esta.
- Confirmación en lote de sugerencias de Alta confianza a nivel de lista completa
  (mencionado en el punto 6) — es una extensión natural de Quick Accept, pero solo
  tiene sentido con volumen de historial real, que esta épica es justamente la que
  habilita a generar.

### Beneficios

- Habilita, por primera vez, un ciclo de uso real completo con datos reales de
  este usuario (los mismos 4 documentos de este análisis, hoy parcialmente
  bloqueados en el import).
- Le da datos reales al motor de sugerencias que ya está construido y ya
  funciona — el retorno de las 4 PR de S7-S12 se realiza recién cuando existan
  Contrapartes reales con defaults reales, no antes.
- Reduce fricción de clasificación sin agregar ninguna heurística nueva: el
  camino elegido en el punto 5 (checkbox de un clic, no una pantalla de
  configuración separada) evita construir una superficie de decisión nueva.

### Riesgos

- El wiring automático de cuenta financiera para tarjeta (ítem 5) requiere tocar
  los parsers de PDF para extraer el número de cuenta del texto — no es un cambio
  de UI, es un cambio de parsing con superficie de tests más amplia que el resto
  de la épica. Debería ir como PR separado y último, no bloqueando el resto.
- Alta en contexto de Contraparte desde `movements.html` toca el mismo modal
  compartido que ya sostiene PR-U1 a PR-U4 (individual + lote) — hay que verificar
  con cuidado que el modo lote (varios movimientos con contrapartes distintas
  potencialmente nuevas) no complique la UX de alta en contexto; puede ser
  razonable limitar el alta en contexto al modo individual únicamente en una
  primera versión.
- Ninguno de estos cambios es reversible sin costo una vez que haya datos reales
  cargados (una Contraparte creada con un `Default*` mal puesto queda en el
  historial) — justifica ir con PRs chicos y probarlos con datos reales entre uno
  y otro, no un cambio grande de una sola vez.

### Dependencias

- Ítem 1 (ruteo por contenido) no depende de nada — es el primero por orden lógico
  (ver roadmap).
- Ítems 2 y 3 (pantallas CRUD) no dependen entre sí ni de nada más que el backend
  ya existente.
- Ítem 4 (checkbox de default) depende de que exista la pantalla de Contrapartes
  (ítem 2) — necesita, como mínimo, poder ver y corregir manualmente lo que el
  checkbox va guardando.
- Ítem 5 (wiring de cuenta) es independiente de los ítems 2-4, pero de mayor
  riesgo — puede ir en paralelo o al final, no bloquea ni es bloqueado por ellos.
- Ítem 6 (quitar obligatoriedad de `CounterpartyType`) depende de la existencia
  del formulario de alta en contexto (ítem 2), porque es ahí donde se sentiría la
  fricción que se está evitando.

---

## 9. Roadmap — PRs pequeños, autocontenidos, mergeables

Mismo criterio ya usado en toda la Épica S/U: cada PR tiene un único objetivo, bajo
riesgo, se puede mergear solo, y no requiere que el siguiente exista para tener
valor por sí mismo.

### PR-O1 — Ruteo de `.xls` BBVA por contenido, no por nombre de archivo

**Objetivo único:** que `BbvaBankStatementImportHandler.CanHandle` deje de
depender de un glob sobre el nombre del archivo y pase a inspeccionar la primera
fila del XLS (`"Detalle de Movimientos de Cuenta: CA$ ..."`), igual criterio que
ya usan los `IStatementParser` de PDF sobre su propio contenido.

**Por qué primero:** es el único punto de todo este documento que bloquea
literalmente el primer paso de cualquier flujo. No tiene sentido planificar nada
del resto del roadmap si el archivo de origen ni siquiera entra al sistema.

**Riesgo:** bajo — el cambio es local a un único método de detección, no toca el
parser en sí (`BbvaBankStatementParser`, que ya probamos que funciona con los
archivos reales), no toca el modelo de datos.

**Justificación arquitectónica:** unifica el criterio de ruteo entre los dos
formatos que hoy usan estrategias distintas para el mismo problema (identificar la
fuente por contenido) — reduce, no agrega, superficie conceptual.

### PR-O2 — Mensajes de diagnóstico de import legibles en `imports.html`

**Objetivo único:** que un archivo rechazado muestre, en la pantalla que ya existe,
un motivo entendible por un usuario no técnico, no el mensaje interno actual.

**Por qué en este orden:** depende de PR-O1 solo en que, después de O1, el
universo de rechazos legítimos cambia (menos falsos negativos) — pero es
autocontenido: mejora la experiencia incluso para los rechazos que sigan siendo
legítimos (archivo corrupto, banco no soportado).

**Riesgo:** bajo — cambio de presentación, no de lógica de negocio.

### PR-O3 — Pantalla de administración de Contrapartes

**Objetivo único:** CRUD completo (listar, crear, editar, desactivar/reactivar),
mirror exacto del patrón ya usado en `accounts.html`, incluyendo los 3 campos
`Default*`. Sin alta en contexto todavía — eso es PR-O5.

**Por qué en este orden:** es el bloqueo más citado de todo el análisis (punto 4.1
del documento anterior) y no depende de ningún otro PR de este roadmap — puede
mergearse en cualquier momento después de O1/O2, pero se beneficia de ir antes de
O5 porque da un lugar donde verificar/corregir lo que el alta en contexto vaya
creando.

**Riesgo:** bajo — el backend ya existe sin cambios, es la misma clase de PR que
`accounts.html` ya demostró como patrón seguro.

**Justificación arquitectónica:** cierra la brecha entre backend completo (desde
PR-S7) y UI inexistente, sin tocar el backend.

### PR-O4 — Pantalla de administración de Categorías

**Objetivo único:** igual que O3, pero para `Category` (sin `Default*`, con
`SortOrder`/`IsSystem`/`ParentId` reservado para jerarquía futura — sin exponer
`ParentId` en esta pantalla, ya que hoy siempre es null y no hay UI de jerarquía).

**Por qué después de O3, no antes:** menor urgencia (Categoría ya viene poblada
por seed, punto 1.3) — prioricé Contraparte primero porque es la entidad
genuinamente vacía y bloqueante.

**Riesgo:** bajo, mismo patrón que O3.

### PR-O5 — Alta de Contraparte en contexto, desde el modal de clasificación individual

**Objetivo único:** agregar una opción "+ Nueva contraparte" dentro de
`movements.html`, en modo individual únicamente (no en modo lote, por el riesgo ya
señalado en la sección 8), con el nombre pre-sugerido a partir de la descripción
del movimiento.

**Por qué después de O3:** reutiliza el mismo endpoint (`POST /api/counterparties`)
que O3 ya deja probado en producción vía la pantalla dedicada — reduce el riesgo de
introducir un segundo camino de creación sin haber validado primero el camino
principal.

**Riesgo:** medio — es el único PR de este roadmap que modifica el modal
compartido entre modo individual y lote (el mismo que PR-U1 a PR-U4 ya tocaron
varias veces) — requiere el mismo cuidado de diff dirigido y verificación byte a
byte ya establecido como práctica en esta serie de PRs.

**Justificación arquitectónica:** evita el patrón "navegar lejos y perder
contexto" identificado en el punto 4.3 — sin introducir ningún mecanismo nuevo de
persistencia, solo un atajo de UI sobre un endpoint ya existente y ya probado.

### PR-O6 — Checkbox "recordar como default de esta contraparte" en el modal de clasificación

**Objetivo único:** al clasificar un movimiento con Contraparte asignada, si los
valores elegidos de Categoría/Tipo/Impacto difieren del `Default*` actual (o no
existe), ofrecer un checkbox opcional (tildado por defecto la primera vez) que
dispara `PUT /api/counterparties/{id}` con los nuevos defaults, además del
`POST /api/movement-review/classify` normal.

**Por qué después de O3 y O5:** depende de que exista un lugar (O3) para revisar/
corregir lo que este checkbox va guardando, y de que ya sea razonablemente fácil
crear una Contraparte (O5) para que este PR tenga con qué trabajar en la práctica.

**Riesgo:** bajo-medio — dos escrituras en la misma acción de usuario (classify +
update de counterparty); hay que decidir explícitamente qué pasa si una de las dos
falla (recomendación: la clasificación del movimiento nunca debe fallar ni
revertirse por un error al guardar el default — son operaciones independientes,
con manejo de error separado).

**Justificación arquitectónica:** no crea ningún mecanismo de "aprendizaje" nuevo
— reduce a un clic algo que hoy ya es posible hacer manualmente vía la pantalla de
Contrapartes (O3), sin duplicar lógica de negocio en el frontend.

### PR-O7 — Quitar obligatoriedad de `CounterpartyType` en el alta

**Objetivo único:** que `POST /api/counterparties` acepte `Type` ausente (default
razonable, ej. `Other`), y que el formulario de alta en contexto (O5) no lo pida.
La pantalla de administración (O3) puede seguir mostrándolo como campo editable
opcional.

**Por qué al final:** es el de menor urgencia y menor riesgo de todo el roadmap —
tiene sentido hacerlo una vez que O5 ya esté en uso real y se pueda confirmar que,
en efecto, nadie lo completa con intención (validación empírica de la hipótesis del
punto 4.4, no solo de lectura estática de código).

**Riesgo:** muy bajo — relaja una validación existente, no agrega ninguna.

### PR-O8 — Wiring automático de cuenta financiera (banco)

**Objetivo único:** al importar un XLS de banco, si `BankStatement.AccountNumber`
coincide (match exacto o normalizado) con el `AccountNumber` de una
`FinancialAccount` existente, asignar `FinancialAccountId` automáticamente al
persistir — sin tocar el caso "no hay ninguna cuenta con ese número todavía" (eso
puede quedar en null como hoy, a la espera de que el usuario cree la cuenta desde
O3-equivalente para `FinancialAccount`, que ya existe como `accounts.html`).

**Por qué después de O3/O4, no antes:** de nada sirve cruzar contra
`FinancialAccount.AccountNumber` si `FinancialAccount` en sí sigue sin ningún dato
cargado con ese número — depende de que el usuario ya haya usado `accounts.html`
(que ya existe hoy, sin cambios) para cargar sus cuentas reales.

**Riesgo:** bajo — cambio acotado al pipeline de import de banco, con un fallback
seguro (null) si no hay match, igual que el comportamiento actual.

### PR-O9 — Wiring automático de cuenta financiera (tarjeta)

**Objetivo único:** extraer el número de cuenta/tarjeta del texto de los PDF de
Visa/Mastercard (`"Mastercard Black cuenta 1278939005"`, `"Visa Signature cuenta
1278896210"` — ya confirmado como texto literal en los dos documentos reales) y
aplicar el mismo cruce que PR-O8, agregando el campo de identificador a
`Transaction`.

**Por qué al final:** es el único PR de todo el roadmap que toca el modelo de
datos (nueva columna/migración) y los parsers de PDF (mayor superficie de tests
que cualquier otro ítem) — el de mayor riesgo real del roadmap, y el que menos
bloquea al resto (todo lo anterior funciona sin él).

**Riesgo:** medio-alto — requiere migración de base de datos y cambios en dos
parsers (`BbvaVisaStatementParser`, `BbvaMastercardStatementParser`) más su
importador — el único PR de esta lista que amerita, como mínimo, la misma
verificación byte a byte contra los archivos reales que ya usamos para validar los
hallazgos de este análisis.

---

## Nota final sobre alcance

No incluí en este roadmap ninguno de los hallazgos puramente correctivos del
análisis anterior que no dependen de esta épica (el bug de detección de USD sin
borde de palabra, la normalización de cuotas, el riesgo de ruteo Visa/Mastercard) —
siguen vigentes y valdría la pena resolverlos, pero son independientes de "cerrar
el circuito de datos maestros" y no deberían competir por el mismo espacio de
revisión. Si querés, puedo priorizarlos como una segunda tanda de PRs correctivos,
más chicos y de menor riesgo cada uno, en paralelo o inmediatamente después de esta
épica.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no ejecuté `git
add`, no hice ningún commit ni push. Todo el trabajo fue lectura de código en
`origin/master` (vía `git show`) y una búsqueda (`grep`) de uso de
`CounterpartyType` en todo `src/` para verificar la ausencia de consumidores antes
de afirmarlo.
