# Auditoría de alcance — ¿estamos construyendo el MVP o desviándonos de él?

> Rol asumido para este documento: Product Architect + Software Architect responsable de llevar FinancialMcp a producción. No es una revisión de PR ni un análisis de una historia puntual — es una auditoría completa de todo lo documentado y todo lo implementado, contra el objetivo original del producto.

## Cómo se hizo esta auditoría

Releí completo (no solo títulos): los 5 ADR (`docs/Decisions/`), las 2 épicas documentadas formalmente (`EpicaI-Importacion.md`, `EpicaO-ImportacionManual.md`), el roadmap maestro (`FinancialMcp-vNext.md`), `docs/UX/ClassificationUX.md`, `docs/patch/enriquecimiento-tarjeta-debito.md`, los 2 documentos de `docs/Archive/`, y los 19 documentos de análisis acumulados en `docs/Architecture/` (series PRS1-S12 del motor de sugerencias, PRU1-UI1 de la nueva UX de clasificación, y los 8 documentos de auditoría/análisis de dominio y flujo funcional). También verifiqué contra el código actual varios puntos donde la documentación podía estar desactualizada (idempotencia real de tarjeta PDF, estado del `<select>` de cuenta financiera en `movements.html`, existencia de `accounts.html`).

---

# Paso 1 — El MVP real reconstruido

Tomando tu definición literal, no el roadmap actual, la lista mínima indispensable es:

1. **Importar de forma confiable** los movimientos de las fuentes que hoy existen en la práctica: extracto bancario (Caja de Ahorro BBVA) y resumen de tarjeta de crédito (Visa/Mastercard PDF). "Confiable" significa: sin duplicados al reimportar, sin pérdida silenciosa de filas, con visibilidad de qué falló y por qué.
2. **Ver todos los movimientos en un solo lugar**, pendientes y ya clasificados.
3. **Clasificar cada movimiento** con las dimensiones que ya importan para responder "en qué gasté" (categoría), "cómo afecta mi patrimonio" (impacto financiero: gasto/ingreso/pago de deuda/movimiento interno) y opcionalmente "con quién se relaciona" (contraparte).
4. **Saber con qué medio pagué** — banco vs. tarjeta. Esto ya existe hoy (`MovementSource`), no requiere nada nuevo.
5. **Detectar gastos sospechosos** (posibles duplicados/splits) — ya existe (`ISuspicionDetector`).
6. **Detectar movimientos sin clasificar** — parcialmente existe (el dato es calculable), pero no se muestra en ningún lado (badge sin cablear).
7. **Corregir manualmente** cualquier clasificación — ya existe (`movements.html`, modal de clasificación).
8. **Preguntarle a un LLM (MCP)** cosas como "¿en qué gasté demasiado?", "¿cuánto puedo ahorrar?", "¿cómo evolucionaron mis gastos?" — parcialmente existe (4 herramientas MCP ya construidas sobre `ClassifiedMovement`). "¿Qué presupuesto debería tener?" y "¿qué estrategia de ahorro/inversión?" son preguntas de razonamiento que un LLM ya podría intentar responder combinando las 4 herramientas existentes sin necesitar una entidad nueva de "Presupuesto" — no asumas que esa parte del MVP está bloqueada por falta de backend; probablemente ya sea alcanzable con las piezas que existen.

**Lo que el MVP explícitamente NO necesita, por tu propia definición**: múltiples bancos, billeteras virtuales, brokers, criptomonedas, inversiones, transferencias entre cuentas propias modeladas con precisión, ni ningún concepto de "Institución" reutilizable — todo eso es evolución posterior, tal como vos mismo lo planteaste.

---

# Paso 3 — Tabla completa: cada iniciativa documentada vs. el MVP

Agrupé por épica/iniciativa (no PR por PR — hay más de 50 PRs documentados en total, una tabla a ese nivel sería inmanejable). Cada fila resume el veredicto de sus PRs internos.

| Iniciativa | Qué es | Veredicto vs. MVP | Estado real |
|---|---|---|---|
| **Épica I — Confiabilidad de importación** (I1-I7) | Idempotencia y trazabilidad de errores para tarjeta PDF, al nivel de banco. | **Necesaria para el MVP** — es literalmente "importar sin duplicados ni pérdida silenciosa". | 🟡 Parcial: I1/I2 implementados (`ImportBatch` existe, diagnósticos ya no se descartan para banco). **I3 (idempotencia real de tarjeta) sigue sin estar completa** — verifiqué el código: `ImportFileProcessingSink` solo dedupea en memoria dentro del mismo archivo, sin consultar `ExternalId`s existentes contra la base antes de insertar. Reimportar el mismo PDF hoy puede chocar contra el índice único sin manejo — un `SaveChangesAsync` sin try/catch alrededor. I7 (investigar fingerprint Visa/Mastercard) sigue abierta. |
| **Épica J — Modelo de Cuentas Financieras** (roadmap) | Introducir `FinancialAccount` como entidad explícita. | **Útil pero puede esperar**, con un matiz importante: la versión mínima (que un movimiento sepa de qué cuenta salió, cuando solo hay una cuenta real) no bloquea nada del MVP — "con qué medio pagué" ya se responde con `MovementSource`. | 🟡 Parcial y **desalineada de su propio documento**: la entidad existe, y M5 (nuestra Épica M, no la J del roadmap) ya construyó la autoasignación — el roadmap general todavía la marca "📋 Planificada". Ver Paso 5. |
| **Épica K — Nueva UX de clasificación** (K1-K6) | Reemplazar `group-reconciliation.html` por `movements.html`. | **Necesaria para el MVP** — es la pantalla de clasificación manual. | ✅ Completa (confirmado por el propio roadmap y `ClassificationUX.md`). |
| **Épica L — Visibilidad de cobertura** | Badge de "pendientes de clasificar" en dashboard/nav. | **Necesaria para el MVP** — es exactamente tu punto "detectar movimientos sin clasificar". | ❌ No implementada. El dato es calculable hoy sin trabajo nuevo (filtrar `IMovementsQueryService.GetAsync` por `Status == null`) — es la brecha más barata de cerrar que sigue abierta. |
| **Épica M (roadmap) — Cuentas de inversión** | Habilitar `FinancialAccount.Type=Investment`. | **Pertenece al producto final**, explícitamente fuera del MVP por tu propia definición. | 📋 No iniciada. Correcta como está: no tocar todavía. |
| **Épica N — Simplificación del formulario de clasificación** | Derivar `FinancialImpact` por defecto. | Útil, mejora fricción de uso diario — no bloquea el MVP pero lo hace más usable. | 📋 No iniciada. |
| **Épica O — Importación Manual e Historial** (O1-O9) | Botón "Importar" desde UI + CRUD de catálogos (Category/Counterparty) + cierre del circuito de datos maestros. | **Necesaria para el MVP** — sin poder crear/editar Counterparty y sin poder importar manualmente, "clasificar correctamente" y "corregir manualmente" quedan cojos. | 🟡 Parcial: O1 (fix de ruteo `.xls` real), O2 (botón importar), O7 (Type opcional en Counterparty), O8 (pantalla counterparties.html) confirmados hechos. Resto sin confirmar desde los documentos solos. |
| **Épica S — Motor de sugerencias de clasificación** (S1-S14) | Sugerir automáticamente categoría/contraparte al clasificar, basado en historial. | **Útil pero puede esperar** en su forma completa — el MVP solo necesita poder clasificar manualmente, no que el sistema adivine. Es aceleración de uso diario, no una capacidad faltante. | ✅ Mayormente implementado (S1-S9, S11 confirmados). ⚠️ El propio PRS12 deja una corrección de bug (confianza `High` incorrecta en sugerencias por `Counterparty.Default*` desactivado) sin confirmar si se implementó. |
| **Épica U — UX de clasificación rápida** (U1-U7) | Reducir clics para aceptar una sugerencia, mostrar confianza visualmente, restaurar lista en modal de lote. | Útil pero puede esperar — mejora de fricción sobre un flujo que ya funciona manualmente. | 🟡 Parcial: U3, U4 confirmados. U2 (aceptar en un clic, la de mayor impacto según el propio análisis) sin confirmar. |
| **Épica UI — Arquitectura de UI compartida** (UI1-UI5) | Extraer CSS/JS compartido entre las 5 páginas, eliminar duplicación real (incluyendo un bug real: `dashboard.html` sin `esc()`, XSS potencial). | El bug de XSS es **necesario arreglar antes de producción** (no es opcional, es una vulnerabilidad real); el resto de la extracción de CSS/JS compartido es deuda técnica que puede esperar. | ❌ No implementada, ni siquiera iniciada según el propio documento. |
| **Nuestra Épica M — Mejoras al flujo de importación** (M1-M9) | Diagnósticos de importación + autoasignación de cuenta financiera para Caja de Ahorro. | Mixta: M2 (bug real de parseo) era **necesaria**; M5 (autoasignación) es **útil pero puede esperar** dado que hoy solo existe una cuenta real; M1/M3/M9 (diagnósticos) son **útiles pero pueden esperar** — no bloquean ningún flujo, solo lo explican mejor. | 🟡 M2 y M5 implementadas. M1, M3, M9 solo documentadas, no implementadas. |
| **ADR-001 a ADR-005** | Decisiones de modelo de datos (4 dimensiones, Excel histórico, consumo/pago de tarjeta, FinancialAccount antes de inversión, ImportBatch). | Todas **necesarias** en el sentido de que ya rigen decisiones tomadas — no son historias a implementar, son las reglas del juego. | ✅ Vigentes, aunque ADR-001 tiene una inconsistencia sin resolver (ver Paso 4/9). |
| **Serie de auditorías/análisis de dominio y flujo** (`analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md`, `redisenoflujofuncional.md`, `auditoriaflujoproducto.md`, `auditoriafuncionalcompletaveredicto.md`, `auditoriasemanticamovimientosreales.md`) | Análisis conceptuales, sin PRs asociados directamente, cuestionando si el modelo actual pide demasiadas decisiones al usuario. | Contiene al menos **un hallazgo necesario para el MVP** (bug de USD con regex sin límite de palabra, que guarda montos incorrectos — es un bug de corrección de datos, no una mejora) mezclado con ideas de evolución (colapsar `CounterpartyType`, no preguntar `MovementType`). | ❌/🟡 Sin confirmar si el bug de USD ya se corrigió — señal de alarma, ver Paso 6. |

---

# Paso 4 — Desvíos detectados (¿hace falta ahora o puede esperar?)

Sin juicio de valor, solo la pregunta que pediste:

| Concepto | ¿Hace falta ahora? |
|---|---|
| `FinancialAccount` con `Institution`+`ProductType`+`Identifier` | **Puede esperar.** Con una sola institución real (BBVA) y una sola cuenta bancaria, la pregunta que resuelve ("¿de qué cuenta salió?") tiene una única respuesta posible — no hay ambigüedad que resolver todavía. |
| Autoasignación de cuenta para tarjeta de crédito | **Puede esperar.** Ni siquiera es implementable hoy (ningún parser extrae el número de tarjeta) — sería trabajo nuevo sin usuario esperándolo. |
| Billeteras virtuales / exchanges / inversiones como `FinancialAccount.Type` nuevos | **Puede esperar.** Cero importadores reales para esas fuentes hoy — es diseñar para datos que no existen todavía en el sistema. |
| `Counterparty` vs `FinancialAccount` (superposición conceptual) | **Hace falta una decisión ahora**, aunque no la implementación — ver Paso 9. La razón por la que esto sí es distinto de los anteriores: no es una capacidad nueva, es una inconsistencia ya presente en el modelo que cualquier historia nueva sobre cualquiera de las dos entidades puede agravar. |
| Ideas de auto-conectores (Open Banking, APIs) | **Puede esperar**, ni siquiera mencionado en ningún ADR o épica actual — coherente con tu propia definición de "futuros conectores". |

---

# Paso 5 — Documentos muertos, duplicados o desactualizados

- **Colisión de nombre "Épica M"**: ya señalada en una ronda anterior de esta conversación — el roadmap general reserva "Épica M" para inversión (`InvestmentAccount`), pero `docs/Architecture/EpicaMImportWorkflow.md` (nuestro trabajo de esta ronda) usa el mismo nombre para un tema distinto. Sigue sin resolverse.
- **`docs/RoadMaps/FinancialMcp-vNext.md` está desactualizado en un punto central**: marca la Épica J "📋 Planificada" cuando la entidad `FinancialAccount` ya existe en código y ya tiene lógica de asignación automática (M5) — construida bajo el nombre de una épica que el propio roadmap no conoce. Es el documento que se autodefine como "fuente de verdad del proyecto" y hoy no lo es.
- **`docs/UX/ClassificationUX.md` tiene una sección objetivamente incorrecta hoy**: §1, "Movimientos", dice que la columna de cuenta financiera "hoy es un `<select>` editable por fila" — verifiqué el código: desde PR-P1 es de solo lectura, y desde M5 puede autoasignarse. También dice en §1.5 que "Cuentas" es una pantalla que "hoy no existe" — `accounts.html` ya existe (confirmado, con navegación desde PR-Nav).
- **`docs/Architecture/reconstruccionenrichasync.md`** es un artefacto de diagnóstico puntual (la simulación de matching que generé yo mismo esta sesión, entregada como archivo suelto) — no es un documento de diseño, no debería vivir como documentación permanente.
- **`docs/Architecture/analisisproximaepicausabilidad.md`** propone un roadmap de 9 PRs ("Épica O") que en los hechos terminó siendo `docs/Epics/EpicaO-ImportacionManual.md` con otro contenido — quedó parcialmente absorbida/superada por el documento formal de la épica, con riesgo de que alguien lea ambos y no sepa cuál manda.
- **`docs/Architecture/auditoriasemanticamovimientosreales.md`**: sus dos hallazgos concretos (desfasaje de fila del título, ambigüedad de `MovementType.Payment`) ya están capturados en otro lado — el primero corregido por M2, el segundo repetido casi textual en `analisissimplificacionmodelodominio.md`. Es contenido duplicado, no un documento vivo.
- **`docs/Archive/ReviewClassificationEnginev2ADR.md`** ya está correctamente archivado — es la arquitectura descartada (`IMatchScorer`/`IMatchingRule`) que terminó siendo reemplazada por el motor de sugerencias más simple (`IClassificationSuggestionService`, serie S1-S12). No hace falta tocarlo, solo confirmo que está bien ubicado.
- **No until until encontré ninguna historia que contradiga otra de forma activa** — la única contradicción real es la de `ADR-001` (declara `FinancialAccount`/`Counterparty` ortogonales) vs. `CounterpartyType.OwnAccount/OwnCard/Investment` (ya descripta en la ronda anterior de esta conversación).

---

# Paso 6 — Nuevo plan de MVP, camino más corto

Ordenado por prioridad real, no por facilidad:

1. **Confirmar y, si sigue abierto, corregir el bug de montos USD** (regex sin límite de palabra en normalización de descripción/monto, señalado en `auditoriaflujoproducto.md`). Es un bug de corrección de datos — sin esto, "saber cuánto gasté" puede estar devolviendo un número falso. Máxima prioridad porque socava la confianza en todo lo demás.
2. **Cerrar Épica I3**: idempotencia real de tarjeta PDF contra la base (no solo dentro del mismo archivo) + manejo del choque contra el índice único. Sin esto, "importar todos mis movimientos" no es seguro de reintentar — un caso de uso diario básico (reimportar sin querer) puede duplicar gastos.
3. **Épica L (badge de pendientes)**: es la pieza más barata y más directamente pedida por tu propia definición de MVP ("detectar movimientos sin clasificar") — el dato ya es calculable, solo falta cablearlo.
4. **Confirmar el bug de XSS en `dashboard.html`** (falta de `esc()`, señalado en `PRUI1analisisarquitecturaui.md`) — no es deuda técnica, es una vulnerabilidad real antes de producción.
5. **Verificar el estado real de Épica O** (creación de Counterparty sin salir de pantalla, CRUD de catálogos) — sin esto, "corregir manualmente" tiene fricción real cada vez que aparece una contraparte nueva.
6. Recién después de esto: continuar con Épica S/U (motor de sugerencias, UX de un clic) — son aceleradores de un flujo que para entonces ya funciona bien manualmente, no habilitadores de una capacidad faltante.

**Explícitamente fuera de este camino corto** (van al backlog, Paso 8): toda la familia `FinancialAccount`/`Institution`/`ProductType`/`Identifier`, M1/M3/M9 (diagnósticos de importación — mejoran explicabilidad, no desbloquean nada), Épica UI (extracción de CSS/JS compartido, salvo el fix puntual de XSS), Épica N, Épica M-inversión.

---

# Paso 7 — Plan vs. código real

| Ítem del plan corto | Estado |
|---|---|
| Bug de montos USD | ❓ No pude confirmar desde los documentos si se corrigió — requiere verificación directa contra el código de normalización antes de asumir nada. |
| Idempotencia real de tarjeta PDF (I3) | ❌ Falta — confirmado por lectura directa de `ImportFileProcessingSink.cs` esta misma sesión. |
| Badge de pendientes (Épica L) | ❌ Falta — confirmado por `ClassificationUX.md` y el roadmap, ningún script lo completa. |
| Fix de XSS en dashboard.html (`esc()`) | ❓ No confirmado si se corrigió desde `PRUI1`. |
| CRUD de catálogos sin fricción (Épica O) | 🟡 Parcial — O7/O8 confirmados, resto sin verificar. |
| Importación banco confiable (nombre de archivo real, offset de fila) | ✅ Hecho (fix de patrón `.xls`, M2). |
| Autoasignación de cuenta (M5) | ✅ Hecho — pero, por Paso 4, no era estrictamente necesario para el MVP todavía. |
| Motor de sugerencias (S1-S12) | ✅ Mayormente hecho — tampoco era estrictamente necesario todavía, según este mismo criterio. |

---

# Paso 8 — Backlog de evolución (nada se pierde, solo se saca del camino)

**Futuras inversiones**
- `FinancialAccount.Type=Investment`, `InvestmentAccount` (Épica M del roadmap, ADR-004).
- Movimientos internos de inversión (dividendos, compra/venta de activos) como dominio separado.

**Brokers**
- Importador PPI (o similar) — no existe ningún parser ni análisis técnico todavía, solo la mención en esta conversación.

**Cripto**
- Importador Binance/exchanges — mismo estado que brokers, sin ningún trabajo previo.

**Wallets**
- Mercado Pago, Lemon — sin importador, sin lugar claro en `FinancialAccountType` (ya señalado como límite del enum actual).

**Modelo de dominio (Institution/ProductType/Identifier)**
- Terna de identidad para `FinancialAccount` — analizada en profundidad en esta conversación, sin implementar.
- Reconciliación `Counterparty` vs `FinancialAccount` (`OwnAccount`/`OwnCard`/`Investment`) — ver Paso 9, necesita un ADR antes de que cualquiera de los dos ítems de arriba avance.

**Automatización / IA avanzada**
- Motor de sugerencias con reglas/embeddings/LLM (mencionado como evolución futura en `PRS1analisismotorsugerencias.md`, explícitamente deferido).
- Detección heurística de "pago de resumen coincide con total de tarjeta" (ADR-003, marcado como refinamiento futuro).

**Multi-banco**
- Cualquier banco además de BBVA — sin importador, sin ningún análisis todavía.

**Optimización de dominio / UX de fricción**
- Épica N (derivar `FinancialImpact` por defecto).
- Épica UI (extracción de CSS/JS compartido, más allá del fix de XSS puntual).
- Colapsar `CounterpartyType` a 2 valores reales, sacar `MovementType` del formulario (ideas de `analisissimplificacionmodelodominio.md`/`redisenoflujofuncional.md`, ninguna implementada).
- M1/M3/M9 de nuestra Épica M — mejores diagnósticos de importación.
- Épica U (aceptar sugerencia en un clic y el resto de su roadmap).

---

# Paso 9 — `FinancialAccount` y `Counterparty`: ¿antes o después del MVP?

**La implementación completa (Institution/ProductType/Identifier, cobertura de tarjetas/inversión/billeteras) puede esperar** — ningún ítem del MVP tal como lo definiste la necesita: hay una sola cuenta real hoy, "con qué medio pagué" ya se responde sin tocar `FinancialAccount`.

**Pero la decisión de qué hacer con `CounterpartyType.OwnAccount/OwnCard/Investment` no puede esperar tanto**, por una razón de producto, no de DDD: **`ADR-003` ya da por planificada la UX que va a empujar al usuario a usar `Counterparty` tipo `OwnCard` para clasificar pagos de resumen** — eso es trabajo de Épica N/K, que sí está en el camino corto de este mismo MVP (clasificar correctamente, sin doble conteo de gastos de tarjeta, es requisito explícito de tu propia lista). Si esa UX se construye antes de decidir la relación con `FinancialAccount`, cada usuario real que clasifique un pago de resumen va a estar alimentando el lado del modelo que probablemente haya que migrar después — no es un problema de elegancia de diseño, es directamente el riesgo de tener que migrar datos de producción reales una vez que el MVP ya esté en uso diario.

**Conclusión**: no rediseñes `FinancialAccount` ahora — pero sí valdría la pena, antes de tocar la UX de clasificación de pagos de tarjeta (que sí es parte del camino corto), dejar escrita una decisión de una sola línea: "el pago de resumen se sigue clasificando vía `Counterparty.OwnCard` hasta que `FinancialAccount` cubra tarjetas de crédito; ese día, la migración es X" — no hace falta resolverlo, alcanza con no ignorarlo.

---

# Paso 10 — Limpieza documental

| Documento | Acción | Por qué |
|---|---|---|
| `docs/RoadMaps/FinancialMcp-vNext.md` | **Actualizar** (reescritura de las secciones 7-9) | Se autodeclara fuente de verdad y no lo es — no menciona Épicas S/U/UI/M(import), y marca J como no iniciada cuando ya tiene código. |
| `docs/UX/ClassificationUX.md` | **Actualizar** (§1.2 y §1.5) | Describe un `<select>` editable que ya no existe y una pantalla de Cuentas que ya existe. |
| `docs/Architecture/EpicaMImportWorkflow.md` | **Mantener, pero renombrar** | Colisión de nombre con la Épica M de inversión del roadmap general — ya señalado, sigue pendiente. |
| `docs/Architecture/reconstruccionenrichasync.md` | **Archivar** | Es un artefacto de diagnóstico puntual, no documentación de diseño viva. |
| `docs/Architecture/analisisproximaepicausabilidad.md` | **Archivar** | Su contenido quedó absorbido (con cambios) por `docs/Epics/EpicaO-ImportacionManual.md`; mantener ambos vivos genera ambigüedad sobre cuál manda. |
| `docs/Architecture/auditoriasemanticamovimientosreales.md` | **Archivar** | Sus dos hallazgos ya viven en otro lado (M2 corregido; ambigüedad de `MovementType` repetida en `analisissimplificacionmodelodominio.md`). |
| `docs/Architecture/PRS1/PRS6/PRS8/PRS11/PRS12` (serie motor de sugerencias) | **Archivar**, salvo la recomendación abierta de PRS12 | Documentan PRs ya mayormente implementados (S1-S9, S11) — valor histórico, no decisiones pendientes. Extraer la recomendación no confirmada de PRS12 (bug de confianza `High`) como ítem de backlog antes de archivar. |
| `docs/Architecture/PRU1/PRU3/PRU4` | **Archivar**, salvo el estado de U2 | Mismo criterio — U3/U4 confirmados implementados. Confirmar el estado real de U2 (la de mayor impacto según el propio documento) antes de archivar U1. |
| `docs/Architecture/PRUI1analisisarquitecturaui.md` | **Mantener** | No implementado — sigue siendo trabajo real pendiente (incluyendo el bug de XSS), no un documento histórico. |
| `docs/Architecture/analisisentidadcounterparty.md` | **Mantener**, referenciar desde el futuro ADR de Paso 9 | Directamente relevante a la decisión pendiente sobre `CounterpartyType`. |
| `docs/Architecture/analisisnavegacion.md` | **Archivar** | Sus recomendaciones ya se implementaron (PR-Nav, esta misma conversación). |
| `docs/Architecture/analisissimplificacionmodelodominio.md` | **Mantener** | Hallazgo activo y no implementado (`MovementType` sin consumidores reales) — insumo directo para Épica N. |
| `docs/Architecture/auditoriaflujoclasificacion.md` | **Mantener** | Insumo directo para Épica N/U, sin implementar. |
| `docs/Architecture/auditoriaflujoproducto.md` | **Mantener** | Contiene el hallazgo del bug de USD, prioridad #1 de este plan — no archivar hasta confirmar que se corrigió. |
| `docs/Architecture/auditoriafuncionalcompletaveredicto.md` | **Archivar una vez que este documento (`AuditoriaMVP.md`) se apruebe** | Es el cierre de síntesis anterior a esta auditoría — esta la reemplaza como "estado actual". |
| `docs/Architecture/redisenoflujofuncional.md` | **Mantener** | Síntesis de producto de mayor valor sin implementar en todo el árbol de documentos — insumo directo para Épica N. |
| `docs/Architecture/ImportWorkflowReview.md` | **Mantener** | Vigente, base directa de la Épica M actual (M1-M9), varias historias sin implementar todavía. |
| `docs/Epics/EpicaI-Importacion.md` | **Actualizar** (marcar I1/I2 hechos, I3 parcial, I4-I7 pendientes de confirmar) | Sigue teniendo trabajo real abierto (I3), no se archiva. |
| `docs/Epics/EpicaO-ImportacionManual.md` | **Actualizar** (marcar O1/O2/O7/O8 hechos) | Ídem — trabajo real abierto, no se archiva. |
| `docs/Decisions/ADR-001` a `ADR-005` | **Mantener todos**; **actualizar ADR-001** con una nota sobre la inconsistencia con `CounterpartyType` encontrada esta sesión | Son las reglas vigentes del dominio — no se tocan salvo para documentar la grieta ya encontrada, siguiendo el mismo criterio que el propio ADR-001 pide ("no extender el modelo por acumulación silenciosa"). |
| `docs/Archive/*` | **Mantener sin cambios** | Ya están correctamente archivados. |
| `docs/patch/enriquecimiento-tarjeta-debito.md` | **Mantener sin cambios** | Registro histórico de una decisión ya implementada, sigue siendo preciso. |

---

# Veredicto final

**El proyecto sí se desvió del camino más corto al MVP, pero no de forma grave** — la mayoría del trabajo de las últimas semanas (motor de sugerencias, UX de clasificación, autoasignación de cuenta) es trabajo de *calidad* sobre un flujo que ya existe, no trabajo *equivocado*. El desvío real y concreto es específico: **empezamos a diseñar el modelo de `FinancialAccount` para un futuro con decenas de instituciones y productos cuando el presente todavía tiene una sola cuenta real** — ese diseño es correcto para cuando haga falta, pero hoy no desbloquea nada que el MVP necesite.

Lo que sí encontré y es genuinamente urgente, independiente de cualquier decisión de diseño de dominio: el bug de montos USD (si sigue sin corregir) y la falta de idempotencia real en la importación de tarjeta PDF — ambos son defectos de corrección/confiabilidad de datos, exactamente el tipo de problema que un MVP no puede tener antes de usarse a diario.

No implementé nada, no modifiqué ningún archivo del repositorio, como pediste.
