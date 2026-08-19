FinancialMcp — Mapa de confianza de datos y roadmap de investigación
Documento de trabajo. No contiene código ni implementación — es el mapa para decidir, tarea por tarea, qué investigar y en qué orden. Cada hallazgo distingue explícitamente hecho verificado en el código (con archivo y línea) de hipótesis a confirmar. Fuentes cruzadas: lectura directa del código en claude/financialmcp-audit-roadmap-sgzqqi (commit dede331) + docs/PROJECT_STATUS.md, docs/RoadMaps/FinancialMcp-vNext.md, docs/Decisions/ADR-001 a ADR-008, docs/Architecture/CentroDeAuditoria.md, docs/Epics/Epica-PlanificacionMensual.md.

1. Resumen ejecutivo
El proyecto ya tiene una base de documentación inusualmente honesta sobre su propio estado (PROJECT_STATUS.md se autodeclara "no apto para producción" y lista sus propios riesgos). Este documento no repite ese inventario general — se enfoca en los cinco problemas que planteaste, verificando cada uno contra el código real.

Lo que quedó confirmado como hecho, no como sospecha:

El bug de duplicados de banco tiene causa raíz identificada y es estructural, no intermitente. BankStatement.ExternalId se calcula como SHA256(NombreDeArchivo | Hoja | fila | NúmeroDeFila). El nombre de archivo forma parte del hash. Dos archivos distintos —por definición, con nombres distintos— nunca pueden producir el mismo ExternalId para el mismo movimiento real, aunque el movimiento sea idéntico en fecha, importe y descripción. La ventana de solapamiento que describís (importar cada 5 días, con superposición de fechas) es exactamente el escenario que este diseño no cubre. El propio código lo documenta como riesgo conocido y sin resolver.
El detector de sospechosos actual (SuspicionDetector) usa únicamente monto ± tolerancia y fecha ± ventana — ninguna otra señal. No compara descripción, no compara ExternalId, no distingue "mismo movimiento real" de "dos movimientos distintos que casualmente se parecen". Es, tal cual lo nombrás vos, el caso que no querés usar como criterio de borrado — y en el código de hoy es el único criterio que existe.
No existe ningún endpoint ni comando para borrar BankStatement/Transaction hoy. Cualquier limpieza de duplicados históricos que se haya hecho hasta ahora fue manual, directo contra la base, sin pasar por ninguna validación del sistema.
La integridad referencial entre "movimiento clasificado" y "movimiento original" es por convención, no por FK. ClassifiedMovementItem, MovementAuditDecision, InvestigationReference e ImportBatchLine referencian filas de BankStatement/Transaction mediante SourceEntityType + SourceId sin clave foránea. Si se borra una fila duplicada de origen sin un barrido explícito de estas cuatro tablas, se generan referencias rotas silenciosas — no hay ningún mecanismo de la base de datos que lo impida ni lo avise.
El bug de planificación (agosto/septiembre) tiene causa raíz confirmada y coincide, además, con una decisión de diseño explícita y documentada. Un PlanningItem pertenece a un PlanningMonth fijo por clave foránea (PlanningMonthId), asignado una sola vez al crearlo. EditPlanningItemHandler solo modifica Title/ExpectedAmount/DueDate — nunca PlanningMonthId. El propio documento de diseño del módulo (docs/Epics/Epica-PlanificacionMensual.md, sección 7, regla 2) dice explícitamente: "DueDate es y sigue siendo un dato descriptivo... Ninguna futura iteración debe agregarle alertas, colores de urgencia, recordatorios." Es decir: el sistema está haciendo exactamente lo que el diseño dice que debe hacer. El problema no es un bug de fecha — es que tu modelo mental ("cambiar el vencimiento debería mover el gasto de mes") no coincide con el modelo implementado ("el mes es fijo, el vencimiento es solo un dato para ordenar la lista"). Esto es una decisión de producto pendiente, no una corrección de código obvia.
El MCP hoy son 32 tools de solo-invocación-individual, sin loop propio. El propio ADR-006 ya declara el principio correcto para lo que pedís en el punto 4: "El razonamiento sigue del lado del cliente MCP... el servidor no es un agente autónomo, no corre su propio loop de decisiones." Evolucionar hacia una experiencia conversacional es, en gran medida, una decisión de dónde vive el razonamiento (¿el cliente MCP que ya usás, o un loop propio server-side con Ollama?) antes que una decisión de qué tools nuevas construir.
Lo que sigue siendo hipótesis, marcado como tal en cada tarjeta: cuántos duplicados reales existen hoy en tu base, si el ExternalId de tarjeta (Transaction, basado en contenido) tiene su propio punto débil, y qué proporción de las "clasificaciones dudosas" que ya reporta el Centro de Auditoría son en realidad síntomas de estos duplicados no detectados.

Postura general: todo lo que sigue es investigación y diseño conceptual. Ninguna tarjeta de este documento se traduce en código todavía — cada una termina en una decisión a tomar, no en un PR.

2. Roadmap por fases
FASE 0 — Recuperar confianza
DATA-001 — Auditoría de integridad de la base actual
Prioridad: CRITICAL
Tipo: Investigación / Integridad de datos
Problema: No existe hoy ninguna vista consolidada de "¿en qué estado está mi base ahora mismo?" a nivel de integridad estructural (huérfanos, duplicados, referencias rotas). El Centro de Auditoría (AuditReportService) audita clasificación (sospechosos, mal clasificados, pendientes) pero no audita integridad referencial entre tablas.
Evidencia: docs/Architecture/CentroDeAuditoria.md es explícito: "No detecta nada por su cuenta" y solo reutiliza IReviewEngine/IClassificationSuggestionService — ninguno de los dos revisa huérfanos de SourceEntityType+SourceId. Ya existe un mecanismo extensible de verificaciones (IImportConsistencyCheck/IImportConsistencyVerifier, src/FinancialSystem.Application/Imports/IImportConsistencyCheck.cs), pero su alcance está confirmado como acotado a una corrida de importación puntual ("una corrida ya persistida" — no una auditoría de toda la base).
Hipótesis: es probable que ya existan hoy en la base: (a) BankStatement/Transaction duplicados por el bug de IMPORT-001, (b) ClassifiedMovementItem cuyo SourceId no resuelve a ninguna fila (si alguna limpieza manual ya se hizo), (c) movimientos con múltiples ClassifiedMovement apuntando al mismo SourceId (doble clasificación). Ninguno de los tres está cuantificado todavía.
Investigación necesaria: diseñar (sin implementar) un conjunto de consultas de verificación: duplicados por (Date, Amount, Concept/Description) dentro de BankStatement/Transaction; ClassifiedMovementItem.SourceId sin fila correspondiente en su SourceEntityType; MovementAuditDecision/InvestigationReference en la misma situación; movimientos con ExternalId distinto pero contenido idéntico entre BankStatement y su archivo origen (cruce con ImportBatchLine); totales de FinancialMetricsService recalculados a mano sobre una muestra y comparados contra lo que devuelve el servicio.
Solución propuesta (conceptual): un reporte de integridad, separado del Centro de Auditoría (que audita clasificación, no estructura), que se pueda correr bajo demanda y arme una lista de hallazgos con severidad. No es una tool nueva todavía — es el criterio con el que se diseña esa tool, una vez decidido.
Dependencias: ninguna — es el punto de partida.
Riesgo: si se salta esta fase, cualquier corrección posterior (idempotencia, borrado de duplicados) se diseña sin saber cuál es el tamaño real del problema.
Criterio de terminado: existe una lista concreta, con conteos reales, de cuántas filas de cada tipo están en cada categoría de inconsistencia. No hace falta que sea automatizada — alcanza con que sea reproducible.
FASE 1 — Importación e idempotencia
IMPORT-001 — Identidad inestable de BankStatement.ExternalId
Prioridad: CRITICAL
Tipo: Bug / Integridad de datos — verificado en el código, no es hipótesis
Problema: el ExternalId de un movimiento de cuenta bancaria no identifica el movimiento — identifica su posición dentro de un archivo concreto.
Evidencia:
src/FinancialSystem.Infrastructure/Imports/BankStatements/BbvaBankStatementParser.cs:215-220: BuildExternalId(sourceFile, sheetName, rowNumber) → SHA256("{NombreDeArchivo}|{Hoja}|row|{NúmeroDeFila}").
El propio doc-comment de la entidad (src/FinancialSystem.Domain/Entities/BankStatement.cs:12-17) lo declara así: "No existe número de operación único en el XLS del BBVA... Riesgo documentado: si el banco re-exporta con filas insertadas en el medio, los RowNumbers cambian y esas filas se re-insertan. Es el mejor compromiso posible dado el formato del archivo."
docs/RoadMaps/FinancialMcp-vNext.md §6 confirma que este riesgo sigue activo y sin resolver ("la fragilidad posicional del ExternalId de BankStatement"), listado en docs/PROJECT_STATUS.md §13 como prioridad #1 del plan de estabilización del proyecto — es decir, ya estaba identificado antes de este documento, pero seguía sin resolverse.
El nombre de archivo (Path.GetFileName(sourceFile)) es parte del hash. Dos exportaciones del banco en fechas distintas casi con certeza tienen nombres de archivo distintos (nombre con fecha/timestamp, o al menos distinto por convención de descarga) — esto por sí solo ya rompe la idempotencia entre corridas para cualquier movimiento que aparezca en ambos archivos, independientemente de si el RowNumber se mantiene estable o no. El riesgo documentado en el código ("filas insertadas en el medio") describe solo una de las dos causas; la otra (nombre de archivo distinto) es más grave porque no depende de ningún caso borde — ocurre siempre que se re-exporta con un nombre de archivo distinto.
Hipótesis: falta confirmar (a) si el banco genera nombres de archivo estables entre descargas (si el nombre fuera idéntico letra por letra, el problema se reduciría al riesgo ya documentado del RowNumber, más acotado) y (b) cuántos duplicados reales ya generó esto en tu base (ver DATA-001/IMPORT-003).
Investigación necesaria: revisar 2-3 pares reales de archivos bancarios con fechas solapadas (los que ya generaron duplicados, según tu ejemplo) y verificar: ¿el nombre de archivo cambia entre descargas? ¿el RowNumber del mismo movimiento (haberes, por ejemplo) es distinto entre ambos archivos? Esto separa "el nombre de archivo es la causa principal" de "el corrimiento de filas es la causa principal" — la solución conceptual difiere según cuál domine.
Solución propuesta (conceptual, no código): la identidad de un BankStatement debería depender del contenido del movimiento (fecha + importe + descripción, con la misma lógica que ya usa Transaction.ExternalId vía SheetParserHelpers.BuildTransactionExternalId), no de su posición de archivo. El propio proyecto ya resolvió este problema para tarjeta/catch-all (ver IMPORT-002) — es candidato natural a replicar el mismo criterio para banco, en vez de diseñar uno nuevo. Migrar el criterio de identidad de filas ya existentes es una decisión aparte (afecta al índice único vigente y a filas históricas) que no se resuelve en esta tarjeta.
Dependencias: ninguna para investigar. Cualquier cambio de esquema depende de haber cerrado DATA-001 (para saber cuántas filas históricas quedarían con una identidad recalculada distinta de la que tienen hoy) y de una decisión de producto sobre si conviene invalidar el ExternalId histórico o convivir con dos generaciones de identidad.
Riesgo: cambiar el criterio de ExternalId sin plan de migración puede generar el efecto inverso — que movimientos históricos, con el ExternalId viejo, se reinterpreten como "nuevos" en la próxima importación y se dupliquen otra vez, esta vez de forma masiva.
Criterio de terminado (de la investigación, no de la corrección): queda documentado, con evidencia de archivos reales, cuál de las dos causas (nombre de archivo vs. corrimiento de fila) explica los duplicados que ya viste, y una propuesta conceptual de identidad estable lista para decidir.
IMPORT-002 — Robustez real de Transaction.ExternalId (tarjeta/catch-all)
Prioridad: MEDIUM
Tipo: Investigación
Problema: a diferencia de BankStatement, Transaction.ExternalId ya está basado en contenido — pero conviene no asumir que está resuelto solo porque el criterio es mejor.
Evidencia: src/FinancialSystem.Application/Helpers/SheetParserHelpers.cs:50-64 — BuildTransactionExternalId(date, amount, description, couponNumber): si hay CouponNumber (número de operación del banco), el hash usa solo ese valor ("coupon|{couponNumber}"); si no, cae a SHA256("{date:O}|{amount}|{descripción normalizada}"). docs/RoadMaps/FinancialMcp-vNext.md §5 confirma que este patrón "no se rediseña" — se considera terminado.
Hipótesis: el fallback por fecha+monto+descripción puede colisionar (dos compras idénticas el mismo día, mismo comercio, mismo importe — ej. dos cafés de $1500 en el mismo bar el mismo día) tratándose como el mismo ExternalId y perdiendo la segunda transacción real como si fuera un duplicado. También sin confirmar: si CouponNumber es realmente estable entre corridas o puede faltar/cambiar en un re-export.
Investigación necesaria: revisar docs/patch/enriquecimiento-tarjeta-debito.md y los tests de tests/FinancialSystem.Infrastructure.Tests/Imports/ para ver si este caso de colisión (misma fecha+monto+descripción, dos operaciones reales distintas) está cubierto o es un punto ciego conocido. Revisar con cuántos archivos reales de tarjeta se validó este criterio.
Solución propuesta (conceptual): si se confirma el punto ciego, evaluar si conviene incorporar una señal adicional (orden dentro del día, hora si el extracto la trae) al fallback — sin tocar el camino con CouponNumber, que es el caso robusto.
Dependencias: ninguna.
Riesgo: bajo mientras se mantenga como investigación — el riesgo aparece solo si se decide tocar el cálculo sin medir antes cuántas transacciones reales dependen del fallback vs. de CouponNumber.
Criterio de terminado: confirmado (o descartado) si existe colisión real de ExternalId entre dos transacciones de tarjeta genuinamente distintas, con datos reales.
IMPORT-003 — Cuantificar los duplicados ya producidos por IMPORT-001
Prioridad: HIGH
Tipo: Investigación
Problema: sabemos la causa; no sabemos el tamaño del daño ya hecho.
Evidencia: ninguna todavía — es exactamente lo que hay que producir.
Hipótesis: el volumen depende de cuántas veces se repitió el patrón "importar cada ~5 días con solapamiento" desde que existe BbvaBankStatementImporter con este criterio de ExternalId.
Investigación necesaria: query sobre BankStatement agrupando por (Date, Amount, Concept) con HAVING COUNT(*) > 1, cruzando con SourceFile distinto para confirmar que son duplicados entre archivos (no dentro del mismo archivo, que ya está cubierto por el índice único). Cruzar esas filas contra ClassifiedMovementItem para saber cuántas ya fueron clasificadas dos veces (impacto real en métricas) vs. cuántas siguen sin clasificar (impacto solo en movements.html).
Solución propuesta (conceptual): ninguna todavía — este número es insumo directo de DEDUPE-004 y de la decisión de si el saneamiento histórico (Fase 2) es urgente o puede esperar.
Dependencias: DATA-001 (mismo tipo de query, conviene resolverlas juntas).
Riesgo: ninguno — es solo lectura.
Criterio de terminado: un número concreto de filas duplicadas y, de esas, cuántas ya afectan un ClassifiedMovement (gasto contado dos veces en algún dashboard).
FASE 2 — Auditoría y saneamiento de datos existentes
DEDUPE-001 — Taxonomía de confianza para duplicados
Prioridad: HIGH
Tipo: Investigación / Diseño
Problema: hoy no existe ninguna clasificación de "qué tan seguro estoy de que esto es un duplicado" — el único mecanismo (SuspicionDetector) da un sí/no binario basado en un solo criterio débil.
Evidencia: src/FinancialSystem.Infrastructure/Review/SuspicionDetector.cs:75-82 — IsPossibleDuplicate(a, b) compara únicamente |monto_a - monto_b| <= tolerancia y |fecha_a - fecha_b| <= ventana_días. No hay ninguna comparación de texto/descripción, ni de ExternalId, ni de SourceFile. El propio nombre de la clase y su doc-comment (línea 8) son honestos: dice "posibles duplicados", no "duplicados confirmados".
Hipótesis: ninguna — el criterio actual es exactamente el que vos ya identificaste como insuficiente. No hace falta hipotetizar, hace falta diseñar los niveles que faltan.
Investigación necesaria: definir, sin implementar, al menos tres niveles:
Confirmado — señal que garantiza que es el mismo movimiento real (candidato: mismo ExternalId bajo un criterio de identidad ya corregido por IMPORT-001, o combinación exacta de fecha+monto+descripción normalizada+incluso Balance consecutivo si aplica, entre dos filas de SourceFile distinto).
Altamente probable — coincide en fecha exacta + monto exacto + descripción textualmente idéntica (no solo parecida), pero sin poder confirmar el origen.
Parecido — lo que ya detecta SuspicionDetector hoy (monto ± tolerancia, fecha ± ventana): sirve para señalar "revisar", nunca para borrar.
Solución propuesta (conceptual): ningún nivel salvo el 1 (Confirmado) debería habilitar borrado sin revisión humana explícita. Los niveles 2 y 3 alimentan un reporte para que la persona decida — coherente con el principio que ya usa el Centro de Auditoría ("genera recomendaciones, no aplica cambios", docs/Architecture/CentroDeAuditoria.md §5).
Dependencias: IMPORT-001 (para saber qué campo de identidad usar en el nivel "Confirmado" — si ExternalId no se corrige antes, el nivel 1 queda sin una señal confiable propia y termina degradando al nivel 2).
Riesgo: definir la taxonomía sin haber cerrado IMPORT-001 lleva a construir el nivel "Confirmado" sobre una base igual de frágil que la que se quiere reemplazar.
Criterio de terminado: los tres niveles están documentados con su regla exacta y con ejemplos reales de tu base (usando los duplicados ya encontrados en IMPORT-003) clasificados manualmente en cada nivel, para validar que la regla los separa bien.
DEDUPE-002 — Señales disponibles para identidad de alta confianza
Prioridad: HIGH
Tipo: Investigación
Problema: antes de fijar la regla del nivel "Confirmado" (DEDUPE-001), hay que saber qué datos realmente aporta el banco más allá de fecha/monto/descripción.
Evidencia: BankStatement (src/FinancialSystem.Domain/Entities/BankStatement.cs) expone Balance (saldo posterior al movimiento) además de Date/Concept/Amount. El comentario de la clase confirma que no hay número de operación en el XLS de BBVA Caja de Ahorro. Transaction (tarjeta) sí tiene CouponNumber cuando el extracto lo trae (SheetParserHelpers.cs:58).
Hipótesis: el Balance (saldo posterior) podría ser una señal fuerte no explotada todavía: si dos movimientos "iguales" tienen saldos posteriores distintos, casi seguro no son el mismo movimiento (o uno de los dos es realmente un duplicado insertado en un punto distinto de la secuencia). No está verificado si el saldo es consistente entre dos exportaciones que se solapan, ni si esa comparación es viable en la práctica (depende del orden y de que no haya movimientos intermedios de otras fuentes).
Investigación necesaria: con los pares de archivos reales de IMPORT-001, comparar el Balance de los movimientos que se sabe que son el mismo (el de haberes, por ejemplo) entre ambas exportaciones. Confirmar si coincide siempre, y si serviría como segunda señal independiente de fecha+monto+descripción.
Solución propuesta (conceptual): si el saldo resulta consistente, incorporarlo como señal adicional (no reemplazo) para el nivel "Confirmado" de DEDUPE-001 — mismo monto + misma fecha + misma descripción + mismo saldo posterior es un criterio mucho más fuerte que cualquiera de los cuatro por separado.
Dependencias: ninguna, puede investigarse en paralelo con IMPORT-001.
Riesgo: ninguno, es solo lectura de datos ya importados.
Criterio de terminado: confirmado si el saldo es o no una señal confiable, con ejemplos reales.
DEDUPE-003 — Mecanismo seguro de borrado histórico (huérfanos y referencias blandas)
Prioridad: CRITICAL
Tipo: Arquitectura / Integridad de datos
Problema: borrar una fila de BankStatement/Transaction hoy no tiene ningún camino seguro — ni siquiera existe el endpoint para hacerlo, y si existiera, nada impediría dejar referencias rotas.
Evidencia:
No existe ningún endpoint de borrado sobre BankStatement/Transaction en src/FinancialMcp.Api/Endpoints/ (búsqueda de MapDelete sobre esas entidades: cero resultados).
ClassifiedMovementItem.SourceId (src/FinancialSystem.Domain/Review/ClassifiedMovementItem.cs:13-18), MovementAuditDecision, InvestigationReference e ImportBatchLine referencian el movimiento original por SourceEntityType + SourceId, deliberadamente sin FK — el propio doc-comment lo dice: "Evita cascadas indeseadas... La integridad referencial se mantiene por convención de negocio" (no por la base de datos).
BankStatementConfiguration.cs/TransactionConfiguration.cs no tienen ninguna FK entrante desde esas cuatro tablas — la base de datos no puede impedir ni advertir el borrado de una fila todavía referenciada.
Hipótesis: ninguna — este es el riesgo estructural que vos identificaste en el punto 2 de tu pedido ("clasificaciones huérfanas, relaciones inconsistentes, totales incorrectos, referencias rotas"), y está confirmado que hoy nada lo previene.
Investigación necesaria: mapear, para cada una de las cuatro tablas con referencia blanda, qué pasaría si se borra un BankStatement/Transaction referenciado: ¿el snapshot de ClassifiedMovementItem (que sí copia los datos originales — OriginalAmount/OriginalDate/OriginalDescription) sigue siendo útil sin la fila fuente, o pierde valor de auditoría? ¿Qué debe pasar con un MovementAuditDecision o una InvestigationReference que apunta a una fila borrada?
Solución propuesta (conceptual): antes de habilitar cualquier borrado físico, decidir una de dos estrategias (no implementar ninguna todavía): (a) un barrido explícito, dentro de la misma operación, que localice y resuelva toda referencia a un SourceId antes de permitir el borrado (bloquear el borrado si hay referencias sin resolver, en vez de dejarlas rotas); o (b) no borrar físicamente nunca un movimiento ya clasificado — solo permitir borrado físico de duplicados que todavía no tienen ningún ClassifiedMovementItem/MovementAuditDecision/InvestigationReference apuntándolos, y para el resto, un mecanismo de "fusión" (mover las referencias del duplicado hacia el original y recién ahí borrar). La decisión entre (a) y (b) es de producto, no solo técnica.
Dependencias: DEDUPE-001 (nivel "Confirmado" primero) y DATA-001 (saber cuántas filas ya tienen referencias antes de diseñar el barrido).
Riesgo: es el ítem de mayor riesgo de todo el documento — un borrado mal diseñado no solo pierde el movimiento duplicado, puede degradar retroactivamente clasificaciones, investigaciones y decisiones de auditoría ya tomadas sobre ese movimiento.
Criterio de terminado: existe un diseño conceptual aprobado (no código) de cómo se resuelve cada una de las cuatro referencias blandas antes de cualquier borrado, con los casos borde (fila referenciada por más de una tabla, fila referenciada por una investigación cerrada, etc.) explícitamente contemplados.
DEDUPE-004 — Inventario cuantitativo de duplicados/incoherencias históricas
Prioridad: HIGH
Tipo: Investigación / Integridad de datos
Problema: mismo objetivo que IMPORT-003 pero de alcance más amplio — no solo duplicados por el bug de ExternalId, sino cualquier incoherencia que DATA-001 encuentre.
Evidencia: depende de correr DATA-001.
Hipótesis: el volumen de "clasificaciones dudosas" que ya reporta hoy audit.html probablemente incluye, sin distinguirlos, tanto errores reales de clasificación como síntomas de movimientos duplicados (dos filas iguales, cada una clasificada por separado, cada una "dudosa" a su manera). No está verificado.
Investigación necesaria: cruzar la salida de DATA-001/IMPORT-003 (duplicados de origen) contra el reporte de AuditReportService.BuildFullAuditReportAsync para el mismo período, y ver cuánto se solapan.
Solución propuesta (conceptual): ninguna todavía — es el insumo final para decidir el orden real de saneamiento (qué se limpia primero: lo más numeroso, lo de mayor impacto en montos, o lo más fácil de resolver con certeza).
Dependencias: DATA-001, IMPORT-003.
Riesgo: ninguno.
Criterio de terminado: un inventario único, con conteos, que priorice qué limpiar primero.
FASE 3 — Modelo de clasificación y confiabilidad
MODEL-001 — Verificar el modelo de capas contra el código real
Prioridad: HIGH
Tipo: Investigación
Problema: el modelo conceptual que pedís documentar (original → interpretación → sugerencia → decisión confirmada → derivados) ya existe en gran parte en el diseño actual, pero conviene confirmarlo capa por capa en vez de asumirlo.
Evidencia:
Original/importado: Transaction/BankStatement. El doc-comment de ClassifiedMovement (línea 27-28) dice que estas tablas "permanecen intactas e inmutables" — pero esto es una declaración de intención en un comentario, no una restricción verificada: no hay ningún control técnico (permisos, trigger, campo readonly a nivel EF) que impida un UPDATE directo sobre ellas. IMPORT-001 ya muestra que ni siquiera la identidad (ExternalId) es tan estable como se asume.
Sugerencia: ClassificationSuggestion, producida por IClassificationSuggestionService — confirmado como efímera, nunca persistida, recalculada en cada request (docs/RoadMaps/FinancialMcp-vNext.md §3: "Sugerencias de matching efímeras — se recalculan en cada request, no se persisten").
Decisión confirmada: ClassifiedMovement. El doc-comment (línea 16-18) es taxativo: "Toda fila en esta tabla representa verdad financiera verificada por el usuario. No existen estados intermedios ni sugerencias aquí." ClassificationStatus tiene solo dos valores (Confirmed/Reviewed, según docs/PROJECT_STATUS.md §9) — ambos representan una decisión ya tomada, no una jerarquía de confianza.
Derivados: FinancialMetricsService (dashboards), AuditReportService (auditoría), PlanningMatchSuggestionService (planificación) — todos recalculan en cada request desde ClassifiedMovement, ninguno persiste su propio resultado (excepción: MovementAuditDecision, que no es un dato derivado sino la anotación de que una persona revisó un hallazgo — confirmado en docs/Architecture/CentroDeAuditoria.md §4).
Punto flojo detectado: ProcessingSource tiene un valor (ConfirmedFromSuggestion) sin productor actual — el motor que lo generaba fue retirado en PR-L4 (docs/RoadMaps/FinancialMcp-vNext.md §4). Es un resto de un modelo anterior, no evidencia de una capa rota hoy, pero conviene confirmar que ninguna consulta o tool asuma que ese valor todavía se genera.
Hipótesis: el modelo de 5 capas que describís ya está bastante bien separado en el código — la principal duda no es si existen las capas, sino si "original/importado" es realmente inmutable en la práctica (sin ningún control técnico que lo garantice) y si el paso de "sugerencia" a "decisión confirmada" (ClassifyMovementCommand) tiene algún punto donde una sugerencia se persista sin pasar por una acción explícita del usuario.
Investigación necesaria: leer ClassifyMovementHandler completo para confirmar que siempre requiere una acción explícita del llamador (nunca hay una ruta donde una sugerencia se auto-confirme); buscar cualquier UPDATE directo sobre Transaction/BankStatement fuera de los importadores (enriquecimiento de débito, BbvaDebitCardEnrichmentHandler, es un caso legítimo ya documentado — verificar si hay otros no documentados).
Solución propuesta (conceptual): producir un documento corto (tipo ADR) que fije las 5 capas explícitamente, cite dónde vive cada una en el código, y declare qué se considera una violación del modelo (ej. "ningún handler puede escribir ClassifiedMovement sin pasar por ClassifyMovementCommand"), para que futuras funcionalidades (incluido el agente conversacional de la Fase 7) tengan una regla clara a la que atenerse.
Dependencias: ninguna dura, pero conviene hacerlo después de DATA-001 para poder citar violaciones reales si aparecen, en vez de solo teóricas.
Riesgo: bajo — es documentación, no cambio de código.
Criterio de terminado: documento aprobado, con cada capa citada contra el código real y al menos un caso límite (enriquecimiento de débito, ProcessingSource.ConfirmedFromSuggestion) explícitamente resuelto.
MODEL-002 — Catálogo de inconsistencias posibles y cómo detectarlas
Prioridad: HIGH
Tipo: Integridad de datos
Problema: tu lista de inconsistencias posibles (duplicados, huérfanos, clasificaciones que apuntan a movimientos inexistentes, sin clasificar, contradictorias, totales que no coinciden, referencias blandas rotas, datos "inmutables" modificados) no tiene hoy, cada una, una consulta o regla que la detecte de forma sistemática.
Evidencia: ya cubierto parcialmente por DATA-001 (huérfanos, referencias rotas) y por el Centro de Auditoría existente (sin clasificar = PendingMovements, clasificaciones dudosas = comparación contra sugerencia/defaults). Lo que no está cubierto por nada existente, confirmado por lectura de AuditReportService: doble clasificación del mismo SourceId (dos ClassifiedMovementItem con el mismo SourceEntityType+SourceId) y modificación de un campo que debería ser inmutable (OriginalDate en ClassifiedMovementItem es init-only a nivel de compilador — confirmado protegido; pero Transaction/BankStatement no tienen ninguna protección equivalente, ver MODEL-001).
Hipótesis: las "clasificaciones contradictorias" (dos clasificaciones del mismo movimiento con dimensiones incompatibles) son, en teoría, imposibles si ClassifyMovementCommand es la única puerta de escritura — pero esto depende de que MODEL-001 confirme que no hay otra vía de escritura.
Investigación necesaria: para cada tipo de inconsistencia de tu lista, anotar explícitamente: ¿ya lo detecta algo? ¿qué consulta lo detectaría si no? Priorizar las que dependen de datos ya corrompidos (duplicados, huérfanos) sobre las que son estructuralmente imposibles hoy (salvo que MODEL-001 encuentre lo contrario).
Solución propuesta (conceptual): este catálogo es el contenido real de la futura auditoría de integridad (DATA-001) — no es una tarea aparte de implementación, es el diseño de qué debe verificar esa auditoría cuando se construya.
Dependencias: DATA-001, MODEL-001.
Riesgo: ninguno, es documentación/diseño.
Criterio de terminado: una tabla con cada tipo de inconsistencia de tu lista original, su estado actual (detectada / no detectada / estructuralmente imposible) y la consulta o regla que la detectaría.
FASE 4 — Motor de sugerencias
SUGGEST-001 — Entender el problema real antes de tocar el motor
Prioridad: MEDIUM (deliberadamente no CRITICAL — bloqueada a propósito)
Tipo: Investigación
Problema: todavía no se sabe si "las sugerencias fallan" es un problema de datos (duplicados/huérfanos ensuciando el historial que alimenta las sugerencias), un problema de modelo (falta de fuzzy matching, como intuís) o ambos a la vez.
Evidencia: docs/PROJECT_STATUS.md §2 declara el motor de sugerencias como el módulo con mejor cobertura de tests del repositorio ("Terminado... Alta — la mejor cobertura de tests del repositorio"). Esto es una señal fuerte de que el motor en sí no es la parte más frágil del sistema — contradice, con evidencia, la intuición de que hace falta más matching difuso como primer paso.
Hipótesis: si el historial de ClassifiedMovement que alimenta IClassificationSuggestionService contiene movimientos duplicados (Fase 1/2), las sugerencias por coincidencia histórica pueden estar aprendiendo de datos sucios sin que el motor en sí tenga ningún defecto — mejorar el algoritmo sin sanear el historial primero arriesga optimizar sobre datos que no deberían estar ahí.
Investigación necesaria: explícitamente diferida hasta cerrar Fase 2. Cuando corresponda: medir cuántas sugerencias fallidas/pobres coinciden con movimientos que DEDUPE-004 ya marcó como duplicados o inconsistentes, antes de evaluar cualquier cambio al algoritmo.
Solución propuesta (conceptual): ninguna — es intencional no proponer nada acá todavía, siguiendo tu propio criterio ("no quiero empezar agregando fuzzy matching").
Dependencias: DEDUPE-003/004 (saneamiento histórico) y MODEL-001 (confirmar que el historial que alimenta sugerencias es el correcto).
Riesgo: el riesgo real es el inverso — empezar por acá sin cerrar antes la Fase 2.
Criterio de terminado (de la investigación, cuando se retome): un diagnóstico de si el problema percibido de sugerencias es de datos, de algoritmo, o ambos, con evidencia de casos reales.
FASE 5 — Planificación (bug agosto/septiembre)
PLAN-001 — Causa raíz confirmada: DueDate no mueve el ítem de mes
Prioridad: HIGH
Tipo: Bug — verificado en el código, no es hipótesis
Problema: un PlanningItem sigue apareciendo en el mes en el que fue creado aunque se le cambie el DueDate a otro mes.
Evidencia:
PlanningItem.PlanningMonthId (src/FinancialSystem.Domain/Planning/PlanningItem.cs:13) fija a qué PlanningMonth pertenece el ítem — es el campo que representa el período real, no DueDate.
EditPlanningItemHandler.Handle (src/FinancialSystem.Application/Planning/Commands/EditPlanningItemHandler.cs:21-23) solo actualiza Title, ExpectedAmount y DueDate — nunca toca PlanningMonthId. No existe ningún comando en src/FinancialSystem.Application/Planning/Commands/ para mover un ítem entre meses.
PlanningQueryService.GetByPeriodAsync (src/FinancialSystem.Infrastructure/Planning/PlanningQueryService.cs:13-23) trae los ítems de un mes filtrando PlanningMonth.Period vía el Include(m => m.Items) de la relación por FK — nunca filtra ni agrupa por DueDate.
planning.html (línea 642) usa DueDate únicamente para ordenar la lista dentro del mes ya cargado — confirmado por el propio comentario del código: "Ordena por DueDate ascendente; los ítems sin DueDate quedan al final."
Coincide, dato por dato, con la decisión de diseño ya documentada en docs/Epics/Epica-PlanificacionMensual.md §7, regla 2: "DueDate es y sigue siendo un dato descriptivo. Como máximo ordena la lista visualmente. Ninguna futura iteración debe agregarle alertas, colores de urgencia, recordatorios o notificaciones."
Hipótesis: ninguna sobre la causa — está cerrada. Lo que sigue abierto es si esta regla de diseño es la que realmente querés (ver PLAN-002).
Investigación necesaria: ninguna adicional sobre el código — la causa ya está confirmada de punta a punta (entidad → handler → query → UI).
Solución propuesta (conceptual): no corresponde proponer una corrección todavía — depende enteramente de la decisión de producto de PLAN-002.
Dependencias: ninguna.
Riesgo: ninguno en investigarlo; el riesgo aparece si se "corrige" sin resolver primero PLAN-002 (ver abajo).
Criterio de terminado: ya cumplido para la investigación — la causa está documentada con archivo y línea. Este ítem se cierra formalmente cuando PLAN-002 defina qué comportamiento se espera.
PLAN-002 — Decisión de producto: ¿qué debería pasar cuando cambia DueDate?
Prioridad: HIGH
Tipo: UX / Producto
Problema: hay al menos tres comportamientos posibles y ninguno está decidido todavía. Corregir código antes de decidir esto es exactamente lo que el pedido original pide evitar ("no conviertas automáticamente cada hipótesis en tarea de implementación").
Evidencia: el propio documento de diseño de Planificación (Epica-PlanificacionMensual.md §7, regla 2) fue explícito y deliberado al declarar DueDate como dato puramente descriptivo, "para cualquier ampliación futura del módulo" — es decir, ya anticipó y rechazó a propósito la idea de que DueDate dispare comportamiento. Cualquier cambio acá reabre esa decisión de diseño, no la corrige por descuido.
Hipótesis: tu expectativa ("le cambié el vencimiento a septiembre, debería dejar de aparecer en agosto") es un modelo de "gasto fijo con vencimiento móvil" — más parecido a una obligación que se puede reprogramar que a una casilla de una checklist mensual fija. El diseño actual modela lo segundo (una checklist por mes, con vencimiento solo informativo). Ninguna de las dos es objetivamente "la correcta" — es una decisión de qué es Planificación para vos.
Investigación necesaria: no es investigación de código — es una decisión a tomar, con al menos tres opciones concretas a evaluar:
Mantener el diseño actual, pero corregir la UX: dejar DueDate descriptivo (sin mover el ítem), pero avisar explícitamente en el formulario de edición ("este ítem sigue perteneciendo a la planificación de agosto aunque cambies el vencimiento") para que el comportamiento no sorprenda.
Agregar un comando explícito "mover a otro mes": separado de editar DueDate, un botón/acción deliberada que sí cambie PlanningMonthId (con las mismas reglas que ya existen para copiar un mes — sección 6.2 de la épica).
Cambiar el criterio de agrupación: que el mes de un ítem se derive de DueDate en vez de un PlanningMonthId fijo — este es el cambio de mayor alcance, porque contradice directamente la filosofía documentada del módulo ("Planificación representa una intención", independiente del calendario real) y probablemente requiere repensar cómo funciona "copiar un mes" (sección 6.2, hoy pensada sobre un PlanningMonthId fijo).
Solución propuesta: no corresponde proponer una — es la pregunta que hay que resolver con vos antes de tocar nada de este módulo.
Dependencias: PLAN-001 (ya resuelta).
Riesgo: implementar cualquiera de las tres opciones sin decidir cuál es la intención real puede resolver el síntoma que reportaste y generar un problema distinto (ej. la opción 3 rompe la independencia deliberada entre Planificación y calendario que el resto del módulo asume).
Criterio de terminado: elegida una de las tres opciones (o una cuarta no listada acá), documentada la razón, recién ahí corresponde una tarjeta de implementación — fuera de este documento.
PLAN-003 — Otras pantallas afectadas por la misma lógica
Prioridad: MEDIUM
Tipo: Investigación — verificado en el código, no es hipótesis
Problema: confirmar si el mismo comportamiento se filtra a otras pantallas.
Evidencia: dashboard.html consume GET /api/planning-months/dashboard-summary?period=... (línea 1455), que resuelve en PlanningQueryService.GetDashboardSummaryAsync (src/FinancialSystem.Infrastructure/Planning/PlanningQueryService.cs:59-83) — el mismo patrón: filtra por PlanningMonth.Period, no por DueDate, y calcula pendingDueDates.Min() sobre los ítems ya fijados a ese mes. La tarjeta "Planificación" del Dashboard va a mostrar el mismo comportamiento que planning.html: un ítem con DueDate en septiembre pero todavía en el PlanningMonth de agosto sigue contando ahí.
Hipótesis: ninguna — está confirmado que es la misma causa raíz, no una independiente.
Investigación necesaria: ninguna adicional — ya localizado. Verificar solo que no haya un tercer consumidor de PlanningItem/DueDate fuera de planning.html y dashboard.html (búsqueda rápida por consumidores de /api/planning-months y /api/planning-items).
Solución propuesta: la misma que se decida en PLAN-002 — no hace falta una corrección separada para el Dashboard, comparte el mismo origen de datos.
Dependencias: PLAN-002.
Riesgo: ninguno.
Criterio de terminado: confirmado que no hay una tercera pantalla con el mismo problema fuera de las dos ya identificadas.
FASE 6 — Detección segura de duplicados + eliminación
CLEAN-001 — Flujo de revisión humana antes de cualquier borrado
Prioridad: CRITICAL
Tipo: Arquitectura / Seguridad
Problema: falta el flujo que decida, para cada duplicado detectado, si se borra, se fusiona o se ignora — y quién lo decide.
Evidencia: el Centro de Auditoría ya tiene un precedente de diseño para esto en un caso adyacente: MovementAuditDecision (docs/Architecture/CentroDeAuditoria.md §5) registra que una persona revisó un hallazgo de clasificación dudosa y decidió mantenerlo — nunca corrige nada automáticamente. No existe hoy el equivalente para duplicados (no hay tabla ni comando de "decisión sobre un duplicado").
Hipótesis: ninguna — es una funcionalidad a diseñar, no un bug.
Investigación necesaria: definir, apoyándose en el patrón ya validado de MovementAuditDecision, qué acciones humanas son necesarias: marcar un grupo como "confirmado duplicado, listo para limpiar", marcar como "parecido pero no es duplicado" (para que deje de aparecer en el reporte), o "duplicado confirmado pero no limpiar todavía" (por ejemplo, si ya tiene clasificaciones o investigaciones asociadas que primero hay que resolver, ver DEDUPE-003).
Solución propuesta (conceptual): una pantalla/flujo derivado del Centro de Auditoría (mismo criterio de "nunca modifica datos automáticamente"), donde: los duplicados nivel "Confirmado" (DEDUPE-001) se muestran agrupados, el usuario decide caso por caso o en lote, y solo esa decisión explícita dispara el mecanismo de borrado seguro (CLEAN-002) — nunca el sistema borra por su cuenta, ni siquiera para el nivel "Confirmado".
Dependencias: DEDUPE-001, DEDUPE-003.
Riesgo: si se automatiza el borrado sin este paso (aunque sea para el nivel "Confirmado"), se pierde la posibilidad de revertir una decisión equivocada de la taxonomía.
Criterio de terminado: diseño conceptual aprobado de la pantalla/flujo, con los tres estados de decisión (limpiar / no es duplicado / posponer) cubiertos.
CLEAN-002 — Mecanismo de borrado sin dejar inconsistencias
Prioridad: CRITICAL
Tipo: Integridad de datos
Problema: una vez decidido qué se borra (CLEAN-001), falta el mecanismo que lo ejecute sin las consecuencias que listaste (huérfanos, totales incorrectos, referencias rotas).
Evidencia: ver DEDUPE-003 — las cuatro referencias blandas (ClassifiedMovementItem, MovementAuditDecision, InvestigationReference, ImportBatchLine) y la ausencia total de FK son el motivo por el que este mecanismo no puede ser un simple DELETE.
Hipótesis: ninguna adicional a las ya cubiertas por DEDUPE-003.
Investigación necesaria: para cada referencia blanda, confirmar el comportamiento esperado al borrar su fila origen (ya adelantado en DEDUPE-003) y, específicamente, cómo se recalculan los totales derivados (FinancialMetricsService) — dado que son calculados en cada request desde ClassifiedMovement, no desde BankStatement/Transaction directamente, hay que confirmar si borrar el duplicado de origen sin tocar el ClassifiedMovement que ya lo clasificó deja ese total intacto (porque el total ya no depende de la fila de origen) o si genera una referencia rota en ClassifiedMovementItem.SourceId que después ninguna pantalla sabe explicar.
Solución propuesta (conceptual): el mecanismo de borrado debería operar siempre sobre el par completo (duplicado + todas sus referencias), nunca sobre la fila de origen sola — siguiendo el mismo espíritu de "operación única, sin dejar estados intermedios" que ya usa ReviewMovementsHandler (procesa un lote completo en una sola operación batch, según docs/Architecture/CentroDeAuditoria.md §3).
Dependencias: CLEAN-001, DEDUPE-003.
Riesgo: el de mayor impacto de todo el documento si se ejecuta mal — pérdida irreversible de trazabilidad financiera.
Criterio de terminado: diseño conceptual que, para cada referencia blanda, especifica exactamente qué pasa al borrar, validado contra al menos un caso real de duplicado ya identificado en DATA-001/IMPORT-003.
CLEAN-003 — Borrado físico vs. soft-delete/fusión — decisión pendiente
Prioridad: HIGH
Tipo: Arquitectura
Problema: tu pedido dice explícitamente "el borrado físico debe ser seguro y explícito" — pero no está decidido si "borrado físico" es realmente la estrategia correcta frente a alternativas como desactivación lógica o fusión.
Evidencia: el proyecto ya tiene precedente de no usar borrado físico en otros catálogos: Category/Counterparty/FinancialAccount usan "desactivación lógica en vez de borrado físico" (docs/PROJECT_STATUS.md §3). Es la única convención de borrado que existe hoy en el sistema, y no es borrado físico.
Hipótesis: ninguna — es una decisión, no un hecho a verificar.
Investigación necesaria: evaluar si el mismo criterio (desactivación lógica) aplica a movimientos duplicados, o si ahí sí conviene el borrado físico real que pedís, dado que a diferencia de una Category, un BankStatement duplicado no tiene ningún valor de referencia futura una vez confirmado como duplicado exacto.
Solución propuesta (conceptual): dos caminos a evaluar, no a decidir en este documento: (a) marcar el duplicado con un estado ("descartado por duplicado", sin borrar la fila, preservando toda referencia blanda intacta — más simple de implementar de forma segura, pero dejaría "basura" visible en consultas que no filtren ese estado); (b) borrado físico real solo después de que CLEAN-002 garantice que no quedan referencias — más fiel a tu pedido original, más riesgo si CLEAN-002 tiene un caso no contemplado.
Dependencias: CLEAN-002.
Riesgo: elegir (b) sin haber cerrado completamente CLEAN-002 es el escenario que querés evitar explícitamente.
Criterio de terminado: decisión tomada y documentada, con la razón, antes de diseñar cualquier endpoint o comando de borrado.
FASE 7 — FinancialMcp como agente conversacional
AGENT-001 — Inventario y evaluación de las tools MCP actuales como base del agente
Prioridad: MEDIUM
Tipo: Investigación / Arquitectura
Problema: antes de diseñar la experiencia conversacional, hay que saber con qué se cuenta hoy.
Evidencia: hosts/FinancialSystem.McpServer/Tools/ tiene 8 clases (AuditDatabaseTools, AuditTools, ConfigurationTools, FinancialTools, InvestigationTools, MovementTools, ProjectTools, RegistryTools, SystemTools) con 32 tools en total (grep -c "[McpServerTool]"). docs/UserGuide/McpUserGuide.md confirma, con las propias palabras del proyecto: "No escribe datos financieros" (única excepción: CreateInvestigation), "No es un agente autónomo. No decide qué tool llamar, no encadena llamadas, no corre ningún loop de razonamiento propio. El razonamiento vive siempre del lado del cliente MCP." ADR-006 fija el mismo principio como decisión de arquitectura, no como limitación temporal: "El razonamiento sigue del lado del cliente MCP... el servidor no es un agente autónomo."
Hipótesis: ninguna sobre el estado actual — está documentado con precisión inusual. La pregunta abierta es si ese principio (razonamiento 100% del lado del cliente) sigue siendo el correcto para lo que pedís, dado que un cliente MCP genérico (Claude Desktop, Claude Code) ya cumple, hoy, el rol de "conversar e interpretar intención" que describís en tu ejemplo — la pregunta real no es "cómo construyo un agente conversacional" sino "¿el agente conversacional que ya tengo (el cliente MCP) tiene las tools que necesita, con el catálogo bien diseñado?".
Investigación necesaria: probar en la práctica, con un cliente MCP real, las cuatro preguntas de ejemplo que diste ("¿cuánto gasté en supermercados este mes?", "¿tengo duplicados en agosto?", "¿por qué se clasificó así?", "compará julio contra agosto") y verificar cuáles ya se resuelven con el catálogo actual de 32 tools sin ninguna tool nueva, y cuáles no.
Solución propuesta (conceptual): ninguna todavía — este inventario es el insumo de AGENT-004.
Dependencias: ninguna, puede hacerse en paralelo con cualquier otra fase (es investigación pura, no depende de que los datos estén saneados).
Riesgo: ninguno.
Criterio de terminado: una tabla de las preguntas de ejemplo (y otras que agregues) contra qué tool(s) del catálogo actual las resolvería, marcando huecos reales.
AGENT-002 — Separación consulta / sugerencia / modificación en el catálogo de tools
Prioridad: HIGH
Tipo: Arquitectura / Seguridad
Problema: formalizar, para cualquier tool nueva que se agregue en el futuro, en cuál de las tres categorías cae — antes de que exista la primera tool ambigua.
Evidencia: el catálogo actual ya respeta esta separación de hecho, verificado por McpUserGuide.md: prácticamente todo es solo lectura, con CreateInvestigation como la única excepción de escritura, y esa escritura no toca datos financieros (crea un registro de investigación, no un ClassifiedMovement/Transaction). Es una separación real, no solo declarada.
Hipótesis: ninguna — falta formalizarla como regla explícita antes de que la Fase 7 avance, no corregir nada existente.
Investigación necesaria: ninguna de código — es una decisión de gobierno del catálogo. Documentar la regla: qué necesita una tool para calificar como "consulta/análisis" (siempre disponible), qué para "sugerencia" (siempre disponible, nunca escribe, siempre aclara que es una sugerencia y no un hecho — mismo criterio que ya usa IClassificationSuggestionService), y qué para "modificación" (requiere confirmación humana explícita fuera del loop del LLM, replicando el criterio ya usado hoy: toda escritura real pasa por FinancialMcp.Api, nunca directo desde una tool del MCP).
Solución propuesta (conceptual): esta regla es, en los hechos, la extensión natural del principio que ya fija ADR-006 ("las tools son pequeñas, de responsabilidad única, y mayormente de solo lectura... el MCP no modifica datos financieros directamente") — no hace falta inventar un modelo nuevo, hace falta declararlo explícitamente como criterio de aceptación para toda tool futura, incluidas las que pueda necesitar el agente conversacional.
Dependencias: ninguna dura, pero tiene más sentido después de AGENT-001 (para tener ejemplos concretos de a qué categoría cae cada tool existente).
Riesgo: si no se fija esto antes de construir tools nuevas para el agente, es fácil que una tool "de análisis" termine escribiendo algo por conveniencia (ej. "guardar esta comparación para la próxima vez") sin pasar por el criterio de confirmación explícita.
Criterio de terminado: documento corto (extensión de ADR-006) con la regla y las 32 tools actuales clasificadas contra ella, sin excepciones sin justificar.
AGENT-003 — Memoria/contexto de conversación vs. memoria de investigaciones
Prioridad: MEDIUM
Tipo: Investigación
Problema: ya existe una "memoria" en el sistema (Investigaciones, ADR-007) — hay que entender si sirve como base para la memoria conversacional que pedís, o si son dos conceptos distintos que no deberían mezclarse.
Evidencia: docs/PROJECT_STATUS.md §2 confirma que Investigaciones (Domain/Memory, InvestigationTools) está "Terminado (Fases 2-4 de su ADR)" pero es "Experimental — cero tests". ADR-006 Fase 4 (memoria general del MCP) está explícitamente fuera de alcance hasta que exista una ADR propia: "Se diseña mediante una ADR independiente cuando exista una necesidad real — este documento no fija su modelo de datos ni su mecanismo de escritura."
Hipótesis: una Investigation (ADR-007) es memoria de hallazgos financieros específicos ("por qué este movimiento es raro"), con su propio ciclo de vida (abierta/resuelta) — probablemente no es el mismo concepto que "recordar el hilo de esta conversación" (qué preguntaste hace 5 mensajes). Mezclarlos podría forzar el modelo de Investigación a cargar con un propósito que no tiene.
Investigación necesaria: releer docs/Architecture/Decisions/ADR-007-McpMemory.md completo (no solo lo citado en PROJECT_STATUS.md) para confirmar el alcance real de la Fase 5 pendiente, y decidir si la memoria conversacional es una extensión de ese mismo modelo o un concepto nuevo y separado.
Solución propuesta (conceptual): ninguna todavía — corresponde recién después de que AGENT-004 decida dónde vive el loop del agente (la respuesta cambia según si el "estado de la conversación" lo mantiene el cliente MCP, que ya tiene su propio historial, o un loop server-side nuevo, que necesitaría persistir el suyo).
Dependencias: AGENT-004.
Riesgo: diseñar memoria conversacional antes de esa decisión arriesga construir dos sistemas de memoria redundantes (uno ya existe con Investigaciones).
Criterio de terminado: decidido si la memoria conversacional reutiliza el modelo de Investigaciones, lo extiende, o es deliberadamente independiente — con la razón documentada.
AGENT-004 — Dónde vive el loop de razonamiento
Prioridad: HIGH
Tipo: Arquitectura
Problema: es la decisión estructural de la que dependen AGENT-002/003 — y la más importante de la Fase 7, porque cambia el resto del diseño.
Evidencia: hoy el "loop conversacional" ya existe y ya funciona: es el cliente MCP (Claude Desktop, Claude Code, u otro) que interpreta tu pregunta, decide qué tools llamar, encadena resultados y te responde en lenguaje natural — exactamente el flujo que describís en tu punto 4. ADR-006 lo declara como decisión de arquitectura deliberada, no como límite temporal a superar.
Hipótesis: tu pedido menciona explícitamente Ollama como parte de la evolución — esto sugiere que la intención no es "reemplazar" al cliente MCP que ya cumple ese rol, sino algo distinto: ¿un modo de uso sin depender de un cliente MCP externo (por ejemplo, para uso desde una interfaz propia, no desde Claude Desktop)? Eso sí requeriría un loop propio server-side orquestando Ollama contra las mismas 32 tools (o un subconjunto). No está confirmado cuál es el escenario de uso real que motiva esto.
Investigación necesaria: aclarar contigo el escenario de uso concreto: ¿conversar desde Claude Desktop/Claude Code ya es suficiente (en cuyo caso el trabajo real es AGENT-001/002, no un loop nuevo)? ¿o hace falta una interfaz propia (web, ej. dentro de wwwroot) que converse usando Ollama como motor, sin pasar por un cliente MCP externo? Son dos proyectos distintos con esfuerzo muy distinto.
Solución propuesta (conceptual): si la respuesta es "ya alcanza con un cliente MCP externo", el trabajo de la Fase 7 se reduce a completar el catálogo de tools (AGENT-001) y formalizar la separación de permisos (AGENT-002) — no hace falta ningún loop nuevo. Si la respuesta es "hace falta una interfaz propia", ahí sí corresponde diseñar un loop server-side sobre Ollama, reusando las mismas tools/servicios ya existentes (nunca reimplementando lógica financiera dentro del prompt, coherente con tu propio principio: "Ollama no debe convertirse en la fuente de verdad financiera") — pero ese diseño es un documento aparte, posterior a esta decisión.
Dependencias: ninguna técnica — es una decisión de producto que probablemente conviene resolver con AskUserQuestion en la próxima sesión de trabajo sobre este tema específico.
Riesgo: diseñar un loop server-side complejo sin confirmar antes que el cliente MCP existente no alcanza sería el mayor desperdicio de esfuerzo posible de todo este roadmap.
Criterio de terminado: decidido el escenario de uso real, con la razón, antes de escribir cualquier diseño de loop conversacional.
3. Lista de tareas numeradas
#	ID	Nombre	Fase	Prioridad
1	DATA-001	Auditoría de integridad de la base actual	0	CRITICAL
2	IMPORT-001	Identidad inestable de BankStatement.ExternalId	1	CRITICAL
3	IMPORT-002	Robustez real de Transaction.ExternalId	1	MEDIUM
4	IMPORT-003	Cuantificar duplicados ya producidos	1	HIGH
5	DEDUPE-001	Taxonomía de confianza para duplicados	2	HIGH
6	DEDUPE-002	Señales disponibles para identidad de alta confianza	2	HIGH
7	DEDUPE-003	Mecanismo seguro de borrado histórico	2	CRITICAL
8	DEDUPE-004	Inventario cuantitativo de duplicados/incoherencias	2	HIGH
9	MODEL-001	Verificar el modelo de capas contra el código real	3	HIGH
10	MODEL-002	Catálogo de inconsistencias posibles	3	HIGH
11	SUGGEST-001	Entender el problema real de sugerencias (diferida)	4	MEDIUM
12	PLAN-001	Causa raíz confirmada del bug agosto/septiembre	5	HIGH
13	PLAN-002	Decisión de producto sobre DueDate	5	HIGH
14	PLAN-003	Otras pantallas afectadas (Dashboard)	5	MEDIUM
15	CLEAN-001	Flujo de revisión humana antes de borrar	6	CRITICAL
16	CLEAN-002	Mecanismo de borrado sin inconsistencias	6	CRITICAL
17	CLEAN-003	Borrado físico vs. soft-delete/fusión	6	HIGH
18	AGENT-001	Inventario de tools MCP actuales	7	MEDIUM
19	AGENT-002	Separación consulta/sugerencia/modificación	7	HIGH
20	AGENT-003	Memoria conversacional vs. memoria de investigaciones	7	MEDIUM
21	AGENT-004	Dónde vive el loop de razonamiento	7	HIGH
Cambio propuesto sobre tu orden original de fases: lo mantendría casi idéntico — es un orden razonable y ya prioriza integridad antes que funcionalidades nuevas, tal como pediste. Dos ajustes concretos:

PLAN (Fase 5) no tiene ninguna dependencia real de datos financieros — Planificación está deliberadamente aislada de Movimientos/Clasificación (Epica-PlanificacionMensual.md §5: "sin relación con ninguna entidad de clasificación existente"). Podés resolver PLAN-001/002/003 en paralelo con cualquier otra fase, incluso antes de la Fase 0, sin ningún riesgo cruzado — no hace falta esperar al orden secuencial si te interesa cerrarlo rápido.
AGENT-001 (inventario de tools actuales) tampoco depende de nada — es investigación pura sobre lo que ya existe. Podés adelantarla en paralelo con la Fase 0 si querés tener ya un panorama de qué tan lejos está el catálogo actual de lo que imaginás, aunque la Fase 7 completa sí debe esperar (por AGENT-002, que si depende de que el resto esté resuelto para no formalizar reglas sobre un catálogo que además tiene datos poco confiables debajo).
El resto del orden (0 → 1 → 2 → 3 → 4 → 6 → 7, con 5 desacoplada) se sostiene: cada fase necesita lo que la anterior confirma.

4. Mapa de dependencias
opcional

gobierna

DATA-001
Auditoría de integridad

IMPORT-001
ExternalId inestable (banco)

IMPORT-002
Robustez ExternalId (tarjeta)

IMPORT-003
Cuantificar duplicados

DEDUPE-001
Taxonomía de confianza

DEDUPE-002
Señales de alta confianza

DEDUPE-003
Mecanismo seguro de borrado

DEDUPE-004
Inventario histórico

MODEL-001
Modelo de 5 capas

MODEL-002
Catálogo de inconsistencias

SUGGEST-001
Motor de sugerencias (diferido)

PLAN-001
Causa raíz confirmada

PLAN-002
Decisión de producto DueDate

PLAN-003
Otras pantallas

CLEAN-001
Revisión humana previa

CLEAN-002
Borrado sin inconsistencias

CLEAN-003
Físico vs. soft-delete

AGENT-001
Inventario de tools

AGENT-002
Separación de permisos

AGENT-003
Memoria conversacional

AGENT-004
Dónde vive el loop

Lectura de las dependencias clave que pediste explícitamente:

El detector de duplicados confiable (DEDUPE-001) depende de la identidad confiable del movimiento (IMPORT-001). Sin corregir (o al menos entender del todo) por qué ExternalId es inestable, cualquier regla de "duplicado confirmado" que se diseñe hereda la misma fragilidad.
Mejorar las sugerencias (SUGGEST-001) depende del modelo de clasificación confiable (MODEL-001) y del saneamiento histórico (DEDUPE-003). Confirmado además por evidencia adicional: el motor ya es el módulo mejor testeado del repositorio — el riesgo real no es el algoritmo, es lo que lo alimenta.
Borrar cualquier duplicado histórico (Fase 6) depende de la taxonomía (DEDUPE-001) y del mecanismo de referencias blandas (DEDUPE-003) — nunca al revés.
El agente conversacional (Fase 7) no depende técnicamente de nada de lo anterior para investigarse (AGENT-001 es paralelo a todo), pero sí depende de ello por una razón de producto que vos mismo fijaste: no tiene sentido darle a un agente conversacional (ni a Ollama) herramientas de consulta sobre datos que todavía no son confiables — el valor de "preguntale al sistema" cae si la respuesta puede estar inflada por duplicados sin detectar.
5. Qué investigar primero
En este orden concreto, arrancando ya:

DATA-001 + IMPORT-003 (se resuelven con las mismas consultas): cuantificar el problema real antes de diseñar nada. Es lectura pura, cero riesgo, y sin esto todo lo demás se diseña a ciegas.
IMPORT-001, confirmación final: ya está verificado en el código que la causa es real; falta solo el paso empírico (revisar 2-3 archivos reales tuyos) para saber si domina el nombre de archivo o el corrimiento de fila — eso cambia el diseño de la solución conceptual.
PLAN-001/PLAN-002, en paralelo con lo anterior (no compite por atención con nada): la causa ya está encontrada, solo falta la decisión de producto, que depende de vos, no de más código.
AGENT-001, también en paralelo: correr las cuatro preguntas de ejemplo contra un cliente MCP real, para saber cuánta distancia hay realmente entre "lo que tengo" y "lo que quiero" en la Fase 7 — puede cambiar cuánta urgencia le das a esa fase.
6. Qué NO tocar todavía
No modificar el cálculo de ExternalId (ni de BankStatement ni de Transaction) hasta cerrar IMPORT-001 y decidir cómo migran las filas históricas — cambiarlo sin plan de migración puede duplicar en masa lo que hoy está bien.
No borrar ningún movimiento histórico, ni siquiera "a mano" en la base, hasta que exista la taxonomía (DEDUPE-001) y el mecanismo de referencias blandas (DEDUPE-003/CLEAN-002) — cualquier borrado manual hoy tiene el mismo riesgo de huérfanos que se busca evitar diseñando.
No tocar el motor de sugerencias (ni agregar fuzzy matching, ni cambiar heurísticas existentes) hasta cerrar la Fase 2 — es tu propio criterio, y la evidencia (mejor cobertura de tests del repo) lo refuerza: no es ahí donde está el problema más urgente.
No construir ningún loop de agente con capacidad de escritura, ni sobre Ollama ni sobre ningún otro modelo, hasta que AGENT-002 (separación de permisos) esté formalizado y la Fase 2 haya cerrado — el propio ADR-006 ya fija este límite como principio, no hace falta reabrirlo, solo respetarlo también para lo nuevo.
No implementar ninguna de las tres opciones de PLAN-002 hasta decidir cuál — cualquiera de las tres es una corrección legítima de código, pero implementar la equivocada reabre el mismo problema con otra forma.
No tocar I7 (ruteo Visa/Mastercard) como parte de este roadmap — es un riesgo real y ya documentado (docs/RoadMaps/FinancialMcp-vNext.md §6, ítem 3) pero es un problema de selección de parser, no de idempotencia ni de duplicados — mezclarlo acá diluye el foco. Vale la pena resolverlo en la misma ventana de trabajo que la Fase 1 por estar en la misma área de código, pero como tarjeta separada.
7. Criterios de éxito generales
Reproducibilidad: cualquier persona (vos, o una sesión de IA nueva) puede correr la auditoría de integridad (DATA-001) y obtener el mismo resultado sobre el mismo estado de la base — no depende de memoria ni de contexto tácito.
Importación segura y verificada con datos reales, no solo en teoría: reimportar un extracto bancario que se solapa con uno ya importado (100 filas, 60 nuevas + 40 ya existentes, tu propio ejemplo) inserta exactamente las 60 nuevas — verificado con al menos un par real de archivos tuyos, no solo con un test sintético.
Cero borrados sin trazabilidad: para cada borrado que se ejecute (cuando exista el mecanismo), queda registro de qué se borró, por qué se consideró "Confirmado", y quién lo decidió — coherente con el patrón ya usado por MovementAuditDecision.
Ningún borrado deja referencias rotas: verificado corriendo la auditoría de integridad (DATA-001) inmediatamente después de cualquier limpieza — el criterio de éxito no es "creo que quedó bien", es "la auditoría no encuentra huérfanos nuevos".
El modelo de 5 capas queda documentado y es la referencia para todo lo que se construya después (incluida la Fase 7) — cualquier tool o funcionalidad nueva puede explicarse en términos de esas capas, igual que ADR-001 ya exige eso para las 4 dimensiones de clasificación.
El bug de planificación se cierra con una decisión de producto explícita, no con un parche silencioso — la corrección que se implemente eventualmente (fuera de este documento) debe poder señalarse a PLAN-002 como su justificación.
El agente conversacional, cuando se construya, nunca es la fuente de verdad de una cifra — toda respuesta financiera que dé se puede rastrear a una tool de solo lectura sobre datos ya saneados, nunca a una inferencia del modelo de lenguaje. Este documento no cierra esta fase — la deja lista para diseñarse recién cuando la Fase 2 esté cerrada.
Fuente: lectura directa del código en la rama claude/financialmcp-audit-roadmap-sgzqqi (commit dede331), cruzada contra docs/PROJECT_STATUS.md, docs/RoadMaps/FinancialMcp-vNext.md, docs/Decisions/ADR-001 a ADR-008, docs/Architecture/CentroDeAuditoria.md, docs/Epics/Epica-PlanificacionMensual.md y docs/UserGuide/McpUserGuide.md. Ningún archivo de código fue modificado para producir este documento.