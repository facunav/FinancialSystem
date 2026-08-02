# FinancialMcp — Informe crítico pre-v1.0

**Rol:** Principal Software Architect / Technical Lead evaluando si el sistema está listo para una v1.0.
**Alcance:** lectura completa del repositorio (código, 44 documentos `.md`, migraciones, tests, HTML/JS/CSS). Ningún archivo fue modificado — este es un informe de solo análisis.
**Contexto temporal relevante:** todo el historial de git visible (50 commits) está comprimido entre **2026-07-29 y 2026-08-02** (~5 días). Esto no es un proyecto con años de deuda orgánica — es un proyecto construido a muy alta velocidad, fuertemente asistido por IA, con **7.392 líneas** solo en `docs/Architecture/` (27 documentos de "análisis"/"auditoría") frente a **133 líneas** en el documento de arquitectura formal. Esa proporción es en sí misma un hallazgo: el proyecto genera documentación de diagnóstico más rápido de lo que la consolida, y varios documentos que se autodeclaran "fuente de verdad" ya quedaron desactualizados por código escrito **horas después**, en la misma sesión de trabajo.

Esto condiciona todo el informe: el código es, en general, sorprendentemente disciplinado para su edad (comentarios que explican el *porqué*, no el *qué*; ADRs; tests anclados a bugs reales). El problema de fondo no es "código descuidado" — es **velocidad de cambio superando la capacidad de mantener coherente la verdad documentada**, y **dos vacíos de seguridad bloqueantes** que nadie parece haber evaluado todavía porque el foco estuvo puesto en producto.

---

## 1. Documentación

### 1.1 Documentación obsoleta (recomendación: eliminar o archivar)

| Documento | Por qué está obsoleto |
|---|---|
| `docs/Architecture/AuditoriaMVP.md`, `RoadmapMVP.md`, `MVPDefinitivo.md` | `docs/Architecture/EstadoMVP.md` dice textualmente que **reemplaza a los tres**. Los tres siguen en el repo sin marca de archivado; un lector que abra cualquiera sin conocer `EstadoMVP.md` recibe información contradictoria (p. ej. bugs que uno da por pendientes y otro por corregidos). |
| `docs/Architecture/reconstruccionenrichasync.md` | Es literalmente el output crudo de una sesión de diagnóstico puntual (simulación manual sobre dos Excel concretos), no documentación de diseño. Tres documentos distintos ya piden eliminarlo. |
| `docs/Architecture/analisisnavegacion.md` | Su propuesta (extender la sidebar con Importaciones/Cuentas/Contrapartes) ya está implementada verbatim en `dashboard.html:777-793`. Puramente histórico. |
| `docs/Architecture/analisisproximaepicausabilidad.md` (723 líneas) | Absorbido por `docs/Epics/EpicaO-ImportacionManual.md`, que usa otra numeración y otro contenido. Mantener ambos genera el riesgo, ya señalado internamente, de que nadie sepa cuál manda. |
| `docs/Architecture/auditoriasemanticamovimientosreales.md` | Sus dos hallazgos (desfasaje de fila del XLS, ambigüedad de `MovementType.Payment`) están repetidos en otro documento y ya marcados como corregidos en `EstadoMVP.md`. |
| `docs/Architecture/McpServerSetup.md` | Catálogo de tools desactualizado: dice "17 tools en 7 clases", el código tiene **9 clases** hoy (falta `RegistryTools`, `AuditDatabaseTools` y las tools de Ollama). |

### 1.2 Documentación duplicada (recomendación: fusionar/archivar como serie)

- **Serie PRS (motor de sugerencias):** `PRS1`, `PRS6`, `PRS8`, `PRS11`, `PRS12` — 5 documentos, cada uno el análisis previo al PR siguiente de la misma línea de trabajo. Verificado contra código: la cadena se implementó completa (comentarios `PR-S3` a `PR-S12` presentes en `ClassificationSuggestionService.cs`). Es bitácora de una funcionalidad **terminada** — archivar como conjunto, no mantener como referencia activa.
- **Serie PRU/PRUI (UX de clasificación):** `PRU1`, `PRU3`, `PRU4`, `PRUI1`. A diferencia de la serie PRS, `PRUI1analisisarquitecturaui.md` describe trabajo **no ejecutado** (extraer `wwwroot/shared/`) — ese documento sigue siendo un plan vigente y no debe archivarse; los otros tres sí (ya implementados y verificados en el CSS/JS de `movements.html`).
- **Trío "simplificación del modelo de clasificación":** `analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md`, `redisenoflujofuncional.md` (~300-340 líneas cada uno) llegan, con métodos distintos, a la **misma tesis**: `MovementType` no tiene consumidor real verificado en el código, y el usuario en la práctica responde una sola decisión en vez de cuatro. Dos documentos distintos del propio repositorio (`AuditoriaMVP.md`, `RoadmapMVP.md`) ya piden fusionarlos en uno solo como insumo de la futura Épica N. **Fusionar en un único documento.**
- **Cuarteto de auditoría de producto:** `auditoriaflujoproducto.md`, `auditoriafuncionalcompletaveredicto.md`, `auditoriasemanticamovimientosreales.md`. El segundo se autodeclara síntesis final de los otros — mantenerlo solo a él, archivar el resto.

### 1.3 Documentación que contradice el código (verificado directamente)

- **`docs/UX/ClassificationUX.md`** describe la cuenta financiera en `movements.html` como un `<select>` editable. El código actual (`movements.html:1048-1055`) la renderiza como `<span class="status-badge">` de solo lectura, con un comentario explícito en el código que dice lo contrario del doc.
- **`docs/RoadMaps/FinancialMcp-vNext.md`** — el documento que el propio `README.md` designa como *"fuente de verdad del proyecto"* — está desactualizado en varios puntos concretos:
  - Marca **Épica J** (`FinancialAccount`) como "📋 Planificada" cuando está **sustancialmente implementada** (`FinancialAccount.cs`, 3 migraciones, `accounts.html` funcional). Tres auditorías internas distintas ya señalaron esto sin que se corrigiera.
  - No menciona en absoluto las Épicas S (motor de sugerencias), U/UI (UX de clasificación rápida), el **Centro de Auditoría** (`audit.html`, `AuditReportService`, `MovementAuditDecision` — funcionalidad completa con 10+ commits dedicados) ni el módulo de **Planificación Mensual**. Un documento que se declara "fuente de verdad" no conoce una parte sustancial del sistema que ya está en producción.
- **`docs/Architecture/Architecture.md`** línea 27 sigue describiendo `MovementsQueryService`/`MovementView.Suggestions` como "sin consumidor en `movements.html`" — otro documento del propio repositorio (`PRS11`) ya señaló que esto quedó desactualizado desde PR-S5, y nunca se corrigió.
- **`docs/Architecture/Decisions/ADR-007-McpMemory.md`** declara explícitamente *"ninguna fase de esta ADR está implementada"*. Esto es **falso**: el código implementa completamente Fase 2 (persistencia: `Investigation`, `InvestigationFinding`, `InvestigationReference`), Fase 3 (6 tools MCP CRUD en `InvestigationTools.cs`) y Fase 4 (integración con Ollama vía `AskInvestigation`). Git confirma que el ADR se escribió el mismo día que se implementaron estas fases, y nunca se actualizó — es el hallazgo de discrepancia doc/código más flagrante de todo el informe.
- **`docs/Decisions/ADR-005-import-batch-trazabilidad.md`** dice "Estado: Aceptado (planificación; entidad no implementada)". Falso: `ImportBatch`/`ImportBatchLine` existen, se persisten en cada corrida (`FileImportRouter.PersistImportBatchAsync`) y tienen endpoint de lectura + UI (`imports.html`).
- **`docs/Epics/Epica-PlanificacionMensual.md`** — su propio encabezado dice *"diseño funcional cerrado, sin historias técnicas [...] no define contrato de API, modelo de base de datos ni plan de PRs"*. Es, paradójicamente, **el documento más nuevo del repositorio**, seguido en la misma sesión por 7 commits que implementaron todo eso end-to-end. El documento quedó obsoleto por su propio código, escrito horas después.
- **`docs/UserGuide/McpUserGuide.md`** (el documento activo más reciente de su categoría) ya está un paso atrás: dice "8 clases de tools", el código tiene 9 (falta `AuditDatabaseTools`, agregada después).

### 1.4 Contradicciones entre documentos/épicas

- **Numeración de épicas desincronizada**: `vNext.md` numera hasta la **O**; `RoadmapMVP.md`/`MVPDefinitivo.md`/`PRU1` ya usan Épicas **S**, **U** y **UI** con PRs propios que `vNext.md` no conoce. Planificación Mensual no tiene letra asignada. Nadie arbitra cuál numeración es la vigente.
- **Colisión de nombre "Épica M"**: `docs/Architecture/EpicaMImportWorkflow.md` ("Mejoras al flujo de importación") vs. la Épica M de `vNext.md` ("Cuentas de inversión"). Señalada por al menos 2 documentos internos, nunca resuelta — el archivo sigue llamándose igual.
- **ADR-001 (4 dimensiones fijas, "no se agregan campos nuevos sin un ADR que reemplace este") vs. `analisissimplificacionmodelodominio.md`**: el análisis refuta con evidencia (grep del propio código) el argumento que sostiene al ADR (`MovementType` "sin cambios de contrato" porque nunca se lee, no porque sea necesario). El ADR sigue "Aceptado", el análisis que lo cuestiona con evidencia exhaustiva no tiene ADR de reemplazo — exactamente el proceso que el propio ADR-001 exige para poder modificarse, y que nunca se ejecutó.
- **ADR-002 vs. sus propias notas** (caso correctamente resuelto, mencionado como contraejemplo positivo): el ADR original decía que `group-reconciliation.html` "no se elimina"; una nota interna posterior en el mismo archivo documenta que sí se eliminó (PR-L4/L5). Es el único caso del repositorio donde una reversión de decisión quedó bien documentada dentro del propio ADR.
- **ADR-003** (pago de tarjeta): el modelo de dominio está completo y correcto, pero la UI nunca llegó a guiar la distinción — el propio `vNext.md` reconoce que "el riesgo de doble conteo... sigue sin percibirse resuelto al usar el producto", tres documentos después de identificado.

### 1.5 TODOs y notas temporales

No se encontraron marcadores `TODO`/`FIXME`/`HACK`/`XXX` reales en el código (los "todo"/"todos" detectados por grep son la palabra española, no marcadores). Es una fortaleza real de disciplina. La deuda "temporal no eliminada" está en la documentación, no en el código: `reconstruccionenrichasync.md` es la nota de sesión más clara que debería haberse borrado y no se borró.

### 1.6 Recomendación de consolidación

**Eliminar/archivar en `docs/Archive/`:** `AuditoriaMVP.md`, `RoadmapMVP.md`, `MVPDefinitivo.md`, `reconstruccionenrichasync.md`, `analisisnavegacion.md`, `analisisproximaepicausabilidad.md`, `auditoriasemanticamovimientosreales.md`, la serie `PRS1/6/8/11/12`, y de la serie de auditoría de producto todo salvo `auditoriafuncionalcompletaveredicto.md`.

**Actualizar con prioridad alta** (documentos que se autodeclaran fuente de verdad y mienten sobre el estado real): `docs/RoadMaps/FinancialMcp-vNext.md` (Épica J, más incorporar S/U/UI/Auditoría/Planificación), `docs/Architecture/Decisions/ADR-007-McpMemory.md` (corregir "nada implementado"), `docs/Decisions/ADR-005` (marcar entidad como implementada), `docs/UX/ClassificationUX.md` §1.2, `docs/Architecture/McpServerSetup.md` y `docs/UserGuide/McpUserGuide.md` (catálogo de tools), `docs/Epics/Epica-PlanificacionMensual.md` (encabezado de estado).

**Fusionar:** `analisissimplificacionmodelodominio.md` + `auditoriaflujoclasificacion.md` + `redisenoflujofuncional.md` en un único documento, insumo directo de la Épica N.

**Promover a documentación permanente / crear la que falta:** el **Centro de Auditoría** (`audit.html`, `AuditReportService`, `MovementAuditDecision`, tool MCP `AuditDatabase`) es una funcionalidad completa, con modelo de datos propio, **sin ningún documento de diseño ni épica** — es la brecha documental más grande del repositorio. Formalizar además un ADR nuevo que resuelva la tensión ADR-001 vs. evidencia de `MovementType`, y resolver formalmente la numeración de épicas.

**Mantener sin cambios:** ADR-004, ADR-007 (una vez corregido su encabezado), `docs/Archive/*` (correctamente archivados), `docs/patch/enriquecimiento-tarjeta-debito.md` (registro histórico preciso), `PRUI1analisisarquitecturaui.md` y `analisisentidadcounterparty.md` (planes vigentes, no ejecutados), `EpicaO-ImportacionManual.md`.

---

## 2. Épicas

| Épica | Estado | Implementado (verificado) | No implementado / desviado |
|---|---|---|---|
| **Review & Classification Engine v2** (`docs/Archive/`) | Terminada, mayormente retirada | Motor original construido y luego retirado por completo en PR-L4 por falta de consumidor real. Correctamente archivado. | — |
| **Épica K — UX de clasificación** | Completada | `movements.html` sin matching Legacy; `ClassifyMovementCommand`/Handler. | El propio doc de UX (`ClassificationUX.md`) quedó con una sección desactualizada. |
| **Épica I — Confiabilidad de importación** | Parcial | I1-I5: `ImportBatch`/`ImportBatchLine`, idempotencia de `Transaction` por contenido, endpoints de historial. | **I7 pendiente** (colisión de fingerprint Visa/Mastercard — riesgo activo de pérdida silenciosa de datos). |
| **Épica J — Cuentas Financieras** | Marcada "Planificada", en realidad **sustancialmente implementada** | `FinancialAccount`, 3 migraciones, `accounts.html` CRUD completo. | Falta la asignación automática de cuenta al importar (`FinancialAccountId` queda `null` hasta asignación manual) — problema #4 ya documentado en `vNext.md`. |
| **Épica L — Visibilidad de cobertura** | Parcial | Badge `navPending` en el nav ya funciona. | No se encontró el indicador de "% del período clasificado" en el Dashboard — el hallazgo que la propia auditoría interna califica como el más grave para la confianza del producto. |
| **Épica M — Cuentas de inversión** (`vNext.md`) | No iniciada | — | `InvestmentAccount` no existe en el código. |
| **"Épica M" — Mejoras de importación** (`EpicaMImportWorkflow.md`, colisiona de nombre con la anterior) | Parcial | M2 (desfasaje de fila) y M5 (autoasignación de cuenta débito) hechas. | M1 ("Enriquecidos" en `imports.html`), M8 (limpiar "Confirmado" inalcanzable — `movements.html` sigue teniendo ese label), M9 (diagnóstico de cuenta sin match) — no implementadas. |
| **Épica N — Simplificación del formulario** | No iniciada | — | `movements.html` sigue pidiendo `MovementType` obligatorio. Único caso donde doc y código coinciden exactamente en "no hecho". |
| **Épica O — Importación Manual** | Doc dice "planificada, decisión pendiente"; en realidad **implementada** | `POST /api/imports` funciona, reutiliza el mismo `IFileImportRouter` que el Worker, sin duplicar lógica. `CounterpartyType` ya no obligatorio, `counterparties.html` existe. | Documentación desactualizada, no funcionalidad faltante. |
| **Épica S — Motor de sugerencias** (sin doc de épica formal) | Terminada | Comentarios `PR-S3` a `PR-S12` verificados en `ClassificationSuggestionService.cs`, incluidas las correcciones de bugs S11/S12 con tests. | Dos mejoras estructurales de bajo riesgo recomendadas por el propio equipo (extraer `Normalize`) nunca se hicieron. |
| **Épica U/UI — UX de un clic / arquitectura de UI compartida** | U parcial, UI no iniciada | Chips de confianza, quick-accept. | `wwwroot/shared/` nunca se creó — el problema de duplicación CSS/JS que la propia Épica UI diagnosticó **empeoró** (pasó de 5 a 8 páginas sin extraer nada compartido). |
| **Planificación Mensual** | Terminada (contradice su propio encabezado "sin historias técnicas") | Modelo, CRUD, resumen, copia de mes — todo implementado y con tests fieles a la épica. | **Scope creep documentado**: `PlanningMatchSuggestionService` implementa el matching contra Movimientos que la propia épica (sección 9) marca explícitamente "fuera del MVP". No destructivo (solo lectura) pero es una desviación real de alcance. |
| **Centro de Auditoría** (sin ningún doc de épica) | Implementada y en producción | `audit.html`, `AuditReportService`, `MovementAuditDecision`, 10+ commits dedicados. | Cero documentación de diseño — ninguno de los 44 `.md` la menciona. |

**Contradicciones detectadas entre épicas:** colisión de nombre "Épica M" (dos funcionalidades distintas); numeración I-O vs. S/U/UI sin arbitrar; Planificación Mensual sin letra; `vNext.md` (fuente de verdad declarada) no reconoce 4 piezas ya construidas (J completa, Auditoría, Planificación, S/U).

---

## 3. Código muerto

| Elemento | Ubicación | Acción |
|---|---|---|
| `CommonHelper` (wrapper redundante de `SheetParserHelpers`) | `src/FinancialSystem.Application/Helpers/CommonHelper.cs` | **Eliminar** — sin llamadores; el código real ya usa `SheetParserHelpers` directamente. |
| `OpenApiCompatibilityStubs` (stubs de `Microsoft.AspNetCore.OpenApi` sin usar; el proyecto usa Swashbuckle) | `src/FinancialMcp.Api/OpenApiCompatibilityStubs.cs` | **Eliminar** — resto de un intento abandonado de usar el generador nativo de OpenAPI. |
| `PdfStatementParseOptions` (opciones nunca registradas ni inyectadas; los mismos valores están hardcodeados y duplicados en cada parser) | `src/FinancialSystem.Application/Imports/PdfStatementParseOptions.cs` | **Eliminar o conectar realmente** — abstracción construida a medias. |
| `ToolRegistry.Tools` desincronizado: 5 tools reales (`FinancialTools.*` ×4, `AuditDatabaseTools.AuditDatabase`) invisibles para `ListAvailableTools` y para el contexto de `AskProjectKnowledge`/`AskInvestigation` | `hosts/FinancialSystem.McpServer/ToolRegistry.cs` | **Revisar y corregir con prioridad — es un bug funcional**, no solo prolijidad: el propio asistente IA del sistema no sabe que existen sus propias herramientas de consulta financiera. |
| Instrumentación de diagnóstico `[DIAG-FA]`/similares dejada en producción con `LogWarning`, incluyendo un caso **dentro de un `foreach` (una línea de Warning por transacción importada)** y otro que ejecuta una query completa extra solo para loguear | `ImportFileProcessingSink.cs` (4 bloques), `FinancialMetricsService.cs:27-58`, `PdfStatementParserBase.cs:214-217`, `ClassifyMovementHandler.cs:42-50` | **Eliminar** (o degradar a `LogDebug` fuera del loop). Afecta performance y ensucia logs de producción con nivel Warning en operaciones normales — entrena a ignorar warnings reales. |
| `ToSourceEntityType` duplicado literal | `ClassificationSuggestionService.cs:440-444` y `AuditReportService.cs:541-545` (duplicación reconocida en comentario del propio código) | **Eliminar duplicación** — extraer a un helper compartido. |
| `Normalize()` de texto reimplementado en dos módulos con la misma lógica esencial | `ClassificationSuggestionService.cs` vs. `PlanningMatchSuggestionService.cs` | **Revisar** — duplicación consciente y documentada (para no tocar el módulo de Suggestions), baja prioridad. |
| `Category.ParentId`/`Parent` — columna e índice para una jerarquía de categorías que no existe (doc-comment: "hoy siempre es null") | `Domain/Entities/Category.cs:149-150` | **Revisar** — esquema especulativo en producción; documentar la intención explícitamente o retirar hasta que la feature exista. |
| `TransactionInsightsWorker` — genera insights vía Ollama/OpenAI pero solo los loguea; no persiste, no hay endpoint/tool que los consuma | `hosts/FinancialSystem.Worker/Services/TransactionInsightsWorker.cs` | **Revisar** — funcionalidad huérfana ("solo consumible mirando la consola"); decidir si se persiste o se marca explícitamente como spike. |
| `MatchScore`/`AmountDelta` en `ClassifiedMovement`, con comentario que los describe como "métricas del motor de sugerencias" **actual**, cuando en realidad son residuos del motor de matching legado retirado (nunca escritos por el motor vigente) | `Domain/Review/ClassifiedMovement.cs:102-112` | **Revisar** — el comentario es directamente engañoso; corregirlo o retirar las columnas. |
| Migración completamente vacía (`Up()`/`Down()` sin contenido) | `Migrations/20260719142752_UpdateFinancialAccountTable.cs` | **Opcional** — no rompe nada, pero es ruido de proceso (debió usarse `migrations remove` antes del commit). |

**No se encontraron:** handlers/endpoints/tools MCP huérfanos (todo lo registrado tiene un consumidor real), bloques de código comentado, marcadores `Obsolete`/`Legacy`/`V1`/`V2` en nombres de clases, CSS muerto dentro de cada página individual, ni SQL histórico suelto fuera de las migraciones EF. Buena señal de higiene general.

---

## 4. Arquitectura

### 4.1 Clean Architecture — cumplida parcialmente, con dos violaciones reales

- **`FinancialSystem.Application` referencia el paquete completo `Microsoft.EntityFrameworkCore`** (no solo `.Abstractions`), además de `ClosedXML` y `UglyToad.PdfPig` directamente en su `.csproj`. `IApplicationDbContext` expone `DbSet<T>` tal cual, y 14 handlers usan LINQ-to-Entities (`ToListAsync`, `Include`, etc.) directamente. Esto es el patrón "DbContext como abstracción", pragmático pero **no** es lo que promete el nombre `IApplicationDbContext` (abstracción agnóstica del ORM) ni lo que exige Clean Architecture en sentido estricto: cambiar de EF Core requeriría reescribir Application, no solo Infrastructure. Que además la carpeta de parsers (que depende de `PdfPig`/`ClosedXML`, librerías de I/O concretas) viva dentro de `Application/Parsing` en vez de `Infrastructure` refuerza la mezcla de capas.
- **CQRS aplicado de forma inconsistente entre módulos.** Planning/Investigations/Review/Audit tienen Command + Handler en Application, con Endpoints delgados. Pero **Category, Counterparty, FinancialAccount, Transaction y BankStatement no tienen ninguna capa Application** — su lógica de negocio (normalización de nombre, chequeo de unicidad, cálculo de `SortOrder`, semántica de "desactivar en vez de borrar") vive directamente dentro de `Endpoints.cs`, en la capa Api, operando contra `IApplicationDbContext`. Es una erosión real de la arquitectura declarada: la mitad del sistema respeta el patrón, la otra mitad lo saltea para CRUDs "triviales" que dejaron de serlo (tienen validaciones y reglas reales).

### 4.2 MediatR — ausente pese al nombre del patrón

No hay un solo `using MediatR` ni el paquete en ningún `.csproj`. Los "Commands"/"Handlers" son clases planas con un método `Handle()`, registradas como servicios comunes e invocadas directamente desde Endpoints/Tools — no hay mediador real (`ISender`, `IRequestHandler<T>`). No es un bug funcional (es un estilo "Command+Handler sin mediador" legítimo), pero **es engañoso**: la estructura de carpetas y la descripción del proyecto comunican "usamos MediatR" cuando no es cierto. Antes de v1.0 hay que decidir explícitamente y documentarlo — o se agrega el paquete real, o se declara "CQRS sin mediador" a propósito.

### 4.3 God-classes / tamaño de archivos

Revisando los 10 archivos `.cs` más largos (excluyendo migraciones auto-generadas): **ninguno es un god-class real** en el sentido de mezclar responsabilidades no relacionadas. `AuditReportService.cs` (656 líneas) es el más cuestionable — no por tamaño sino porque mezcla cálculo y formateo de texto en los mismos métodos, lo que además provoca el problema de performance de la sección 6. El resto de los archivos grandes (tools MCP de 400-600 líneas) son extensos por formateo de salida legible, no por acumular responsabilidades.

### 4.4 Duplicación y acoplamientos

- `ToSourceEntityType` duplicado (ver sección 3).
- Estructura casi idéntica entre `BbvaVisaStatementParser` y `BbvaMastercardStatementParser` (aceptable, es duplicación de datos/regex, no de lógica) — pero el mecanismo de desambiguación entre ambos es **implícito y frágil**: depende exclusivamente del orden de registro en DI (`DependencyInjection.cs:70-71`), sin ningún mecanismo explícito de prioridad/especificidad. Agregar un tercer banco, o reordenar el DI por accidente, puede cambiar silenciosamente qué parser gana sin que ningún test lo detecte a nivel de compilación.
- `AuditReportService` depende de `IReviewEngine` + `IMovementsQueryService` + `IClassificationSuggestionService` — acoplamiento deliberado y documentado para compartir exactamente la misma lógica entre MCP y Api. Correcto, no es un red flag.

### 4.5 Decisiones ya tomadas y correctas (no tocar sin evidencia nueva)

Sin repositorio genérico (uso directo de `IApplicationDbContext`), sin bus de eventos, sugerencias de matching efímeras (no persistidas, se recalculan) — las tres son decisiones documentadas y razonables para el tamaño actual del sistema. No se recomienda introducir estas abstracciones "por si acaso".

---

## 5. Base de datos

- **`Category.ParentId`** — columna e índice (`IX_Categories_ParentId`) para una jerarquía que nunca se construyó ("hoy siempre null"). Además, a diferencia de todas las demás relaciones del proyecto (declaradas explícitamente con `HasOne`/`HasForeignKey`), esta depende de convención implícita de EF — inconsistencia de estilo dentro del mismo archivo de configuración.
- **Migración vacía** `20260719142752_UpdateFinancialAccountTable` — no afecta datos, es ruido de historial (ver sección 3).
- **`SourceEntityType` + `SourceId` (Guid) sin FK real** hacia `Transaction`/`BankStatement`, usado en `MovementAuditDecision`, `ClassifiedMovementItem`, `InvestigationReference`. Es una decisión de diseño explícita y bien documentada (evita cascadas indeseadas, permite agregar fuentes sin migrar), pero **sacrifica integridad referencial a nivel de base de datos** — nada impide en el motor de PostgreSQL que un `SourceId` apunte a una fila que ya no existe. Es un trade-off aceptable para un sistema personal, pero debería quedar explícito como riesgo aceptado, no implícito.
- **Índices vs. filtros reales**: en general **por encima del promedio** para un proyecto de este tamaño — `ClassifiedMovement` tiene índices simples y compuestos que cubren exactamente los filtros usados en `FinancialMetricsService` y `PlanningMatchSuggestionService`; `MovementAuditDecision` tiene índice único `(SourceEntityType, SourceId)` que coincide con el patrón de consulta real (aunque conviene confirmar el orden de columnas del índice generado, ya que las queries filtran solo por `SourceId`). No se encontraron filtros usados sistemáticamente sin índice de soporte.
- **No se encontraron tablas huérfanas** ni relaciones claramente innecesarias. El único candidato de "normalización pendiente" real es `Category.ParentId` (esquema especulativo), el resto del modelo (texto libre en `Notes`/`Title`/`Comment`) es correctamente texto libre por diseño, no candidato a catálogo.
- El historial de migraciones es, salvo la migración vacía, lineal — no hay evidencia de idas y vueltas de diseño de esquema. `DropLegacyImportedExpenses` es una limpieza legítima de una tabla ya retirada del dominio, no una indecisión.

---

## 6. Performance (priorizado por impacto)

### Alto impacto

**`AuditReportService.BuildFullAuditReportAsync` recalcula lo mismo hasta 3 veces por invocación.** Dentro de un único reporte de auditoría (usado tanto por la tool MCP `AuditDatabase` como, presumiblemente, por `audit.html`): `IMovementsQueryService.GetAsync` se ejecuta **3 veces** con los mismos parámetros, `IReviewEngine.GenerateAsync` **2 veces**, `IClassificationSuggestionService.SuggestAsync` (que además hace *table scan* completo, ver abajo) hasta **3 veces**. Es paradójico porque tanto `MovementsQueryService` como `ClassificationSuggestionService` tienen comentarios extensos explicando por qué evitan N+1 — la optimización se pierde en el orquestador, no en las piezas individuales. Es la tool de auditoría más pesada del sistema y la única compartida entre MCP y UI. **Recomendación: calcular una sola vez y pasar el resultado a los sub-métodos en vez de dejar que cada uno vuelva a consultar.**

### Medio impacto

- **`ClassificationSuggestionService.SuggestAsync` hace *table scan* completo de `ClassifiedMovements`** sin `Where`, decisión consciente y documentada como aceptable "para un sistema personal de un solo usuario", con un plan B explícito (columna normalizada indexada) si el volumen crece. Combinado con el hallazgo anterior (se llama hasta 3 veces por auditoría) y con que también lo dispara cada `GET /api/movements`, el costo crece más rápido de lo que el comentario original anticipaba — vale la pena revisar el volumen real antes de v1.0.
- **N+1 real en `InvestigationTools.AskInvestigation`**: por cada referencia de una investigación se llama `IMovementLookupService.GetBySourceAsync` dentro de un `foreach`, y cada llamada dispara entre 2 y 4 queries secuenciales. Con 15 referencias, esto son ~30-60 round-trips secuenciales en una sola invocación. Volumen de uso probablemente bajo, pero es el N+1 más claro del repositorio — un método de lookup en lote lo resolvería.
- **Instrumentación de diagnóstico que ejecuta queries extra solo para loguear**: `FinancialMetricsService.GetPeriodSummaryAsync` corre una segunda query completa (`boundaryRows`) únicamente para loguear filas de borde en cada llamada a `GetMonthlySummary`/`GET /api/metrics/summary` — impuesto de performance permanente sobre el endpoint de resumen más usado del sistema, dejado de un diagnóstico puntual (ver también sección 3).

### Bajo impacto

- Catálogos pequeños (`Categories`/`Counterparties`/`FinancialAccounts`) se releen en cada request desde múltiples lugares distintos sin cache — bajo impacto al volumen actual, buen candidato a `IMemoryCache` si el tráfico crece.
- `FileParserFactory.ResolvePdfParser` usa `.GetAwaiter().GetResult()` sobre una llamada async — no es un problema de performance hoy (no hay `SynchronizationContext` capturable en Minimal API/Generic Host), pero es un patrón frágil ante un futuro cambio de contexto de ejecución.
- Disciplina de `AsNoTracking()` en queries de solo lectura: **consistente y correcta** en todo Infrastructure — fortaleza real, no hay que tocarla.

---

## 7. Seguridad

### Bloqueante para cualquier despliegue más allá de localhost

1. **No existe ningún mecanismo de autenticación ni autorización en toda la API.** Verificado exhaustivamente: `Program.cs` no llama `UseAuthentication()`/`UseAuthorization()`, ningún endpoint tiene `[Authorize]`/`.RequireAuthorization()`. **Todos los endpoints están completamente abiertos**, incluyendo `DELETE`/desactivación de cuentas, categorías, contrapartes, ítems de planificación, y **`POST /api/imports`, que sube y ejecuta importación de archivos arbitrarios contra la base de producción**. Cualquiera con acceso de red al puerto de la API puede leer, modificar o borrar lógicamente todos los datos financieros sin ninguna credencial. Para un sistema de finanzas personales, esto es el hallazgo más grave de todo el informe.
2. **Credenciales de PostgreSQL hardcodeadas e idénticas, versionadas en git**, en los 3 hosts (`Host=localhost;...;Username=postgres;Password=postgres`). El riesgo real es bajo porque apuntan a `localhost` de desarrollo, pero el patrón — commitear connection strings con password en texto plano — es una mala práctica que contrasta con el manejo correcto de `OpenAI:ApiKey` (vacío en los `appsettings*.json`, resuelto por variable de entorno/user secrets). El equipo conoce el patrón correcto y no lo aplicó de forma consistente.

### Alto

3. **Riesgo de ruteo incorrecto Visa↔Mastercard en PDFs** (ver sección 9) — pérdida silenciosa de movimientos reales, reconocido en un test que documenta la limitación sin corregirla.
4. **`ExternalId` de `BankStatement` basado en posición** (nombre de archivo + hoja + fila), no en contenido — frágil ante renombres de archivo o inserción de filas nuevas por el banco, con riesgo de duplicación silenciosa de saldos reales.
5. **Instrumentación de diagnóstico con `LogWarning` dentro de un loop de importación**, exponiendo montos/cuentas/descripciones línea por línea en logs de producción con severidad alta.

### Medio

6. **Sin límite de tamaño en `POST /api/imports`** (ningún `[RequestSizeLimit]`/`FormOptions` declarado) y **sin validación de contenido real vs. extensión declarada** (magic bytes) — un archivo renombrado con extensión falsa llega sin chequeo de firma.
7. **Sin middleware global de manejo de excepciones** ni validación estructurada (no hay FluentValidation/DataAnnotations en ningún endpoint) — toda la validación es `if (string.IsNullOrWhiteSpace(...))` ad-hoc, inconsistente entre endpoints.
8. **Envío de datos financieros reales a OpenAI** (`TransactionInsightsWorker`) sin ningún mecanismo de consentimiento visible en UI — solo config de servidor (`InsightsWorker:Provider`).
9. **Sin ninguna política de CORS** — no es una vulnerabilidad (es "seguro por defecto" ante navegadores cross-origin), pero indica que no se pensó el modelo de hosting más allá de same-origin.

### Bajo

10. Ruta de filesystem personal de un desarrollador filtrada en `appsettings.Development.json` del Worker.
11. Riesgo teórico de degradación de servicio vía `.xlsx` con alta tasa de compresión, sin límite de tamaño descomprimido ni de entradas del ZIP (se agrava por el punto 6).

---

## 8. Frontend

### Inventario

8 páginas en `wwwroot`: `dashboard.html` (1491 líneas, la única con sidebar completo), `movements.html` (1816), `accounts.html`, `counterparties.html`, `imports.html`, `audit.html`, `planning.html`, `index.html` (redirect puro). **No existe `categories.html`** pese a que el CRUD completo de categorías ya existe en el backend — hueco real de UI.

### Navegación

Solo `dashboard.html` tiene sidebar de navegación completa. Las **6 pantallas restantes usan un único link `← Dashboard`** — para ir de `accounts.html` a `counterparties.html` el usuario debe pasar siempre por el Dashboard. Este problema ya fue diagnosticado con precisión por el propio equipo (`PRUI1analisisarquitecturaui.md`, propuesta de sidebar compartida) y **nunca se resolvió**; empeoró, porque las 2 páginas más nuevas (`audit.html`, `planning.html`) repitieron el mismo patrón aislado en vez de esperar la refactorización propuesta.

### JavaScript y CSS — duplicación real, con divergencia ya demostrada

- `getJson()` está duplicado, no compartido, en las 7 páginas con `<script>` (dos de ellas, `accounts.html`/`counterparties.html`, son copias byte-a-byte). **`dashboard.html` tiene una versión estrictamente más débil** (usa texto plano de error en vez de parsear `ProblemDetails` como el resto) — el mismo hallazgo que señaló el análisis interno previo, **sigue sin corregirse**.
- Bloque de tokens CSS (`:root { --bg: ...}`) duplicado en las 7 páginas — el volumen total de CSS repetido **creció** respecto a la medición del propio equipo (de 5 a 7-8 páginas), sin que se ejecutara ninguno de los pasos de la hoja de ruta `PRUI1` (extraer `tokens.css`/`components.css`/`app.js` a `wwwroot/shared/`, que **no existe**).
- Puntos ya corregidos y verificados (para dar crédito donde corresponde): la función `esc()` de escape de HTML está presente y en uso en las 7 páginas (el riesgo de XSS que señalaba el análisis previo ya no existe), y el badge `navPending` del nav ya se completa correctamente.

### Evaluación general

Backend sólido para el tamaño del proyecto. Frontend funcional pero en estado de "prototipo que creció orgánicamente": cada corrección de un helper compartido requiere tocar manualmente 7 archivos, con el riesgo ya materializado dos veces (divergencia en `esc()`, ya resuelta; divergencia en `getJson()`, todavía sin resolver). No es "no funcional", pero no es mantenible a mediano plazo si se siguen agregando pantallas con el mismo patrón.

---

## 9. Importación

### Fortalezas verificadas

- Worker y API llaman exactamente al mismo `IFileImportRouter.RouteAsync` — sin duplicación de lógica de negocio entre el disparador automático y el manual.
- Idempotencia real y robusta en el pipeline catch-all (PDF/CSV/XLSX genérico vía `ImportFileProcessingSink`): `ExternalId` **basado en contenido** (número de cupón con fallback a hash de fecha+monto+descripción), índice único real, consulta batch previa a insertar. Este mecanismo ya resuelve lo que `docs/Epics/EpicaI-Importacion.md` describe como problema abierto ("tarjeta no es idempotente") — la documentación está desactualizada en este punto específico, la funcionalidad ya existe.
- Manejo de filas parcialmente inválidas consistentemente bueno en todos los parsers: fecha/monto inválido o fila incompleta se cuenta como `skipped` con diagnóstico, sin abortar el resto del archivo.
- `XlsxSanitizer` — la pieza mejor documentada de todo el pipeline, con workaround explícito y bien razonado para un bug conocido de ClosedXML.
- Sin N+1 en ninguna parte del pipeline de importación — todas las resoluciones de duplicados/cuenta financiera son una sola query por lote.

### Debilidades reales

- **Riesgo de ruteo incorrecto Visa↔Mastercard**: el fingerprint de `BbvaVisaStatementParser` (`\bBBVA\b`) es lo bastante genérico para matchear también un extracto Mastercard, y `FileParserFactory` usa "primer `CanHandle`=true gana" según el orden de registro en DI (Visa antes que Mastercard). Un resumen Mastercard real corre riesgo de perder movimientos silenciosamente (0 líneas extraídas, sin excepción). **Ya está documentado como limitación conocida en un test** (`KnownLimitation_PdfContainingBothBbvaAndMastercardText...`) en vez de corregido — es exactamente el PR **I7** pendiente de la épica de importación.
- **Inconsistencia de robustez entre las 3 fuentes**: `Transaction.ExternalId` es por contenido (robusto); `BankStatement.ExternalId` es por posición — archivo+hoja+fila (frágil ante renombres o filas nuevas del banco). El mismo sistema tiene dos niveles de confiabilidad distintos para problemas equivalentes.
- Manejo de errores inconsistente: CSV con columnas faltantes lanza una excepción cruda (`InvalidOperationException`) en vez de un diagnóstico estructurado como el resto del pipeline — se recupera aguas arriba, pero el mensaje de error para el usuario es distinto en calidad al resto.
- Sin fallback explícito para Latin-1/Windows-1252 (común en exports bancarios argentinos legacy) — solo detección automática de BOM UTF-8.
- Ninguna verificación de "conjunto de fechas consistente" a nivel de archivo — cada fecha se parsea independientemente, con riesgo de ambigüedad `dd/MM` vs `MM/dd` en casos límite.
- Sin límite de tamaño de archivo ni procesamiento asíncrono fuera del hilo del request en la subida manual — un archivo grande bloquea el hilo HTTP hasta terminar.
- `ImportBatch` (auditoría) se persiste en un `SaveChangesAsync` separado, en un scope de DI distinto, **después** de que los datos financieros ya se guardaron — decisión consciente y documentada, pero significa que es posible tener movimientos importados sin su `ImportBatch` correspondiente si el proceso falla entre ambos guardados. Rompe parcialmente la promesa de trazabilidad total de ADR-005.
- `BbvaDebitCardEnrichmentHandler` descarta silenciosamente coincidencias ambiguas (comportamiento conservador correcto) pero sin dejar ningún rastro accionable para el usuario más allá de un contador interno.
- Instrumentación de diagnóstico `[DIAG-FA]` olvidada en producción, con logs de severidad Warning por cada transacción insertada (ver secciones 3 y 6).

---

## 10. Clasificación

### Cómo funciona realmente

100% determinístico, **sin IA, sin embeddings, sin fuzzy matching**: dos heurísticas. (1) coincidencia **exacta** de descripción normalizada contra el historial de `ClassifiedMovement`, con un piso de `MinSampleSize = 5` y confianza por mayoría calificada 2/3. (2) valores por defecto configurados manualmente en `Counterparty` (`DefaultCategoryId`/`DefaultMovementType`/`DefaultFinancialImpact`), que compiten como sugerencias `High`. Implementa exactamente el modelo de 4 dimensiones de ADR-001, sin desviación — de los pocos casos donde doc y código coinciden perfectamente.

### Fortalezas

- Separación estricta y sostenida entre `IReviewEngine` (carga + sospechosos) y sugerencias — nunca se mezclaron.
- Cada sugerencia lleva un motivo legible + evidencia cuantitativa (`MatchCount`/`WinnerCount`), nunca una caja negra.
- El motor de auditoría **reutiliza el mismo cálculo** que el de sugerencias — no hay una segunda implementación de "qué está mal clasificado" que pueda divergir silenciosamente.
- Disciplina real de "corregir el bug antes de seguir": dos correcciones seguidas (S11/S12) del mismo defecto (sugerir categorías/contrapartes desactivadas), con tests dedicados.

### Debilidades

- **`MinSampleSize = 5` es un umbral fijo sin base empírica** — el propio roadmap del equipo proponía medir volumen real antes de comprometerse a heurísticas más caras, y ese checkpoint nunca se ejecutó.
- Comparación de descripción por **igualdad exacta**, sin fuzzy matching — cualquier variación no cubierta por las dos reglas de normalización existentes fragmenta el historial silenciosamente.
- **Hardcodeado a un solo banco (BBVA)** en toda la capa de parsing — extender a otro banco requiere un parser nuevo desde cero, sin ningún punto de extensión declarativo.
- Reglas **no configurables** — cualquier regla nueva requiere código + deploy. Decisión YAGNI consciente (documentada: "esperar a 3+ heurísticas reales" — el proyecto nunca llegó a la tercera), pero deja al motor estructuralmente estancado.
- Comentario engañoso en `ClassifiedMovement.MatchScore`/`AmountDelta` (ver sección 3).
- **`ProcessingSource` no se actualiza al reclasificar** — la trazabilidad de "por qué está clasificado así" se degrada después de la primera edición manual.
- **Sin feedback loop de rechazo**: no hay ningún mecanismo que registre "el usuario rechazó esta sugerencia" — una sugerencia mal aceptada por error queda indistinguible de una clasificación genuina en el historial futuro (y por lo tanto puede reforzarse a sí misma).
- **`ADR-007` desactualizado de forma engañosa** (ver sección 1.3) — el subsistema de memoria del MCP está más avanzado de lo que su propio documento de arquitectura admite.

### Cobertura de tests — desequilibrada

Cobertura fuerte y de buena calidad en el motor de Sugerencias (12+9+7 casos, anclados a bugs reales) y parcialmente en Planning/Imports. **Prácticamente sin tests**: `SuspicionDetector` (algoritmo de grafos/combinatoria para detectar duplicados/splits — lógica no trivial, cero tests), `ReviewEngine`, `MovementLoader` (con un comentario propio advirtiendo sobre un riesgo de inversión de signo — sin test que lo cubra), `AuditReportService` (656 líneas, el corazón del Centro de Auditoría — cero tests), todos los handlers de `Investigations` (memoria del MCP), y la mayoría de los handlers de Planning salvo `CopyPlanningMonthHandler`. Es decir: **las piezas más nuevas y menos maduras del sistema (Auditoría, Investigaciones/memoria) son las que menos red de seguridad automatizada tienen** — justo lo contrario de lo deseable antes de una v1.0.

---

## 11. Planificación mensual

Modelo, flujos de creación/copia y fórmulas de resumen implementados **exactamente** como especifica `docs/Epics/Epica-PlanificacionMensual.md`, con tests que verifican las reglas de negocio puntuales (p. ej. "copiar un mes nunca copia `ExpectedIncome`"). Es, junto con el motor de Sugerencias, el módulo con mejor alineación doc-código-test de todo el repositorio.

**Desviación real de alcance**: `PlanningMatchSuggestionService` implementa el matching PlanningItem↔Movimiento clasificado que la propia épica (sección 9) marca explícitamente *"fuera de este documento y del MVP"*. No es destructivo (solo lectura, nunca escribe automáticamente — respeta la filosofía general del producto), pero es scope creep documentado contra un documento de alcance que decía lo contrario.

**Código sobrante/duplicado**: `PlanningMatchSuggestionService.Normalize` reimplementa desde cero una normalización de texto casi idéntica a la de `ClassificationSuggestionService.Normalize`, con el propio código admitiendo la duplicación como consciente ("fuera de alcance de este patch"). Oportunidad de simplificación de baja prioridad: unificar ambos normalizadores.

---

## 12. Experiencia de usuario (recorrido de un usuario nuevo)

- **Onboarding poco guiado**: no hay pantalla de "primeros pasos". Un usuario nuevo llega al Dashboard sin datos, debe entender por su cuenta que necesita importar extractos (¿por carpeta vigilada del Worker? ¿por el botón de `imports.html`?) y crear categorías **sin ninguna pantalla dedicada** (no existe `categories.html` pese a que el CRUD backend está completo) — el catálogo inicial de categorías debe crearse vía API cruda o un script, una barrera real para alguien no técnico.
- **Navegación con fricción constante**: desde cualquier pantalla secundaria (Cuentas, Contrapartes, Auditoría, Planificación, Importaciones), volver a otra pantalla secundaria obliga a pasar siempre por el Dashboard — no hay navegación lateral directa.
- **Clasificación repetitiva más pesada de lo necesario**: el formulario sigue pidiendo 4 campos (incluido `MovementType`, que la propia investigación interna del equipo concluyó que no tiene consumidor real verificado) para cada movimiento — la Épica N, que buscaba reducir esto a la decisión real que el usuario toma en la práctica, no se implementó.
- **Sin indicador de confianza en los números**: el Dashboard puede mostrar un resumen mensual calculado sobre una fracción minoritaria de los movimientos reales sin ninguna advertencia — no hay "% clasificado este período" visible, pese a ser (según la propia auditoría interna) el hallazgo de UX más grave del producto.
- **Riesgo de doble conteo silencioso**: nada en el formulario de clasificación orienta a distinguir "pago de resumen de tarjeta" de "gasto normal" — el modelo de dominio lo resuelve (ADR-003) pero el usuario puede clasificar mal por simple falta de guía visual, inflando artificialmente sus métricas de gasto sin darse cuenta.
- **Confianza del sistema comprometida por seguridad**: si este producto llegara a exponerse más allá de `localhost` en su estado actual, cualquier persona en la misma red podría ver o alterar los datos financieros del usuario sin ninguna credencial — esto no es solo un problema técnico, es un problema de experiencia y confianza del producto en sí.

---

## 13. Futuras funcionalidades propuestas

### Alta prioridad

| Funcionalidad | Problema que resuelve | Beneficio | Complejidad | Impacto |
|---|---|---|---|---|
| **Autenticación básica de la API** (aunque sea un único usuario con API key/cookie) | Hoy cualquiera en la red puede leer/alterar/borrar todos los datos financieros | Habilita cualquier despliegue fuera de `localhost` con seguridad mínima | Baja-media | Crítico — bloqueante hoy |
| **Indicador de cobertura de clasificación** (Épica L) | El usuario no sabe si sus métricas reflejan la realidad o una fracción del período | Confianza real en el producto; barato de construir sobre datos que ya existen | Baja | Alto |
| **Guía de UX para pago de tarjeta vs. consumo** (cierre de ADR-003) | Riesgo activo de doble conteo silencioso de gastos | Corrige la métrica más importante del sistema (cuánto gasté) | Baja | Alto |
| **Conciliación automática pago-de-resumen ↔ extracto de tarjeta** | Hoy nada vincula el débito bancario del pago del resumen con el consumo de tarjeta que paga | Elimina manualmente la mayor fuente de doble conteo; complementa el punto anterior con automatización real | Media | Alto |
| **Gastos fijos con recordatorio de vencimiento** (Fase 2 del roadmap, nunca construida) | Es una de las razones fundacionales declaradas del proyecto (README) y todavía no existe | Entrega el valor central prometido desde el inicio del producto | Media-alta | Alto |

### Media prioridad

| Funcionalidad | Problema que resuelve | Beneficio | Complejidad | Impacto |
|---|---|---|---|---|
| Presupuestos por categoría con alertas de desvío (Fase 2 roadmap) | No hay forma de saber si un mes se está desviando del patrón histórico mientras ocurre | Convierte el sistema de "registro pasivo" a "asistente activo" | Media | Medio-alto |
| Reglas de clasificación configurables (data-driven) | El motor está estancado en 2 heurísticas hardcodeadas; cualquier regla nueva exige deploy | Extiende el motor sin recompilar; sienta base para más bancos | Media-alta | Medio (solo si el volumen real lo justifica — medirlo primero) |
| Abstracción de "banco"/parser plugin | Todo el pipeline está hardcodeado a BBVA | Habilita soportar otro banco sin reescribir desde cero | Alta | Medio (solo si hay necesidad real de multi-banco) |
| Exportación de reportes (PDF/Excel) del resumen mensual | No hay forma de sacar los datos del sistema para compartir/archivar | Utilidad práctica de bajo esfuerzo | Baja-media | Medio |
| Registro de sugerencias rechazadas (feedback loop) | El motor no distingue una sugerencia mal aceptada de una clasificación genuina | Mejora la calidad de las heurísticas futuras sin rediseñar el motor | Media | Medio |

### Baja prioridad

| Funcionalidad | Problema que resuelve | Beneficio | Complejidad | Impacto |
|---|---|---|---|---|
| Multiusuario / finanzas compartidas de hogar | Sistema actualmente asume un único usuario | Amplía el mercado potencial del producto | Alta | Bajo (fuera de la visión actual declarada) |
| UI responsiva / PWA para mobile | Uso hoy limitado a escritorio | Comodidad de uso diario | Media | Bajo-medio |
| Cuentas de inversión completas (Épica M, ya en roadmap a largo plazo) | Fuera de alcance actual, de mayor incertidumbre de producto | Cierra la Fase 4 del README | Alta | Bajo hoy (no hay urgencia de negocio clara) |
| Dark mode / pulido visual general | Cosmético | Mejora percibida, no funcional | Baja | Bajo |

---

## 14. Roadmap (ordenado por valor, no por dificultad)

1. **Seguridad — autenticación de la API y saneamiento de credenciales.** Sin esto, nada del resto importa: es la precondición para que el sistema pueda usarse de forma segura en cualquier escenario más allá de un único desarrollador en su propia máquina.
2. **Corregir el ruteo Visa/Mastercard (I7)** y **unificar el `ExternalId` de `BankStatement` a base de contenido** — integridad de los datos financieros es el segundo requisito no negociable de un sistema que se llama a sí mismo "fuente de verdad financiera".
3. **Indicador de cobertura de clasificación (Épica L)** — barato, y resuelve el mayor problema de confianza del producto identificado internamente.
4. **Guía de UX + conciliación automática de pago de tarjeta (cierre de ADR-003)** — corrige la métrica más citada del sistema (cuánto gasté).
5. **Simplificación del formulario de clasificación (Épica N)** — reduce fricción en la acción más repetida de toda la aplicación.
6. **Actualizar `vNext.md` y corregir ADR-007/ADR-005** — sin una fuente de verdad confiable, cada nueva sesión de trabajo (humana o de IA) parte de información incorrecta, lo que ya generó scope creep real (Planificación Mensual) y funcionalidad invisible (Centro de Auditoría).
7. **Extraer `wwwroot/shared/`** — detiene la divergencia de bugs entre 7-8 páginas antes de que se agregue una novena.
8. **Gastos fijos con recordatorios** — primera entrega real de la Fase 2 prometida desde el README original.
9. **Tests para `SuspicionDetector`, `ReviewEngine`, `AuditReportService`, handlers de `Investigations`** — proteger las piezas más nuevas y menos maduras antes de seguir construyendo sobre ellas.
10. **Refactor de performance de `AuditReportService`** — barato de corregir, alto impacto en la tool de auditoría más usada.
11. **Auto-asignación de `FinancialAccount` al importar** — cierra un hueco de Épica J ya señalado.
12. **Presupuestos y alertas de desvío** — siguiente escalón natural de valor de producto, una vez que los datos son confiables (pasos 1-4).
13. **Reglas de clasificación configurables / soporte multi-banco / cuentas de inversión** — dejar para después: son inversiones de mayor esfuerzo cuyo retorno depende de validar primero que el volumen de uso las justifica.

---

## 15. Limpieza priorizada

### Urgente

- Agregar autenticación a la API (sección 7.1).
- Sacar las credenciales de PostgreSQL del repositorio (variables de entorno / user secrets) y rotarlas si alguna vez se usaron fuera de un entorno estrictamente local.
- Eliminar la instrumentación de diagnóstico `[DIAG-FA]`/similar en los 4 archivos identificados, en particular la que loguea por fila dentro de un loop.
- Corregir `ToolRegistry.Tools` (5 tools MCP reales invisibles para el propio asistente del sistema).
- Resolver la colisión de fingerprint Visa/Mastercard (I7) o, como mínimo, agregar un mecanismo explícito de desambiguación en vez de depender del orden de DI.

### Recomendado

- Eliminar `CommonHelper.cs`, `OpenApiCompatibilityStubs.cs`; decidir sobre `PdfStatementParseOptions.cs`.
- Archivar la documentación obsoleta listada en la sección 1.6.
- Actualizar `docs/RoadMaps/FinancialMcp-vNext.md`, `ADR-007`, `ADR-005`, `ClassificationUX.md`, `McpServerSetup.md`, `McpUserGuide.md`.
- Fusionar el trío de documentos de simplificación del modelo de dominio.
- Refactorizar `AuditReportService.BuildFullAuditReportAsync` para eliminar la recomputación triple.
- Extraer `wwwroot/shared/` (tokens CSS + helpers JS comunes).
- Corregir o eliminar el comentario engañoso sobre `MatchScore`/`AmountDelta` en `ClassifiedMovement`.
- Agregar validación estructurada (FluentValidation o similar) y un middleware global de manejo de excepciones en la API.

### Opcional

- Unificar `ToSourceEntityType` y `Normalize()` duplicados.
- Retirar o formalizar `Category.ParentId`.
- Limpiar la migración vacía del historial (documentar, no reescribir historia ya publicada).
- Agregar `categories.html`.
- Decidir el destino de `TransactionInsightsWorker` (persistir o marcar como experimental).

---

## 16. Informe final — veredicto

**¿Firmaría esta v1.0 hoy, como responsable de aprobarla antes de producción? No.** No por la calidad del código —que es notablemente disciplinada para la velocidad a la que se construyó— sino por dos huecos de seguridad bloqueantes (sin autenticación, credenciales versionadas) que nadie parece haber evaluado todavía porque el trabajo se concentró enteramente en producto. Ningún volumen de buenas prácticas de dominio compensa que hoy cualquier persona con acceso de red pueda leer o alterar datos financieros personales sin ninguna credencial.

Descontando seguridad, lo que encontré es un proyecto con una relación inusual entre calidad de ingeniería y calidad de proceso documental: el código tiene comentarios que explican decisiones (no solo qué hace, sino por qué), ADRs reales, tests anclados a bugs de producción concretos (no tests triviales), y una disciplina notable de "no dejar código muerto" ni marcadores `TODO` sueltos. Al mismo tiempo, la documentación se desactualiza más rápido de lo que se corrige: encontré un ADR que miente sobre su propio estado de implementación el mismo día en que se volvió falso, un roadmap "fuente de verdad" que no conoce funcionalidad completa en producción (Centro de Auditoría, Planificación Mensual), y una épica que se autodescribe como "sin implementar" cuando ya está construida end-to-end. Esto no es negligencia — es la consecuencia natural de moverse muy rápido sin un proceso que fuerce a cerrar el círculo "documentar → construir → volver a documentar". Antes de v1.0, ese proceso necesita existir, o la próxima ronda de trabajo (humana o de IA) va a seguir tomando decisiones sobre una base falsa, como ya pasó con el scope creep de Planificación Mensual.

No doy por buena ninguna decisión solo porque "funciona": el motor de sugerencias funciona, pero está estancado en 2 heurísticas desde hace varios PRs sin haber medido el volumen real que el propio equipo se propuso medir; el Centro de Auditoría funciona, pero recalcula el mismo resultado hasta 3 veces por invocación; la importación funciona para BBVA, pero es una excepción hardcodeada a un solo banco disfrazada de pipeline general; y el patrón "Command+Handler" funciona, pero no es CQRS+MediatR aunque lo parezca. Ninguno de estos es un defecto grave por sí solo — juntos, describen un sistema que todavía no pasó por una ronda de consolidación real, algo esperable a 5 días de desarrollo intenso, pero no aceptable para poner un "1.0" encima sin esa ronda.

---

## Top 20 mejoras con mayor retorno

Ordenadas por impacto real sobre el proyecto (no por dificultad de implementación).

1. **Agregar autenticación/autorización a toda la API** — hoy cualquier persona con acceso de red puede leer, modificar o borrar todos los datos financieros sin credenciales. Bloqueante absoluto.
2. **Sacar las credenciales de PostgreSQL del repositorio** (env vars/secrets) y dejar de commitearlas — mismo problema de fondo que el punto 1, menor severidad real pero mismo tipo de riesgo.
3. **Corregir la colisión de fingerprint Visa/Mastercard en PDFs (I7)** — pérdida silenciosa de datos financieros reales, ya reconocida y no corregida.
4. **Eliminar la instrumentación de diagnóstico `[DIAG-FA]`** de los 4 archivos identificados — ruido de logs, exposición de datos financieros en texto plano a nivel Warning, y coste de performance real en el endpoint de resumen más usado.
5. **Refactorizar `AuditReportService.BuildFullAuditReportAsync`** para eliminar la recomputación triple — la tool de auditoría más pesada del sistema, hoy hace 3x el trabajo necesario.
6. **Indicador de cobertura de clasificación en el Dashboard (Épica L)** — resuelve el hallazgo de confianza más grave que la propia auditoría interna del proyecto ya identificó, con esfuerzo bajo.
7. **Actualizar `docs/RoadMaps/FinancialMcp-vNext.md`** para reflejar el estado real (Épica J, Auditoría, Planificación, S/U/UI) — es la fuente de verdad declarada del proyecto y hoy desconoce partes sustanciales del sistema.
8. **Corregir `ADR-007-McpMemory.md`** (dice "nada implementado" siendo falso) — el documento de arquitectura más importante sobre la memoria del MCP miente sobre su propio estado.
9. **Guiar en la UI la distinción pago de tarjeta vs. consumo** (cerrar el loop de ADR-003) — el modelo de dominio ya lo resuelve, la UI no lo comunica, y esto infla artificialmente la métrica de gasto más citada del sistema.
10. **Simplificar el formulario de clasificación (Épica N)** — reduce fricción real en la acción más repetida de la aplicación, con evidencia propia del equipo de que uno de los 4 campos no tiene consumidor real.
11. **Corregir `ToolRegistry.Tools`** — 5 herramientas MCP reales (incluidas las 4 de métricas financieras) son invisibles para el propio asistente de IA del sistema.
12. **Extraer `wwwroot/shared/`** (tokens CSS + JS común) — detiene una divergencia de bugs ya demostrada (dos veces) entre 7-8 páginas HTML autocontenidas.
13. **Archivar/consolidar los ~15 documentos obsoletos o duplicados** de `docs/Architecture/` — reduce el riesgo de que futuras sesiones de trabajo (humanas o de IA) tomen decisiones sobre información superada.
14. **Agregar middleware global de manejo de errores + validación estructurada** en la API — hoy cada endpoint decide a mano qué validar, sin ningún contrato central.
15. **Escribir tests para `SuspicionDetector`, `ReviewEngine`, `AuditReportService` y los handlers de `Investigations`** — son las piezas más nuevas del sistema y las que menos red de seguridad automatizada tienen.
16. **Unificar el `ExternalId` de `BankStatement` a base de contenido** (igual que `Transaction`) — hoy es posicional y frágil ante cualquier cambio de formato del banco.
17. **Límite de tamaño de archivo + validación de contenido real (magic bytes)** en `POST /api/imports` — hoy no hay ningún techo ni verificación más allá de la extensión declarada.
18. **Definir consentimiento explícito para el envío de datos financieros a OpenAI** (`TransactionInsightsWorker`) — hoy depende solo de configuración de servidor, sin ningún control visible para el usuario.
19. **Eliminar código muerto** (`CommonHelper.cs`, `OpenApiCompatibilityStubs.cs`, decidir `PdfStatementParseOptions.cs`) — bajo esfuerzo, reduce ruido de mantenimiento.
20. **Resolver formalmente la tensión ADR-001 vs. evidencia de `MovementType` sin consumidor** — con un ADR nuevo que decida, con datos, si el modelo de 4 dimensiones se mantiene tal cual o se ajusta; la evidencia ya existe hace varios documentos, solo falta la decisión formal que el propio ADR-001 exige.
