# Auditoría del flujo de clasificación — ¿tiene sentido el modelo de interacción?

Commit base: `origin/master` (`e22335e`). Documento de solo análisis: no se
modificó ningún archivo del repositorio. No propongo código ni arquitectura
— esto es exclusivamente sobre qué debería decidir un humano, qué debería
resolver el sistema solo, y qué debería directamente dejar de existir.

**Marco de evaluación, explícito**: cada juicio de este documento se hace
contra una sola métrica — *¿este diseño permite clasificar 500 movimientos
en el menor tiempo posible, con la menor cantidad de clics y de decisiones
humanas, sin que el usuario pierda control sobre la información?* No evalúo
nada por facilidad de implementación.

Para las preguntas de "qué conoce el parser", releí en esta sesión
`BbvaBankStatementParser.cs`, `BbvaTransactionLineParser.cs`,
`MastercardTransactionLineParser.cs`, `BbvaVisaStatementParser.cs`,
`BbvaMastercardStatementParser.cs`, el dominio (`FinancialAccount`,
`Counterparty`, `Category`, `MovementType`, `FinancialImpact`,
`ClassifiedMovement`, `FinancialMovement`) y los 4 documentos bancarios
reales ya usados en análisis anteriores de esta misma conversación (2 XLS de
Caja de Ahorro BBVA, 1 PDF Visa, 1 PDF Mastercard).

---

## Cuenta financiera

**¿Qué información conoce ya el parser?** Para el banco, todo: el título del
XLS trae literalmente "CA$ 214-45099/4", y ese número identifica a esa
cuenta en las dos exportaciones reales usadas en este proyecto (mismo
número, dos rangos de fecha distintos). Para tarjeta, el texto del PDF
también lo dice de forma explícita e inequívoca — "Visa Signature cuenta
1278896210", "Mastercard Black cuenta 1278939005" — el parser ya lee esa
línea, solo que hoy no la conserva.

**¿Qué información conoce el sistema, más allá del parser?** Sabe, sin
ambigüedad, qué *tipo* de documento se importó (el propio ruteo por
fingerprint ya distingue "esto es un extracto de Visa" de "esto es un
extracto de Mastercard" de "esto es un extracto de banco") — antes incluso
de mirar el número de cuenta.

**¿En qué momento hace falta preguntársela al usuario?** Exactamente una
vez por cuenta/tarjeta real que el usuario tiene — no una vez por
movimiento, ni una vez por archivo importado. Un extracto de Visa siempre
pertenece, en su totalidad, a la misma tarjeta — no existe ningún escenario
real donde dos filas del mismo archivo importado deban ir a cuentas
distintas. La única decisión humana legítima es la primera vez que aparece
un número de cuenta nunca visto: ponerle un nombre reconocible ("Visa
Signature BBVA" en vez de "1278896210"). Después de esa única vez, cada
archivo nuevo de la misma cuenta debería reconocerse solo.

**¿Hay algún caso donde el usuario deba elegirla a mano, siempre?** No
encuentro ninguno. Ni siquiera en el caso de una persona con múltiples
tarjetas del mismo banco: cada tarjeta tiene su propio número de cuenta
distinto, así que sigue siendo resoluble por dato, no por elección.

**Veredicto**: esto no debería ser, conceptualmente, un campo del flujo de
clasificación. Es un dato de la *importación*, resuelto una vez por cuenta
real, nunca por movimiento. Que hoy viva como un `<select>` en cada fila de
la tabla es tratar como "decisión de clasificación, repetida 500 veces" algo
que en realidad es "dato de identidad, resuelto una vez".

---

## Categoría

**¿Tiene sentido pedirla siempre?** No en el sentido literal de "siempre
preguntar". Sí en el sentido de que, cuando hace falta preguntarla, es una
decisión genuinamente humana — es la única de las cuatro dimensiones que
responde "para qué", que ni el banco ni ningún otro dato del sistema puede
inferir por sí solo la primera vez que aparece un comercio.

**¿Puede sugerirse?** Sí, y ya se hace (historial de descripciones
idénticas). **¿Puede heredarse?** Sí — de una Contraparte ya conocida
(`Counterparty.DefaultCategoryId`, ya implementado en el backend).
**¿Puede aprenderse?** En el sentido de "el sistema mejora con el uso sin
que nadie programe una regla nueva por cada comercio" — sí, ese es
exactamente el propósito del historial: cada clasificación pasada enseña
algo para la próxima aparición del mismo patrón.

**Dónde el modelo actual pide de más**: hay una franja de movimientos donde
Categoría es, en los hechos, redundante con lo que Tipo/Impacto ya dicen.
Una transferencia entre mis propias cuentas no "se gastó en" nada — no tiene
un "para qué" real, tiene un "hacia dónde". El propio catálogo de
categorías del sistema ya lo admite a medias: existe una categoría
"Transferencias" sembrada por defecto, que en la práctica es una categoría
placeholder para movimientos que no tienen categoría real — el usuario
termina eligiendo "Transferencias" como categoría de una transferencia
porque el formulario lo obliga a elegir algo, no porque esa elección
aporte información nueva sobre el movimiento (el hecho de que es una
transferencia ya está expresado en otro campo). Lo mismo pasa con pagos de
tarjeta/deuda: la categoría "correcta" es mecánica, no una decisión.

**Veredicto**: Categoría debería seguir siendo una pregunta real para
Gastos e Ingresos genuinos — ahí es donde de verdad importa "en qué se
fue la plata". Para Transferencias internas y Pagos de deuda, no debería
preguntarse en absoluto: el propio hecho de ser una transferencia o un pago
de tarjeta ya lo dice todo lo que hace falta saber, y forzar una elección
ahí es pedirle al usuario que rellene un casillero vacío de sentido.

---

## Contraparte

**¿Tiene sentido obligar al usuario a abandonar el flujo para crear una
nueva?** No, y no encuentro ningún argumento a favor. El catálogo existe
(`Counterparty`, con CRUD completo) — el problema no es que la entidad no
tenga sentido, es que crearla hoy exige una excursión completa fuera de la
pantalla donde se descubre que hace falta.

**¿Cómo debería sentirse esa experiencia?** Como una sola acción continua,
no como dos pantallas. El usuario está mirando "MERPAGO*LACOCA, $17.100" —
en ese momento tiene toda la información que necesita para decidir cómo
llamar a esa contraparte. Cualquier diseño que lo obligue a soltar ese
contexto (cerrar, navegar, volver a buscar la misma fila) es peor que
cualquier diseño que lo mantenga en el mismo lugar, sin importar los
detalles de implementación.

**Alternativas, evaluadas en términos puramente de interacción** (no de
código):

1. **Combobox con autocompletado, donde "crear nueva" es una opción más de
   la misma lista** — el usuario empieza a tipear el nombre, ve las
   contrapartes existentes que matchean, y si ninguna sirve, la última
   opción de la lista es literalmente "Crear 'La Coca' como nueva
   contraparte". Es la opción de menor fricción posible: para el caso común
   (contraparte ya existe) no agrega ningún paso; para el caso nuevo, agrega
   exactamente una acción más (confirmar la creación), no una pantalla
   nueva. Dado que el único dato realmente necesario para crear una
   contraparte es el nombre (ver el análisis dedicado a esta entidad), no
   hace falta ningún formulario adicional — "crear" puede ser, literalmente,
   una sola confirmación.
2. **Botón "+ Nueva" que abre un mini-formulario dentro del mismo modal** —
   un paso más que la opción 1 (hay que notar que no está en la lista,
   apretar un botón aparte, recién ahí escribir el nombre), pero sigue sin
   abandonar la pantalla. Razonable si alguna vez hiciera falta pedir más de
   un dato al crear — hoy no hace falta.
3. **Diferir la creación por completo**: dejar que el usuario clasifique con
   un nombre libre de texto, sin crear todavía una entidad formal, y
   "promover" ese texto a Contraparte real más adelante, en lote. Reduce la
   fricción del momento a cero, pero tiene un costo real: mientras no se
   promueve, ese comercio no hereda ningún default y cada aparición nueva
   sigue sin reconocerse como la misma contraparte hasta que alguien decida
   formalizarla — pospone exactamente el beneficio que la entidad existe
   para dar.

**Veredicto**: la opción 1 es la que mejor cumple la métrica de "menos
clics, sin perder control" — no interrumpe nunca el flujo principal, y el
costo de crear una contraparte nueva es, en el peor caso, un clic más que
seleccionar una existente. La opción 3 optimiza el momento pero le cuesta al
mes siguiente (comercios que deberían reconocerse y no se reconocen porque
nunca se promovieron).

---

## Tipo de movimiento — análisis conceptual, no de código

Vos mismo decís que este campo te genera dudas como usuario. Coincido, y
creo que el motivo es identificable.

**¿Qué significa exactamente?** Responde "qué clase de operación fue esto"
(Compra/Transferencia/Pago/Cobro/Comisión/Interés/Reintegro/Ajuste/Otro) —
en principio distinto de "cómo afecta mi patrimonio" (Impacto). El ejemplo
de libro que justifica esta separación es real: una comisión bancaria es
Compra... no, es Comisión + Gasto; una transferencia a mí mismo es
Transferencia + Movimiento interno. Ahí sí hace falta la distinción.

**¿Es comprensible?** Solo parcialmente. La frontera entre "Compra" y "Pago"
no es intuitiva para un usuario común: ¿pagar la factura de luz es una
"Compra" (compré el servicio) o un "Pago" (pagué una factura)? Las dos
lecturas son razonables en el habla cotidiana, y el sistema no da ninguna
pista de cuál corresponde — cada usuario termina inventándose su propio
criterio interno, lo cual es exactamente el síntoma de un campo mal resuelto
conceptualmente: si dos personas razonables clasificarían el mismo
movimiento distinto, el campo no está midiendo algo objetivo.

**¿Está resolviendo un problema real?** Sí, pero un problema mucho más
chico que el que el diseño actual expone. Repasando los 9 valores contra la
realidad: "Compra" casi siempre implica Impacto=Gasto; "Pago" casi siempre
implica Impacto=Pago de deuda o Gasto; "Cobro" casi siempre implica
Impacto=Ingreso; "Comisión" casi siempre implica Impacto=Gasto. En estos
casos, Tipo no aporta ninguna decisión que Impacto no contenga ya — es un
eco. Los únicos dos casos donde Tipo de verdad separa realidades distintas
son: (a) una Transferencia, que puede ser hacia mí mismo o hacia un tercero
(sí importa distinguirlo), y (b) un Interés, que puede ser a favor o en
contra (también importa). El problema real que este campo intenta resolver
es angosto — cabe en dos preguntas puntuales, no en una lista de 9 opciones
presentada siempre.

**¿Existe una forma más intuitiva de representar esa información?** Sí: en
vez de una lista de 9 términos técnicos ofrecida siempre, la pregunta
debería aparecer solo cuando el propio texto del banco ya identificó una
"Transferencia" ("¿es entre tus propias cuentas o a otra persona?") o un
"Interés" ("¿lo cobraste o te lo cobraron?") — dos preguntas humanas,
puntuales, hechas solo cuando hace falta, en vez de una taxonomía técnica
presentada en todos los casos.

**¿El usuario debería decidirlo, o debería inferirse?** Debería inferirse
en la enorme mayoría de los casos — el propio texto del banco ya lo dice
casi siempre ("TRANSFERENCIA", "PAGO DE TARJETA...", "INTERESES GANADOS" son
vocabulario cerrado y consistente, ya verificado con los extractos reales
de este proyecto). El usuario debería decidir solo en el residuo genuinamente
ambiguo, y ni siquiera con el vocabulario técnico actual.

**Veredicto**: como concepto de dominio (distinguir naturaleza de efecto
patrimonial), tiene sentido conservarlo. Como *decisión que se le pide al
usuario en cada movimiento*, no tiene sentido — debería dejar de aparecer
como pregunta en el flujo principal casi siempre, quedando como un dato
calculado, visible si alguien quiere auditar el detalle, pero no como una de
las cosas que hay que "elegir" para avanzar.

---

## Impacto financiero

**¿Es realmente una decisión del usuario, o es consecuencia de otras
decisiones?** Analizándolo desde cero: casi siempre es consecuencia, no
decisión. El signo del importe (¿entró o salió plata?) siempre se conoce con
certeza absoluta — nunca es ambiguo, el banco lo dice sin excepción. Lo
único que falta para pasar de "signo del importe" a "Impacto financiero
completo" es saber si la contraparte del movimiento es "yo mismo" (entonces
es Movimiento interno) o un tercero genuino (entonces es Gasto o Ingreso
real), más el caso especial y ya reconocible por texto de "Pago de tarjeta"
(Pago de deuda).

Esto conecta directamente con un hallazgo del análisis dedicado a la
entidad Contraparte: dos de sus valores de tipo, `OwnAccount` y `OwnCard`,
existen en el dominio desde el principio, documentados como "cuenta propia
del mismo usuario en otro banco/entidad" — pero hoy no tienen ningún
consumidor. Esa es, exactamente, la pieza que le falta al sistema para que
Impacto financiero deje de ser una pregunta y pase a ser un cálculo: si al
crear una Contraparte el usuario marca, una sola vez, "esta soy yo mismo/una
cuenta mía" (para las 2-3 cuentas propias que cualquiera tiene, no para
cada movimiento), el sistema puede derivar Impacto financiero automáticamente
para el 100% de las transferencias futuras hacia esa contraparte.

**Veredicto**: Impacto financiero debería preguntarse casi nunca. Debería
calcularse a partir de: signo del importe (siempre conocido) + si la
contraparte es una cuenta propia (una marca que se hace una vez por cuenta
propia, no por movimiento) + patrones de texto ya deterministas (pago de
tarjeta, intereses). El único residuo real de decisión humana es "¿esta
plata que sale/entra es un gasto/ingreso genuino, o es plata que sigue
siendo mía en otro lado?" — y esa pregunta ya la responde, de forma
indirecta y sin fricción, con quién se relaciona el movimiento.

---

## Un hallazgo que atraviesa Tipo de movimiento e Impacto financiero juntos

Analizados por separado, ambos campos terminan señalando la misma
conclusión: **desde la perspectiva del usuario, no debería haber dos
preguntas independientes acá — debería haber una.** Nadie elige "Compra" y
"Gasto real" como dos decisiones separadas: viajan juntos siempre que uno
de los dos ya está claro. La única pregunta humana real, cuando hace falta
hacerla, es una versión en lenguaje llano de Impacto financiero ("¿esto es
un gasto, un ingreso, una transferencia entre tus cuentas, o el pago de una
deuda?") — Tipo de movimiento pasa a ser un detalle técnico que el sistema
completa solo a partir de esa misma respuesta más el texto del banco, sin
que el usuario tenga que verlo ni entenderlo, salvo que quiera auditar el
detalle de un movimiento puntual.

No estoy diciendo que el dominio deba perder una de las dos dimensiones —
el ejemplo de "comisión bancaria = Comisión + Gasto" sigue siendo válido
como registro interno. Digo que **la interfaz no debería pedir dos
decisiones donde el usuario solo tiene una que tomar.**

---

## Resultado

### 1. Qué decisiones realmente debería tomar el usuario

- **Categoría**, solo para movimientos que son Gasto o Ingreso genuinos
  (no para transferencias internas ni pagos de deuda, que no tienen un
  "para qué" real) — y solo la primera vez que aparece un comercio/patrón
  nuevo, nunca de nuevo una vez que hay historial o un default heredado de
  Contraparte.
- **Nombre de una Contraparte nueva**, en el momento en que se descubre que
  hace falta, sin abandonar la pantalla — un solo dato (el nombre), no un
  formulario.
- **"¿Cómo afecta esto a tu patrimonio?"**, en lenguaje llano (gasto /
  ingreso / transferencia entre tus cuentas / pago de deuda), solo cuando
  el sistema no puede derivarlo solo — principalmente la primera vez que
  aparece una transferencia hacia una contraparte cuyo carácter (propia o
  de un tercero) todavía no se sabe.
- **Nombrar una cuenta/tarjeta la primera vez que aparece un número de
  cuenta nuevo** — una sola vez por cuenta real, nunca por movimiento ni
  por archivo importado.
- **Marcar, una vez por contraparte, si es una cuenta propia** (no una
  decisión por movimiento — una configuración que se hace 2-3 veces en toda
  la vida útil de la cuenta, no 500 veces al mes).

### 2. Qué decisiones debería tomar automáticamente el sistema

- **Cuenta financiera**, el 100% de las veces después de la primera
  aparición de cada cuenta/tarjeta real — el dato ya está en el propio
  documento importado.
- **Tipo de movimiento**, en la enorme mayoría de los casos — a partir del
  vocabulario cerrado que el banco ya usa en su propio texto (verificado con
  extractos reales: "TRANSFERENCIA", "PAGO DE TARJETA...", "INTERESES
  GANADOS", etc.) y de la sección del documento en la que aparece (un
  consumo dentro de "Consumos" de tarjeta es, por definición, una Compra).
- **Impacto financiero**, en la enorme mayoría de los casos — a partir del
  signo del importe (siempre cierto) más si la contraparte es una cuenta
  propia (dato configurado una vez) más los mismos patrones de texto que ya
  resuelven Tipo.
- **Categoría e Impacto para movimientos que son mecánicamente
  transferencias o pagos de deuda** — no hay decisión real que tomar ahí,
  solo un hecho que confirmar.
- **Contraparte recurrente** — una vez que el usuario clasificó "MERPAGO*
  LACOCA" una vez, cualquier variante razonable del mismo patrón debería
  reconocerse sin volver a preguntar (esto hoy no ocurre, ya señalado en el
  análisis de flujo completo, y refuerza por qué automatizar esta capa
  importa: cada comercio nuevo debería costar una decisión, no una por mes).

### 3. Qué información hoy sobra o genera confusión

- **Tipo de movimiento como campo siempre visible y siempre elegido a
  mano** — sobra en su forma actual (lista de 9 opciones ofrecida siempre);
  el concepto que representa no sobra, pero casi nunca necesita presentarse
  como una pregunta.
- **Cuenta financiera como campo de la fila de clasificación** — no es
  información que sobre, es información mal ubicada: pertenece a la
  importación, no a la clasificación.
- **"Transferencias" como categoría elegible para transferencias internas y
  pagos de tarjeta** — es un placeholder que el usuario llena porque el
  formulario lo obliga, sin que esa elección agregue ningún dato real por
  encima de lo que Tipo/Impacto ya dicen.
- **Dos campos independientes (Tipo + Impacto) donde el usuario en la
  práctica solo tiene una decisión** — ya desarrollado arriba: no son
  redundantes en el dominio, pero sí lo son como par de preguntas separadas
  en la interfaz.
- **`CounterpartyType` con 9 valores, obligatorio en un alta en contexto que
  debería costar un solo campo** — ya señalado en el análisis dedicado a
  esta entidad: sin consumidor real hoy, y su único propósito legítimo
  identificado (distinguir cuentas propias) merece resolverse con una sola
  marca booleana ("es una cuenta mía"), no con una taxonomía de 9 valores.

### 4. Flujo ideal desde cero, manteniendo el dominio actual

Mismo modelo de 4 dimensiones (Categoría/Impacto/Tipo/Contraparte) — el
dominio no cambia. Lo que cambia es cuántas de esas 4 dimensiones se le
piden al usuario, y cuándo.

1. **Importar** un PDF/XLS. La cuenta/tarjeta se resuelve sola contra el
   catálogo (o, si es la primera vez, se pide un nombre — una vez, no por
   movimiento). Cero decisiones por fila en este paso, siempre.
2. **El sistema clasifica solo todo lo que puede clasificar solo**: cada
   movimiento pasa por reglas deterministas de texto (vocabulario del
   banco) + historial de descripciones + defaults de Contraparte + el
   estado "es una cuenta propia" de cada Contraparte ya conocida. El
   resultado: Tipo e Impacto quedan resueltos para la enorme mayoría de las
   filas sin que el usuario los vea nunca como pregunta.
3. **El usuario ve una lista, no un formulario por fila.** La mayoría de
   las 500 filas de un mes real ya llegan con las 4 dimensiones resueltas
   con confianza suficiente — se muestran como hechos ya clasificados, con
   una sola acción posible: confirmar todo el lote de una vez, o corregir
   una fila puntual si algo no cierra.
4. **La minoría que de verdad necesita al usuario** (comercio nuevo sin
   default, transferencia cuyo destino no se sabe si es propio) se agrupa
   aparte, y para cada una de esas filas se pide **una sola cosa**:
   Categoría (con el nombre y monto del movimiento visibles al lado, para
   decidir con contexto), y si hace falta una Contraparte nueva, su nombre
   se escribe en el mismo paso, sin salir de la pantalla.
5. **Impacto/Tipo casi nunca aparecen como pregunta** — solo en el caso
   residual de una transferencia hacia una contraparte cuyo carácter
   (propia o de tercero) todavía no está marcado, y ahí la pregunta es en
   lenguaje llano ("¿es una cuenta tuya?"), no una lista técnica.
6. **El mes siguiente**, con las cuentas ya nombradas y las contrapartes
   recurrentes ya conocidas, el ciclo completo se reduce, en la práctica, a:
   importar, revisar la lista (mayoritariamente ya clasificada sola),
   resolver los pocos comercios genuinamente nuevos, y listo — sin volver a
   tocar Cuenta financiera, Tipo, ni Impacto para nada que el sistema ya
   aprendió a reconocer.

Contra la métrica pedida (500 movimientos, mínimo de clics y decisiones,
sin perder control): este flujo reduce el trabajo humano real a,
aproximadamente, una decisión por comercio genuinamente nuevo del mes —
no una decisión por fila, cuatro veces, como hoy.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ningún patch.
Es exclusivamente un análisis del modelo de interacción — no propongo cómo
implementarlo.
