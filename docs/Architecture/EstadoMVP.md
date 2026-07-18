# Estado del MVP — documento único

> Este documento reemplaza a `AuditoriaMVP.md`, `RoadmapMVP.md` y `MVPDefinitivo.md` (generados en rondas anteriores de esta misma revisión) como única fuente de verdad sobre el estado del MVP. Los tres quedan superados por este — ver punto 5.

---

## 1. El MVP, reconstruido

Importar todos los movimientos (banco, débito, crédito) sin duplicarse ni perderse → reconciliar automáticamente lo que se pueda (débito↔caja de ahorro) → clasificar (categoría, medio de pago, cuánto, dónde) → visualizar → que esos datos sean confiables para que un MCP razone sobre hábitos financieros. Nada de múltiples bancos, billeteras, brokers, exchanges, cripto, ni ningún modelo de dominio más general que el que ya existe.

---

## 2. Estado real, funcionalidad por funcionalidad (evidencia de código)

| Funcionalidad del MVP | Estado | Evidencia |
|---|---|---|
| Importar banco (Caja de Ahorro) | ✅ Terminada | Patrón de archivo real reconocido; desfasaje de fila corregido (M2). |
| Importar tarjeta de débito | ✅ Terminada | Enriquecimiento contra Caja de Ahorro funcionando; cuenta financiera asignada automáticamente (M5). |
| Importar tarjeta de crédito (Visa/Mastercard PDF) | ✅ Bug corregido | `Currency="USD"` con `Amount` en pesos — causa raíz confirmada en `CurrencyDetector.TryExtractUsdAmount` (segundo intento permisivo que tomaba el total en pesos como si fuera el monto en USD), corregida en `BbvaTransactionLineParser.cs`/`CurrencyDetector.cs`. Mastercard no tenía este bug. 3 tests de regresión agregados. |
| Reconciliar automáticamente (débito↔banco) | ✅ Terminada | Simulado contra 95 operaciones reales de débito y 108 de banco: 91 enriquecidas, 4 ambiguas (correctamente conservador), 0 sin match. |
| Clasificar gastos (categoría, medio, contraparte) | ✅ Terminada | `movements.html`, motor de sugerencias, aceptar sugerencia en 1 clic — verificado en código, ya implementado. |
| Corregir manualmente sin fricción | ✅ Terminada | Creación de contraparte sin salir de pantalla — verificado en código (`createCounterpartyInline`). |
| Saber cuánto/dónde/con qué medio/categoría | ✅ Terminada | `FinancialMetricsService` + 4 herramientas MCP, ya funcionando sobre `ClassifiedMovement`. |
| Visualizar (dashboard) | ⚠ Bug de seguridad | Funciona, pero `dashboard.html` es la única de 5 pantallas sin función de escape — un nombre de categoría puede inyectar HTML sin escapar. |
| Detectar movimientos sin clasificar | ❌ No empezada | El dato ya es calculable (filtrar por `Status == null`); ningún script completa el badge que ya existe en el HTML. |
| Idempotencia real de tarjeta PDF | ✅ Bug corregido | `ImportFileProcessingSink` ahora consulta `ExternalId` existentes contra la base antes de insertar (mismo patrón que `BbvaBankStatementImporter.PersistAsync`). Reimportar ya no lanza excepción ni pierde las transacciones nuevas de un archivo parcialmente repetido. 2 tests con EF Core InMemory agregados. |
| MCP con datos confiables | 🟡 Parcial | Las 4 herramientas ya existen; su confiabilidad depende de cerrar el bug de moneda primero — no hace falta backend nuevo para las preguntas más simples. |

---

## 3. Bugs bloqueantes, en orden de resolución

**1. Moneda/importe en tarjeta de crédito — ✅ CORREGIDO.** El mecanismo confirmado por lectura directa del código, al implementar el fix, resultó distinto al diseñado en las rondas de análisis previas: `TransactionNormalizer`/`ImportValueParser.DetectCurrencyFromText`/`CurrencyHint` no estaban involucrados (`CurrencyHint` ya se poblaba correctamente desde `PdfStatementParserBase.cs`). El bug real vivía enteramente en `CurrencyDetector.TryExtractUsdAmount`: un segundo intento que, cuando "USD" no tenía un monto pegado al lado, tomaba el primer monto disponible en el resto de la línea — en la práctica, el total en pesos — y lo devolvía como si fuera el monto en dólares. Corregido eliminando ese segundo intento y hardeando `BbvaTransactionLineParser` para que, sin un monto en USD confiable, registre la transacción en ARS (moneda e importe cambian juntos). `MastercardTransactionLineParser` no tenía este bug. 3 tests de regresión agregados en `tests/FinancialSystem.Infrastructure.Tests/Parsing/BbvaTransactionLineParserTests.cs`.

**Hallazgo relacionado, fuera de alcance de este fix, movido al backlog**: `CurrencyDetector.Detect` (`\bUSD\b`) no reconoce "USD" cuando aparece pegado sin espacio a la palabra anterior (ej. `...8GUSD`, patrón real documentado en el comentario del parser) — una transacción en dólares así formateada queda silenciosamente clasificada como ARS. Es un bug distinto (misdetección, no inconsistencia Currency/Amount) y no se tocó en este PR.

**2. Idempotencia de tarjeta PDF — ✅ CORREGIDO.** Confirmado por lectura directa: el sink solo dedupeaba dentro del mismo archivo (`HashSet` en memoria), sin consultar la base — reimportar chocaba contra el índice único de `Transactions.ExternalId` sin manejo, y al ser `SaveChangesAsync` una sola transacción implícita, se perdían también las transacciones nuevas del mismo archivo (no "se duplicaba" — fallaba toda la corrida). Corregido con la misma consulta batch que ya usa `BbvaBankStatementImporter.PersistAsync`. 2 tests con EF Core InMemory agregados en `tests/FinancialSystem.Infrastructure.Tests/Imports/ImportFileProcessingSinkIdempotencyTests.cs`.

**3. XSS en dashboard.html.** Severidad acotada (self-XSS en app de un solo usuario), fix barato — siguiente tarea.

Ningún otro bug bloqueante encontrado con evidencia de código en esta revisión.

---

## 4. Backlog — producto futuro (nada se pierde, solo se saca del camino)

**Modelo de cuentas avanzado**: `Institution`/`ProductType`/`Identifier`, tarjetas de crédito como `FinancialAccount` distinta, `Money` como Value Object.
**Parsing**: `CurrencyDetector.Detect` no reconoce "USD" pegado sin espacio a la palabra anterior (ej. `...8GUSD`) — transacción en dólares clasificada silenciosamente como ARS. Distinto del bug ya corregido arriba.
**Inversiones**: `FinancialAccount.Type=Investment`, `InvestmentAccount`.
**Brokers / Cripto / Wallets / Multi-banco**: sin importador ni modelo hoy, ninguno con trabajo previo real.
**Reconciliación de dominio**: superposición `Counterparty.OwnAccount/OwnCard/Investment` vs. `FinancialAccount` — decisión de una línea pendiente (ver ADR-001), no bloquea nada.
**Motor de sugerencias avanzado / IA de matching**: el actual ya funciona sin esto.
**UX de fricción**: valores por defecto en el formulario (Épica N), extracción de CSS/JS compartido entre páginas (salvo el fix de XSS, que sí es bloqueante), diagnósticos de importación más explicativos (nuestra Épica M: M1/M3/M9), asignación manual de cuenta cuando la automática no alcanza (M6), futuro de la columna "Alerta" (M7).

---

## 5. Documentación a actualizar

- **`docs/RoadMaps/FinancialMcp-vNext.md`** → Actualizar: es el único documento que debería quedar como fuente de verdad del roadmap; hoy marca la Épica J como no iniciada cuando ya tiene código (M5). Absorber el contenido de este documento y reemplazar las secciones 7-9.
- **`AuditoriaMVP.md`, `RoadmapMVP.md`, `MVPDefinitivo.md`** (generados en rondas anteriores) → Archivar/descartar, superados por este documento.
- **`docs/UX/ClassificationUX.md`** → Actualizar §1.2 (describe un `<select>` editable de cuenta que ya es de solo lectura) y §1.5 (dice que "Cuentas" no existe, cuando `accounts.html` ya existe).
- **`docs/Architecture/analisisproximaepicausabilidad.md`, `analisisnavegacion.md`, `auditoriasemanticamovimientosreales.md`** → Archivar, contenido absorbido por otros documentos ya implementados.
- **`docs/Architecture/reconstruccionenrichasync.md`** → Eliminar, artefacto de diagnóstico puntual, no documentación de diseño.
- **`docs/Architecture/analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md`, `redisenoflujofuncional.md`** → Unificar en un solo documento (misma conclusión repetida tres veces), insumo de la futura Épica N — no se toca hasta que esa épica se retome.
- **Series `PRS*`/`PRU*` de `docs/Architecture/`** → Archivar (S1-S11, U1-U4 confirmados implementados); extraer como ítems sueltos de backlog las dos recomendaciones sin confirmar (S12: bug de confianza en sugerencia desactivada; UI1-UI5: extracción de CSS/JS, salvo XSS).

---

## 6. Orden exacto de trabajo hasta MVP estable

1. ~~**Fix de moneda/importe en tarjeta de crédito** (bug #1)~~ — ✅ Hecho.
2. ~~**Idempotencia de tarjeta PDF** (bug #2)~~ — ✅ Hecho.
3. **Fix de XSS en dashboard** (bug #3) — siguiente tarea.
4. **Badge de pendientes de clasificar** — única funcionalidad del MVP todavía no empezada; el dato ya es calculable, es la tarea más chica de las cuatro.
5. **Actualizar `FinancialMcp-vNext.md`** para que vuelva a ser la única fuente de verdad, incorporando el estado real confirmado en este documento.

Con estos 4 puntos técnicos cerrados, el MVP tal como fue definido queda completo — todo lo demás documentado en esta conversación es backlog de producto futuro, no camino a un MVP usable.

No implementé nada, no modifiqué ningún archivo del repositorio.
