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
| Importar tarjeta de crédito (Visa/Mastercard PDF) | ⚠ Bug bloqueante | Funciona, pero puede persistir `Currency="USD"` con `Amount` en pesos — confirmado línea por línea contra el código, no es hipótesis. |
| Reconciliar automáticamente (débito↔banco) | ✅ Terminada | Simulado contra 95 operaciones reales de débito y 108 de banco: 91 enriquecidas, 4 ambiguas (correctamente conservador), 0 sin match. |
| Clasificar gastos (categoría, medio, contraparte) | ✅ Terminada | `movements.html`, motor de sugerencias, aceptar sugerencia en 1 clic — verificado en código, ya implementado. |
| Corregir manualmente sin fricción | ✅ Terminada | Creación de contraparte sin salir de pantalla — verificado en código (`createCounterpartyInline`). |
| Saber cuánto/dónde/con qué medio/categoría | ✅ Terminada | `FinancialMetricsService` + 4 herramientas MCP, ya funcionando sobre `ClassifiedMovement`. |
| Visualizar (dashboard) | ⚠ Bug de seguridad | Funciona, pero `dashboard.html` es la única de 5 pantallas sin función de escape — un nombre de categoría puede inyectar HTML sin escapar. |
| Detectar movimientos sin clasificar | ❌ No empezada | El dato ya es calculable (filtrar por `Status == null`); ningún script completa el badge que ya existe en el HTML. |
| Idempotencia real de tarjeta PDF | ❌ No empezada | Confirmado en código: solo dedupea en memoria dentro del mismo archivo, sin consultar la base antes de insertar. |
| MCP con datos confiables | 🟡 Parcial | Las 4 herramientas ya existen; su confiabilidad depende de cerrar el bug de moneda primero — no hace falta backend nuevo para las preguntas más simples. |

---

## 3. Bugs bloqueantes, en orden de resolución

**1. Moneda/importe en tarjeta de crédito.** Mecanismo confirmado: `BbvaTransactionLineParser` (Visa) decide correctamente con `CurrencyDetector.Detect` (`\bUSD\b`) si debe re-extraer el monto en dólares — pero esa decisión se descarta. `TransactionNormalizer.ResolveCurrency` vuelve a adivinar la moneda desde la descripción ya limpia, con `ImportValueParser.DetectCurrencyFromText` (`Contains("USD")`, sin límite de palabra) — un criterio distinto y más laxo. Cuando "USD" aparece pegado a la palabra anterior sin espacio (patrón real, documentado en el propio comentario del parser), el primer detector no lo ve pero el segundo sí — la transacción queda etiquetada `Currency="USD"` con el monto en pesos.

**Diseño acordado para el fix** (ya revisado en profundidad en rondas anteriores, no se vuelve a discutir): cada parser declara su decisión de moneda con certeza explícita en el mismo paso donde decide el importe; el único lugar que resuelve "no se pudo determinar" queda centralizado y explícito, no una re-detección heurística. Sin Value Object `Money` — no hay ninguna operación real del producto hoy que lo justifique.

**2. Idempotencia de tarjeta PDF.** Reimportar el mismo resumen puede duplicar movimientos o romper la corrida contra el índice único sin manejo.

**3. XSS en dashboard.html.** Severidad acotada (self-XSS en app de un solo usuario), fix barato, se resuelve junto con lo anterior.

Ningún otro bug bloqueante encontrado con evidencia de código en esta revisión.

---

## 4. Backlog — producto futuro (nada se pierde, solo se saca del camino)

**Modelo de cuentas avanzado**: `Institution`/`ProductType`/`Identifier`, tarjetas de crédito como `FinancialAccount` distinta, `Money` como Value Object.
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

1. **Fix de moneda/importe en tarjeta de crédito** (bug #1) — el de mayor impacto, ataca directamente "cuánto gasté".
2. **Idempotencia de tarjeta PDF** (bug #2).
3. **Fix de XSS en dashboard** (bug #3) — chico, se hace junto con el punto anterior.
4. **Badge de pendientes de clasificar** — única funcionalidad del MVP todavía no empezada; el dato ya es calculable, es la tarea más chica de las cuatro.
5. **Actualizar `FinancialMcp-vNext.md`** para que vuelva a ser la única fuente de verdad, incorporando el estado real confirmado en este documento.

Con estos 4 puntos técnicos cerrados, el MVP tal como fue definido queda completo — todo lo demás documentado en esta conversación es backlog de producto futuro, no camino a un MVP usable.

No implementé nada, no modifiqué ningún archivo del repositorio.
