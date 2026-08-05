# Análisis de simplificación del modelo de clasificación — intento de refutación

Commit base: `origin/master` (`01a7163`). Documento de solo análisis: no se
modificó ningún archivo del repositorio. Este documento parte explícitamente
de intentar **refutar**, no confirmar, la conclusión de un análisis anterior
("Cuenta financiera no debería ser decisión del usuario", "Tipo de
movimiento e Impacto financiero representan casi siempre la misma
información", "CounterpartyType nació para identificar cuentas propias").
Releí en esta sesión, específicamente para buscar evidencia en contra: el
dominio completo (`MovementType.cs`, `FinancialImpact.cs`,
`ClassifiedMovement.cs`, `Counterparty.cs`), `ClassificationSuggestionService.cs`
completo, `ClassifiedMovementConfiguration.cs`, **`FinancialMetricsService.cs`
completo** (el backend real del Dashboard), **`FinancialTools.cs` completo**
(las herramientas reales expuestas al MCP/LLM), los DTOs y endpoints, y la
documentación existente del proyecto — incluido **ADR-001** (la decisión
formal que fija las 4 dimensiones) y la Épica N del roadmap, ambos
invocados acá específicamente porque son la evidencia más fuerte que
encontré *en contra* de mi conclusión anterior, y que reporto con honestidad
aunque no terminen sosteniéndola.

**Resultado del intento de refutación, adelantado**: no encontré evidencia
que sostenga que `MovementType` sea una decisión independiente en la
práctica. Encontré, en cambio, evidencia nueva y más fuerte de lo que tenía
antes — incluyendo que los dos consumidores que el propio ADR-001 nombra
como razón para no tocar el modelo (`FinancialMetricsService` y
`FinancialTools`) **no usan `MovementType` ni una sola vez**. Desarrollo
esto con evidencia exacta abajo.

---

## Parte 1 — Los 9 valores de `MovementType`, uno por uno

Metodología: para cada valor, busqué con `git grep` cada lugar del código
donde se *lee* (no solo se persiste) el valor de `MovementType`, para
determinar si alguna decisión de negocio depende de él.

| Valor | Qué representa | Decisión de negocio que habilita | Quién lo consume realmente | ¿Independiente de `FinancialImpact`? | ¿Podría inferirse? | ¿Desaparecería si se rediseñara hoy? |
|---|---|---|---|---|---|---|
| **Purchase** | Compra de bien/servicio | Ninguna verificada | Nadie — se persiste, se muestra como chip, se sugiere; ningún cálculo lo lee | No — co-ocurre casi siempre con `Expense` (verificado: 210 filas reales de "PAGO CON VISA DEBITO" + todo "Consumos" de tarjeta) | Sí, con altísima confianza (sección del documento + prefijo textual) | Sí, como pregunta |
| **Transfer** | Movimiento de fondos entre cuentas/personas | La única realmente genuina: decide si el efecto es interno o real | Nadie hoy de forma automática (Impacto se pide aparte, sin derivarse de esto) | **Sí, es el caso más claro de independencia real** | El hecho de ser transferencia sí (100% de los casos reales analizados usan el prefijo "TRANSFERENCIA"); el destino (propio/tercero) no, sin un dato adicional | Como dropdown de 9 opciones, sí; el concepto sobrevive como una pregunta puntual distinta |
| **Payment** | Pago de deuda/factura/servicio | Ninguna verificada | Nadie | No — co-ocurre determinísticamente con `DebtPayment` cuando el texto dice "PAGO DE TARJETA..." (verificado: el monto exacto del XLS coincide centavo a centavo con el PDF de cada tarjeta) | Sí, mismo prefijo textual | Sí |
| **Receipt** | Cobro por venta/servicio | Ninguna verificada | Nadie | No — co-ocurre con `Income` | Sí (signo positivo + ausencia de otros patrones) | Sí |
| **Fee** | Comisión bancaria | Ninguna verificada (aunque "cuánto pagué de comisiones" sería un reporte razonable — no existe hoy) | Nadie | No — co-ocurre con `Expense` | Sí, del vocabulario de cargos ya visible en los extractos reales | Se mantendría como etiqueta calculada para un reporte futuro, no como pregunta |
| **Interest** | Interés ganado o pagado | El único otro caso, junto con Transfer, con independencia real de dirección | Nadie hoy | Parcialmente — pero el signo del importe ya resuelve "ganado vs pagado" sin necesitar este campo, y el signo nunca es ambiguo (a diferencia de la elección manual) | Sí, completamente, del signo (verificado: "INTERESES GANADOS" siempre positivo en los datos reales) | Sí, sin pérdida real de información |
| **Refund** | Reintegro de un gasto anterior | Debería reducir el gasto de la categoría original — pero **verificado en `FinancialMetricsService.cs`: no hay ninguna lógica que trate un Refund distinto de cualquier otro Income** | Nadie, ni siquiera donde conceptualmente debería importar | En principio sí, en la práctica no se aprovecha en ningún lado | Parcialmente (texto real: "BONIF. CONSUMO..." con signo negativo dentro de una sección de consumos) | El concepto tiene valor futuro ligado a Categoría, no como una opción más de una lista de 9 |
| **Adjustment** | Corrección contable | Ninguna — catch-all sin regla clara | Nadie | No verificable, es residuo por definición | No de forma confiable | Probablemente se fusiona con Other |
| **Other** | Catch-all | Ninguna | Nadie | — | No | Es la válvula de escape del enum, no una categoría con contenido propio |

**Evidencia de "nadie lo consume", en el sentido más literal**: `git grep
-n "MovementType"` sobre todo `src/` da 100 coincidencias — revisadas una
por una, son declaraciones de propiedad, DTOs, migraciones, índices,
`<option>` de HTML, y el motor de sugerencias tratándolo como una dimensión
más a sugerir. **Ninguna coincidencia es un `if`/`switch`/`Where` que decida
algo distinto según cuál de los 9 valores sea.** El motor de sugerencias lo
sugiere; nadie lo usa después de sugerido.

---

## Parte 2 — Los 4 valores de `FinancialImpact`, mismo ejercicio

| Valor | Qué representa | Decisión de negocio que habilita | Quién lo consume realmente | ¿Independiente de `MovementType`? | ¿Podría inferirse? | ¿Desaparecería? |
|---|---|---|---|---|---|---|
| **Expense** | Gasto real | **Es literalmente el filtro de `GetPeriodSummaryAsync`, `GetExpensesByCategoryAsync`, `GetMonthlyTrendAsync` y `CompareWithPreviousMonthAsync`** — los 4 métodos de `FinancialMetricsService.cs`, sin excepción, y por lo tanto de `GetMonthlySummary`/`GetExpensesByCategory` en `FinancialTools.cs` (que delegan a ese mismo servicio) | Todo el Dashboard, todo el MCP | No realmente — casi sinónimo de "signo negativo + no es transferencia propia + no es pago de deuda" | En gran parte sí (el signo siempre se conoce) | No — es el corazón funcional del sistema. La pregunta no es si el dato debe existir, es si el usuario debe elegirlo a mano |
| **Income** | Ingreso | Mismo rol que Expense, para el signo contrario — consumido en los mismos 4 métodos | Igual de indispensable | Igual que Expense | Igual que Expense | No |
| **InternalMovement** | Movimiento entre cuentas propias | Evitar que una transferencia a mí mismo infle "cuánto gasté"/"cuánto gané" — se ve en que ninguno de los 4 métodos lo suma a nada (exclusión implícita, verificado leyendo cada `Where`) | Los mismos 4 métodos, por omisión | Depende de un dato que hoy no existe en ningún lado del sistema: si la contraparte es una cuenta propia | Parcialmente, ligado a ese mismo dato faltante | No, es genuinamente necesario |
| **DebtPayment** | Pago de deuda ya contada como gasto antes | Evita duplicar como gasto un consumo de tarjeta ya contado — verificado con el ejemplo real (pago de resumen Visa/Mastercard, monto exacto) | Mismos 4 métodos, por omisión | Altamente inferible por texto ("PAGO DE TARJETA...") | Sí, con confianza muy alta | No |

**Conclusión de la comparación directa**: `FinancialImpact` tiene
consumidores reales, verificados, en el código que efectivamente calcula
todo lo que el producto le muestra al usuario. `MovementType` no tiene
ninguno. Esto no es una intuición — es el resultado de leer
`FinancialMetricsService.cs` (147 líneas) y `FinancialTools.cs` completos y
contar, literal y exhaustivamente, cuántas veces cada uno aparece en una
condición que cambia el resultado.

---

## Parte 3 — Dónde aparecen juntos, y si son dos dimensiones reales o una consecuencia de la otra

**Todos los lugares donde ambos campos coexisten** (relevado explícitamente,
no de memoria):

1. `ClassifiedMovement.cs` — dos propiedades independientes, ambas
   requeridas.
2. `ClassifiedMovementConfiguration.cs` — cada uno con su propio índice
   simple (`HasIndex(x => x.MovementType)`, `HasIndex(x => x.FinancialImpact)`),
   más un índice compuesto `(CounterpartyId, FinancialImpact)` — ninguno de
   los dos índices individuales tiene un consumidor de consulta verificado
   (no hay ningún `Where`/filtro por `MovementType` en todo el código; el de
   `FinancialImpact` sí se usa, pero siempre solo, nunca combinado con
   `CounterpartyId` en ninguna consulta real encontrada).
3. `ClassifyMovementCommand`/`ClassifyMovementHandler` — ambos llegan como
   parámetros independientes, ambos se asignan tal cual, sin ninguna
   validación cruzada entre sí.
4. `Counterparty.DefaultMovementType`/`DefaultFinancialImpact` — dos campos
   opcionales paralelos, ambos consumidos igual por el motor de sugerencias.
5. `ClassificationSuggestionService.BuildSuggestions`/`AddDimensionSuggestion`
   — trata las 4 dimensiones (`Category`, `MovementType`, `FinancialImpact`,
   `Counterparty`) con el mismo algoritmo genérico, sin ningún trato especial
   que reconozca una relación entre `MovementType` y `FinancialImpact`.
6. `movements.html` — dos `<select>` independientes en el mismo modal
   (`#cMovementType`, `#cImpact`), sin que elegir uno modifique el otro.
7. `ADR-001-cuatro-dimensiones-clasificacion.md` — los fija como dos de las
   4 dimensiones "independientes" del modelo, por decisión explícita.
8. `docs/RoadMaps/FinancialMcp-vNext.md`, Épica N ("Simplificación del
   formulario de clasificación") — planificada, no implementada:
   *"Derivar `FinancialImpact` por defecto para los `MovementType` no
   ambiguos, sin eliminar el campo."*

**El punto 8 merece desarrollo aparte, porque es la evidencia más fuerte que
encontré en contra de mi conclusión anterior**, y la trato con la seriedad
que corresponde en vez de descartarla.

### Lo que dice la Épica N, y por qué no la contradigo del todo

La Épica N ya planificada asume la dirección de causalidad
"`MovementType` → deriva → `FinancialImpact`" — el usuario seguiría
eligiendo Tipo siempre, y el sistema le ahorraría el segundo clic
(Impacto) para los casos "no ambiguos". Es un paso real de simplificación,
ya identificado por el proyecto antes de este documento, y coincide en el
diagnóstico de fondo (la Parte 1 de arriba, fila por fila, confirma
exactamente lo que la propia frase "para los `MovementType` no ambiguos"
da por sentado: que la mayoría de los valores de Tipo son mecánicamente
predecibles).

Donde diverjo es en la dirección y en el alcance. La propia Épica N, al
calificar "no ambiguos", ya reconoce implícitamente que algunos valores de
Tipo SÍ son ambiguos respecto de Impacto (el caso de Transfer, exactamente
el que identifiqué en la Parte 1 como la única independencia real) — pero
sigue dejando ese caso ambiguo resuelto con el mismo dropdown técnico de 9
opciones, en vez de resolverlo con el dato que realmente lo determina (si
la contraparte es una cuenta propia). Además, "sin eliminar el campo"
significa que el usuario seguiría eligiendo Tipo para el 100% de los
movimientos, incluidos los 7 de 9 valores que la propia Parte 1 muestra como
mecánicamente derivables — solo dejaría de elegir Impacto después. Eso
reduce el trabajo a la mitad de los casos simples, pero no ataca el caso
donde de verdad hace falta pensar (Transfer), ni evita la pregunta técnica y
poco intuitiva de elegir entre 9 valores.

**No descarto la Épica N — la leo como el mismo diagnóstico, con un alcance
más conservador que el que yo propondría.** Es compatible con ir más lejos,
no una razón para no hacerlo.

### Verificación explícita de la afirmación de ADR-001

ADR-001 justifica mantener el modelo actual, en parte, diciendo que
*"`FinancialMetricsService` y los 4 `FinancialTools` del MCP siguen siendo
válidos sin cambios de contrato"* con las 4 dimensiones fijas. Fui a
verificar esa afirmación directamente en el código que nombra, buscando
específicamente evidencia que refutara mi hipótesis:

```
$ git grep -n "MovementType" -- hosts/FinancialSystem.McpServer/Tools/FinancialTools.cs
(sin resultados)
```

`FinancialTools.cs` no usa `MovementType` una sola vez. Tampoco
`FinancialMetricsService.cs` (confirmado leyendo el archivo completo,
147 líneas, los 4 métodos públicos). La afirmación del ADR es técnicamente
cierta (nada se rompe si el modelo sigue igual), pero **no es evidencia de
que `MovementType` sea necesario** — es evidencia de que esos dos
componentes, los mismos que el ADR cita como razón para no tocar el modelo,
funcionan hoy sin leerlo jamás.

### Respuesta directa a la Parte 3

Ambos campos aparecen juntos en 8 lugares distintos del código y la
documentación. En ninguno de esos 8 lugares encontré evidencia de que
aporten dos decisiones independientes en la práctica — encontré, en
cambio, evidencia repetida de que 7 de 9 valores de `MovementType` son
consecuencia mecánica de `FinancialImpact` (o del signo del importe, que es
más confiable que cualquiera de los dos campos), y que los 2 valores donde
sí hay independencia real (`Transfer`, `Interest`) se resuelven mejor con
datos que no son ninguno de los dos campos actuales (si la contraparte es
propia; el signo del importe).

---

## Parte 4 — `CounterpartyType`: no los consumidores actuales, el modelo mínimo necesario

Razonando desde cero, sin mirar qué usa el código hoy: ¿qué necesita saber
el sistema sobre una Contraparte que no pueda saber de otra forma?

Repasé el problema real que motivaría distinguir tipos de contraparte —
derivar `FinancialImpact` automáticamente — y llegué a esto:
"¿es esta contraparte una de mis propias cuentas?" no alcanza con un sí/no
simple, porque **pagar mi propia tarjeta de crédito y transferir a mi propia
caja de ahorro no son el mismo caso**, aunque las dos sean, en cierto
sentido, "yo mismo". Ya está resuelto en el dominio actual, con datos
reales: `FinancialImpact.DebtPayment` (pagar una tarjeta) es distinto de
`FinancialImpact.InternalMovement` (mover plata entre cuentas propias) —
uno salda un pasivo, el otro mueve un activo. Verificado con los datos
reales de este mismo proyecto: "PAGO DE TARJETA VISA/MASTERCARD" nunca se
trató como movimiento interno, siempre como pago de deuda.

Eso significa que el modelo mínimo necesario **no es un booleano** — hace
falta distinguir, como mínimo, tres estados:

1. **Tercero genuino** (el caso por defecto, sin marca).
2. **Cuenta propia de activo** (otra cuenta bancaria mía — habilita derivar
   `InternalMovement`).
3. **Cuenta propia de pasivo/deuda** (una tarjeta o préstamo mío — habilita
   derivar `DebtPayment`).

Esto no son diez tipos, y tampoco es ninguno — son exactamente **dos
marcas especiales** sobre un tercer estado por defecto (nada). Y acá está
lo que no esperaba encontrar al razonar desde cero, sin mirar el código: **esos
dos valores ya existen, literalmente, en el enum actual** — `OwnAccount`
("Cuenta propia del mismo usuario en otro banco/entidad") y `OwnCard`
("Tarjeta de crédito propia, para registrar pagos de resumen"). No los
inventé — los redescubrí por necesidad funcional, sin partir de qué había
en el código.

**¿Y los otros 7 valores?** (`Person`, `Business`, `Company`, `Bank`,
`Service`, `Government`, `Investment`). Ninguno de los 7 resuelve un
problema que `Category` no resuelva ya desde un ángulo más útil. "¿Le pagué
a un organismo de gobierno?" es, en el fondo, la misma pregunta que "¿esto
fue en la categoría Impuestos?" — Category ya responde "para qué se usó el
dinero" con más granularidad y con una interfaz que ya existe. Distinguir
si la contraparte "es" una Persona, una Empresa o un Servicio no habilita
ninguna decisión que no habilite ya, mejor, la Categoría.

**Veredicto de la Parte 4**: el modelo mínimo necesario para
`CounterpartyType` son exactamente los 2 valores que ya identifiqué como el
único propósito real de la entidad (`OwnAccount`/`OwnCard`), más el estado
implícito "ninguno de los dos" para todo lo demás. Los otros 7 valores no
tienen, ni buscando desde cero sin mirar consumidores, ningún problema de
negocio propio que resuelvan.

---

## Parte 5 — Modelo mínimo si FinancialMcp se diseñara hoy desde cero

Sin copiar el modelo actual campo por campo — reconstruyendo desde la
pregunta "¿qué necesita saber el sistema para responder cuánto gasté, en
qué, y con quién, sin perder ningún caso real ya identificado en los 4
documentos bancarios de este proyecto?".

| Campo | Por qué existe | Qué problema resuelve | Quién lo usa | ¿Lo decide el usuario? |
|---|---|---|---|---|
| **Fecha, Descripción, Importe, Moneda** | Son el hecho crudo, no una clasificación | Trazabilidad e identidad del movimiento | Todo | No — siempre vienen del banco |
| **Cuenta financiera** | Identidad de origen | Reporting futuro por cuenta/tarjeta | Nada hoy, potencialmente futuro | **No, casi nunca** — se resuelve solo desde el documento importado; el usuario solo la nombra una vez, la primera vez que una cuenta nueva aparece |
| **Categoría** | "Para qué se usó el dinero" | Es el eje real de "en qué gasto" — el único que hoy tiene un consumidor de reporting con contenido semántico específico (`GetExpensesByCategoryAsync`) que ninguna otra dimensión puede reemplazar | Dashboard (gastos por categoría), MCP | **Sí, pero solo para Gasto/Ingreso genuinos** — no para transferencias internas ni pagos de deuda, donde no tiene un "para qué" real |
| **Contraparte** (con la marca "cuenta propia de activo / de pasivo / ninguna") | "Con quién se relaciona" + el dato que permite derivar el efecto patrimonial de una transferencia sin preguntarlo | Habilita default de Categoría; habilita derivar Impacto para transferencias y pagos de deuda sin preguntarlo por movimiento | Motor de sugerencias, derivación automática de Impacto | El nombre, sí, la primera vez (una sola vez por comercio/cuenta). La marca de "cuenta propia", una vez por contraparte, no por movimiento |
| **Efecto patrimonial** (equivalente a hoy `FinancialImpact`, en lenguaje llano: Gasto / Ingreso / Transferencia entre mis cuentas / Pago de deuda) | Es el único de los dos campos técnicos actuales con consumidores reales y verificados — el corazón de cada cálculo del Dashboard y del MCP | Responde "¿cuánto gasté, cuánto gané, cuánto es solo mío moviéndose?" | Los 4 métodos de métricas, los 2 `FinancialTools` reales | **Rara vez** — se deriva del signo del importe (siempre cierto) + la marca de Contraparte + patrones de texto ya verificados con datos reales (~88% de cobertura). Se pregunta solo cuando ninguna de esas tres fuentes alcanza |
| **Naturaleza técnica** (equivalente a hoy `MovementType`, pero nunca presentado como pregunta) | Detalle interno para auditoría o reporting futuro más granular (distinguir una comisión de un gasto común dentro de "Expense", por ejemplo) | Ningún consumidor real hoy — se conserva como metadato calculado, no como decisión | Nadie con evidencia real hoy; candidato a reporting futuro | **No, nunca directamente** — se calcula del mismo texto/patrón que ya resuelve Efecto patrimonial; queda visible para quien quiera auditar el detalle de un movimiento puntual, editable si el cálculo se equivocó, pero no bloquea nunca la clasificación |

Ningún dato del modelo actual desaparece de la base — `MovementType`
sigue existiendo como columna, con el mismo contenido semántico de hoy.
Lo que cambia es exclusivamente **quién decide su valor**: hoy lo decide el
usuario, siempre, por cada movimiento; en este modelo lo decide el sistema,
siempre, salvo el residuo genuinamente ambiguo, que además ya no se
pregunta con el vocabulario técnico de "Tipo de movimiento" sino con el
lenguaje llano de "Efecto patrimonial".

---

## Resultado

### 1. Qué partes del modelo actual son indispensables

- **Las 4 dimensiones como concepto de dominio** (Categoría, Impacto,
  Naturaleza técnica, Contraparte) — ADR-001 tiene razón en que ninguna
  debería colapsarse a nivel de *dato almacenado*; cada una responde una
  pregunta real y distinta ("para qué", "cómo afecta mi patrimonio", "qué
  clase de operación técnica fue", "con quién").
- **`FinancialImpact` como campo con contenido semántico real** — es,
  verificado con evidencia exhaustiva, el único de los dos campos técnicos
  actuales con consumidores reales en producción (`FinancialMetricsService`,
  `FinancialTools`).
- **`Category`, como decisión genuinamente humana** para el subconjunto de
  movimientos que son Gasto o Ingreso reales.
- **El registro determinístico (`InternalMovement`/`DebtPayment`) de que un
  movimiento no debe contar como gasto/ingreso real** — sin esto, el
  Dashboard mentiría, literalmente, sumando pagos de tarjeta como si fueran
  gasto nuevo.

### 2. Qué partes podrían simplificarse

- **`MovementType` como pregunta independiente, siempre presentada al
  usuario** — sin consumidor real verificado en ningún lado del sistema;
  7 de 9 valores son consecuencia mecánica de `FinancialImpact` o del signo
  del importe; los 2 valores con independencia real (`Transfer`, `Interest`)
  se resuelven mejor con datos que ya existen en otro lugar.
- **`CounterpartyType` como taxonomía de 10 valores obligatoria** — el
  modelo mínimo necesario, razonado desde cero, son exactamente 2 marcas
  especiales (cuenta propia de activo / de pasivo) sobre un estado por
  defecto; los otros 7 valores no resuelven ningún problema que Categoría
  no resuelva ya.
- **Cuenta financiera como decisión por movimiento** — el dato ya existe en
  el documento importado en el 100% de los casos reales verificados en este
  proyecto; no hay ningún caso legítimo donde dos filas del mismo archivo
  deban ir a cuentas distintas.
- **El acoplamiento de "Efecto patrimonial" a un vocabulario técnico** —
  el contenido (Gasto/Ingreso/Transferencia interna/Pago de deuda) es
  indispensable; la forma en que se pregunta (una lista de 9 opciones
  técnicas de Tipo, en vez de una pregunta directa en lenguaje llano) no lo
  es.

### 3. Modelo mínimo desde cero

Desarrollado en la Parte 5: mismas 4 dimensiones de dominio, ninguna
eliminada — pero solo dos de ellas (Categoría, para Gasto/Ingreso; Nombre
de Contraparte, la primera vez) siguen siendo preguntas activas para el
usuario en el flujo normal. Efecto patrimonial se deriva casi siempre;
Naturaleza técnica nunca se pregunta; Cuenta financiera se resuelve en el
import, no en la clasificación.

---

## Respuesta a la pregunta de fondo

*¿El modelo actual refleja el problema real, o refleja las primeras
decisiones de diseño que se tomaron al empezar?*

Con la evidencia reunida en este documento, mi respuesta es: **el modelo de
*datos* refleja el problema real — las 4 dimensiones existen por buenas
razones y ninguna sobra como concepto.** Lo que refleja las primeras
decisiones de diseño, sin haberse vuelto a cuestionar desde entonces, es
**el modelo de *interacción*** construido encima: la asunción, nunca
verificada hasta este documento, de que las 4 dimensiones del dato deben
traducirse una a una en 4 decisiones independientes pedidas al usuario. Esa
traducción 1 a 1 es la que no resiste la evidencia — ni la de los
consumidores reales del código (`MovementType` sin ningún consumidor
verificado), ni la de los datos bancarios reales usados en este proyecto
(el vocabulario del banco ya resuelve la mayoría de los casos por texto),
ni la del propio roadmap del proyecto (la Épica N ya reconoce parte de este
problema, aunque con un alcance más conservador).

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ningún
patch. Cada afirmación de "sin consumidor" está sostenida por una búsqueda
exhaustiva (`git grep`) sobre el código completo de `origin/master`, no por
inferencia — incluida la verificación directa, línea por línea, de los dos
componentes (`FinancialMetricsService.cs`, `FinancialTools.cs`) que el
propio ADR-001 del proyecto cita como razón para no tocar el modelo.
