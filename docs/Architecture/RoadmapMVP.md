# Roadmap hacia el MVP — decisión de Tech Lead

> Este documento se apoya en la auditoría anterior (`AuditoriaMVP.md`, misma sesión) — no repito ahí donde ya quedó fundamentado con cita de código/documento, pero cada veredicto de acá es autocontenido y accionable por sí mismo. Rol: Product Architect + Software Architect + Tech Lead, decidiendo el camino más corto a un MVP usable a diario, no el producto ideal.

---

# Paso 1 — El MVP verdadero, reconstruido desde el objetivo original

Ignorando roadmap/épicas/ADRs como fuente, solo tu objetivo original:

**Para usar FinancialMcp todos los días necesito, y nada más que esto:**

1. Importar mis movimientos de banco y tarjeta de crédito sin que se dupliquen ni se pierdan filas en silencio.
2. Ver todos los movimientos (pendientes y clasificados) en un solo lugar.
3. Clasificarlos: categoría, impacto financiero, opcionalmente contraparte — con qué medio pagué ya sale gratis del origen del movimiento.
4. Que el sistema me avise cuándo hay movimientos sin clasificar todavía.
5. Que el sistema me avise de movimientos sospechosos (posibles duplicados/splits).
6. Poder corregir cualquier clasificación a mano, sin fricción.
7. Preguntarle a un LLM sobre esos datos ya clasificados y confiar en la respuesta.

Ese es el MVP completo. No incluye: múltiples bancos, tarjetas de crédito asociadas a una cuenta específica, billeteras, brokers, cripto, inversión, ni ningún concepto de "Institución" reutilizable — todo eso, por tu propia definición, es después.

---

# Paso 2 — Lectura de contexto

Reutilizo la lectura completa de la auditoría anterior (5 ADRs, 2 épicas formales, roadmap maestro, `ClassificationUX.md`, 21 documentos de análisis en `docs/Architecture/` y `docs/Archive/`) — no la repito acá. Donde este documento se apoya en un hallazgo concreto de esa lectura, lo cito.

---

# Paso 3 — Cada iniciativa contra el MVP (nueva codificación)

| Iniciativa | Veredicto |
|---|---|
| Épica I — idempotencia/trazabilidad de importación | ✅ Necesaria para el MVP |
| Épica K — pantalla de clasificación (`movements.html`) | ✅ Necesaria — 🟠 ya implementada, cerrada |
| Épica L — badge de pendientes de clasificar | ✅ Necesaria — ❌ no implementada, es el punto 4 del MVP sin cablear |
| Épica O — importación manual + CRUD de catálogos | ✅ Necesaria — 🟠 parcialmente implementada |
| Bug de montos USD (regex sin límite de palabra) | ✅ Necesaria — es un riesgo de dato, no una historia |
| Bug de XSS en `dashboard.html` (`esc()` faltante) | ✅ Necesaria — riesgo real antes de producción, no deuda técnica |
| Épica J — `FinancialAccount` como entidad | 🟡 Útil pero puede esperar — 🟠 ya parcialmente implementada (ver Paso 4) |
| Épica S — motor de sugerencias de clasificación | 🟡 Útil pero puede esperar — 🟠 mayormente implementada |
| Épica U — UX de un clic para aceptar sugerencias | 🟡 Útil pero puede esperar — 🟠 parcialmente implementada |
| Épica N — valores por defecto en el formulario | 🟡 Útil pero puede esperar — ❌ no iniciada |
| Épica UI — CSS/JS compartido entre páginas | ⚪ Mejora futura (salvo el fix puntual de XSS, que es ✅) — ❌ no iniciada |
| Nuestra Épica M — M1/M3/M9 (diagnósticos de importación) | 🟡 Útil pero puede esperar — 🟠 M2/M5 implementadas, resto no |
| `Institution`/`ProductType`/`Identifier` en `FinancialAccount` | ⚫ Sobreingeniería para esta etapa — ❌ no implementada, y no la necesitás con una sola cuenta real |
| Tarjetas de crédito como `FinancialAccount` distinta | 🔵 Pertenece al producto final — ❌ ni siquiera es posible hoy (ningún parser extrae el identificador) |
| `InvestmentAccount` / Épica M-inversión (roadmap) | 🔵 Pertenece al producto final — ❌ no iniciada, correcto que siga así |
| Wallets, exchanges, brokers, multi-banco | ⚪ Mejora futura — ❌ no iniciada, ningún importador existe |
| Colapsar `CounterpartyType`, sacar `MovementType` del formulario | 🟡 Útil pero puede esperar — ❌ no implementada, insumo de Épica N |
| `CounterpartyType.OwnAccount/OwnCard/Investment` vs. `FinancialAccount` | ⚫ Sobreingeniería resolverla *completa* ahora — pero **la decisión mínima no puede esperar** (ver Paso 9) |

---

# Paso 4 — Dónde sobrepensamos el producto

Respuesta directa, sin juicio de diseño, solo "¿hace falta ahora?":

| Tema | ¿Hace falta antes del MVP? |
|---|---|
| `FinancialAccount` con identidad `Institution+ProductType+Identifier` | No. Una sola cuenta real no genera ambigüedad que resolver. |
| Autoasignación de cuenta para tarjeta de crédito | No. No es implementable hoy sin antes tocar los parsers PDF — nadie lo está esperando. |
| Wallets / Exchanges / Brokers como conceptos de dominio | No. Cero importadores reales, es diseñar sobre datos que no existen. |
| Multi-banco | No. Un solo banco real hoy. |
| Discusión DDD sobre `FinancialAccount` vs `Counterparty` (Value Object, Bounded Context, Aggregate Root) | No, la discusión de diseño en sí puede esperar. Sí hace falta, ya, una **decisión de una línea** sobre qué modelo usar para clasificar pagos de tarjeta — ver Paso 9, es la única excepción real de este listado. |
| Motor de sugerencias con reglas/embeddings/IA | No. El motor actual (S1-S12) ya funciona sin eso; es una aceleración, no una capacidad faltante. |

**Conclusión del Paso 4**: el sobrepensamiento real está concentrado en un solo lugar — el modelo de `FinancialAccount` para productos/instituciones que todavía no existen en el sistema. Todo lo demás que se documentó (motor de sugerencias, UX de clasificación, arquitectura de UI compartida) es trabajo de calidad sobre el flujo del MVP, no una desviación de él.

---

# Paso 5 — Riesgos reales de producto, priorizados

1. **Montos USD potencialmente guardados mal** (regex de normalización sin límite de palabra, `auditoriaflujoproducto.md`) — rompe la confianza en "cuánto gasté", el corazón del MVP. Sin verificar si sigue vigente, es el riesgo #1 por definición: no sabés si tu propio dato ya está mal.
2. **Reimportar un PDF de tarjeta puede duplicar o romper la corrida** (Épica I3 sin cerrar, confirmado por lectura directa del código: `ImportFileProcessingSink` no chequea `ExternalId` contra la base antes de insertar, sin manejo del choque contra el índice único). Riesgo de pérdida de confianza + posible excepción no controlada en un flujo de uso normal (reimportar sin querer).
3. **XSS en `dashboard.html`** — no es un riesgo de "producto" en el sentido de datos, pero sí de seguridad antes de que el sistema esté en uso diario real; lo subo por delante de cualquier feature nueva.
4. **Sin visibilidad de cobertura de clasificación** (Épica L) — no es un bug, pero es un riesgo de confianza silencioso: el dashboard puede resumir un mes calculado sobre una fracción minoritaria de los movimientos reales sin ningún aviso. El usuario puede tomar decisiones ("¿en qué gasto de más?") sobre datos incompletos sin saberlo.
5. **Doble conteo de gastos de tarjeta por clasificación incorrecta del pago de resumen** (`ADR-003`, mecanismo correcto en el dominio pero sin guía de UX todavía) — riesgo de clasificación incorrecta, no de bug de código.
6. **Fricción para crear `Counterparty` nueva** — no es un riesgo de dato, es un riesgo de abandono: si corregir manualmente es incómodo, el usuario deja de clasificar y el sistema deja de ser confiable por acumulación de pendientes, no por un bug puntual.

Los primeros tres son bugs/vulnerabilidades reales — se resuelven, no se diseñan. Los últimos tres son huecos de proceso — se cierran con la Épica L/O/N ya documentadas, no con nada nuevo.

---

# Paso 6 — Roadmap nuevo, mínimo, por etapas de valor

**Etapa 0 — Verificación (antes de escribir cualquier línea de código nueva)**
Confirmar contra el código actual si el bug de USD y el bug de XSS siguen vigentes. No están confirmados ni descartados en la documentación — es la primera acción, no una implementación.

**Etapa 1 — Confiabilidad de datos**
Cerrar Épica I3 (idempotencia real de tarjeta PDF contra la base) + corregir el bug de USD si sigue vigente. Desbloquea: poder confiar en "cuánto gasté" y poder reimportar sin miedo.

**Etapa 2 — Visibilidad**
Épica L (badge de pendientes). Desbloquea: "detectar movimientos sin clasificar", explícitamente en tu lista de MVP, hoy inexistente en la UI aunque el dato ya es calculable.

**Etapa 3 — Fricción de uso diario**
Terminar lo que falte de Épica O (creación de `Counterparty` sin salir de pantalla, CRUD de catálogos) + corregir XSS si sigue vigente. Desbloquea: "corregirlos manualmente" sin fricción, y una superficie mínima de seguridad.

**Etapa 4 — Confianza en el MCP**
Con las 3 etapas anteriores cerradas, los datos que el MCP expone ya son confiables — recién ahí tiene sentido invertir en las preguntas más avanzadas ("¿qué presupuesto debería tener?"), probablemente sin backend nuevo, solo iterando el prompt/orquestación sobre las 4 herramientas MCP que ya existen.

**Todo lo demás — motor de sugerencias más inteligente, UX de un clic, `FinancialAccount` para tarjetas/inversión, wallets, brokers, cripto — queda fuera de este roadmap, en el backlog del Paso 12.**

---

# Paso 7 — Roadmap vs. código

| Etapa | Estado |
|---|---|
| 0 — Verificar USD/XSS | ❓ Sin confirmar — primera acción concreta recomendada |
| 1 — Idempotencia tarjeta PDF | ❌ Falta (confirmado por código) |
| 1 — Fix USD (si aplica) | ❓ Depende de la verificación |
| 2 — Badge de pendientes | ❌ Falta |
| 3 — Fricción Épica O | 🟡 Parcial (O1/O2/O7/O8 confirmados; resto sin verificar) |
| 3 — Fix XSS (si aplica) | ❓ Depende de la verificación |
| 4 — Preguntas avanzadas al MCP | 🟡 Las 4 herramientas base ya existen; preguntas de presupuesto/estrategia de ahorro probablemente no necesiten backend nuevo, solo prompting — sin verificar todavía si el MCP host ya tiene ese nivel de orquestación |

---

# Paso 8 — Disposición de cada historia (implementadas y no)

| Historia/Épica | Disposición |
|---|---|
| Épica K (nueva UX de clasificación) | **Cerrar definitivamente** — completa, sin trabajo pendiente documentado. |
| Épica I (I1, I2) | **Cerrar** esas dos — hechas. |
| Épica I (I3, I4-I7) | **Mantener abierta**, es Etapa 1 del roadmap nuevo. |
| Épica L | **Mantener**, sin cambios de alcance — es Etapa 2 tal cual está documentada. |
| Épica O (O1/O2/O7/O8) | **Cerrar** esas cuatro. |
| Épica O (resto) | **Actualizar** el documento para reflejar qué falta realmente, antes de retomarla en Etapa 3. |
| Épica J (roadmap general) | **Reescribir** — el documento asume que no empezó; en los hechos ya tiene código (vía nuestra Épica M). Fusionar su alcance real con lo que M5 ya construyó, en vez de mantener dos fuentes. |
| Épica S (S1-S9, S11) | **Cerrar definitivamente** — implementadas, funcionando. |
| Épica S (S12, recomendación sin confirmar) | **Mover a backlog** como ítem puntual de un solo PR (bug de confianza `High` en sugerencia por Counterparty desactivado) — no reabrir toda la épica por esto. |
| Épica U (U3, U4) | **Cerrar**. |
| Épica U (U1, U2, U5-U7) | **Mover a backlog** — mejora de fricción sobre un flujo que ya funciona, no bloquea el MVP. |
| Épica UI (UI1-UI5) | **Dividir**: extraer el fix de XSS como ítem aislado y urgente (Etapa 3); mover el resto (extracción de CSS/JS compartido) a backlog. |
| Nuestra Épica M — M2, M5 | **Cerrar definitivamente** — implementadas. |
| Nuestra Épica M — M1, M3, M9 | **Mover a backlog** — mejoran explicabilidad, no desbloquean nada del MVP. |
| Épica M-inversión (roadmap), M6-M7 (nuestra épica, cuenta manual/alerta) | **Mover a backlog**, sin cambios — ya estaban correctamente fuera de foco. |
| `analisisproximaepicausabilidad.md` | **Archivar** — su contenido quedó absorbido por `EpicaO-ImportacionManual.md`. |
| `auditoriasemanticamovimientosreales.md` | **Archivar** — hallazgos ya capturados en otro lado (M2, `analisissimplificacionmodelodominio.md`). |
| `analisisnavegacion.md` | **Archivar** — implementada. |
| `reconstruccionenrichasync.md` | **Eliminar** (o archivar si preferís no borrar nada) — es un artefacto de diagnóstico puntual de esta sesión, no una historia ni un documento de diseño. |
| `analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md`, `redisenoflujofuncional.md` | **Fusionar** en un único documento de insumo para Épica N (hoy son 3 documentos con conclusiones superpuestas sobre el mismo tema: reducir decisiones por movimiento). |
| `auditoriaflujoproducto.md`, `auditoriafuncionalcompletaveredicto.md` | **Mantener** hasta confirmar el estado del bug de USD; luego **archivar**, superados por esta auditoría. |

---

# Paso 9 — Revisión de dominio desde la óptica del MVP

- **`FinancialAccount`**: lo que necesito hoy ya existe (la entidad, con `Type=Bank`, y la autoasignación para Caja de Ahorro). No hace falta nada más — congelar cualquier extensión hasta después del MVP.
- **`Counterparty`**: lo que necesito hoy ya existe y funciona (creación, valores por defecto, clasificación). Puedo simplificar el enum (`CounterpartyType` a 2-3 valores reales) más adelante, no es bloqueante — congelar.
- **`Institution`/`ProductType`**: no existen y no hacen falta. Congelar sin excepción.
- **`InvestmentAccount`**: no existe, correctamente fuera de alcance. Congelar.
- **Wallets/Brokers**: no existen, ni el importador ni el modelo. Congelar.
- **La única pieza que no puede congelarse del todo**: la relación entre `CounterpartyType.OwnCard`/`OwnAccount`/`Investment` y `FinancialAccount`. No porque el MVP la necesite resuelta — sino porque Épica N/K (que sí son del MVP, guían la clasificación de pago de resumen de tarjeta) van a apoyarse en `Counterparty.OwnCard` tal como está hoy. La decisión mínima necesaria no es un rediseño: es escribir una frase en `ADR-003` o `ADR-001` confirmando "por ahora se sigue usando `Counterparty.OwnCard` para esto; el día que `FinancialAccount` cubra tarjetas, hay una migración pendiente" — para que nadie construya sobre esa pieza sin saber que es provisoria.

---

# Paso 10 — ¿Separar MVP estable de Desarrollo?

Hoy todo corre sobre una única base de datos, con tus datos reales importados. Cada PR de este mismo proyecto (incluidas migraciones de esta sesión: `FinancialAccount`, `FinancialAccountId` en `BankStatement`/`Transaction`, enriquecimiento de Débito) ya se aplicó, en los hechos, contra esa misma base — no es un riesgo hipotético, es lo que venimos haciendo.

**Ventajas de separar:**
- Cualquier migración o bug de un feature nuevo no puede corromper ni perder tus datos reales.
- Podés probar agresivamente (inclusive con datos sintéticos) sin cuidar cada paso.
- Es la práctica estándar apenas hay datos reales que importan.

**Riesgos/costo de separar:**
- Mantener dos entornos sincronizados en esquema (migraciones aplicadas en el mismo orden en ambos) requiere disciplina, aunque sea manual.
- Para un proyecto de un solo usuario, un pipeline de CI/CD completo sería sobreingeniería — no hace falta eso.

**Recomendación**: sí, separar, pero con la versión más liviana posible — no un pipeline nuevo, solo:
1. Una segunda base de datos (puede ser el mismo servidor Postgres, otro nombre de base) para desarrollo.
2. Cualquier migración/feature nuevo se prueba primero ahí.
3. Recién se aplica a la base con tus datos reales una vez validado — un checklist manual alcanza, no hace falta automatizarlo todavía.

**Cuándo hacerlo**: antes de retomar cualquier ítem del backlog que implique cambios de esquema (todo lo de `FinancialAccount` evolution, wallets, inversión) — no es bloqueante para la Etapa 1-3 del roadmap nuevo, que en su mayoría no requieren migraciones nuevas (I3 reutiliza columnas existentes; L y el fix de XSS son solo frontend/consulta). Podés arrancar la Etapa 1 ya, y resolver la separación de entornos en paralelo, antes de que aparezca la primera migración de la próxima etapa post-MVP.

---

# Paso 12 — Backlog de evolución por tema

**Inversiones**: `FinancialAccount.Type=Investment`, `InvestmentAccount`, movimientos internos de inversión.
**Brokers**: importador PPI (sin ningún trabajo previo).
**Cripto**: importador Binance/exchanges (sin ningún trabajo previo).
**Wallets**: Mercado Pago, Lemon — sin importador, sin lugar en el enum actual.
**Multi-banco**: cualquier banco además de BBVA.
**Mejoras de dominio**: `Institution`/`ProductType`/`Identifier`, reconciliación `Counterparty`/`FinancialAccount`, colapsar `CounterpartyType`, sacar `MovementType` del formulario.
**IA avanzada / Automatizaciones**: motor de sugerencias con reglas/embeddings/LLM, detección heurística de pago de resumen.
**UX**: Épica U (U1/U2/U5-U7), Épica N, nuestra M1/M3/M9.
**Performance/arquitectura**: Épica UI (extracción de CSS/JS compartido, más allá del fix de XSS).

---

# Recomendación final

1. **Próximas semanas**: Etapa 0 (verificar USD/XSS) → Etapa 1 (idempotencia tarjeta + fix USD) → Etapa 2 (badge de pendientes) → Etapa 3 (fricción de Épica O + fix XSS). En paralelo, armar la segunda base de datos de desarrollo antes de que aparezca la primera migración post-MVP.
2. **Congelar hasta después del MVP**: todo `FinancialAccount`/`Institution`/`ProductType`/`Identifier` más allá de lo ya construido, wallets, brokers, cripto, multi-banco, `InvestmentAccount`, motor de sugerencias avanzado, Épica U/N, M1/M3/M9, extracción de CSS/JS compartido.
3. **Riesgos a resolver antes de seguir agregando funcionalidad**: bug de USD (verificar), idempotencia de tarjeta PDF, XSS en dashboard, separación de entornos antes de la próxima migración de esquema.
4. **Nueva fuente de verdad**: este documento + `AuditoriaMVP.md` reemplazan, en conjunto, a `docs/RoadMaps/FinancialMcp-vNext.md` como estado actual — pero ese archivo debería reescribirse con este contenido para volver a ser la única fuente, en vez de sumar un tercer documento paralelo.
5. **Historias a archivar/reescribir**: ver tabla completa del Paso 8 — en resumen, cerrar K/I1-I2/O1-O2-O7-O8/S1-S9-S11/U3-U4/M2-M5 definitivamente; reescribir Épica J del roadmap general y Épica O; archivar los 3 documentos de análisis ya absorbidos; fusionar los 3 documentos de simplificación de interacción en uno solo.

No escribí código, no diseñé implementación, no modifiqué ningún archivo del repositorio, como pediste.
