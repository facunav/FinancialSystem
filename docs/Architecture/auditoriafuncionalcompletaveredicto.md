# Auditoría funcional completa — ¿este sistema ya reemplaza a Excel?

Commit base: `origin/master` (`51408bf`). Documento de solo análisis: no se
modificó ningún archivo del repositorio. Este documento sintetiza y cierra
la serie de auditorías de esta conversación (flujo completo del producto,
flujo de clasificación, simplificación del modelo, auditoría semántica de
movimientos reales) en un único veredicto, recorriendo el sistema una vez
más de punta a punta como usuario nuevo — con la UI y los datos reales tal
como están hoy en `origin/master`, incluyendo lo que ya cambió desde el
primer análisis de esta conversación (PR-O7: `CounterpartyType` ya no es
obligatorio; PR-O8: `counterparties.html` ya existe).

**Veredicto en una línea, para no enterrarlo**: el sistema tiene un motor
de clasificación técnicamente sofisticado construido sobre un primer paso
(importar el archivo real) que puede fallar en silencio, y un último paso
(confiar en el dashboard) que no avisa cuándo no debería confiarse en él.
Ambos extremos del flujo son, hoy, más frágiles que el centro — y son,
justamente, el primer y el último contacto que un usuario nuevo tiene con
el producto.

---

## 1. Importación

**Fricción real, verificada contra los archivos reales de este proyecto**:
`BbvaBankStatementImportHandler.CanHandle` exige que el nombre del `.xls`
matchee `Caja*.xls`/`*ahorros*.xls`/`*corriente*.xls`. Los dos archivos
reales usados en todo este proyecto (`Debito_29_05_2026_al_10_07_2026.xls`)
no cumplen ninguno de los tres. El archivo cae al handler genérico, que no
tiene parser para `.xls` (`ExcelWorkbookParser` solo declara `.xlsx`), y el
resultado es un `ImportBatch` con "0 insertados" y un diagnóstico —
"no hay parser registrado para la extensión '.xls'"— que no menciona en
ningún momento que el verdadero motivo es el nombre del archivo. No hay
ninguna notificación activa; el usuario tiene que saber que existe
`imports.html` (que además no tiene ningún link de acceso en toda la
aplicación) y entrar a revisarla.

**Información que el sistema ya conoce pero no usa**: el número de cuenta
("CA$ 214-45099/4") y el número de tarjeta ("Visa Signature cuenta
1278896210", "Mastercard Black cuenta 1278939005") están en el propio
texto de cada documento, siempre. Además — hallazgo nuevo al releer las
filas reales fila por fila para el análisis anterior de esta serie — el
parser lee el título en la fila 0 del XLS, pero en los dos archivos reales
la fila 0 está vacía y el título real está en la fila 1. Es decir: incluso
para el único dato que el parser de banco sí *intenta* extraer, la
evidencia de las filas reales sugiere que hoy probablemente no lo logra.

**Errores poco claros**: confirmado — el mensaje de diagnóstico
("no hay parser registrado") es técnicamente cierto pero engañoso: un
parser para `.xls` sí existe (`BbvaBankStatementParser`), y de hecho
interpreta perfectamente el contenido de estos archivos reales cuando
llega a ejecutarse — el problema nunca fue el contenido, fue el nombre.

**Veredicto de esta sección**: es el punto más frágil de todo el flujo,
porque es también el primero. Un usuario nuevo que suelta su propio
extracto real de BBVA en la carpeta tiene una posibilidad concreta y no
hipotética de que no pase nada, sin ningún indicio de por qué.

---

## 2. Clasificación, campo por campo — evidencia, no opinión

Repito acá, condensado y con la evidencia ya reunida en los análisis
anteriores de esta serie, la pregunta pedida para cada campo: *¿de verdad
el usuario debería decidir esto, o el sistema ya podría saberlo?*

- **Cuenta financiera** — el sistema ya podría saberlo el 100% de las
  veces. El dato está en el documento importado en los 4 casos reales de
  este proyecto, sin excepción. Hoy es un `<select>` vacío por fila, sin
  ningún atajo por archivo.
- **Categoría** — es la única de las 4 dimensiones donde el usuario debería
  decidir, y solo para movimientos que son Gasto/Ingreso genuinos. Para
  transferencias internas y pagos de deuda, forzar una elección (típicamente
  la categoría-placeholder "Transferencias") no aporta información nueva
  sobre el movimiento.
- **Contraparte** — debería decidirse una sola vez por comercio real
  (el nombre), en el momento en que se descubre que falta, sin abandonar
  la pantalla. Hoy `counterparties.html` ya existe (PR-O8) — el catálogo
  dejó de ser un problema — pero **el modal de clasificación de
  `movements.html` sigue sin ningún atajo "+ Nueva" dentro de él**, así
  que la fricción de fondo (salir del flujo, crear, volver, re-encontrar
  la fila) sigue intacta.
- **Tipo de movimiento** — el sistema ya podría saberlo en la enorme
  mayoría de los casos, verificado con los 442 movimientos reales de
  banco: 210 de "PAGO CON VISA DEBITO" (Compra), 156 combinadas de
  "TRANSFERENCIA"/"Transferencia inmediata", 9 de "PAGO DE TARJETA..." más
  otras con el mismo efecto bajo el patrón "CUENTA VISA/MASTERCARD
  NRO...." (Pago de deuda), 8 de "INTERESES GANADOS" (Interés), 4 de
  "PAGO DE HABERES" (Cobro) — sobre 442 filas reales, el vocabulario
  cerrado del banco ya resuelve la inmensa mayoría sin ambigüedad.
  Verificado además, con evidencia exhaustiva de código
  (`git grep` sobre todo `src/`), que **ningún consumidor real del sistema
  lee este campo para tomar una decisión** — ni `FinancialMetricsService.cs`
  (el backend completo del Dashboard), ni `FinancialTools.cs` (las
  herramientas expuestas al MCP). Se persiste, se indexa, se pregunta
  siempre — y no cambia ningún resultado que el usuario vea.
- **Impacto financiero** — este sí tiene consumidor real y verificado: es
  literalmente el filtro de los 4 métodos de `FinancialMetricsService.cs`.
  Pero, con los datos reales, es también mayormente derivable: el signo del
  importe siempre se conoce con certeza (nunca es ambiguo), y solo falta
  saber si la contraparte es una cuenta propia para resolver el resto —
  dato que hoy no existe en ningún lugar del sistema. El caso ambiguo real,
  confirmado con dos patrones de texto distintos ("DEBITO DIRECTO" vs.
  "PAGO DE TARJETA...") que hoy comparten el mismo valor de Tipo sin que
  ese campo los distinga, es la evidencia más concreta de que estos dos
  campos, tal como están hoy, se piden como si fueran dos decisiones
  independientes cuando en la práctica son, la enorme mayoría de las veces,
  la misma decisión contada dos veces.

**Veredicto de esta sección**: de las 4 dimensiones pedidas hoy por cada
movimiento, la evidencia de código y de datos reales sostiene que solo una
(Categoría, y solo para una fracción de los casos) es una decisión que
realmente necesita al usuario cada vez.

---

## 3. Catálogos

**Estado actual, verificado fresco**: `accounts.html` (Cuentas) y
`counterparties.html` (Contrapartes, PR-O8) ya existen con CRUD completo.
**No existe pantalla de Categorías.** Ninguna de las tres tiene alta en
contexto desde el modal de clasificación — la creación siempre implica
salir de `movements.html`.

**¿En qué momento natural deberían crearse?**
- Cuenta: una sola vez, al importar el primer archivo de una cuenta/tarjeta
  nueva — hoy no ocurre nunca de forma automática.
- Categoría: la primera vez que un movimiento de Gasto/Ingreso genuino no
  encaja en ninguna de las 11 categorías de sistema ya sembradas — momento
  que hoy exigiría una pantalla que no existe.
- Contraparte: la primera vez que aparece un comercio nuevo, en el mismo
  instante de clasificar ese movimiento — hoy exige abandonar la pantalla,
  aunque el destino de esa excursión (`counterparties.html`) ya sea
  razonable una vez que se llega.

**Pasos innecesarios**: el viaje completo para dar de alta una Contraparte
nueva sigue siendo: memorizar/copiar el nombre → salir del modal → navegar
a una URL que no está linkeada desde ningún lado → crear → volver → volver
a encontrar la fila → recién ahí clasificar. Ninguno de esos pasos, salvo
"crear", aporta algo — son navegación pura.

---

## 4. Dashboard — ¿confío en los números?

Leí `FinancialMetricsService.cs` completo, otra vez, específicamente para
responder esta pregunta como usuario, no como desarrollador.

**No.** Y la razón concreta es: los 4 métodos
(`GetPeriodSummaryAsync`, `GetExpensesByCategoryAsync`,
`GetMonthlyTrendAsync`, `CompareWithPreviousMonthAsync`) consultan
exclusivamente `ClassifiedMovements` — movimientos que el usuario ya
terminó de clasificar. Un movimiento "Pendiente" en `movements.html` no
existe para el Dashboard, ni como advertencia, ni como una línea gris al
pie que diga "esto es parcial". Si alguien importa 500 movimientos reales y
clasifica 60, el Dashboard muestra los mismos KPIs prolijos, con la misma
autoridad visual, que si hubiera clasificado los 500.

**Qué me falta**: un número, en algún lugar visible del Dashboard, que
diga cuántos movimientos del período siguen sin clasificar. Es la
diferencia entre "esto es lo que gasté" y "esto es lo que gasté, de lo que
ya alcancé a revisar" — y hoy el sistema no distingue una frase de la otra
en ningún lugar de la interfaz.

**Qué dudas me quedan**: además de la cobertura, hay gasto real que el
Dashboard nunca puede ver porque nunca llega a ser un movimiento — los
impuestos y cargos de cada resumen de tarjeta (Impuesto de Sellos, IIBB,
IVA RG 4240 — del orden de $42.000 solo en el resumen Visa real de este
proyecto) no matchean el formato de línea de consumo de ningún parser y se
descartan en silencio antes de llegar a existir como dato.

**Veredicto de esta sección**: es el punto donde la ausencia de una señal
(cobertura de clasificación) es más dañina que cualquier error visible —
porque un número mal calculado que se ve prolijo genera más confianza
equivocada que un número que directamente falla.

---

## 5. Experiencia completa — cronometraje

Usando el volumen real de este mismo proyecto (442 movimientos de banco +
del orden de 90 de tarjeta en dos meses, cifra consistente con "cientos de
movimientos" que ya usaste como referencia en pedidos anteriores):

**Primer mes, sin historial todavía** (el escenario más honesto para medir
si el producto sirve, porque es el que decide si alguien sigue usándolo):
- Cuenta financiera: 1 clic por movimiento, sin excepción, sin atajo por
  archivo → **~500 clics que no representan ninguna decisión real**, solo
  transcripción de un dato que el sistema ya descartó al importar.
- Clasificación: para cada movimiento sin sugerencia (el 100% del primer
  mes, porque `quickAcceptValues` solo se activa con las 4 dimensiones en
  confianza Alta simultánea, y no hay historial todavía) — abrir modal,
  elegir Categoría, revisar/corregir Tipo e Impacto (con el riesgo real,
  ya documentado, de que el default "Compra"/"Gasto real" quede sin
  corregir en movimientos que no lo son), elegir o crear Contraparte,
  Guardar → **5-6 interacciones por movimiento**.
- Total aproximado, solo para el primer mes: del orden de **3.000
  interacciones** (clics + decisiones) para clasificar el volumen real de
  un usuario que factura como el de este proyecto. A un ritmo optimista de
  2 segundos por interacción, eso es más de **100 minutos** de trabajo
  puramente repetitivo, en la sesión donde el usuario todavía está
  decidiendo si vale la pena seguir.

**¿Cuáles agregan valor?** Elegir Categoría, la primera vez por comercio.
Nombrar una contraparte, la primera vez. Corregir una sugerencia
equivocada, cuando la hay.

**¿Cuáles son puro trabajo repetitivo?** Cuenta financiera, siempre.
Tipo/Impacto para el ~85% de los casos que el propio vocabulario del banco
ya resuelve sin ambigüedad (verificado con los 442 movimientos reales).
Reclasificar cada cuota de una compra en cuotas como si fuera un comercio
nuevo. El viaje de ida y vuelta a Contrapartes por cada comercio nuevo.

---

## Funcionalidades a cuestionar directamente, con evidencia

Se pidió explícitamente cuestionar decisiones ya tomadas si la evidencia lo
sostiene. Esto es lo que encontré:

**El motor de sugerencias históricas (PR-S1 a PR-S12) resuelve un problema
real, pero llega tarde al momento donde más importa.** Su mecanismo central
—coincidencia exacta contra clasificaciones pasadas— no tiene absolutamente
nada que ofrecer en el primer mes de uso, que es exactamente el mes que
decide si alguien abandona. Mientras tanto, la evidencia de la sección 2
muestra que una capa mucho más simple —reglas determinísticas sobre el
vocabulario cerrado del banco, sin ningún historial previo— ya cubriría el
~85% de Tipo/Impacto desde el primer movimiento importado, sin esperar a
que exista ningún dato. Esa capa nunca se construyó. Dicho de otra forma:
se invirtió una cantidad considerable de esfuerzo en que el sistema
aprenda de lo que el usuario ya clasificó, y comparativamente poco en que
el sistema lea lo que el banco ya escribió — y es esto último lo que más
rápido demostraría valor a un usuario nuevo.

**Quick Accept y la codificación por color de confianza (PR-U1, PR-U3)
son, por la misma razón, funcionalidades cuyo valor está condicionado a
que ya exista historial — en el momento crítico de "¿sigo usando esto?",
contribuyen cero.** No digo que no aporten valor — lo aportan, a partir del
segundo o tercer mes — digo que no son la prioridad si el objetivo es
sobrevivir al primer mes.

**Las alertas de "posible duplicado/split" (K6) no tienen ninguna acción
de resolución, verificado en el propio código** (`renderWarningCell`: "sin
acción propia... no tiene ninguna pantalla hoy"). Una alerta sin forma de
resolverse deja de ser una alerta y pasa a ser ruido visual permanente —
cuestiono si esta funcionalidad, tal como está hoy, aporta más de lo que
cuesta en confianza acumulada ("el sistema me marca cosas que nunca puedo
resolver").

**La clasificación en lote (PR-L2/PR-U4) aplica la misma clasificación a
todos los movimientos seleccionados sin ninguna vista previa por fila de lo
que cada uno sugeriría individualmente.** Es segura solo si el usuario ya
verificó a mano que el lote es homogéneo — lo cual, para un lote grande,
es exactamente el trabajo que la función debería ahorrar. No encontré
evidencia de que reduzca el riesgo de clasificación incorrecta más allá de
lo que ya reduce clasificar fila por fila con sugerencias.

**Ninguna pantalla actual sobra.** Repasé las 5 (Dashboard, Movimientos,
Importaciones, Cuentas, Contrapartes) — cada una resuelve un problema real
y distinto, sin superposición de contenido entre sí. **Sí falta una**:
Categorías, sin ninguna pantalla propia hoy.

---

## El momento exacto en que alguien volvería a Excel

No es un solo momento — son, en orden de probabilidad de que ocurran
realmente, con los datos y el código de este proyecto:

1. **En los primeros cinco minutos**: soltar el extracto real de BBVA y no
   ver que pasó nada, sin ningún mensaje que explique por qué.
2. **Dentro de la primera hora**, si el paso 1 se resuelve: clasificar
   varias decenas de movimientos, notar que hay que reasignar la cuenta
   fila por fila para algo que el sistema ya debería saber, y sentir que el
   "ahorro de tiempo" prometido no se nota todavía.
3. **La primera vez que aparece un comercio nuevo** y hay que abandonar la
   pantalla para crear una contraparte — la comparación mental inmediata es
   "en Excel esto era escribir una celda".
4. **Al mirar el Dashboard por primera vez con clasificación parcial**: ver
   un resumen prolijo y no saber si representa la mitad de sus movimientos
   o el total — y no tener ninguna forma de saberlo sin volver a
   Movimientos a contar a mano. Esto es, de los cuatro, el más grave: en
   Excel, lo que ves es exactamente lo que cargaste, ni más ni menos — acá,
   la prolijidad visual del Dashboard puede transmitir una confianza que
   los datos subyacentes no respaldan, y esa es una forma de fallar peor
   que la fealdad honesta de una planilla.

---

## Tabla de priorización — ordenada únicamente por valor para el usuario

| Problema | Impacto real | Frecuencia | Dolor para el usuario | Complejidad de solución |
|---|---|---|---|---|
| El `.xls` real de BBVA puede rechazarse en silencio por el nombre de archivo | Bloqueante — nada funciona si el import falla | Cada archivo importado, hasta corregirse | Altísimo — no hay ninguna pista de la causa real | Media |
| El Dashboard no indica cuánto del período está clasificado | Alto — confianza falsa en cualquier resumen mostrado | Siempre, mientras haya movimientos pendientes | Alto — se descubre tarde, silenciosamente | Baja-media |
| Consumo real en USD sin espacio antes de "USD" se guarda con el monto equivocado | Alto — corrompe el dato, no solo la experiencia | Baja, pero real y repetible (verificado con datos reales) | Alto — rompe confianza en los números apenas se nota | Baja |
| No se puede crear una Contraparte sin abandonar la pantalla de clasificación | Alto — interrumpe el flujo en el momento de mayor fricción | Alta en el primer mes, decreciente después | Alto | Baja-media |
| Cuenta financiera nunca se infiere del import | Alto — trabajo repetitivo que crece con el volumen | Una vez por movimiento, todos los meses | Alto, acumulativo | Media |
| Tipo/Impacto no se derivan del vocabulario del banco pese a cubrir ~85% de los casos reales | Medio-alto — cientos de decisiones evitables por mes | Muy alta (afecta casi todos los movimientos) | Medio-alto | Baja (regla de prefijo, sin motor nuevo) |
| Cuotas de una misma compra nunca se reconocen entre sí | Medio-alto | Media, depende del usuario | Medio-alto | Baja |
| Pantallas de administración sin ningún link de navegación | Alto en descubribilidad | Siempre, para todo usuario nuevo | Alto la primera vez | Baja-media |
| Impuestos/cargos de resumen de tarjeta nunca se importan | Medio — gasto real invisible | Cada resumen de tarjeta | Medio | Media |
| Alertas de posible duplicado sin ninguna acción de resolución | Bajo-medio — ruido acumulado | Depende del volumen de duplicados reales | Bajo-medio, pero creciente con el uso | Media-alta |
| Falta pantalla de administración de Categorías | Medio | Baja (el seed de 11 categorías cubre bastante) | Bajo-medio | Baja |
| Comentario de clasificación invisible después de guardarlo | Bajo | Depende de cuánto lo use el usuario | Bajo | Baja |

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ningún
patch ni propuse ninguna arquitectura nueva. Cada hallazgo de este
documento está sostenido por código o datos reales ya verificados en esta
conversación, releídos y confirmados una vez más específicamente para este
veredicto final.
