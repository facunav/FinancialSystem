# MVP definitivo — revisión crítica final

> Rol: Lead Software Architect + Product Owner. Esta vez verifiqué contra código, no contra documentación, los dos riesgos que en la auditoría anterior habían quedado sin confirmar (bug de USD, XSS en dashboard). El resultado cambia con precisión el diagnóstico anterior — ver Paso 9.

---

# Paso 1 — El MVP, deducido del proyecto, no de la documentación

Mirando lo que el código ya construye y para qué sirve cada pieza, el MVP es: un lugar único donde entran mis movimientos de banco y tarjetas, sin duplicarse ni perderse; donde cada uno queda clasificado (gasto/ingreso, categoría, con qué medio pagué, cuándo); donde puedo corregir a mano lo que el sistema no resolvió solo; y donde esos datos ya clasificados son lo bastante confiables como para que un LLM, vía MCP, pueda razonar sobre ellos sin que yo tenga que verificar cada número antes de confiar en la respuesta. Nada de esto necesita saber qué banco es, cuántas cuentas tengo, ni modelar ningún producto financiero que hoy no importo.

---

# Paso 2 — MVP vs. estado actual

**Terminado**: importación de banco y tarjeta de débito (con el bug de fila y la falta de detección de cuenta ya corregidos), pantalla de clasificación completa (`movements.html`), motor de sugerencias funcionando, aceptar sugerencia en un clic (verifiqué el código: ya existe, comentario "PR-U1: sugerencia lista para aceptar con un clic sin abrir el modal"), creación de `Counterparty` sin salir de pantalla (verifiqué: existe `createCounterpartyInline`, modal "Nueva contraparte"), 4 herramientas MCP sobre `ClassifiedMovement`.

**Falta**: idempotencia real de importación de tarjeta de crédito PDF contra la base (confirmado en la ronda anterior, sigue sin verificarse una corrección); badge de movimientos pendientes de clasificar.

**Sobra** (para esta etapa, no en sí): todo el trabajo de identidad de `FinancialAccount` (`Institution`/`ProductType`/`Identifier`) — ver Paso 4.

**Se desvió**: el foco pasó, en las últimas semanas, de cerrar los huecos de arriba a diseñar un modelo de cuentas para bancos/productos que todavía no existen en el sistema.

**Quedó a mitad de camino**: Épica I (banco y Excel legacy tienen idempotencia real, tarjeta PDF no); Épica J del roadmap general (la entidad existe, la asignación automática para Caja de Ahorro existe, pero el roadmap que la describe no sabe que eso ya pasó).

---

# Paso 3 — Disposición de cada historia

| Historia | Acción | Por qué |
|---|---|---|
| Épica K (clasificación) | **FINALIZAR** | Completa, sin nada pendiente documentado. |
| Épica I1, I2 | **FINALIZAR** | Hechas. |
| Épica I3 (idempotencia tarjeta PDF) | **CONTINUAR** | Es riesgo real de datos, ver Paso 9. |
| Épica I4-I7 | **PAUSAR** | Dependen de I3; no vale planificarlas hasta cerrar esa base. |
| Épica L (badge pendientes) | **CONTINUAR** | Explícitamente parte del MVP, dato ya calculable, solo falta cablear. |
| Épica O (creación de catálogos, importación manual) | **FINALIZAR** en su mayoría — verifiqué que la creación inline de `Counterparty` y el botón de importar ya existen. | El resto (si queda algo) es de bajo riesgo, no bloquea nada. |
| Épica J (roadmap general) | **UNIFICAR** con nuestra Épica M | Ambas describen la misma pieza (`FinancialAccount`) bajo nombres distintos — mantenerlas separadas ya generó confusión una vez. |
| Épica S (motor de sugerencias, S1-S11) | **FINALIZAR** | Implementado y funcionando. |
| Épica S (S12, bug de confianza en sugerencia desactivada) | **CONTINUAR** como ítem aislado, no reabrir la épica entera. |
| Épica U (U1-U4) | **FINALIZAR** | Verifiqué: aceptar en un clic, confianza visual y lista en modal de lote ya están. |
| Épica U (U5-U7) | **MOVER AL PRODUCTO FUTURO** | Refinamientos sobre un flujo que ya funciona. |
| Épica UI (CSS/JS compartido) | **PAUSAR**, salvo el fix de XSS | Ver Paso 9 — el XSS es urgente, el resto es deuda técnica que no bloquea el MVP. |
| Nuestra Épica M — M2, M5 | **FINALIZAR** | Implementadas. |
| Nuestra Épica M — M1, M3, M9 | **MOVER AL PRODUCTO FUTURO** | Explican mejor un resultado que ya es correcto — no desbloquean nada. |
| Épica M-inversión (roadmap), M6-M7 (nuestra épica) | **MOVER AL PRODUCTO FUTURO** | Sin cambios de criterio. |
| Épica N (valores por defecto) | **MOVER AL PRODUCTO FUTURO** | Reduce fricción sobre un flujo que ya funciona manualmente. |
| Discusión `Institution`/`ProductType`/`Identifier` | **ARCHIVAR como análisis, no como historia activa** | Ver Paso 4. |
| `analisisproximaepicausabilidad.md`, `analisisnavegacion.md`, `auditoriasemanticamovimientosreales.md` | **ARCHIVAR** | Contenido absorbido por otros documentos ya implementados. |
| `analisissimplificacionmodelodominio.md` + `auditoriaflujoclasificacion.md` + `redisenoflujofuncional.md` | **UNIFICAR** | Tres documentos, una sola conclusión (reducir decisiones por movimiento) — insumo de Épica N cuando llegue su turno. |
| `reconstruccionenrichasync.md` | **ELIMINAR** | Artefacto de diagnóstico puntual, no documentación de diseño. |

---

# Paso 4 — `FinancialAccount`: veredicto extremadamente crítico

**¿Alcanza para el MVP?** Sí, tal cual está hoy. Tiene la entidad, tiene `Type=Bank`, tiene la autoasignación automática para Caja de Ahorro (M5). Eso ya cubre "desde qué cuenta salió" para el 100% de las fuentes que el MVP necesita — banco. No hace falta nada más de esta entidad para cumplir el objetivo original.

**¿El trabajo de `Institution`/`ProductType`/`Identifier` desbloquea algo del MVP?** No, ninguna parte. Con una sola cuenta real, "¿de qué cuenta salió?" tiene una única respuesta posible sin necesidad de ninguna identidad compuesta. Este trabajo solo cobra sentido el día que exista una segunda cuenta, un segundo banco o una tarjeta de crédito con identificador extraíble — ninguna de esas tres condiciones existe hoy en el sistema.

**Dicho sin rodeos**: las últimas rondas de análisis de dominio sobre `FinancialAccount` (Value Object, Bounded Context, terna de identidad, comparación contra Binance/PPI/Mercado Pago) fueron trabajo de diseño correcto, pero prematuro — resolvían un problema que el producto todavía no tiene. Es exactamente el patrón que describiste al pedir esta auditoría: invertir tiempo en un problema futuro antes de terminar el problema presente.

---

# Paso 5 — `Counterparty`: ¿hace falta tocarlo para el MVP?

No. `Counterparty` ya funciona completo para el MVP: se crea, se edita, tiene valores por defecto, se usa en clasificación, y la creación inline ya existe (verificado en código). La única pregunta abierta sobre `Counterparty` — su superposición con `FinancialAccount` (`OwnAccount`/`OwnCard`/`Investment`) — es una pregunta sobre el producto futuro (tarjetas de crédito como cuenta propia), no sobre el MVP. La única acción que no puede esperar no es tocar el código: es dejar escrita una frase reconociendo que esa superposición existe, para que nadie construya encima sin saberlo — nada más.

---

# Paso 6 — Cada idea de las auditorías

| Idea | Decisión |
|---|---|
| Múltiples bancos | NO HACER (para esta etapa) |
| Brokers | NO HACER |
| Inversiones | NO HACER |
| Wallets | NO HACER |
| Binance | NO HACER |
| MercadoPago | NO HACER |
| Tarjetas múltiples como cuentas distintas | NO HACER |
| `Institution` | HACER DESPUÉS DEL MVP |
| `ProductType` | HACER DESPUÉS DEL MVP |
| `Identifier` | HACER DESPUÉS DEL MVP |
| `FinancialAccount` avanzado | HACER DESPUÉS DEL MVP |
| Dashboard complejo / analytics avanzado | NO HACER — el dashboard actual ya alcanza para las preguntas del MVP |
| Score / reglas inteligentes de clasificación | NO HACER — el motor de sugerencias actual ya funciona sin esto |
| Idempotencia real de tarjeta PDF | **HACER AHORA** |
| Badge de pendientes de clasificar | **HACER AHORA** |
| Fix del bug de USD (ver Paso 9) | **HACER AHORA** |
| Fix de XSS en dashboard (ver Paso 9) | **HACER AHORA** |

---

# Paso 7 — Roadmap nuevo (desde cero, 4 etapas)

**ETAPA 0 — Confiabilidad**
Corregir el bug de USD (mecanismo confirmado en Paso 9). Cerrar idempotencia real de importación de tarjeta PDF. Corregir el XSS de `dashboard.html`. Nada de esto es una historia nueva — son 3 defectos puntuales y acotados.

**ETAPA 1 — MVP usable**
Badge de movimientos pendientes de clasificar (Épica L). Con Etapa 0 + esto, el ciclo completo del MVP (importar → clasificar → confiar → preguntar) queda cerrado sin huecos conocidos.

**ETAPA 2 — Calidad**
Terminar lo que reste de Épica O si algo queda pendiente de verificar. Fix puntual de S12 (confianza de sugerencia sobre entidad desactivada). Nada más — esta etapa es deliberadamente chica.

**ETAPA 3 — Producto final**
Todo lo demás: `Institution`/`ProductType`/`Identifier`, tarjetas de crédito como `FinancialAccount`, inversión, wallets, brokers, cripto, multi-banco, Épica N, Épica U5-U7, Épica UI (resto), motor de sugerencias avanzado.

---

# Paso 8 — Limpieza documental (solo listar, no ejecutar)

- **Obsoletos**: `analisisnavegacion.md`, `analisisproximaepicausabilidad.md` (contenido implementado o absorbido).
- **Duplicados**: `analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md`, `redisenoflujofuncional.md` (misma conclusión, tres documentos).
- **Absorbidos por otros**: `auditoriasemanticamovimientosreales.md` (por M2 + `analisissimplificacionmodelodominio.md`); `analisisproximaepicausabilidad.md` (por `EpicaO-ImportacionManual.md`).
- **Para eliminar**: `reconstruccionenrichasync.md` (artefacto de diagnóstico, no documentación).
- **Ya no representan el estado del código**: `docs/RoadMaps/FinancialMcp-vNext.md` (marca Épica J como no iniciada, siendo que ya tiene código); `docs/UX/ClassificationUX.md` §1.2 y §1.5 (describen un `<select>` editable de cuenta y una pantalla "Cuentas" inexistente que ya no son así).

---

# Paso 9 — Deuda técnica REAL (verificada contra código esta misma ronda)

1. **Bug de montos USD, confirmado y con mecanismo preciso** (distinto al que describía la documentación original). Hay dos detectores de moneda independientes que no están de acuerdo: `CurrencyDetector.Detect` (`BbvaTransactionLineParser.cs:145`), que decide si hay que re-extraer el monto en USD, usa `\bUSD\b` — con límite de palabra, así que **no detecta** "USD" cuando queda pegado a la palabra anterior sin espacio (ej. `...8GUSD 11,14`, un patrón real documentado en el propio comentario del parser). `ImportValueParser.DetectCurrencyFromText` (`ImportValueParser.cs:210`), usado después por `TransactionNormalizer` para etiquetar el campo `Currency` — porque `CurrencyHint` nunca se completa (`ExtractedTransaction.cs:8`) —, usa `Contains("USD")` sin límite de palabra, así que **sí** detecta ese mismo caso. Resultado real: una transacción puede quedar persistida con `Currency = "USD"` pero `Amount` igual al valor en pesos (porque el primer detector, más estricto, no disparó la re-extracción del monto en dólares). Es un defecto de datos real, no una hipótesis — verificado línea por línea.
2. **Idempotencia real de tarjeta PDF, todavía sin resolver** — confirmado en la ronda anterior, sigue vigente: `ImportFileProcessingSink` solo dedupea en memoria dentro del mismo archivo, sin consultar contra la base antes de insertar.
3. **XSS en `dashboard.html`, confirmado** — es la única de las 5 pantallas sin función `esc()`; `cat.categoryDisplayName` (un nombre de categoría editable por el usuario) se interpola sin escapar en `innerHTML` (`dashboard.html:1103`). Severidad real acotada (self-XSS en una app de un solo usuario), pero el fix es barato y la corrección de higiene es correcta antes de cualquier escenario de uso compartido.

No encontré ningún otro riesgo de pérdida de datos, consistencia o seguridad al verificar directamente — el resto de lo documentado en auditorías previas o ya está corregido (verifiqué U1-U4, creación inline de Counterparty) o es mejora, no riesgo.

---

# Paso 10 — Desafiando decisiones anteriores

Sí, hubo sobrediseño, y lo digo sin vueltas: las últimas 3-4 rondas de esta conversación (terna de identidad para `FinancialAccount`, comparación contra Binance/PPI/Mercado Pago, Value Object/Bounded Context/Aggregate Root) fueron ejercicios de arquitectura correctos en su forma pero mal timeados — resolvían un problema que el producto no tiene todavía, mientras dos bugs de datos reales (USD, idempotencia de tarjeta) seguían sin verificarse. La Épica "M-inversión" del roadmap general nunca debió mencionarse en la misma conversación que "voy a importar mi Caja de Ahorro por primera vez" — no porque esté mal diseñada, sino porque compitió por atención con problemas que sí importaban hoy. Ninguna épica necesita desaparecer — pero el orden en que las fuimos discutiendo estuvo invertido respecto al valor real que cada una entrega.

---

# Paso 11 — Respuestas concretas

**¿Qué harías si este proyecto fuera tuyo?** Pararía cualquier discusión de dominio nueva y arreglaría los 3 defectos del Paso 9 primero, con evidencia de código en mano, no con hipótesis de documentación.

**¿Qué dejarías de hacer inmediatamente?** Cualquier análisis de `FinancialAccount`/`Institution`/`ProductType` — no porque esté mal, porque no es lo que bloquea el MVP.

**¿Qué terminarías primero?** El bug de USD — es el que más directamente ataca la confianza en el número más básico del producto ("cuánto gasté").

**¿Qué moverías al producto futuro?** Todo lo del Paso 6 marcado "HACER DESPUÉS" — inversión, wallets, brokers, cripto, multi-banco, identidad avanzada de cuenta, Épica N, UX de fricción restante.

**¿Qué eliminarías?** `reconstruccionenrichasync.md` como documento permanente (no como conocimiento — ya está reflejado en el análisis de M5). Ninguna historia de código necesita eliminarse, solo reordenarse.

**¿Camino más corto a un MVP estable?** Las 2 etapas (0 y 1) de este roadmap — nada más. Todo lo demás documentado ya es, en la práctica, calidad sobre un MVP que en sus 2/3 partes ya funciona.

**¿Qué necesita el MCP para generar recomendaciones útiles?** Nada nuevo de backend — las 4 herramientas ya existen sobre `ClassifiedMovement`. Necesita que esos datos sean confiables (Etapa 0) y completos (Etapa 1, saber qué falta clasificar) — el resto es de prompting/orquestación, no de código nuevo.

**¿Qué puede esperar 6 meses sin afectar el valor del producto?** Todo el Paso 6 "HACER DESPUÉS", sin excepción — ninguno de esos ítems cambia si un usuario diario puede o no usar el sistema hoy.

---

# Backlog del producto futuro

**Inversiones**: `FinancialAccount.Type=Investment`, `InvestmentAccount`, movimientos internos de inversión.
**Brokers**: importador PPI, sin trabajo previo.
**Cripto**: importador de exchanges (Binance), sin trabajo previo.
**Wallets**: Mercado Pago, Lemon — sin importador, sin lugar en el enum actual.
**Multi-banco**: cualquier banco además de BBVA.
**Mejoras de dominio**: `Institution`/`ProductType`/`Identifier`, reconciliación `Counterparty`/`FinancialAccount` (`OwnAccount`/`OwnCard`/`Investment`), colapsar `CounterpartyType`, sacar `MovementType` del formulario.
**IA avanzada / Automatizaciones**: motor de sugerencias con reglas/embeddings/LLM, detección heurística de pago de resumen coincidente.
**UX**: Épica U5-U7, Épica N, nuestra M1/M3/M9 (diagnósticos de importación), M6-M7 (asignación manual de cuenta, futuro de la columna Alerta).
**Performance/arquitectura**: Épica UI (extracción de CSS/JS compartido, más allá del fix de XSS ya incluido en Etapa 0).

No implementé nada, no modifiqué ningún archivo del repositorio.
