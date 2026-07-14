# Épica M — Mejoras al flujo de importación

> Estado: 📋 planificada, no implementada. Documento de planificación, no de diseño técnico — traduce los hallazgos de `docs/Analysis/ImportWorkflowReview.md` en historias de usuario chicas e independientes, cada una implementable en uno o pocos PRs. No define implementación técnica (eso queda para el documento de diseño de cada historia, cuando se decida encararla).

Cada historia mapea a uno o más hallazgos prioritarios del análisis. Se mantiene la separación entre UX y arquitectura salvo en los casos donde el propio hallazgo es, a la vez, un problema de contrato y de visualización — en esos casos se aclara explícitamente por qué no se separan.

---

## M1 — Mostrar el resultado real de la importación de Tarjeta de Débito

**Objetivo.** Que el usuario pueda ver, después de importar un archivo de Tarjeta de Débito, cuántas operaciones se enriquecieron con éxito — no solo cuántas se insertaron.

**Problema que resuelve.** Hallazgo prioritario #1. `BbvaDebitCardEnrichmentHandler` calcula `enriched` correctamente pero solo lo loguea; la UI recibe `Insertados: 0` siempre (por diseño, este handler nunca inserta), indistinguible entre "no pasó nada" y "funcionó como corresponde".

**Beneficio para el usuario.** Certeza inmediata de si la importación cumplió su propósito, sin tener que confiar a ciegas en un resultado ambiguo.

**Riesgos.** El campo nuevo debe agregarse a un contrato (`ImportRunResult`) que comparten los otros dos handlers — hay que decidir si el campo queda opcional/no aplicable para ellos sin que la UI lo muestre donde no corresponde (evitar mostrar "Enriquecidos: 0" en una importación de Caja de Ahorro, donde el concepto no existe).

**Dependencias.** Ninguna — puede implementarse de forma aislada.

**Criterios de aceptación.**
- Al importar un archivo de Tarjeta de Débito con operaciones enriquecibles, la UI muestra la cantidad enriquecida como un dato propio, distinto de "Insertados".
- El historial de importaciones (`imports.html`) refleja el mismo dato para corridas nuevas.
- Una importación de Caja de Ahorro no muestra el campo "Enriquecidos" (no aplica a ese handler).

**Tamaño estimado.** M.

---

## M2 — Corregir el desfasaje de fila en el parser de Caja de Ahorro

**Objetivo.** Ajustar los índices de fila (`TitleRowIdx`/`HeaderRowIdx`/`DataStartIdx`) de `BbvaBankStatementParser` para que coincidan con la estructura real del archivo BBVA.

**Problema que resuelve.** Hallazgo prioritario #2. El parser asume que el título está en la fila 0 y el header en la fila 1; en el archivo real están corridos una fila. Esto produce los diagnósticos "Header inesperado" y "Fila 3: fecha inválida 'Fecha'", y hace que la extracción del número de cuenta (que lee la fila del título) siempre devuelva vacío.

**Beneficio para el usuario.** Elimina diagnósticos ruidosos que no corresponden a un problema real del archivo, y habilita que el número de cuenta se extraiga correctamente — precondición técnica de M5.

**Riesgos.** Si existen archivos ya importados o variantes de exportación de BBVA con la estructura de fila anterior (offset distinto), corregir el índice fijo podría romper esos casos en vez de arreglarlos. Conviene validar contra más de una muestra real antes de mergear, no solo el archivo de esta prueba.

**Dependencias.** Ninguna para implementarse; es precondición de M5 (autodetección de cuenta).

**Criterios de aceptación.**
- Al importar el archivo real de prueba (`Detalle_mov_cuenta_*.xls`), no aparecen diagnósticos de header inesperado ni de fecha inválida en la fila de datos.
- El número de cuenta se extrae correctamente y queda disponible en `BankStatement.AccountNumber`.

**Tamaño estimado.** S.

---

## M3 — Explicar el motivo de cada movimiento omitido

**Objetivo.** Que toda operación reportada como "Omitida" en el historial de importaciones tenga siempre un motivo visible (ambigua, sin coincidencia, duplicada, etc.), para los handlers de Caja de Ahorro y Tarjeta de Débito.

**Problema que resuelve.** Hallazgo prioritario #6. Hoy "Omitidos: 4, Sin diagnósticos" (Débito) y el diagnóstico de header mezclado con errores reales (Caja de Ahorro) no le dan al usuario ninguna razón accionable.

**Beneficio para el usuario.** Entender qué pasó con cada operación sin tener que adivinar ni pedir soporte.

**Riesgos.** Cubrir "todos los handlers" en un único PR puede crecer de alcance rápido. Conviene acotar esta historia a los dos handlers involucrados en el análisis (Débito y Caja de Ahorro) y no extenderla al catch-all sin evaluarlo aparte.

**Dependencias.** Ninguna dependencia dura, pero conviene secuenciarla después de M1: ambas tocan el mismo tipo de superficie (qué información devuelve un handler más allá de los contadores actuales), y resolverlas juntas evita tocar el contrato dos veces.

**Criterios de aceptación.**
- Para el archivo de Débito con 4 omitidos, el historial muestra un motivo individual por cada omisión.
- Para Caja de Ahorro, un diagnóstico de header no se presenta con el mismo peso que un error real de fila.

**Tamaño estimado.** M.

---

## M4 — Avisar cuando hay movimientos importados fuera del período visible

**Objetivo.** Detectar y mostrar, en `movements.html`, que existen movimientos importados fuera del filtro de fecha actualmente aplicado.

**Problema que resuelve.** Hallazgo prioritario #3. El filtro por defecto (mes calendario actual) oculta historial recién importado sin ningún aviso, generando la percepción de que la importación falló.

**Beneficio para el usuario.** Evita la confusión entre "no se importó nada" y "se importó, pero está fuera del período que estás mirando".

**Riesgos.** Bajo — es una adición, no modifica comportamiento existente. Riesgo menor de falsos positivos si el usuario cambia de cuenta o de otros filtros por motivos ajenos a una importación reciente.

**Dependencias.** Ninguna dependencia técnica dura. El propio análisis sugiere resolverla después de M1/M2/M5: una vez que el enriquecimiento y la cuenta financiera se vean bien, es más fácil dimensionar cuánto pesa este problema en la práctica.

**Criterios de aceptación.**
- Al importar movimientos con fecha fuera del mes actual, aparece un aviso indicando que existen movimientos fuera del período mostrado.
- El aviso no aparece cuando todos los movimientos importados caen dentro del filtro activo.

**Tamaño estimado.** S.

---

## M5 — Autodetectar la cuenta financiera desde el número de cuenta del archivo

**Objetivo.** Cruzar `BankStatement.AccountNumber` (ya extraído por el parser, una vez resuelto M2) contra `FinancialAccount.AccountNumber` para asignar `FinancialAccountId` automáticamente cuando hay coincidencia.

**Problema que resuelve.** Hallazgos prioritarios #2 y #4. El archivo ya trae el número de cuenta; el sistema ya tiene ambos valores disponibles, pero nada los cruza — "Sin cuenta detectada" es hoy un estado permanente incluso cuando la información para resolverlo ya existe.

**Beneficio para el usuario.** Elimina la asignación manual para el caso más común: una cuenta que ya está registrada en el sistema.

**Riesgos.** Requiere una decisión de producto antes de implementar: qué hacer si no hay match exacto, qué hacer si el formato de número no coincide literalmente (espacios, guiones), y si una coincidencia parcial debe asignarse automáticamente o dejarse pendiente. Asignar la cuenta incorrecta por un matching demasiado laxo es peor que dejarla sin asignar.

**Dependencias.** Depende de M2 — sin la corrección del desfasaje de fila, no hay número de cuenta real para cruzar.

**Criterios de aceptación.**
- Al importar un archivo cuya cuenta ya existe en `FinancialAccount` (con número coincidente), el movimiento resultante muestra la cuenta asignada automáticamente, sin intervención manual.
- Cuando no hay coincidencia, el movimiento queda con el estado actual ("Sin cuenta detectada"), sin asignar nada por error.

**Tamaño estimado.** M.

---

## M6 — Permitir asignar cuenta financiera manualmente desde Movimientos

**Objetivo.** Reintroducir, en `movements.html`, una forma manual de asignar `FinancialAccountId` a un movimiento cuando la detección automática (M5) no aplica o no encuentra coincidencia.

**Problema que resuelve.** Hallazgo prioritario #4, en la parte que M5 no puede cubrir: cuentas nuevas, sin match, o casos ambiguos.

**Beneficio para el usuario.** Da una salida real a "Sin cuenta detectada" en todos los casos, no solo en el más común.

**Riesgos.** Bajo desde lo técnico — los endpoints (`PUT /api/bank-statements/{id}/financial-account`, `PUT /api/transactions/{id}/financial-account`) ya existen y están completos, solo sin UI que los consuma. El riesgo real es de UX: evitar reintroducir la misma fricción que motivó retirar el `<select>` original de `movements.html`.

**Dependencias.** Ninguna dependencia técnica dura respecto de M5, pero conviene secuenciarla después: que la asignación manual sea la excepción (para los casos que la automática no resuelve) y no el camino principal.

**Criterios de aceptación.**
- Desde `movements.html`, un movimiento sin cuenta financiera puede recibir una cuenta asignada manualmente.
- El cambio persiste y se refleja inmediatamente en la fila del movimiento.

**Tamaño estimado.** S/M.

---

## M7 — Definir el futuro de la columna "Alerta"

**Objetivo.** Tomar una decisión explícita de producto sobre la columna "Alerta": construirle una acción real, o retirarla si no aporta como columna independiente.

**Problema que resuelve.** Hallazgo prioritario #5. La columna es puramente informativa, documentada como sin acción asociada desde que se retiró la pantalla que hubiera actuado sobre ella.

**Beneficio para el usuario.** Elimina una fuente de confusión ("¿qué hago con esto?") sin agregar valor a cambio.

**Riesgos.** Es una decisión de producto antes que técnica. Implementar cualquier cambio sin esa decisión tomada arriesga rehacer el trabajo si se elige la dirección contraria más adelante.

**Dependencias.** Ninguna dependencia con otras historias de esta épica; puede resolverse en cualquier momento, en paralelo.

**Criterios de aceptación.**
- Existe una decisión documentada (mantener con acción nueva, o retirar la columna).
- Si se opta por darle una acción, esta historia se cierra generando una historia de seguimiento con alcance propio (no se implementa la acción dentro de esta misma historia).

**Tamaño estimado.** S (como decisión) — si se decide construir una acción nueva, esa implementación es una historia aparte, no incluida en esta estimación.

---

## M8 — Limpiar la referencia al estado "Confirmado" inalcanzable

**Objetivo.** Eliminar o aclarar las referencias (UI, documentación, código) que presentan `ClassificationStatus.Confirmed` como un estado alcanzable, cuando en la práctica ningún flujo actual lo produce.

**Problema que resuelve.** Remanente de un mecanismo de matching retirado en una épica anterior. No es un bug funcional — es un riesgo de confusión para quien lea el código o la documentación esperando que algún movimiento llegue a ese estado.

**Beneficio para el usuario.** Ninguno directo — beneficio de consistencia y de reducir confusión futura para quien mantenga el sistema.

**Riesgos.** Ninguno funcional. El único cuidado es no eliminar el valor del enum si algo lo referencia estructuralmente (persistencia, serialización) — solo clarificar o retirar su exposición como estado alcanzable.

**Dependencias.** Ninguna.

**Criterios de aceptación.**
- No queda ninguna referencia a "Confirmado" presentada como un estado que un movimiento pueda alcanzar hoy, sin la aclaración correspondiente.

**Tamaño estimado.** S.

---

# Orden recomendado de implementación

1. **M2** — corrección acotada, bajo riesgo, con evidencia exacta del error; desbloquea M5.
2. **M1** — independiente, alto impacto, sin ambigüedad de solución.
3. **M3** — mismo tipo de cambio que M1 (ampliar lo que devuelve un handler); conviene encararla justo después.
4. **M5** — una vez resuelto M2, ya hay número de cuenta real para cruzar.
5. **M6** — cierra el caso que M5 no cubre automáticamente.
6. **M4** — el propio análisis sugiere esperarla hasta poder dimensionar cuánto pesa el problema una vez resueltos M1/M2/M5.
7. **M7** — decisión de producto, puede tomarse en cualquier momento del proceso, en paralelo con lo anterior.
8. **M8** — cosmético, sin urgencia; encaja en cualquier hueco del calendario.

## Qué puede desarrollarse en paralelo

- **M1 y M2** son completamente independientes entre sí (tocan handlers distintos) y pueden avanzar al mismo tiempo.
- **M7** (la decisión sobre "Alerta") no bloquea ni depende de ninguna otra historia — puede resolverse en paralelo con cualquiera de las anteriores.
- **M8** puede tomarse en cualquier momento sin coordinación con el resto.

## Qué debería esperar

- **M5** debe esperar a que **M2** esté resuelta — sin la corrección de fila no hay número de cuenta confiable para cruzar.
- **M6** conviene que espere a **M5** — para que la asignación manual sea el camino de excepción, no el principal, y no se termine construyendo una UI de asignación manual que la automática vuelve innecesaria en la mayoría de los casos.
- **M3** conviene que espere a **M1** — evita tocar el contrato de resultado de los handlers en dos rondas separadas.
- **M4** conviene esperar hasta tener M1, M2 y M5 resueltas, para dimensionar el problema real con esas causas ya corregidas.

## Qué podría descartarse si el producto evoluciona de otra manera

- **M4** — si M1, M2 y M5 reducen lo suficiente la frecuencia con la que el usuario importa historial fuera del mes actual, este aviso puede dejar de justificarse.
- **M6** — si la autodetección de M5 cubre en la práctica la enorme mayoría de los casos reales, la asignación manual puede no valer la fricción de construirla; quedaría como mecanismo de excepción que en los hechos casi nunca se usa.
- **M7** — la decisión misma puede resultar en descartar la columna "Alerta" directamente, en cuyo caso no hay historia de implementación de acción que seguir.
- **M8** — es puramente cosmético; puede posponerse indefinidamente sin costo funcional real.

---

Este documento no es un compromiso de PRs ni de fechas — es la traducción de `docs/Analysis/ImportWorkflowReview.md` a unidades de trabajo chicas para decidir, historia por historia, cuándo y en qué orden encararlas.
