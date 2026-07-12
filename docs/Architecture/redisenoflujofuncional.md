# Rediseño del flujo funcional — el producto, no el código

Este documento no describe clases, servicios ni arquitectura. Sintetiza,
como Product Owner, todo lo que las auditorías anteriores de esta
conversación ya verificaron con código y con los 4 extractos bancarios
reales de este proyecto (BBVA Cuenta, Visa, Mastercard) — y lo usa para
responder una sola pregunta de fondo: si el objetivo es que alguien use
esto todos los meses en vez de una planilla, ¿el modelo funcional actual es
el correcto, o es el resultado de cómo fue creciendo el código?

No es un documento técnico. No propone PRs. Cuestiona todo lo que exista,
incluidas decisiones ya implementadas, sin ningún compromiso con lo que ya
está construido.

---

## 1. Qué debería decidir realmente el usuario

**Cuenta financiera → debería configurarse una sola vez, nunca elegirse.**
El banco ya dice, por escrito, de qué cuenta o tarjeta sale cada
movimiento — verificado en los 4 documentos reales de este proyecto, sin
excepción. No hay ningún caso legítimo donde dos movimientos del mismo
archivo importado deban ir a cuentas distintas. La única intervención
humana razonable es ponerle un nombre reconocible a una cuenta la primera
vez que aparece — una vez en la vida útil de esa cuenta, no una vez por
movimiento.

**Categoría → debería elegirse, pero solo para una fracción de los
movimientos.** Es el único de los datos de clasificación que responde algo
que ni el banco ni el propio sistema pueden inferir la primera vez que
aparece un comercio genuino: "¿para qué se usó este dinero?". Pero esa
pregunta solo tiene sentido para Gasto o Ingreso real — una transferencia
entre cuentas propias o el pago de un resumen de tarjeta no tienen un "para
qué" que categorizar, y forzar una respuesta ahí (hoy, típicamente,
"Transferencias" como categoría-comodín) no agrega ninguna información
real.

**Contraparte → debería configurarse una sola vez, no elegirse cada vez.**
El nombre de un comercio nuevo se decide una vez; a partir de ahí, el
sistema debería reconocerlo solo. Lo que hoy no existe y debería: la
posibilidad de nombrarlo en el mismo instante en que se descubre que hace
falta, sin abandonar la pantalla donde se estaba trabajando.

**Tipo de movimiento → debería inferirse casi siempre, y como pregunta
independiente debería directamente desaparecer.** Verificado con los 442
movimientos reales de este proyecto: el vocabulario del banco (compras
dentro de "Consumos", "TRANSFERENCIA", "PAGO DE TARJETA...", "INTERESES
GANADOS", "PAGO DE HABERES") ya resuelve la enorme mayoría sin ambigüedad.
Verificado también, con evidencia de código, que ningún cálculo real que
el usuario ve depende de este dato. No hay ninguna razón para seguir
pidiéndolo fila por fila — el concepto puede seguir existiendo como un
detalle calculado y auditable, pero no como una decisión.

**Impacto financiero → debería inferirse en la enorme mayoría de los
casos, y preguntarse solo en el residuo genuino.** Es el dato que sí
importa de verdad — es literalmente lo que alimenta cada número del
Dashboard. Pero el signo de cada movimiento siempre se conoce con certeza,
y lo único que falta para resolver el resto es saber si la contraparte es
una cuenta propia — un dato que se configura una sola vez por cuenta
propia, no por movimiento. La pregunta debería sobrevivir solo para el
caso real y angosto donde ni el signo, ni el texto del banco, ni el
carácter de la contraparte alcanzan para decidir solos — principalmente,
una transferencia hacia alguien cuyo carácter (propio o tercero) todavía
no está configurado.

**Comentario → debería desaparecer en su forma actual.** Verificado:
ningún lugar del sistema vuelve a mostrarlo después de guardado. Es
trabajo que el usuario puede hacer, sin ningún retorno visible. O se le da
un propósito real (aparecer en algún historial o detalle consultable), o
se retira del formulario — dejarlo como está hoy es pedirle esfuerzo al
usuario a cambio de nada.

---

## 2. Qué debería resolverse durante la importación

Pensando en el producto ideal, no en el estado actual: al momento en que
un movimiento se le muestra por primera vez al usuario, ya debería llegar
con:

- **Cuenta financiera resuelta**, sin excepción — el único momento donde
  el usuario interviene es, una vez en la vida de cada cuenta real, para
  ponerle nombre.
- **Una propuesta fuerte de Tipo e Impacto**, calculada del vocabulario del
  banco, no una casilla vacía — el usuario la ve como un hecho a confirmar,
  no como una pregunta abierta.
- **Contraparte reconocida**, si el comercio ya se vio antes, con su
  Categoría heredada si ya tiene una configurada.
- **Confirmación visible de qué se importó y qué no** — nunca en silencio.
  Si un archivo se rechaza, el motivo debería ser algo que el usuario
  pueda entender y corregir, no un mensaje interno.
- **Aviso si el archivo ya se importó antes** — evitar que el mismo mes
  entre dos veces sin que el usuario lo note.

El resultado ideal: el usuario nunca "empieza de cero" con un movimiento —
siempre empieza revisando algo que el sistema ya intentó resolver.

---

## 3. Qué debería aprender el sistema

No hablo del mecanismo actual — hablo del comportamiento que un usuario
esperaría, sin que le importe cómo se logra:

- **Nunca debería volver a preguntar la cuenta de un número ya visto.**
- **Nunca debería volver a preguntar el nombre de una contraparte ya
  creada** — ni siquiera si la descripción cruda cambia levemente (un
  cupón distinto, una pasarela de pago distinta para el mismo comercio
  real).
- **Nunca debería tratar una cuota de una compra ya vista como si fuera un
  comercio nuevo** — verificado con datos reales: hoy sí lo hace.
- **Debería recordar, por cada contraparte, qué categoría/tipo/impacto le
  corresponde**, y aplicarlo solo, sin volver a preguntarlo — corrigiendo
  ese recuerdo solo cuando el usuario decide explícitamente cambiarlo.
- **Debería recordar el vocabulario del banco de forma permanente**, no
  como un patrón que hay que redescubrir mes a mes — si "PAGO DE
  HABERES" significó Cobro/Ingreso una vez, debería significarlo siempre,
  sin ninguna curva de aprendizaje por historial.

La diferencia importante con el mecanismo actual: lo de arriba no requiere
esperar a que el usuario haya clasificado nada antes. Un sistema que
"aprende" solo de lo que el usuario ya hizo no tiene nada que ofrecer el
primer día — un sistema que además lee lo que el banco ya escribió, sí.

---

## 4. Qué pantallas necesita el producto, empezando de cero

Ignorando lo que ya existe:

- **Una pantalla central de revisión de movimientos.** Sigue siendo el
  corazón del producto — pero no como una tabla plana de 8 columnas con un
  formulario de 4 campos por fila. Como una lista donde la mayoría de las
  filas ya vienen resueltas, y solo una minoría pide algo concreto.
- **Un Dashboard**, pero que muestre, junto a cada número, cuánto del
  período representa realmente — nunca un resumen que pueda leerse como
  completo sin serlo.
- **Una sola superficie de catálogos** (cuentas, categorías, contrapartes)
  — no tres pantallas top-level separadas. Las tres son, en esencia, la
  misma tarea (nombrar algo, marcarlo activo/inactivo, configurar un
  default), y un usuario que entra a administrar catálogos no debería
  sentir que está en tres productos distintos. Es una decisión distinta de
  la que tomaría de forma incremental sobre el código existente — acá, sin
  restricciones, las fusionaría en una sola experiencia.
- **Visibilidad del estado de las importaciones**, pero no necesariamente
  como una pantalla que hay que recordar visitar — como una señal ambiente
  ("se importaron 2 archivos hoy, 1 con problemas") visible desde donde el
  usuario ya está, con un detalle accesible para quien quiera profundizar.

**Qué desaparecería**: la cuenta financiera como columna/selector de la
pantalla de movimientos — deja de ser un dato de esa pantalla. El
formulario de clasificación con 4 campos siempre visibles — se reemplaza
por una vista de confirmación para lo ya resuelto y un formulario mínimo
(en la práctica, casi siempre un solo campo) para lo que de verdad hace
falta decidir.

**Qué se fusionaría**: las tres pantallas de catálogo, como ya se dijo.

**Qué sobra tal como está diseñado hoy**: ninguna pantalla completa sobra
— cada una resuelve un problema real. Lo que sobra es el *patrón de
interacción* de la pantalla de movimientos (formulario largo, siempre
igual, para todos los casos), no la pantalla en sí.

---

## 5. El flujo ideal, de "tengo un PDF" a "veo el Dashboard"

1. El usuario deja su extracto donde el sistema lo espera. El sistema
   identifica banco, tipo de documento y cuenta/tarjeta por el propio
   contenido — nunca por el nombre del archivo.
2. Si es la primera vez que aparece esa cuenta o tarjeta, se le pide un
   nombre. Una sola vez, un solo campo. Nada más.
3. El sistema procesa cada movimiento y le aplica todo lo que ya puede
   resolver solo: el vocabulario cerrado del banco, las contrapartes ya
   conocidas con sus categorías heredadas, el signo del importe.
4. El usuario abre la pantalla de revisión y ve, de entrada, cuánto ya
   quedó resuelto y cuánto necesita su atención — nunca una lista uniforme
   donde todo parece requerir el mismo esfuerzo.
5. Confirma en un solo gesto todo lo que el sistema ya resolvió con
   confianza razonable.
6. Para lo que de verdad necesita una decisión humana (un comercio nuevo,
   una transferencia sin destino conocido), se le pide una sola cosa por
   vez — con el monto y la descripción real a la vista, nunca a ciegas — y,
   si hace falta nombrar una contraparte nueva, lo hace en el mismo lugar,
   sin perder lo que estaba haciendo.
7. Al cerrar la sesión de revisión, el sistema le muestra explícitamente
   qué quedó pendiente, si algo quedó pendiente. Nunca termina en silencio.
8. Abre el Dashboard y ve los números junto con una afirmación clara de
   qué tan completos son — "esto es el 100% de lo que importaste" o "esto
   es el 80%, faltan 12 movimientos" — nunca ambigüedad.
9. El mes siguiente, repite el mismo ciclo, y cada vez cuesta menos: las
   cuentas ya están nombradas, los comercios recurrentes ya se reconocen,
   el vocabulario del banco no cambia. La curva de esfuerzo debería bajar
   mes a mes de forma marcada, no plana.

---

## 6. Dónde está hoy la mayor complejidad accidental

- **El motor que aprende de clasificaciones pasadas** es, en proporción al
  esfuerzo real invertido en construirlo, la pieza más sofisticada del
  producto — y la que menos aporta en el momento que más importa (el
  primer uso, sin historial todavía). Buena parte del terreno que cubre
  podría cubrirse, desde el primer movimiento, con reglas simples sobre el
  vocabulario del banco, que no necesitan esperar nada.
- **Separar "qué tipo de operación fue" de "cómo afecta mi patrimonio"
  como dos preguntas independientes**, cuando la evidencia real muestra
  que son, en la enorme mayoría de los casos, la misma decisión contada
  dos veces. Esta separación no es un error de implementación — es una
  decisión de diseño que nunca se volvió a cuestionar contra datos reales
  hasta esta serie de análisis.
- **Una taxonomía de diez tipos posibles de contraparte**, cuando el único
  propósito identificable con evidencia (distinguir cuentas propias de
  terceros) necesita, como mucho, dos.
- **Un sistema que detecta movimientos sospechosos** sin ninguna forma de
  resolverlos — construir la detección sin construir la resolución deja el
  trabajo a mitad de camino, y una alerta permanente sin acción entrena al
  usuario a ignorarla.
- **Clasificar en lote asumiendo homogeneidad** sin verificarla — una
  funcionalidad que suena a ahorro de tiempo pero traslada el riesgo de
  error al usuario sin dárselo a entender.

Ninguna de estas piezas es "código mal escrito" — son, todas, decisiones
de producto razonables en el momento en que se tomaron, que crecieron
técnicamente sin que nadie volviera a preguntarse si seguían siendo la
prioridad correcta.

---

## 7. Qué simplificaría, aunque implique borrar

- **La taxonomía de tipos de contraparte**, de diez valores a dos (cuenta
  propia de activo / cuenta propia de deuda), con todo lo demás retirado.
  No encontré, buscando con evidencia y no con intuición, ningún caso real
  que justifique los otros ocho.
- **"Tipo de movimiento" como pregunta del formulario de clasificación.**
  El dato puede seguir existiendo como resultado calculado, pero la
  pregunta en sí debería desaparecer de la experiencia del usuario por
  completo.
- **El campo Comentario, tal como existe hoy** — o se conecta a algo que
  el usuario vuelva a ver, o se retira.
- **Las alertas de posibles duplicados, tal como existen hoy** — sin una
  acción real de resolución diseñada, es mejor no mostrarlas que
  mostrarlas sin salida. O se completa el círculo (detectar y resolver), o
  se retira la mitad que hoy solo genera ruido.
- **El modo de clasificación en lote, tal como está planteado** — lo
  reemplazaría por "confirmar en lote lo que el sistema ya clasificó con
  confianza", que es una operación más segura y, para el usuario, más
  fácil de razonar que "aplicar lo mismo a lo que seleccioné a mano".

Ninguna de estas simplificaciones reduce lo que el producto puede hacer —
reducen exclusivamente cuánto tiene que decidir el usuario para lograr lo
mismo.

---

## Conclusión

### ¿Construiría el mismo producto si empezara hoy?

El mismo *dominio*, sí: clasificar movimientos bancarios reales en
Categoría/Impacto/Tipo/Contraparte, con catálogos administrables y un
Dashboard que resuma todo, es la forma correcta de modelar este problema
— los datos reales de los 4 extractos de este proyecto lo confirman una y
otra vez. El mismo *producto de interacción*, no. Construiría el mismo
esqueleto de dominio, pero invertiría el orden de las prioridades: primero
la capa que resuelve solo lo que el banco ya dice por escrito, después —
si acaso hiciera falta— un motor que aprenda de lo que queda. Hoy el orden
fue el inverso, y es exactamente por eso que el producto se siente más
trabajoso de lo que el dominio subyacente exigiría.

### Qué decisiones conservaría

- El modelo de clasificación con sus cuatro dimensiones de dominio — es
  correcto, y ninguna de ellas sobra como concepto.
- Que un movimiento clasificado sea un registro inmutable, trazable hasta
  su origen — es lo que hace confiable cualquier corrección futura.
- Que los catálogos se desactiven en vez de borrarse.
- Que el motor de sugerencias sea determinístico, sin IA — es la decisión
  correcta para este tamaño de problema, y no la cambiaría aunque
  reordenaría cuándo se invierte en ella.
- La simplicidad tecnológica general del proyecto (sin frameworks pesados)
  — proporcional al tamaño real del problema.

### Qué cambiaría radicalmente

- La asunción de que cada dimensión del dominio debe traducirse en una
  pregunta independiente al usuario — es la raíz de casi todos los
  problemas identificados en esta serie de análisis.
- El orden de inversión: resolver primero lo que el documento ya dice por
  escrito, no primero un motor que aprende de lo que el usuario ya hizo.
- Tratar la cuenta financiera como un dato de cada movimiento en vez de un
  dato de cada importación.
- Construir mecanismos de detección (duplicados, tipos de contraparte)
  más elaborados de lo que su propio uso real terminó necesitando.
- La ausencia total, desde el día uno, de cualquier indicación de cuánto
  de lo mostrado es confiable — no la agregaría al final, la trataría como
  parte del contrato básico del Dashboard desde la primera versión.

Este documento no es una lista de mejoras — es un diagnóstico de que el
modelo de *dominio* del proyecto siempre fue correcto, y que lo que
necesita revisión profunda es el modelo de *interacción* construido
encima, antes de seguir agregando cualquier funcionalidad nueva sobre él.
