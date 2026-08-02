# FinancialMcp — Plan de implementación priorizado (pre-v1.0)

Basado en `FinancialMcp-Informe-PreV1.md`. Este documento es solo planificación — **no se modificó ningún archivo del repositorio**.

Criterio de priorización, en este orden estricto: **(1) evitar pérdida de datos → (2) seguridad → (3) consistencia → (4) mantenimiento → (5) nuevas funcionalidades.**

Convención de numeración de patches: `PATCH-NNN`, secuencial global, agrupado por Epic. Cada patch está pensado para ser un PR chico, revisable en una sola pasada, sin dejar el sistema en un estado intermedio roto (build + tests en verde al final de cada patch).

---

## 1. Epics — resumen priorizado

| Epic | Nombre | Prioridad | Impacto | Riesgo de no hacerlo | Tiempo estimado | Dependencias | Orden recomendado |
|---|---|---|---|---|---|---|---|
| **P** | Integridad de datos e importación | P0 | Alto — es la fuente de verdad financiera del sistema | **Alto** — pérdida/duplicación silenciosa de datos ya activa y confirmada (Visa/Mastercard, `ExternalId` posicional) | 6-8 días | Ninguna | **1º** |
| **Q** | Seguridad y endurecimiento | P0 | Crítico — bloqueante para cualquier uso fuera de `localhost` | **Crítico** — hoy cualquiera en la red puede leer/alterar/borrar todos los datos | 6-8 días | Ninguna (puede correr en paralelo a P si hay 2 personas) | **2º** (o en paralelo con 1º) |
| **R** | Consistencia y confianza del producto | P1 | Alto — credibilidad de las métricas, evita doble conteo y decisiones sobre datos falsos | Medio-alto — ya se materializó una vez (scope creep de Planificación Mensual sobre un roadmap desactualizado) | 6-7 días | R-1 (indicador de cobertura) se beneficia de que P ya esté cerrada (datos confiables antes de mostrar %) | 3º |
| **T** | Consolidación documental | P1 | Medio-alto — barato, evita que futuras sesiones (humanas o de IA) trabajen sobre información falsa | Medio, creciente con el tiempo | 4-5 días | Ninguna dura; conviene documentar el estado post P/Q/R para no reescribir dos veces | 4º (pero T-1/T-2/T-3 pueden arrancar en paralelo desde el día 1, son solo archivado) |
| **W** | Cobertura de tests en módulos críticos | P1-P2 | Alto a mediano plazo — protege Auditoría/Investigaciones, las piezas más nuevas y menos maduras | Medio-alto acumulativo | 5 días | Ninguna dura; W-3 (tests de `AuditReportService`) debe preceder a V-1 | Puede arrancar en paralelo desde el día 1; W-3 antes que V-1 |
| **V** | Deuda de arquitectura y performance | P2 | Medio — mantenibilidad y carga de producción, no user-facing urgente | Medio, la recomputación triple ya afecta latencia de auditoría | 7-9 días | V-1 depende de W-3 (tests primero) | 5º |
| **X** | Consolidación de frontend (UI compartida) | P2 | Medio — reduce deuda creciente, no resuelve ningún bug activo | Medio, crece con cada pantalla nueva | 7-9 días | Ninguna dura | 6º |
| **Y** | Limpieza de código muerto | P2 | Bajo-medio — higiene, quick wins de bajo riesgo | Bajo | 2-3 días | Ninguna | Puede intercalarse en cualquier momento desde el día 1 |
| **Z** | Nuevas funcionalidades (post-v1.0) | P3 | Alto a largo plazo, pero fuera del alcance de "cerrar v1.0" | Bajo en el corto plazo | No estimado en este plan | Depende de que P, Q y R estén cerradas | Último — fuera del ciclo de estabilización |

**Lectura del orden:** P y Q son las únicas dos épicas que bloquean por sí solas cualquier despliegue serio — deberían arrancar el mismo día, en paralelo si hay más de una persona disponible. T-1/T-2/T-3 (archivado puro, sin riesgo) e Y (limpieza de bajo riesgo) son quick wins que se pueden intercalar sin esperar a nada. Z queda explícitamente fuera del ciclo de estabilización: construir funcionalidad nueva antes de cerrar P/Q/R sería repetir el mismo patrón de scope creep que ya generó el desvío de Planificación Mensual.

---

## 2. Epic P — Integridad de datos e importación

*Evitar pérdida de datos financieros reales. Máxima prioridad porque el propio README declara "banco y tarjeta son la fuente de verdad" — un bug de idempotencia o de ruteo de parser rompe esa premisa fundacional.*

| Patch | Título | Alcance | Descripción | Tamaño | Depende de |
|---|---|---|---|---|---|
| **PATCH-001** | Mecanismo explícito de prioridad en `IFileParserFactory` | `Application/Imports/FileParserFactory.cs` (o donde viva la interfaz), `Infrastructure/DependencyInjection.cs` | Reemplazar "primer `CanHandle=true` gana por orden de registro en DI" por un criterio explícito (p. ej. método `Specificity()`/`Priority` en cada parser, o resolución determinística documentada). Sin cambiar todavía los fingerprints. | S | — |
| **PATCH-002** | Endurecer fingerprint de `BbvaVisaStatementParser` | `Application/Parsing/Bbva/Visa/BbvaVisaStatementParser.cs`, test existente `KnownLimitation_PdfContainingBothBbvaAndMastercardText...` | Ajustar el regex `\bBBVA\b` a un patrón específico de Visa que no matchee un extracto Mastercard real. Actualizar el test que hoy documenta la limitación para que verifique la resolución correcta (renombrar de `KnownLimitation_...` a `Resolves...`). | S | PATCH-001 |
| **PATCH-003** | Nuevo cálculo de `ExternalId` por contenido para `BankStatement` | `Infrastructure/Imports/BankStatements/BbvaBankStatementImporter.cs`, `BbvaBankStatementParser.cs` | Introducir un cálculo de `ExternalId` basado en contenido (mismo patrón que `SheetParserHelpers.BuildTransactionExternalId` ya usa para `Transaction`), sin tocar todavía los datos existentes ni el índice único. | M | — |
| **PATCH-004** | Backfill/migración de `ExternalId` existentes de `BankStatement` | Nueva migración EF + script de verificación de duplicados previos al cambio de índice | Recalcular `ExternalId` para filas ya persistidas con el nuevo algoritmo de PATCH-003, verificar que no se generan colisiones inesperadas antes de aplicar el índice único sobre el nuevo esquema. | M | PATCH-003 |
| **PATCH-005** | Transaccionalidad única para `ImportBatch` + datos financieros | `Application/Imports/FileImportRouter.cs` | Envolver el `SaveChangesAsync` de los datos importados y el de `ImportBatch`/`ImportBatchLine` en una única transacción de base de datos (o, si se decide mantener el gap por alguna razón de diseño, documentarlo explícitamente en el código y en ADR-005 en vez de dejarlo implícito). | S | — |
| **PATCH-006** | Diagnóstico estructurado para columnas faltantes en CSV | `Infrastructure/Imports/Parsers/CsvFileParser.cs` | Reemplazar el `InvalidOperationException` crudo por un `FileParseResult` con diagnóstico estructurado, consistente con el resto del pipeline (filas inválidas, archivo vacío, etc.). | XS | — |
| **PATCH-007** | Fallback de encoding Latin-1/Windows-1252 en CSV | `Infrastructure/Imports/Parsers/CsvFileParser.cs` | Detección heurística de encoding cuando no hay BOM UTF-8 (p. ej. reintento con Latin-1 si aparecen caracteres de reemplazo tras el primer intento), con test de caso real. | S | — |
| **PATCH-008** | Trazabilidad de coincidencias ambiguas en enriquecimiento de débito | `Infrastructure/Imports/BbvaDebitCardEnrichmentHandler.cs`, `imports.html` | Exponer el conteo de "coincidencias ambiguas descartadas" en el resultado de la importación (hoy es un contador interno invisible) y mostrarlo en el historial de `imports.html`. | S | — |

**Total Epic P: 8 patches, ~6-8 días.**

---

## 3. Epic Q — Seguridad y endurecimiento

*Bloqueante absoluto. Ningún patch de esta epic requiere entender el dominio financiero — son cambios de infraestructura de la API, lo que los hace paralelizables con Epic P sin conflicto de archivos.*

| Patch | Título | Alcance | Descripción | Tamaño | Depende de |
|---|---|---|---|---|---|
| **PATCH-009** | Mecanismo de autenticación en `FinancialMcp.Api` | `Program.cs`, nueva configuración (API key o cookie de sesión single-user) | Agregar `AddAuthentication`/`UseAuthentication`/`UseAuthorization` al host. **No** aplicar todavía `[Authorize]` a ningún endpoint — solo dejar el mecanismo listo, para poder revisar el patch de infraestructura por separado de su aplicación. | M | — |
| **PATCH-010** | Proteger endpoints de Importaciones y Movimientos | `Endpoints/ImportBatchEndpoints.cs`, `Endpoints/MovementEndpoints.cs` (o equivalentes) | Aplicar `.RequireAuthorization()` a los endpoints más sensibles primero: subida de archivos y clasificación/escritura de movimientos. | S | PATCH-009 |
| **PATCH-011** | Proteger endpoints de catálogos (Accounts/Categories/Counterparties) | `FinancialAccountEndpoints.cs`, `CategoryEndpoints.cs`, `CounterpartyEndpoints.cs` | Aplicar `.RequireAuthorization()` a create/update/delete de los 3 catálogos. | S | PATCH-009 |
| **PATCH-012** | Proteger endpoints de Planning/Audit/Investigations | `PlanningEndpoints.cs`, endpoints de audit/investigations si existen vía Api | Aplicar `.RequireAuthorization()` al resto de los endpoints de escritura. Decidir explícitamente (y documentar) si los GET de solo lectura quedan abiertos o también se protegen. | S | PATCH-009 |
| **PATCH-013** | Sacar credenciales de Postgres de los `appsettings.json` versionados | `appsettings.json`/`appsettings.Development.json` de `FinancialMcp.Api`, `FinancialSystem.Worker`, `FinancialSystem.McpServer` | Vaciar el `ConnectionStrings:Postgres` en los archivos versionados y resolverlo por variable de entorno/user-secrets, igual que ya se hace con `OpenAI:ApiKey`. Documentar en el README cómo configurar el entorno local. | XS | — |
| **PATCH-014** | Límite de tamaño y validación de contenido en `POST /api/imports` | `Endpoints/ImportBatchEndpoints.cs`, `Program.cs` (Kestrel/`FormOptions`) | Agregar `RequestSizeLimit`/`MaxRequestBodySize` explícito y una validación mínima de magic bytes vs. extensión declarada antes de procesar el archivo. | S | — |
| **PATCH-015** | Middleware global de manejo de excepciones | `Program.cs` de `FinancialMcp.Api` | Agregar `UseExceptionHandler`/`AddProblemDetails` para que toda excepción no controlada devuelva una respuesta consistente sin filtrar detalles internos. | S | — |
| **PATCH-016** | Validación estructurada — módulo piloto | `Endpoints/CategoryEndpoints.cs` + librería de validación elegida (FluentValidation u otra) | Introducir el mecanismo de validación estructurada en un solo módulo como plantilla, reemplazando los `if (string.IsNullOrWhiteSpace(...))` dispersos. Deja el patrón listo para replicar en los módulos restantes en un patch posterior (fuera del alcance de v1.0 si el tiempo aprieta). | M | — |
| **PATCH-017** | Consentimiento explícito para envío de datos a OpenAI | `hosts/FinancialSystem.Worker/Services/TransactionInsightsWorker.cs`, `appsettings.json` | Apagar el proveedor OpenAI por default (`InsightsWorker:Provider` = `Ollama` o `None`), requerir opt-in explícito documentado, y dejar constancia en el README/UserGuide de qué datos salen del sistema si se habilita. | XS | — |
| **PATCH-018** | Quitar ruta de filesystem personal filtrada | `hosts/FinancialSystem.Worker/appsettings.Development.json` | Reemplazar la ruta hardcodeada de un desarrollador por un placeholder genérico. | XS | — |

**Total Epic Q: 10 patches, ~6-8 días.**

---

## 4. Epic R — Consistencia y confianza del producto

*Alinea lo que el sistema muestra/documenta con lo que realmente hace — previene que el usuario confíe en números incompletos o que futuras sesiones de trabajo hereden decisiones sobre una base falsa.*

| Patch | Título | Alcance | Descripción | Tamaño | Depende de |
|---|---|---|---|---|---|
| **PATCH-019** | Endpoint de cobertura de clasificación | `Infrastructure/Metrics/FinancialMetricsService.cs`, nuevo endpoint en `Api` | Calcular y exponer "% de movimientos clasificados vs. totales" para un período dado. | S | Idealmente después de Epic P (datos confiables) |
| **PATCH-020** | Indicador visual de cobertura en el Dashboard | `dashboard.html` | Consumir el endpoint de PATCH-019 y mostrar el porcentaje con alguna señal visual clara (no solo un número) cuando la cobertura es baja. | S | PATCH-019 |
| **PATCH-021** | Actualizar `ProcessingSource` al reclasificar | `Application/Review/Commands/ClassifyMovementHandler.cs` | Al reclasificar un movimiento ya clasificado, actualizar `ProcessingSource` en vez de dejar el valor de la clasificación original. Incluir test que cubra el caso de reclasificación. | XS | — |
| **PATCH-022** | Precarga de `FinancialImpact.DebtPayment` para contraparte `OwnCard` | `movements.html`, endpoint de defaults de contraparte si aplica | Cuando el usuario selecciona una `Counterparty` de tipo `OwnCard`, precargar `FinancialImpact = DebtPayment` con indicación visual explícita en el formulario (cierre de ADR-003). | S | — |
| **PATCH-023** | Corregir comentario engañoso de `MatchScore`/`AmountDelta` | `Domain/Review/ClassifiedMovement.cs` | Corregir el doc-comment para reflejar que son residuos del motor de matching legado retirado, o retirar las columnas si se confirma que no tienen ningún productor ni consumidor activo (requiere confirmar antes en el propio patch). | XS | — |
| **PATCH-024** | Sincronizar `ToolRegistry.Tools` con las tools MCP reales | `hosts/FinancialSystem.McpServer/ToolRegistry.cs` | Agregar las 5 entradas faltantes (`FinancialTools.GetMonthlySummary/GetExpensesByCategory/GetMonthlyTrend/CompareWithPreviousMonth`, `AuditDatabaseTools.AuditDatabase`) al catálogo manual. | XS | — |

**Total Epic R: 6 patches, ~6-7 días** (incluye tiempo de diseño de UX para PATCH-020/022, no solo código).

---

## 5. Epic T — Consolidación documental

*Bajo riesgo técnico (son archivos `.md`), alto apalancamiento: evita que la próxima sesión de trabajo —humana o de IA— repita el patrón ya observado de construir sobre un documento "fuente de verdad" que no lo era.*

| Patch | Título | Alcance | Descripción | Tamaño |
|---|---|---|---|---|
| **PATCH-025** | Archivar el cuarteto MVP superado | Mover `AuditoriaMVP.md`, `RoadmapMVP.md`, `MVPDefinitivo.md`, `reconstruccionenrichasync.md` a `docs/Archive/` | Ya declarados superados por `EstadoMVP.md` dentro del propio repositorio. | XS |
| **PATCH-026** | Archivar serie PRS (motor de sugerencias, terminado) | Mover `PRS1/6/8/11/12*.md` a `docs/Archive/` | Verificado en código que la cadena completa se implementó. | XS |
| **PATCH-027** | Archivar documentos de navegación/usabilidad ya implementados | Mover `analisisnavegacion.md`, `analisisproximaepicausabilidad.md`, `auditoriasemanticamovimientosreales.md` a `docs/Archive/` | Contenido ya implementado o absorbido por `EpicaO-ImportacionManual.md`. | XS |
| **PATCH-028** | Fusionar el trío de simplificación del modelo de clasificación | Nuevo doc único a partir de `analisissimplificacionmodelodominio.md` + `auditoriaflujoclasificacion.md` + `redisenoflujofuncional.md`; archivar los tres originales | Insumo directo y consolidado para la futura Épica N. | S |
| **PATCH-029** | Actualizar `docs/RoadMaps/FinancialMcp-vNext.md` | Editar el documento | Marcar Épica J como sustancialmente implementada; incorporar referencias a Épicas S/U/UI, Centro de Auditoría y Planificación Mensual. | S |
| **PATCH-030** | Corregir ADR-007 y ADR-005 | `docs/Architecture/Decisions/ADR-007-McpMemory.md`, `docs/Decisions/ADR-005-import-batch-trazabilidad.md` | Reflejar el estado real de implementación verificado en el informe. | XS |
| **PATCH-031** | Actualizar catálogo de tools en documentación de usuario | `docs/Architecture/McpServerSetup.md`, `docs/UserGuide/McpUserGuide.md` | Reflejar las 9 clases de tools reales (agregar `RegistryTools`, `AuditDatabaseTools`, tools de Ollama). | XS |
| **PATCH-032** | Corregir `docs/UX/ClassificationUX.md` §1.2 | Editar el documento | La cuenta financiera ya no es un `<select>` editable, es un badge de solo lectura — actualizar la descripción. | XS |
| **PATCH-033** | Documento de diseño del Centro de Auditoría | Nuevo `docs/Architecture/CentroDeAuditoria.md` (o `docs/Epics/`) | La brecha documental más grande detectada: funcionalidad completa sin ningún doc de diseño. | M |
| **PATCH-034** | Resolver colisión de nombre "Épica M" y numeración de épicas | Renombrar `docs/Architecture/EpicaMImportWorkflow.md`; nota de numeración en `vNext.md` | Deja explícito qué numeración manda (I-O vs. S/U/UI vs. Planificación Mensual). | XS |
| **PATCH-035** | ADR nuevo: resolución de ADR-001 vs. evidencia de `MovementType` | Nuevo `docs/Decisions/ADR-008-...md` | Decisión formal, con los datos ya recolectados en el trío fusionado (PATCH-028), sobre si el modelo de 4 dimensiones se mantiene o se ajusta. | S |

**Total Epic T: 11 patches, ~4-5 días** (mayormente edición de documentos, PATCH-025/026/027 son casi mecánicos).

---

## 6. Epic W — Cobertura de tests en módulos críticos

*Antes de tocar `AuditReportService` a fondo (Epic V) conviene tener una red de seguridad. Los módulos elegidos son, según el informe, los más nuevos y con cero cobertura hoy.*

| Patch | Título | Alcance | Descripción | Tamaño |
|---|---|---|---|---|
| **PATCH-036** | Tests de `SuspicionDetector` | `tests/.../Review/SuspicionDetectorTests.cs` (nuevo) | Cubrir detección de duplicados (componentes conexos) y de splits (combinatoria acotada a `MaxSplitPartsConsidered`), casos límite de tolerancia. | M |
| **PATCH-037** | Tests de `ReviewEngine`/`MovementLoader` | `tests/.../Review/ReviewEngineTests.cs`, `MovementLoaderTests.cs` (nuevos) | Cubrir en particular el caso de inversión de signo que el propio código advierte en comentario. | M |
| **PATCH-038** | Tests de `AuditReportService` | `tests/.../Audit/AuditReportServiceTests.cs` (nuevo) | Cubrir los 4 métodos públicos principales antes de refactorizarlos en Epic V — sirve como red de seguridad para PATCH-042. | M |
| **PATCH-039** | Tests de handlers de `Investigations` | `tests/.../Investigations/*HandlerTests.cs` (nuevos) | `CreateInvestigationHandler`, `LinkMovementToInvestigationHandler`, `AddInvestigationFindingHandler`, `UpdateInvestigationStatusHandler`. | M |
| **PATCH-040** | Tests de handlers de Planning restantes | `tests/.../Planning/*HandlerTests.cs` (nuevos) | Todos salvo `CopyPlanningMonthHandler` (ya cubierto). | S |

**Total Epic W: 5 patches, ~5 días.**

---

## 7. Epic V — Deuda de arquitectura y performance

| Patch | Título | Alcance | Descripción | Tamaño | Depende de |
|---|---|---|---|---|---|
| **PATCH-041** | Eliminar recomputación triple en `AuditReportService` | `Infrastructure/Audit/AuditReportService.cs` | Calcular movimientos/sugerencias una sola vez en `BuildFullAuditReportAsync` y reusar el resultado en los sub-métodos, en vez de dejar que cada uno vuelva a consultar. | M | PATCH-038 (tests primero) |
| **PATCH-042** | Extraer `ToSourceEntityType` compartido | `Infrastructure/Suggestions/ClassificationSuggestionService.cs`, `Infrastructure/Audit/AuditReportService.cs`, nuevo helper | Eliminar la duplicación literal ya reconocida en comentario por el propio código. | XS | — |
| **PATCH-043** | Unificar `Normalize()` de texto | `ClassificationSuggestionService.cs`, `PlanningMatchSuggestionService.cs` | Extraer a un único helper compartido; correr los tests existentes de ambos módulos sin cambios de comportamiento. | S | — |
| **PATCH-044** | Lookup en lote para `AskInvestigation` | `Infrastructure/Movements/MovementLookupService.cs`, `hosts/FinancialSystem.McpServer/Tools/InvestigationTools.cs` | Agregar `GetManyBySourceAsync` y usarlo en `BuildInvestigationContextAsync` en vez del `foreach` secuencial. | S | — |
| **PATCH-045** | Decisión formal MediatR/CQRS | `docs/Architecture/Architecture.md` + posible rename de clases si se decide formalizar "Command+Handler sin mediador" como patrón oficial | Documentar explícitamente la decisión para que no siga siendo engañosa. | S | — |
| **PATCH-046 a 049** | Homogeneizar CQRS en Category/Counterparty/FinancialAccount/Transaction+BankStatement | `Endpoints/CategoryEndpoints.cs` → `Application/Categories/Commands/*`, y análogo para los otros 3 módulos | Un patch por módulo: extraer la lógica de negocio hoy embebida en el Endpoint a un Command+Handler en Application, dejando el Endpoint delgado. | M cada uno | — |
| **PATCH-050** | Revisión de dependencia Application → EF Core/ClosedXML/PdfPig | Análisis + posible reubicación de `Application/Parsing` a `Infrastructure` | Decidir y documentar si se acepta el trade-off actual o se mueven los parsers concretos a Infrastructure, dejando en Application solo contratos. | L | — |

**Total Epic V: 10 patches, ~7-9 días.**

---

## 8. Epic X — Consolidación de frontend

| Patch | Título | Alcance | Descripción | Tamaño |
|---|---|---|---|---|
| **PATCH-051** | Crear `wwwroot/shared/tokens.css` + migrar página piloto | Nuevo archivo + `accounts.html` | Extraer las variables `:root` comunes; validar el patrón en una sola página antes de replicarlo. | S |
| **PATCH-052** | Migrar `counterparties.html` + `imports.html` a `tokens.css` | Ídem | Segundo lote. | S |
| **PATCH-053** | Migrar `movements.html` + `dashboard.html` a `tokens.css` | Ídem | Tercer lote (las 2 páginas más grandes, separadas para reducir el diff). | S |
| **PATCH-054** | Migrar `audit.html` + `planning.html` a `tokens.css` | Ídem | Último lote. | S |
| **PATCH-055** | Crear `wwwroot/shared/app.js` con helpers comunes | Nuevo archivo | `getJson`, `postJson`, `putJson`, `deleteJson`, `esc`, `showToast`, `handleWriteResponse` — corrigiendo en el mismo movimiento la versión débil de `getJson` en `dashboard.html`. | M |
| **PATCH-056 a 058** | Migrar las 7-8 páginas a `app.js` compartido | Igual agrupación que PATCH-051/052/053/054 | Reemplazar las copias locales por el import del helper compartido. | S cada uno |
| **PATCH-059** | Componente de navegación lateral compartido | `wwwroot/shared/` + las 6 pantallas secundarias | Reemplazar el link único "← Dashboard" por la sidebar completa en todas las pantallas. | M |
| **PATCH-060** | Crear `categories.html` | Nuevo archivo | CRUD de categorías, hoy solo disponible vía API cruda. | M |

**Total Epic X: 10 patches, ~7-9 días.**

---

## 9. Epic Y — Limpieza de código muerto

*Quick wins de bajo riesgo — se pueden intercalar entre cualquiera de las epics anteriores sin planificación especial.*

| Patch | Título | Alcance | Descripción | Tamaño |
|---|---|---|---|---|
| **PATCH-061** | Eliminar `CommonHelper.cs` | `Application/Helpers/CommonHelper.cs` | Sin llamadores; confirmar con build + grep antes de borrar. | XS |
| **PATCH-062** | Eliminar `OpenApiCompatibilityStubs.cs` | `FinancialMcp.Api/OpenApiCompatibilityStubs.cs` | Confirmar que el build no depende de los stubs antes de borrar. | XS |
| **PATCH-063** | Eliminar o conectar `PdfStatementParseOptions.cs` | `Application/Imports/PdfStatementParseOptions.cs` + parsers Visa/Mastercard | Decisión de una línea: si se conecta, requiere registrar `Configure<PdfStatementParseOptions>` y que los parsers la consuman en vez de sus regex hardcodeados (patch más grande); si se elimina, es trivial. | XS o M según decisión |
| **PATCH-064** | Eliminar instrumentación `[DIAG-FA]` restante | `ImportFileProcessingSink.cs`, `PdfStatementParserBase.cs`, `ClassifyMovementHandler.cs` | La de `FinancialMetricsService.cs` ya se cubre en PATCH-041/V; este patch cubre el resto. | S |
| **PATCH-065** | Retirar o formalizar `Category.ParentId` | `Domain/Entities/Category.cs`, `CategoryConfiguration.cs` | Decidir: declarar la relación explícitamente (consistente con el resto del proyecto) o retirar columna+índice hasta que la jerarquía exista de verdad. | S |
| **PATCH-066** | Limpiar migración vacía | `Migrations/20260719142752_UpdateFinancialAccountTable.cs` | Solo si aún no se aplicó en ningún entorno persistente compartido (verificar antes); si ya se aplicó en algún entorno, documentar en vez de remover. | XS |
| **PATCH-067** | Decidir destino de `TransactionInsightsWorker` | `hosts/FinancialSystem.Worker/Services/TransactionInsightsWorker.cs` | Persistir el resultado en una tabla consultable, o marcarlo explícitamente como experimental/spike en código y documentación. | S o M según decisión |

**Total Epic Y: 7 patches, ~2-3 días** (excluyendo la variante grande de PATCH-063/067 si se elige "conectar"/"persistir").

---

## 10. Epic Z — Nuevas funcionalidades (post-v1.0, fuera de este plan)

No se desarrolla en patches en este documento — depende de que P, Q y R estén cerradas. Referencia directa a la sección 13 del informe (`FinancialMcp-Informe-PreV1.md`) para el backlog priorizado (gastos fijos con recordatorio, conciliación pago-de-resumen↔extracto de tarjeta, presupuestos con alertas, reglas de clasificación configurables, abstracción multi-banco, etc.). Retomar recién cuando este plan de estabilización esté cerrado.

---

## 11. Orden de ejecución sugerido (vista de calendario, un solo desarrollador)

```
Semana 1-2   Epic P (PATCH-001 a 008)         + Epic Y intercalado (quick wins)
Semana 2-3   Epic Q (PATCH-009 a 018)         + Epic T-1/T-2/T-3 en paralelo (archivado, sin riesgo)
Semana 3-4   Epic R (PATCH-019 a 024)         + resto de Epic T (PATCH-028 a 035)
Semana 4-5   Epic W (PATCH-036 a 040)         → red de seguridad antes de tocar V-1
Semana 5-6.5 Epic V (PATCH-041 a 050)
Semana 6.5-8 Epic X (PATCH-051 a 060)
             Epic Z — recién después, con roadmap propio
```

Con dos personas, P y Q corren en paralelo desde el día 1 (no comparten archivos), lo que comprime el arranque a ~1 semana antes de converger en R.

---

## 12. Resumen numérico

| Epic | Patches | Tiempo estimado |
|---|---|---|
| P — Integridad de datos | 8 | 6-8 días |
| Q — Seguridad | 10 | 6-8 días |
| R — Consistencia | 6 | 6-7 días |
| T — Documentación | 11 | 4-5 días |
| W — Tests | 5 | 5 días |
| V — Arquitectura/Performance | 10 | 7-9 días |
| X — Frontend | 10 | 7-9 días |
| Y — Código muerto | 7 | 2-3 días |
| **Total (sin Z)** | **67** | **~43-54 días de trabajo de un desarrollador** (≈ 8-11 semanas) |
