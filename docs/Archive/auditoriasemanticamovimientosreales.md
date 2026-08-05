# Auditoría semántica de los movimientos reales

Commit base: `origin/master` (`7c6ff55`). Documento de solo análisis: no se
modificó ningún archivo del repositorio, no se generó ningún patch.

Fuentes de datos, todas reales, todas ya usadas en análisis anteriores de
esta conversación, releídas fila por fila en esta sesión para este
documento específico:
- Dos extractos de Caja de Ahorro BBVA (`Debito_29_05_2026_al_10_07_2026.xls`,
  153 filas; `Debito_30_03_2026_al_15_06_2026.xls`, 293 filas — 442 filas
  reales combinadas, mismo titular, misma cuenta CA$ 214-45099/4).
- Un tercer archivo (`Debito_04_06_2026_al_23_06_2026.xls`) resultó ser, en
  rigor, un `.xlsx` (OOXML) con extensión `.xls` — no pude abrirlo con
  `xlrd` (que solo soporta el binario legado). Es, en sí mismo, otro dato
  real relevante: confirma con un tercer archivo el mismo fenómeno que ya
  motivó la propia decisión de `XlsBankStatementReader.cs` de usar
  `WorkbookFactory.Create` (detección de formato real, no asumir BIFF por
  la extensión) — no lo incluyo en las cifras agregadas por no poder leerlo
  con la misma herramienta, pero lo señalo porque es evidencia real
  adicional de que "extensión `.xls`" no garantiza formato legado.
- Un PDF de Mastercard Black (`Mastercard_Junio.pdf`, 5 páginas).
- Un PDF de Visa Signature (`Visa_Junio.pdf`, 8 páginas, con el detalle de
  consumos más rico de los cuatro documentos).

**Corrección a un análisis anterior, encontrada al releer las filas exactas
para este documento**: en el primer análisis de esta conversación afirmé
que el título con el número de cuenta ("Detalle de Movimientos de Cuenta:
CA$ 214-45099/4") está en la fila 0 del XLS, tal como dice el propio
comentario de `BbvaBankStatementParser.cs`. Al releer las filas reales con
`xlrd`, fila por fila, para los dos archivos, encontré que la fila 0 está
**vacía** en ambos — el título real está en la fila 1, el encabezado
("Fecha | Concepto | ... ") en la fila 2, los datos recién desde la fila 3.
Confirmé además que `XlsBankStatementReader.cs` no filtra ni recorta filas
en blanco (agrega un array vacío por cada fila vacía, preservando el
índice). Como el parser lee el título en `TitleRowIdx = 0` (fila vacía) en
vez de la fila 1 real, el número de cuenta que el documento sí trae por
escrito probablemente se pierde en la práctica contra estos archivos
reales — corrijo acá lo que había afirmado antes con menos precisión.

---

## Categorías naturales, construidas desde cero a partir de los datos reales

No usé el enum `MovementType` para agrupar — agrupé primero por lo que el
texto y el signo de cada fila real muestran, y recién después comparé
contra el enum (sección siguiente).

### 1. Compras/consumos con tarjeta

**Ejemplos reales**: `PAGO CON VISA DEBITO Nro:493916` (débito, -$3.200,00,
210 filas de las 442 del débito), `DLO*PEDIDOSYA MOSTAZA` (Visa,
-$39.284,00), `MERPAGO*LACOCA` (Visa), `SHELL CLASA` (Visa, combustible),
`ACOFAR FCIA FERRANDO` (Visa, farmacia).

- **Tipo de operación real**: compra de bien o servicio en un comercio.
- **Qué conoce ya el parser**: descripción completa, importe, fecha, y —
  para tarjeta — que la línea está dentro de la sección "Consumos" del PDF
  (delimitada por sus propios marcadores de inicio/fin), lo cual ya
  certifica que es una compra sin necesidad de leer el texto.
- **Qué termina pidiéndose al usuario**: Categoría, Tipo, Impacto,
  Contraparte — las 4 dimensiones completas, hoy sin excepción.
- **Qué podría inferirse**: Tipo e Impacto, con certeza casi total, del
  solo hecho de estar en "Consumos". Contraparte, parcialmente — el nombre
  del comercio está en el texto, pero mezclado con el prefijo de pasarela
  (`DLO*`, `MERPAGO*`) que habría que limpiar primero.

### 2. Pagos de resumen de tarjeta (liquidación de deuda)

**Ejemplos reales, dos patrones textuales distintos para el mismo hecho**:
`PAGO DE TARJETA MASTERCARD Nro:00045099` (-$885,00, coincide centavo a
centavo con el saldo del PDF Mastercard) y, en los mismos archivos,
`CUENTA MASTERCARD NRO. 77127893900598` (-$943.902,74) /
`CUENTA VISA NRO. 79127889621098` (-$272.446,64) — montos grandes,
negativos, con el número completo de tarjeta en el texto en vez de la
palabra "Pago".

- **Tipo de operación real**: saldar la deuda de un resumen de tarjeta ya
  contado como gasto en el momento del consumo.
- **Qué conoce ya el parser**: el importe coincide, verificablemente, con
  el saldo de un PDF de tarjeta ya importado — pero el sistema no cruza
  ambos documentos entre sí, cada uno se procesa de forma aislada.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones completas, igual
  que cualquier otra fila.
- **Qué podría inferirse**: Tipo=Payment/Impacto=DebtPayment con altísima
  confianza del prefijo textual — pero acá hay un hallazgo real
  importante: **son dos patrones de texto genuinamente distintos**
  (`"PAGO DE TARJETA..."` y `"CUENTA VISA/MASTERCARD NRO...."`) para
  exactamente el mismo hecho de negocio. Cualquier regla de inferencia por
  prefijo que solo reconozca el primero se perdería el segundo — una
  fracción real y no trivial de estos pagos (los `"CUENTA...NRO"` son, de
  hecho, los montos más grandes de todo el dataset).

### 3. Transferencias bancarias (propias o a terceros, vía CBU)

**Ejemplos reales**: `TRANSFERENCIA` (115 filas), `Transferencia inmediata`
(41 filas), `TRANSF DEBITO Nro:026888` (-$1.100.000,00),
`TRANSF CREDITO Nro:448290` (+$92.277,00).

- **Tipo de operación real**: movimiento de fondos entre cuentas bancarias,
  propias o de terceros.
- **Qué conoce ya el parser**: importe, signo, fecha, y el hecho textual de
  ser transferencia — con altísima confianza (156 filas reales combinadas
  con vocabulario cerrado).
- **Qué termina pidiéndose al usuario**: las 4 dimensiones. En particular,
  Impacto (¿interno o real?) hoy es 100% manual.
- **Qué podría inferirse**: que es una transferencia, sí, con certeza. Si es
  hacia mí mismo o a un tercero, no — ese dato no está en el texto del
  banco en ningún caso real observado; depende de saber si el destino es
  una cuenta propia, dato que hoy el sistema no tiene en ningún lado (ver
  el análisis dedicado a `CounterpartyType`).

### 4. Transferencias hacia/desde una billetera digital vinculada

**Ejemplos reales**: `TRANSFERENCIA CCP214 021784 1 Nro:00010008`
(-$1.026,00, canal "104 - BANCA MOVIL"), `TRANSFERENCIA CAP094 375414 3
Nro:00010008` (-$5.500,00), con un código de cuenta/alias distinto embebido
en cada línea (CCP/CAP + un número que varía por movimiento).

- **Tipo de operación real**: transferencia entre la cuenta bancaria y una
  billetera/fintech vinculada (el patrón CCP/CAP es típico de
  CVU/alias de billeteras virtuales en Argentina, distinto del CBU
  bancario tradicional).
- **Qué conoce el parser**: mismo nivel que la categoría 3 — importe,
  signo, fecha, prefijo textual reconocible.
- **Qué termina pidiéndose al usuario**: igual, las 4 dimensiones.
- **Qué podría inferirse**: igual que la categoría 3 — es, en la práctica,
  el mismo caso de negocio (una transferencia) con una variante textual
  distinta. No encontré evidencia de que la distinción CCP/CAP vs.
  transferencia bancaria común importe para ninguna decisión real del
  usuario — la incluyo como categoría separada por rigor de "agrupar desde
  los datos", pero no la trataría distinta de la categoría 3 en un modelo
  final.

### 5. Débito automático de servicios

**Ejemplo real**: `DEBITO DIRECTO` (15 filas).

- **Tipo de operación real**: pago recurrente de una factura de servicio
  (luz, gas, internet, etc. — el texto real de este dataset no desglosa el
  servicio específico en el Concepto).
- **Qué conoce el parser**: importe, signo, fecha, prefijo textual
  reconocible con vocabulario cerrado.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones.
- **Qué podría inferirse**: Tipo/Impacto con alta confianza (siempre
  Expense real, nunca deuda de tarjeta) — y acá aparece la ambigüedad
  concreta con la categoría 2: un débito automático de servicio y un pago
  de tarjeta son, en el vocabulario técnico actual, ambos candidatos a
  `MovementType.Payment`, pero uno es `Expense` y el otro `DebtPayment` —
  la misma etiqueta de Tipo, dos efectos patrimoniales opuestos. Es la
  ambigüedad Compra/Pago que señalé conceptualmente en el análisis
  anterior, ahora con dos categorías reales y distinguibles por texto que
  la confirman con datos, no con intuición.

### 6. Extracción de efectivo con tarjeta de débito

**Ejemplo real**: `OPERACION EN EFECTIVO TARJE 96477108 OP9300`
(-$60.000,00, repetido con el mismo monto fijo varias veces).

- **Tipo de operación real**: retiro de efectivo en cajero automático con
  tarjeta de débito.
- **Qué conoce el parser**: importe, signo, fecha, prefijo reconocible.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones — y acá el
  usuario se encuentra, además, con que ningún valor de `MovementType`
  describe bien lo que pasó (no es Compra, no es Transferencia en el
  sentido de mover fondos entre cuentas, no es Pago de una factura).
- **Qué podría inferirse**: Tipo/Impacto con alta confianza del texto —
  pero el enum actual no tiene ningún valor que lo represente
  correctamente, solo aproximaciones forzadas.

### 7. Intereses a favor

**Ejemplos reales**: `INTERESES GANADOS` (5 filas), `AJUSTE INTERESES
GANADOS` (3 filas) — siempre positivo, montos chicos (+$3,92, +$14,65).

- **Tipo de operación real**: interés acreditado por el banco sobre saldo.
- **Qué conoce el parser**: importe, signo (siempre positivo en los casos
  reales), fecha, prefijo reconocible.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones.
- **Qué podría inferirse**: Tipo/Impacto con certeza total — el signo solo
  ya resuelve "ganado, no pagado", sin necesitar ningún otro dato.

### 8. Cobro recibido de un tercero vía plataforma de cobro

**Ejemplo real**: `PAGO A TERCEROS OP.1108185` (+$80.653,63, canal
"587 - DATANET").

- **Tipo de operación real**: cobro recibido a través de una plataforma de
  pagos/cobranza (el nombre del canal, DATANET, es un procesador de pagos
  real en Argentina) — dinero que entra por haber cobrado algo, no un
  sueldo.
- **Qué conoce el parser**: importe, signo, fecha, canal.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones.
- **Qué podría inferirse**: que es un ingreso, sí (signo positivo). Que es
  distinto de un sueldo, solo por el canal — dato que hoy el sistema
  captura (`BankStatement.Detail`) pero, según el análisis de flujo
  completo, nunca muestra ni usa.

### 9. Cobro de sueldo/haberes

**Ejemplo real**: `PAGO DE HABERES Nro:99999999` (+$8.107.319,00 y
+$5.190.695,00 en meses distintos — montos grandes, recurrentes).

- **Tipo de operación real**: acreditación de sueldo.
- **Qué conoce el parser**: importe, signo, fecha, prefijo reconocible con
  vocabulario cerrado y específico ("PAGO DE HABERES", inconfundible).
- **Qué termina pidiéndose al usuario**: las 4 dimensiones, otra vez
  completas, para algo que el texto ya identifica sin ambigüedad.
- **Qué podría inferirse**: todo — Tipo, Impacto, y hasta una Categoría
  razonable ("Ingresos", que ya existe como categoría de sistema
  sembrada), sin ninguna decisión humana real pendiente.

### 10. Cambio de moneda extranjera (conversión de saldo)

**Ejemplo real**: `Cambio de moneda extranjera` (+$141.500,00,
+$1.080.000,00 — canal "104 - BANCA MOVIL").

- **Tipo de operación real**: conversión de saldo entre pesos y moneda
  extranjera dentro de la misma cuenta/banco — no es un gasto ni un
  ingreso real, es un cambio de forma del mismo dinero.
- **Qué conoce el parser**: importe, signo, fecha, canal — nada del texto
  distingue si esta conversión tiene una contrapartida (otra fila con
  signo opuesto, en la otra moneda) en el mismo extracto o en otro.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones, sin que
  ninguna de las 9 opciones de `MovementType` describa bien lo que pasó.
- **Qué podría inferirse**: que no es ni gasto ni ingreso real (más cerca
  de `InternalMovement` que de cualquier otra cosa), del prefijo textual.
  Pero el enum no tiene un valor natural para "cambié pesos por dólares",
  distinto de una transferencia entre cuentas.

### 11. Ajustes/compensaciones técnicas del banco

**Ejemplo real**: `COMPENSACION DE FONDOS` (-$0,04, -$8,34, -$0,30 — montos
ínfimos, consistentes con redondeo o reconciliación interna del banco).

- **Tipo de operación real**: ajuste técnico del banco, sin relación con
  ninguna decisión del usuario.
- **Qué conoce el parser**: importe, signo, fecha, prefijo reconocible.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones — para un
  movimiento de $0,04 que no representa ninguna decisión financiera real.
- **Qué podría inferirse**: todo, con certeza — es, literalmente, el caso
  de libro para `Adjustment`, y el único de los 9 valores actuales del
  enum que encontré con un caso real limpio y sin ambigüedad.

### 12. Impuestos y cargos del resumen de tarjeta

**Ejemplos reales**: `IMPUESTO DE SELLOS`, `IIBB PERCEP-BSAS 2,00%`,
`IVA RG 4240 21%`, `DB.RG 5617 30%` — presentes en ambos PDF de tarjeta,
página "Impuestos, cargos e intereses", sumando del orden de $42.000 solo
en el resumen Visa de este proyecto.

- **Tipo de operación real**: cargo impositivo/regulatorio sobre el
  resumen de tarjeta.
- **Qué conoce el parser**: nada — estas líneas **no matchean el formato
  de línea de consumo de ningún parser** (`IsTransactionLine` de
  `BbvaTransactionLineParser`/`MastercardTransactionLineParser` las
  descarta silenciosamente), ya identificado en el análisis de flujo
  completo. No llegan siquiera a ser un movimiento pendiente.
- **Qué termina pidiéndose al usuario**: nada — no hay nada que pedir
  porque el movimiento nunca existe en el sistema.
- **Qué podría inferirse**: todo, si algún día se importaran — es
  exactamente el caso de uso para `MovementType.Fee`, hoy sin ningún dato
  real que lo alimente en este sistema.

### 13. Reintegros/bonificaciones sobre un consumo previo

**Ejemplo real**: `BONIF. CONSUMO OPENPAY*CAMPO VERDE 007214` (-$3.248,46,
signo negativo dentro de la misma sección "Consumos" del PDF Visa, justo
después de un consumo positivo del mismo comercio con el mismo cupón).

- **Tipo de operación real**: devolución parcial de un consumo previo del
  mismo comercio.
- **Qué conoce el parser**: nada especial — se procesa como un consumo más
  dentro de "Consumos", indistinguible de una compra nueva salvo por el
  signo negativo (que el parser no interpreta como señal de nada distinto).
- **Qué termina pidiéndose al usuario**: las 4 dimensiones, exactamente
  igual que si fuera una compra nueva sin relación con la anterior.
- **Qué podría inferirse**: que es un reintegro, del signo negativo dentro
  de una sección de consumos (patrón inusual y reconocible). Ya se
  documentó en el análisis anterior que, aunque se infiriera, el sistema
  hoy no le da ningún trato distinto de un Income genérico.

### 14. Compras en cuotas

**Ejemplos reales**: `MERPAGO*ZETA C.02/03` (+cupón, un mes), y en teoría
`C.01/03` el mes anterior — `MERPAGO*MDQLESPORTSA C.01/06`.

- **Tipo de operación real**: una compra financiada, fraccionada en varias
  liquidaciones mensuales del mismo consumo original.
- **Qué conoce el parser**: nada del vínculo entre cuotas — cada línea se
  procesa de forma completamente aislada.
- **Qué termina pidiéndose al usuario**: clasificar cada cuota como si
  fuera un comercio nuevo, ya documentado en el análisis de flujo completo.
- **Qué podría inferirse**: que es la misma compra que una cuota anterior,
  si el sistema reconociera el formato real `"C.NN/NN"` (con punto) — hoy
  no lo hace (verificado contra el código en análisis anteriores).

### 15. Suscripciones/consumos en moneda extranjera

**Ejemplos reales**: `PLAYSTATION USD 5,99`, `Spotify USD 3,88`,
`NETFLIX.COM 654772294USD 10,37`, `Etsy*Monthly Bill USD 0,40`.

- **Tipo de operación real**: consumo recurrente en dólares, generalmente
  una suscripción.
- **Qué conoce el parser**: el monto en USD explícito en la mayoría de los
  casos — salvo el caso ya documentado (`NETFLIX.COM`) donde la falta de
  espacio antes de "USD" hace que el detector de moneda no lo reconozca.
- **Qué termina pidiéndose al usuario**: las 4 dimensiones, igual que
  cualquier consumo.
- **Qué podría inferirse**: Tipo=Purchase con certeza (misma sección de
  consumos); Categoría="Suscripciones" (ya existe como categoría de
  sistema) con alta confianza dado el vocabulario reconocible
  (Netflix/Spotify/PlayStation).

---

## Comparación contra `MovementType`

### Qué valores sobran

Ninguno, en el sentido estricto de "nunca tiene un caso real que lo
justifique" — cada uno de los 9 valores actuales tiene al menos un patrón
real de este dataset que encaja razonablemente. Lo que sobra, según el
análisis anterior de esta conversación (verificado con evidencia de
código, no repetido acá), es que sea una **decisión independiente pedida
al usuario** — no que el valor en sí no represente nada real.

### Qué valores faltan

- **Cambio de moneda extranjera / conversión de saldo** (categoría 10) —
  ningún valor actual lo describe bien; forzarlo a `Transfer` o `Other`
  pierde información real.
- **Extracción de efectivo** (categoría 6) — mismo problema; no es Compra,
  no es Transferencia entre cuentas, no es Pago de una factura.

### Qué valores nunca aparecen, literalmente, en el texto real del banco

De los 9 valores, solo `AJUSTE INTERESES GANADOS` contiene la palabra
"ajuste" en el texto real — y ese caso específico está mejor cubierto por
`Interest` que por `Adjustment`. `Adjustment` como concepto genérico e
independiente no tiene, en estos 4 documentos, ningún caso real donde el
propio banco use ese vocabulario — el único caso limpio que encontré para
él (`COMPENSACION DE FONDOS`, categoría 11) usa un texto completamente
distinto.

### Qué valores aparecen constantemente

- **Purchase** — dominante: 210 de 442 filas de débito solo con "PAGO CON
  VISA DEBITO", más el grueso de "Consumos" de ambos PDF de tarjeta.
- **Transfer** — 156 filas combinadas de "TRANSFERENCIA"/"Transferencia
  inmediata", más las variantes CCP/CAP/TRANSF DEBITO-CREDITO.
- **Payment** — un bloque real y significativo si se combinan débito
  directo (15) + los dos patrones de pago de tarjeta (9 filas + los montos
  grandes de "CUENTA VISA/MASTERCARD NRO").

### Cuáles son ambiguos, con evidencia real y no intuición

**`Payment` es, con los datos reales de este proyecto, el valor más
ambiguo de los 9.** Cubre, con la misma etiqueta de Tipo, dos categorías
reales con efecto patrimonial opuesto: débito automático de un servicio
(categoría 5, `FinancialImpact.Expense`) y pago de resumen de tarjeta
(categoría 2, `FinancialImpact.DebtPayment`). No es una hipótesis — son
dos patrones de texto reales, distinguibles con certeza
(`"DEBITO DIRECTO"` vs. `"PAGO DE TARJETA..."`/`"CUENTA VISA/MASTERCARD
NRO..."`), que hoy comparten el mismo valor de `MovementType` sin que el
campo aporte ninguna señal para diferenciarlos — solo `FinancialImpact`,
elegido aparte, los distingue de verdad.

---

## La pregunta final

*Si diseñara `MovementType` únicamente observando estos movimientos
reales, sin pensar en compatibilidad ni en el código existente, ¿cómo
sería?*

Agrupando por lo que el texto real y el signo realmente distinguen —no por
lo que sería conceptualmente prolijo— la taxonomía que sale de los datos
es esta, con la misma cantidad aproximada de categorías que hoy (9-10), pero
con contenido distinto en tres puntos concretos:

1. **Compra** (comercio, cualquier moneda, con o sin cuotas — la moneda y
   las cuotas son atributos del monto, no un tipo de operación distinto).
2. **Pago de servicio/factura** (débito automático o pago manual de una
   factura — `Expense`).
3. **Pago de deuda de tarjeta/préstamo** (separado explícitamente de (2),
   porque los datos reales muestran que son dos patrones de texto
   distintos con efecto patrimonial opuesto — la ambigüedad real
   encontrada arriba, resuelta separando en dos valores en vez de uno).
4. **Transferencia** (entre cuentas propias o a terceros, bancaria o vía
   billetera digital — sin distinguir el canal, porque los datos reales no
   muestran que esa distinción importe para ninguna decisión).
5. **Extracción de efectivo** — nuevo, sin equivalente hoy.
6. **Cambio de moneda / conversión de saldo** — nuevo, sin equivalente hoy.
7. **Cobro** (sueldo, cobro de terceros, venta — con Contraparte
   distinguiendo el matiz real, no un valor de Tipo separado por cada
   variante).
8. **Interés** (a favor o en contra — aunque, como ya se estableció, el
   signo del importe alcanza solo para esto).
9. **Comisión/cargo** (bancario o de tarjeta — hoy sin ningún dato real que
   lo alimente, porque ni siquiera se importa).
10. **Ajuste/otro** (catch-all real, para compensaciones técnicas del
    banco y cualquier residuo).

**La diferencia real con el modelo actual no es el número de categorías —
es que los datos reales piden separar donde hoy hay una sola etiqueta
ambigua (Pago de servicio vs. Pago de deuda) y agregar dos casos que hoy no
tienen ningún lugar (extracción de efectivo, cambio de moneda).** Ninguno
de los 9 valores actuales sobra por completo; dos casos reales faltan; uno
es ambiguo de una forma verificable, no solo intuida.

Esto no contradice la conclusión del análisis anterior de esta
conversación (que `MovementType`, decidido campo por campo por el usuario,
no tiene consumidor real verificado) — la confirma desde el ángulo
opuesto: incluso diseñando la taxonomía "correcta" desde los datos reales,
el resultado sigue siendo información que el propio texto del banco ya
resuelve en la enorme mayoría de los casos (Compra, Transferencia, Pago de
servicio, Pago de deuda, Cobro de sueldo, Interés son, los seis,
identificables por prefijo textual con el vocabulario cerrado que estos 4
documentos ya mostraron). Los datos confirman que el *contenido* del campo
representa algo real del dominio financiero — y, al mismo tiempo, que ese
contenido casi nunca necesita preguntársele al usuario para completarse
correctamente.

---

## Confirmación

Durante este análisis no modifiqué ningún archivo del repositorio, no
ejecuté `git add`, no hice ningún commit ni push, y no escribí ningún
patch. Cada categoría está sostenida por al menos una fila real citada
textualmente de los 4 documentos bancarios, releídos específicamente para
este documento — incluida la corrección sobre la fila real del título del
XLS, encontrada al releer los datos con más cuidado que en el primer
análisis de esta conversación.
