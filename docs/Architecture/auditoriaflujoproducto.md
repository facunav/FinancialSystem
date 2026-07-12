# Auditoría del flujo completo del producto

Commit base: `origin/master` (`e7f390d`). Documento de solo análisis: no se
modificó ningún archivo del repositorio. Recorrí el sistema completo como un
usuario nuevo, en el orden que pediste (importar → ver → clasificar → crear
categorías/contrapartes → consultar resultados → volver el mes siguiente),
leyendo en esta sesión el código real de: los 3 parsers de BBVA
(`BbvaBankStatementParser.cs`, `BbvaTransactionLineParser.cs`,
`MastercardTransactionLineParser.cs`, más los dos `IStatementParser` que los
envuelven), el pipeline de import completo (`ImportsFolderWatcherHostedService`
→ `FileImportRouter` → `BbvaBankStatementImportHandler`/
`TransactionImportHandler` → `ImportFileProcessingSink`), el dominio de
clasificación (`FinancialMovement`, `ClassifiedMovement`, `MovementType`,
`FinancialImpact`, `Counterparty`, `Category`, `FinancialAccount`), los
endpoints reales, y las 5 pantallas completas (`movements.html`,
`dashboard.html`, `accounts.html`, `imports.html`, `counterparties.html`).
No busco justificar ninguna decisión previa — donde algo no funciona para el
usuario, lo digo así, más allá de qué PR lo haya construido.

**No incluyo problemas de arquitectura, duplicación de código, ni
recomendaciones de refactor** — eso ya se cubrió en el análisis anterior
(PR-UI1) y quedó fuera de esta auditoría a propósito, tal como pediste.

---

## 1. Problemas críticos del producto

Estos son los que, en mi lectura, realmente le harían preferir Excel a
alguien que probara esto un mes entero.

### 1.1 El resumen bancario real puede rechazarse en silencio, con un mensaje que no explica por qué

**Qué intentaba hacer el usuario**: soltar su extracto de banco (`.xls`) en
la carpeta que el sistema vigila, como primer paso de todo el flujo.

**Qué ocurre hoy**: `BbvaBankStatementImportHandler.CanHandle` exige que el
nombre del archivo matchee uno de tres patrones fijos
(`Caja*.xls`/`*ahorros*.xls`/`*corriente*.xls`). El nombre real que BBVA le
pone a su propia exportación no tiene por qué cumplir ninguno de los tres —
de hecho, los dos archivos reales usados en este proyecto para validar el
parser (`Debito_29_05_2026_al_10_07_2026.xls`) no cumplen ninguno. Cuando
eso pasa, el archivo cae al handler genérico, que tampoco tiene un parser
registrado para `.xls` (solo `.xlsx`), y el resultado queda registrado en
`imports.html` como "0 insertados" con un mensaje interno ("no hay parser
registrado para la extensión") que no menciona en ningún momento que el
verdadero problema es el nombre del archivo. No hay ninguna notificación
activa — el usuario tiene que saber que existe esa pantalla y entrar a
revisarla por su cuenta.

**Qué esperaría el usuario**: soltar el archivo y que se procese, o al menos
recibir un motivo de rechazo que pueda entender y corregir sin adivinar.

**Impacto**: **Alto.** Es el primer paso de todo el flujo — si falla acá, no
hay clasificación, no hay dashboard, no hay nada que evaluar después.

**Dificultad de corrección**: Media (cambiar el criterio de reconocimiento de
`.xls` de "nombre de archivo" a "contenido del archivo", igual que ya se
hace con los PDF).

### 1.2 Asignar la cuenta financiera es un clic manual por cada movimiento, todos los meses, para siempre

**Qué intentaba hacer el usuario**: importar su resumen y ver los
movimientos ya vinculados a la cuenta/tarjeta de la que salieron — dato que
el propio documento ya declara (el PDF dice literalmente "Visa Signature
cuenta 1278896210" en su encabezado; el XLS dice "Cuenta: CA$
214-45099/4").

**Qué ocurre hoy**: cada fila de la tabla de Movimientos tiene su propio
`<select>` de cuenta financiera, siempre vacío al importar, sin ningún
atajo de "aplicar a todo este archivo". Con un mes real de este mismo
proyecto (442 filas de banco + decenas de tarjeta), eso es cientos de clics
que no representan ninguna decisión genuina — la cuenta ya está determinada
por el archivo de origen, el usuario solo está transcribiendo algo que el
sistema ya sabía y descartó.

**Qué esperaría el usuario**: que la cuenta ya viniera asignada, o al menos
poder asignarla una sola vez por archivo/importación en lugar de una vez por
fila.

**Impacto**: **Alto**, y crece cada mes — es de los pocos problemas de este
documento cuyo costo se multiplica exactamente por el volumen de uso real.

**Dificultad de corrección**: Media (cruzar el número de cuenta/tarjeta que
el parser ya extrae contra el catálogo de `FinancialAccount`).

### 1.3 No se puede crear una Contraparte sin abandonar la pantalla de Movimientos

**Qué intentaba hacer el usuario**: clasificar un movimiento de un comercio
que no vio antes y, en el mismo momento, dejar registrada esa contraparte
para no tener que volver a escribirla.

**Qué ocurre hoy**: el `<select id="cCounterparty">` del modal de
clasificación solo lista contrapartes que ya existen. No hay ningún botón
"+ Nueva" dentro del modal. La única forma de crear una es: memorizar o
copiar el nombre del comercio, cerrar (o abandonar) el modal de
clasificación, navegar a `/counterparties.html` (que además no tiene ningún
link visible — hay que conocer la URL), crear la contraparte ahí, volver a
Movimientos, encontrar de nuevo la misma fila, y recién ahí clasificar.

**Qué esperaría el usuario**: crear la contraparte en el momento, sin perder
el contexto del movimiento que la originó.

**Impacto**: **Alto** — en el primer mes de uso real, con decenas de
comercios nuevos, este viaje de ida y vuelta se repite decenas de veces. Es
el tipo de fricción que hace que alguien deje de completar el dato por
cansancio, no por decisión.

**Dificultad de corrección**: Baja-media (agregar un alta liviana dentro del
modal ya existente; el backend ya soporta todo lo necesario).

### 1.4 Un consumo real en dólares se guarda con el monto equivocado

**Qué intentaba hacer el usuario**: importar su resumen de tarjeta y
confiar en que los montos en dólares quedan registrados como dólares.

**Qué ocurre hoy**: verificado con una línea real del extracto Visa
(`NETFLIX.COM 654772294USD 10,37`) — el detector de moneda
(`CurrencyDetector.UsdWordRegex = \bUSD\b`) exige un límite de palabra antes
de "USD". Como el ID de suscripción queda pegado sin espacio
("...294USD"), no hay límite de palabra entre el dígito y la letra, y la
línea se procesa como si fuera en pesos, con el monto equivocado.

**Qué esperaría el usuario**: que el monto en dólares se reconozca como tal
siempre, no solo cuando hay un espacio antes de "USD" por casualidad de
formato.

**Impacto**: **Alto** — no es un problema de experiencia, es un problema de
que el número que el usuario ve no es el número real. Es exactamente el
tipo de cosa que hace perder confianza en cualquier sistema financiero de
un vistazo.

**Dificultad de corrección**: Baja (es un ajuste de una expresión regular).

### 1.5 Las cuotas de una misma compra nunca se reconocen entre sí

**Qué intentaba hacer el usuario**: clasificar una compra en cuotas una sola
vez y que las cuotas siguientes del mismo consumo lleguen ya resueltas.

**Qué ocurre hoy**: verificado con datos reales — el formato real de BBVA
para cuotas es `"C.02/03"` (con punto). Ni la normalización de descripción
del parser ni la del motor de sugerencias reconocen ese formato (ambos
esperan `"C1/3"`, sin punto). El resultado: `"MERPAGO*MDQLESPORTSA C.01/06"`
y `"MERPAGO*MDQLESPORTSA C.02/06"` se tratan como dos comercios sin ninguna
relación entre sí, cada uno exige clasificación manual completa.

**Qué esperaría el usuario**: clasificar la cuota 1 y que el sistema
reconozca las cuotas 2 a N del mismo consumo como el mismo caso.

**Impacto**: **Alto** para cualquier usuario que financia compras en cuotas
(muy común en Argentina) — multiplica el trabajo de clasificación
exactamente en los casos donde debería reducirse (misma compra, distinta
fila).

**Dificultad de corrección**: Baja (ajustar el reconocimiento del formato
real con punto).

---

## 2. Problemas importantes

No impiden usar la aplicación, pero generan fricción real mes a mes.

### 2.1 "Aceptar sugerencia" solo aparece cuando las 4 dimensiones están en máxima confianza a la vez

**Qué intentaba hacer el usuario**: clasificar rápido un movimiento cuyo
comercio ya conoce el sistema.

**Qué ocurre hoy**: el botón de un clic (`quickAcceptValues`) solo se
habilita cuando Categoría, Impacto, Tipo y Contraparte tienen, las 4 al
mismo tiempo, confianza Alta. Si una sola dimensión tiene confianza Media o
Baja (algo esperable en los primeros meses, cuando el historial todavía es
chico), no hay atajo — hay que abrir el modal completo igual, aunque 3 de
4 campos ya vinieran resueltos con certeza razonable.

**Qué esperaría el usuario**: poder aceptar lo que el sistema ya sabe con
confianza y completar a mano solo lo que falta, en vez de todo-o-nada.

**Impacto**: Medio — no bloquea, pero hace que el "modo rápido" se sienta
frágil: aparece y desaparece según un umbral invisible para el usuario.

**Dificultad de corrección**: Media.

### 2.2 Los valores por defecto del modal pueden inducir a clasificar mal

**Qué intentaba hacer el usuario**: clasificar un movimiento que no es una
compra (una transferencia, un cobro de sueldo, un interés).

**Qué ocurre hoy**: cuando no hay sugerencia previa, el modal siempre abre
con Tipo="Compra" e Impacto="Gasto real" preseleccionados — sin importar de
qué se trate el movimiento real. Un usuario apurado que confía en el valor
ya seleccionado puede guardar una transferencia o un ingreso como si fuera
un gasto de compra sin darse cuenta.

**Qué esperaría el usuario**: que el formulario no sugiera activamente algo
que probablemente esté mal, o que al menos no tenga ningún valor
preseleccionado cuando no hay evidencia real detrás.

**Impacto**: Medio — el daño (una métrica de gasto mal calculada) es
silencioso: no hay ningún error visible, solo un dato incorrecto que se
descubre, si acaso, mucho después.

**Dificultad de corrección**: Baja.

### 2.3 Revisar un mes anterior obliga a editar dos campos de fecha a mano, sin aviso del límite real

**Qué intentaba hacer el usuario**: mirar los movimientos de un mes que no
es el actual (por ejemplo, para hacer un cierre mensual).

**Qué ocurre hoy**: `movements.html` no tiene ningún botón de "mes
anterior/siguiente" como sí tiene `dashboard.html` — hay que tocar los
campos "Desde"/"Hasta" a mano y apretar "Aplicar" cada vez. Además, el
backend rechaza cualquier rango mayor a 90 días
(`MaxDateRangeDays`), pero eso no está indicado en ningún lugar de la UI —
si alguien intenta ver, por ejemplo, todo un año de una vez, se encuentra
con un error genérico sin ninguna pista de cuál es el rango permitido.

**Qué esperaría el usuario**: navegar entre meses con la misma comodidad que
ya existe en el Dashboard, y si hay un límite, que se lo digan antes de
fallar.

**Impacto**: Medio — molesta cada vez que se revisa algo que no es "hoy",
que es exactamente el caso de uso de "cierre mensual" que estás describiendo
como el hábito real que querés instalar.

**Dificultad de corrección**: Baja-media.

### 2.4 Las alertas de "posible duplicado/split" no tienen ninguna acción — quedan ahí para siempre

**Qué intentaba hacer el usuario**: entender por qué un movimiento aparece
marcado con "⚠" y resolver esa duda.

**Qué ocurre hoy**: la columna Alerta muestra el motivo (posible duplicado,
posible división, anomalía de redondeo) solo como texto informativo —
verificado en el código (`renderWarningCell`, comentario propio: "K6: solo
información — sin acción propia. Resolver el grupo... no tiene ninguna
pantalla hoy"). No hay forma de decir "esto no es un duplicado" ni de
descartar la alerta.

**Qué esperaría el usuario**: poder resolver la duda (confirmar que es o no
un duplicado) y que la alerta desaparezca una vez resuelta.

**Impacto**: Medio — genera ruido visual permanente sin ninguna salida, lo
que con el tiempo entrena al usuario a ignorar la columna entera (incluso
cuando alguna alerta sí importe).

**Dificultad de corrección**: Media-alta (requiere diseñar qué significa
"resolver" un grupo sospechoso).

### 2.5 El Dashboard puede mostrar un resumen "cerrado" calculado sobre una fracción mínima de los movimientos reales del mes

**Qué intentaba hacer el usuario**: confiar en los KPIs del mes (Ingresos,
Gastos, Balance, Ahorro) como un resumen real de su situación financiera.

**Qué ocurre hoy**: el Dashboard calcula todo sobre `ClassifiedMovement` —
movimientos que todavía están "Pendientes" en `movements.html` no aportan
nada a esos números, y no hay ningún indicador en el Dashboard de qué
porcentaje del mes está clasificado. Si un usuario importó 300 movimientos y
clasificó 40, el Dashboard muestra igual un KPI "cerrado" y prolijo, sin
ninguna advertencia de que representa una fracción chica de la realidad.

**Qué esperaría el usuario**: saber, de un vistazo, si el resumen que está
mirando es confiable o si todavía le falta clasificar buena parte del mes.

**Impacto**: Alto en confianza (es el escenario exacto de "vuelvo a Excel
porque no sé si estos números sirven"), lo bajo a Medio en la clasificación
general porque no impide seguir usando el resto de la app.

**Dificultad de corrección**: Baja-media (mostrar cuántos movimientos del
período siguen Pendientes junto a los KPIs).

### 2.6 Categoría tampoco se puede crear en contexto (mismo problema que Contraparte, menos grave)

**Qué intentaba hacer el usuario**: clasificar un movimiento que no encaja
bien en ninguna de las 11 categorías del sistema.

**Qué ocurre hoy**: mismo patrón que 1.3 — no hay pantalla de administración
de Categorías todavía (ni siquiera con URL directa), y el `<select
id="cCategory">` del modal solo lista lo que ya existe.

**Qué esperaría el usuario**: poder agregar una categoría propia sin salir
del flujo de clasificación.

**Impacto**: Medio (más bajo que Contraparte porque el seed de 11 categorías
del sistema cubre razonablemente el uso inicial de la mayoría de los
usuarios, a diferencia de Contraparte, que siempre arranca vacía).

**Dificultad de corrección**: Baja-media, una vez que exista la pantalla de
administración de Categorías (todavía no construida).

### 2.7 Las pantallas de administración son invisibles si no conocés la URL de memoria

**Qué intentaba hacer el usuario**: encontrar dónde administrar sus
cuentas, contrapartes o revisar el historial de importaciones.

**Qué ocurre hoy**: ni `accounts.html`, ni `imports.html`, ni
`counterparties.html` tienen un solo link entrante en toda la aplicación —
solo son alcanzables tipeando la URL directamente.

**Qué esperaría el usuario**: encontrar esas pantallas desde el menú
principal, como cualquier otra función del sistema.

**Impacto**: Alto en descubribilidad, aunque lo dejo en Medio acá porque ya
existe un análisis y una hoja de ruta específica para esto (PR-UI1) —
lo repito acá porque afecta directamente el recorrido de usuario nuevo que
pediste auditar, no porque sea nuevo.

**Dificultad de corrección**: Baja-media (ya analizada en detalle en otro
documento).

### 2.8 El mismo comercio real aparece bajo variantes de descripción que nunca se agrupan solas

**Qué intentaba hacer el usuario**: que clasificar "PedidosYa" una vez
alcance para todas las compras futuras a través de esa plataforma.

**Qué ocurre hoy**: el extracto real de este mismo proyecto trae, solo para
PedidosYa, variantes como `DLO*PEDIDOSYA MOSTAZA`, `DLO*PEDIDOSYA
MCDONALD`, `DLO*PEDIDOSYA MARKET`, `DLO*PEDIDOSYA BURGER K`, cada una con un
número de cupón distinto. El motor de sugerencias solo reconoce
descripciones **idénticas** — cada variante nueva (cada local distinto
pedido a través de la misma plataforma) exige clasificación manual la
primera vez, sin ningún concepto de "esto también es PedidosYa".

**Qué esperaría el usuario**: que reconocer un patrón de comercio recurrente
no dependa de que el texto completo coincida letra por letra.

**Impacto**: Medio — no bloquea, pero es exactamente el tipo de tarea
repetitiva que seguiría molestando después de 6 meses de uso, tal como
preguntaste.

**Dificultad de corrección**: Media (implica decidir un criterio de
coincidencia parcial, no solo exacta).

### 2.9 En un lote de cientos de filas, no queda ningún rastro visible de qué se clasificó recién

**Qué intentaba hacer el usuario**: revisar 300 movimientos en una sesión y
tener claro, en cualquier momento, qué ya procesó y qué le falta.

**Qué ocurre hoy**: cada clasificación exitosa muestra un toast que
desaparece solo a los pocos segundos (`showToast`, `ms = 3500` por
defecto). No hay ningún contador visible de "clasificados en esta sesión"
ni resumen de progreso más allá del recuento general de Pendientes en el
listado (que además no está en el badge del menú — ver PR-UI1). En una
sesión larga de clasificación, es fácil perder la noción de cuánto avanzó.

**Qué esperaría el usuario**: alguna noción persistente de progreso durante
una sesión larga de clasificación.

**Impacto**: Medio — molesta específicamente en el escenario que vos mismo
describís (clasificar 200-300 movimientos de una sentada).

**Dificultad de corrección**: Baja.

---

## 3. Mejoras deseables

Detalles chicos que no cambian si alguien usa o no la aplicación, pero
suman una vez que lo demás esté resuelto.

- **El campo "Comentario" del modal de clasificación es, en los hechos,
  invisible para siempre después de guardarlo.** Verificado en el DTO real:
  `MovementListItemDto` (lo que devuelve `GET /api/movements`, la fuente de
  toda la tabla de Movimientos) no incluye `Comment` en ningún campo, y
  ninguna otra pantalla lo muestra tampoco. El usuario puede escribir algo
  ahí, guardarlo, y no hay forma de volver a leerlo en ningún lugar del
  sistema.
- **El canal/detalle que el banco ya informa (`"100 - BANCA ONLINE"`, `"104
  - BANCA MOVIL"`, etc.) se captura al importar (`BankStatement.Detail`) pero
  no se muestra en ninguna pantalla.** Es información real que el usuario
  nunca ve, aunque el sistema ya la tiene.
- **Los impuestos y cargos del resumen de tarjeta (Impuesto de Sellos, IIBB,
  IVA RG 4240) no entran al sistema de ninguna forma** — no matchean el
  formato de línea de consumo de ningún parser. Es gasto real (del orden de
  varias decenas de miles de pesos por resumen en los documentos reales de
  este proyecto) que el Dashboard nunca refleja.
- **El botón "Actualizar" de Importaciones es manual** — después de soltar
  un archivo nuevo, hay que acordarse de volver a esa pantalla y apretar el
  botón; no hay ningún refresco automático ni indicación de que algo nuevo
  llegó.

---

## Roadmap priorizado solo por valor de usuario

No por facilidad técnica, no por arquitectura — por cuánto cambia realmente
la experiencia de usar esto todos los meses.

1. **Corregir el monto mal guardado en consumos USD sin espacio antes de
   "USD".** Es lo más grave de todo el documento (los números están mal) y,
   a la vez, lo más chico de arreglar — no hay ninguna razón para no
   resolverlo primero.
2. **Que el resumen bancario real de BBVA se reconozca sin depender del
   nombre del archivo.** Es el bloqueo del primer paso de todo el flujo —
   nada de lo que sigue importa si el usuario ni siquiera logra importar su
   propio extracto.
3. **Poder crear una Contraparte sin salir de la pantalla de Movimientos.**
   Es la fricción que más se repite en las primeras semanas de uso real
   (cada comercio nuevo), y la que más directamente determina si alguien
   sigue clasificando o abandona a mitad de camino.
4. **Que la cuenta financiera se asigne sola al importar.** Es la tarea
   repetitiva que más crece con el volumen de uso — cientos de clics por mes
   que no representan ninguna decisión real.
5. **Que las cuotas de una misma compra se reconozcan entre sí.** Multiplica
   trabajo de clasificación exactamente donde debería reducirse, y es un
   caso de uso muy común en Argentina.
6. **Un indicador de cobertura de clasificación en el Dashboard.** Sin esto,
   el usuario no puede confiar en los números que está mirando — y confiar
   en el resumen mensual es, en el fondo, el motivo por el que alguien
   usaría esto en vez de una planilla.
7. **Que "Aceptar sugerencia" funcione también cuando solo algunas
   dimensiones tienen confianza alta**, no únicamente cuando las 4
   coinciden. Reduce fricción en el caso más común de todos: clasificación
   parcialmente conocida.
8. **Navegación descubrible** (ya tiene su propio análisis y hoja de ruta,
   PR-UI1) — importante, pero por debajo de los puntos anteriores porque
   ninguno de ellos depende de que la navegación esté resuelta primero.
9. El resto de los hallazgos de la sección 2 y 3 (defaults del modal, alertas
   sin acción, agrupar variantes de un mismo comercio, comentario visible,
   impuestos de tarjeta, canal bancario visible) — mejoras reales, pero de
   menor impacto individual que las ocho anteriores.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ni un patch.
Cada hallazgo de este documento está sostenido por código o datos reales
citados explícitamente (líneas de extracto reales, nombres de archivo/
función/DTO reales) — no incluí ninguna opinión de UX sin verificarla contra
el estado actual del repositorio.
