# Revisión funcional del flujo de importación — FinancialMcp

> Basado en una prueba real (`Detalle_mov_cuenta_12_07_2026.xls` + `Últimos_movimientos_2026_07_12.xlsx`) y en lectura directa del código que produjo cada resultado reportado. No es una repetición de auditorías anteriores (arquitectura de handlers, deuda técnica) — es la primera revisión hecha **desde el resultado real que ve un usuario**, no desde el código hacia afuera.

# Resumen ejecutivo

La importación que probaste expone tres problemas independientes, cada uno con evidencia concreta en tus propios resultados:

1. **El parser de Caja de Ahorro lee el archivo con un desfasaje de una fila.** Los dos diagnósticos que viste ("Header inesperado" y "Fila 3: fecha inválida 'Fecha'") no son ruido — son la consecuencia directa y verificable de que el código asume que el título del archivo está en la fila 0, cuando en el archivo real está en la fila 1. Los 220 movimientos se insertaron igual (el parser tolera el corrimiento para las filas de datos), pero **el número de cuenta nunca se extrae**, porque se busca en la fila equivocada — es una causa técnica directa, no solo hipotética, de por qué después viste "Sin cuenta detectada".
2. **La importación de Tarjeta de Débito no te dice si funcionó.** El código calcula cuántas operaciones se enriquecieron exitosamente (`enriched`) pero **nunca lo devuelve a la pantalla** — solo lo escribe en un log de servidor que no ves. "Insertados: 0, Omitidos: 4, Sin diagnósticos" es indistinguible entre "no pasó nada" y "89 movimientos se enriquecieron bien, 4 quedaron ambiguos" — no tenés forma de saberlo desde la UI.
3. **Lo que faltaba ver no estaba oculto por un bug — estaba oculto por el filtro de fecha por defecto.** `movements.html` arranca siempre mostrando solo el mes calendario actual. Un archivo de Caja de Ahorro que cubre varios meses hacia atrás deja la mayoría de sus movimientos fuera de la vista por defecto, sin ningún aviso de que hay más datos importados de los que se están mostrando.

Ninguno de los tres requiere rediseñar la arquitectura — los tres son consecuencia de decisiones puntuales (un índice de fila, un campo no devuelto, un filtro por defecto) tomadas en distintos PRs, sin verlas juntas hasta ahora.

---

# Flujo actual

```
Usuario → botón "Importar archivo" (imports.html)
   → POST /api/imports (multipart/form-data)
       → IFileImportRouter.RouteAsync
           → BbvaBankStatementImportHandler (si el nombre matchea) → BbvaBankStatementParser
             o BbvaDebitCardEnrichmentHandler (si el nombre matchea) → BbvaDebitCardParser
             o TransactionImportHandler (catch-all)
       → ImportBatch (auditoría: insertados/omitidos/fallidos/duplicados) persistido
   ← respuesta: "Importación procesada. Revisá el resultado en la tabla."
Usuario → refresca imports.html → ve fila con Estado/Insertados/Omitidos/Fallidos
Usuario → navega a movements.html (filtro: mes calendario actual, por defecto)
   → ve movimientos, cada uno con Estado (Pendiente/Revisado/Confirmado),
     Cuenta financiera (asignada o "Sin cuenta detectada"), Alerta (si aplica)
```

Nada en este flujo, hoy, le dice al usuario **qué pasó realmente adentro de cada importación** más allá de cuatro contadores. La única forma de entender un resultado ambiguo es abrir el detalle de la corrida (si hay diagnósticos) o ir a Movimientos a inspeccionar fila por fila.

---

# Problemas encontrados

## UX

- **El resultado del import no distingue "no pasó nada" de "pasó, pero no en la forma que esperás verla."** Para Tarjeta de Débito, "Insertados: 0" es *el comportamiento correcto y buscado* (nunca crea movimientos) — pero se ve idéntico a un fallo silencioso. No hay ninguna palabra en pantalla que diga "esto es normal, esta importación enriquece, no crea."
- **"Omitidos: 4, Sin diagnósticos"** no le da al usuario ningún motivo. El código sabe exactamente por qué se omitió cada una (ambigua vs. sin match) — esa información existe en memoria durante el proceso y se descarta antes de llegar a la respuesta.
- **El filtro de período de `movements.html` no avisa que hay datos fuera de rango.** Después de importar un archivo que cubre meses, aterrizar en una vista que por defecto solo muestra el mes actual, sin ningún indicio de "hay más movimientos importados fuera de este período", hace que una importación exitosa se sienta como si hubiera fallado.
- **"Sin cuenta detectada" no explica si es un problema del archivo, del sistema, o algo que el usuario debe resolver.** Es el mismo texto sin importar la causa real.

## Reglas de negocio

- **La columna "Alerta" (K6) es puramente informativa, sin ninguna acción asociada.** Está documentado explícitamente en el propio código (`ClassificationUX.md`): "no implica ninguna acción propia; resolver un grupo sospechoso no tiene ninguna pantalla hoy." La pantalla que en algún momento permitía actuar sobre esto (`group-reconciliation.html`) se eliminó en una épica anterior. Hoy, ver una alerta y no poder hacer nada con ella es una experiencia incompleta, no un atajo hacia algo.
- **El estado "Confirmado" no puede ocurrir nunca hoy**, aunque la UI y las métricas lo siguen contemplando como un estado real (`ClassifyMovementHandler` solo produce `Reviewed`). No es un bug — es un remanente de un mecanismo de matching que ya no existe — pero puede confundir a quien vea "Confirmado" en la documentación o en el código y espere que algún movimiento llegue a tenerlo.

## Arquitectura

- **`ImportRunResult` no tiene espacio para representar "enriquecido."** El contrato que devuelve cada handler solo entiende `Inserted`/`Duplicates`/`Failed`/`Skipped` — pensado originalmente para "crear filas nuevas." Cuando se construyó el handler de Tarjeta de Débito (que por diseño nunca inserta), no había ningún campo natural donde poner "cuántas filas se enriquecieron" — terminó sin reportarse en ningún lado. Esto no es un error puntual: es el contrato compartido entre todos los handlers quedándose corto para un tipo de resultado que el propio sistema ya sabe producir.
- **El parser de Caja de Ahorro no valida su propia estructura contra el archivo real antes de fallar fila por fila.** Detecta que el header no es el esperado (lo loguea como diagnóstico) pero sigue adelante igual, fila por fila, en vez de tratarlo como una señal de "este archivo no tiene la forma que espero, mejor no seguir a ciegas."

## Información insuficiente

- El usuario no tiene, en ningún punto del flujo, una respuesta directa a: *"¿cuántos de mis movimientos quedaron con toda la información completa (cuenta + clasificación) después de esto?"*
- No hay ninguna indicación, en `imports.html` ni en `movements.html`, de que existan movimientos importados fuera del rango de fecha visible por defecto.
- El detalle de una corrida "Exitosa" con `Omitidos > 0` no explica esos omitidos — el estado "Exitosa" puede ocultar que 4 de las operaciones del archivo no se procesaron.

## Automatización faltante

- **Detección de cuenta financiera**: el archivo de Caja de Ahorro *sí* trae el número de cuenta (`CA$ 214-45099/4`, confirmado en el propio archivo real) — el sistema ya tiene el parser preparado para extraerlo (`AccountPattern` regex), pero además de estar mal indexado (ver "Flujo actual"), **aunque se extrajera bien, nada lo cruza contra `FinancialAccount` para asignar `FinancialAccountId` automáticamente** — ese cruce nunca se construyó. Es la combinación de dos automatizaciones faltantes, no una.
- No existe ningún mecanismo, ni siquiera manual, para asignar cuenta financiera a un movimiento desde la UI hoy (el `<select>` que lo permitía se retiró en una simplificación anterior de `movements.html`, priorizando otra fricción). El resultado: "Sin cuenta detectada" es, hoy, un estado permanente, sin salida desde el producto.

---

# Hallazgos prioritarios

Ordenados por impacto real en el usuario, no por facilidad de arreglo:

1. **El resultado de una importación de Tarjeta de Débito no comunica si funcionó.** Es el hallazgo de mayor impacto: literalmente no hay información suficiente para saber si la importación cumplió su propósito.
2. **El desfasaje de fila en el parser de Caja de Ahorro rompe la extracción del número de cuenta**, la única pieza de información que hoy permitiría automatizar la asignación de cuenta financiera. Es la causa raíz técnica detrás de "Sin cuenta detectada" para este archivo puntual.
3. **El filtro de período por defecto esconde datos recién importados sin avisar.** Alto impacto porque afecta la percepción general de "¿esto funcionó?" cada vez que se importa historial.
4. **No hay forma de asignar cuenta financiera desde la UI**, ni automática ni manual — "Sin cuenta detectada" no tiene ninguna salida hoy.
5. **La columna "Alerta" genera una pregunta ("¿qué hago con esto?") que el producto no responde.**
6. **Los diagnósticos de "Omitidos" no explican el motivo** — tanto en Caja de Ahorro (donde sí hay mensaje, pero mezclado con una advertencia de header que no es un fallo real) como en Tarjeta de Débito (donde no hay ninguno).

---

# Recomendaciones funcionales

Sin entrar en implementación — qué debería ser cierto desde el punto de vista del usuario:

- Después de importar, el usuario debería poder responder sin adivinar: *¿cuántos movimientos se crearon, cuántos se enriquecieron, cuántos quedaron con algo pendiente, y por qué?*
- Una importación que "enriquece en vez de crear" debería comunicarse como tal explícitamente, no inferirse de que "Insertados" da cero.
- Un "omitido" siempre debería tener un motivo visible, sin excepción — no debería haber un estado "Exitosa" que oculte operaciones sin explicar.
- El sistema no debería pedirle al usuario información que el archivo ya contiene (el número de cuenta) — si está en el archivo, debería usarse; si no puede usarse automáticamente, el usuario necesita al menos una forma manual de completarlo.
- Cuando hay más datos importados de los que se están mostrando, el usuario debería enterarse sin tener que adivinar que el filtro de fecha es la causa.
- Una alerta sin acción asociada dejó de tener sentido como columna independiente — o se le da una acción real, o dejar de mostrarla como si la tuviera.

---

# Roadmap sugerido

**Resolver primero** (impacto alto, alcance acotado, no dependen entre sí):
- Que el resultado de Tarjeta de Débito muestre cuántas operaciones se enriquecieron — es el hallazgo #1 y el de menor ambigüedad de solución.
- Corregir el desfasaje de fila del parser de Caja de Ahorro — un solo componente, con evidencia exacta de dónde está el error.
- Que "Omitidos"/"Exitosa" en el historial de importaciones siempre traiga un motivo, sin excepción.

**Resolver después** (impacto alto, pero requieren una decisión de producto primero, no solo técnica):
- Cómo se resuelve "Sin cuenta detectada" de forma sostenible — depende de si se prioriza la detección automática (usar el número de cuenta ya extraíble), un mecanismo manual, o ambos.
- Qué hacer con la columna "Alerta" — depende de si se le construye una acción real o se decide que ya no aporta como columna separada.

**Puede esperar** (impacto real pero menor, o consecuencia de los anteriores):
- Avisar en `movements.html` cuando hay movimientos importados fuera del período visible — una vez resueltos los puntos anteriores, este se vuelve más fácil de dimensionar (¿cuánto ocurre esto en la práctica una vez que la cuenta y el enriquecimiento ya se ven bien?).
- Limpiar la referencia a "Confirmado" como estado inalcanzable — cosmético, sin impacto funcional real, no urge.

No incluí este roadmap como compromiso de PRs — es una sugerencia de orden para cuando decidas planificar la siguiente etapa.
