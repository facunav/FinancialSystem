# FinancialMcp — Estado del proyecto

> Puerta de entrada para cualquier persona (humana o IA) que llegue al proyecto. Describe el estado **real** según el código actual, no según documentación histórica. Si algo de acá contradice otro documento, **este archivo manda** hasta que se demuestre lo contrario contra el código.
>
> Lectura estimada: 15 minutos. Última verificación: revisión completa del repositorio (código + 44 documentos `.md`) al cierre de la revisión pre-v1.0.

---

## 1. Resumen general

**FinancialMcp** es una plataforma personal de gestión financiera. Nació como herramienta de conciliación bancaria y evolucionó hacia algo más amplio: una base de conocimiento financiero personal, centrada en **revisar y clasificar movimientos** (no en conciliarlos contra un registro externo), pensada para eventualmente alimentar un asistente de IA (el "Financial Copilot") capaz de responder preguntas sobre hábitos de gasto vía el protocolo MCP.

**Problema que resuelve:** consolidar movimientos de banco, tarjeta de crédito/débito e historial manual (Excel) en un único registro clasificado y confiable, del cual se puedan derivar métricas de gasto y, a futuro, respuestas en lenguaje natural sobre finanzas personales.

**Estado actual:** MVP funcional avanzado, con el flujo completo import → normalización → revisión → clasificación → métricas → MCP operativo de punta a punta para un banco (BBVA). Además del MVP original ya construyó, sin que estuviera en el plan original, un Centro de Auditoría completo y un módulo de Planificación Mensual. **No está listo para producción**: no tiene autenticación en ningún endpoint y las credenciales de base de datos están versionadas en el repositorio.

**Nivel de madurez:** para su tamaño, el dominio de negocio (clasificación, importación, planificación) está implementado con disciplina real — comentarios que explican decisiones, ADRs, tests anclados a bugs de producción concretos. Las debilidades están concentradas en los bordes: seguridad (sin evaluar todavía), frontend (7-8 páginas HTML sin infraestructura compartida), y documentación (varios documentos "fuente de verdad" quedaron desactualizados por código escrito horas después). Es, en esencia, un **MVP sólido en su núcleo de dominio, no endurecido para producción**.

---

## 2. Módulos

| Módulo | Objetivo | Estado | Estabilidad | Dependencias |
|---|---|---|---|---|
| **Importación** (`Application/Imports`, `Infrastructure/Imports`, Worker watcher) | Ingerir extractos de banco/tarjeta y volcarlos a movimientos normalizados | En desarrollo | Alta en banco/catch-all genérico; media en tarjeta de crédito PDF (riesgo conocido de ruteo Visa↔Mastercard) | Domain (`BankStatement`, `Transaction`, `ImportBatch`) |
| **Revisión y clasificación** (`Application/Review`, `Infrastructure/Review`, `Domain/Review`) | Detectar duplicados/sospechosos y clasificar movimientos en 4 dimensiones | Terminado | Alta (núcleo bien probado); `SuspicionDetector`/`ReviewEngine` sin tests propios | Domain, motor de sugerencias |
| **Motor de sugerencias** (`Application/Suggestions`, `Infrastructure/Suggestions`) | Sugerir clasificación por historial + valores por defecto de contraparte | Terminado | Alta — la mejor cobertura de tests del repositorio | Historial de `ClassifiedMovement` |
| **Centro de Auditoría** (`Infrastructure/Audit`, tools MCP `AuditTools`/`AuditDatabaseTools`, `audit.html`) | Detectar movimientos sospechosos/mal clasificados y dar visibilidad del estado de los datos | Terminado (funcionalmente) | Experimental — cero tests, recalcula resultados redundantemente. Documento de diseño: `docs/Architecture/CentroDeAuditoria.md` (PATCH-033). | Review, Suggestions, Movements |
| **Planificación Mensual** (`Application/Planning`, `Infrastructure/Planning`, `Domain/Planning`, `planning.html`) | Presupuesto simple mes a mes, independiente del historial de movimientos | Terminado | Alta (tests fieles a la épica) | Ninguna dura; matching opcional de solo lectura contra `ClassifiedMovement` |
| **Cuentas financieras** (`FinancialAccount`, `accounts.html`) | Modelar cuentas (banco/tarjeta/inversión/efectivo) como entidad explícita | Terminado (núcleo) | Alta | — |
| **Categorías y Contrapartes** (`Category`, `Counterparty`, `counterparties.html`) | Catálogos administrables usados en la clasificación | Terminado | Alta (CRUD simple, sin capa Application propia) | — |
| **Investigaciones / memoria del MCP** (`Domain/Memory`, `Application/Investigations`, `InvestigationTools`) | Persistir hallazgos de investigaciones financieras para que el MCP tenga memoria entre sesiones | Terminado (Fases 2-4 de su ADR) | Experimental — cero tests | Movements, integración con LLM local (Ollama) |
| **Métricas** (`Infrastructure/Metrics`, `FinancialTools`) | Resúmenes mensuales, gasto por categoría, tendencias | Terminado | Alta | `ClassifiedMovement` |
| **Servidor MCP** (`hosts/FinancialSystem.McpServer`) | Exponer el sistema como herramientas MCP para un cliente de IA (Claude, etc.) | Terminado / funcional | Alta — catálogo de herramientas (`ToolRegistry`) sincronizado con las tools reales desde PATCH-024, verificado por test (`ToolRegistrySyncTests`) | Application, Infrastructure |
| **Worker** (`hosts/FinancialSystem.Worker`) | Vigilar carpeta de importación + generar insights vía LLM | Terminado (watcher) / Experimental (`TransactionInsightsWorker`, solo loguea, sin consumidor) | Media | Application, Infrastructure |
| **API** (`src/FinancialMcp.Api`) | Exponer el sistema vía HTTP para el Dashboard | Terminado funcionalmente | Funcional pero **no apta para producción** — sin autenticación | Application, Infrastructure |
| **Dashboard / Frontend** (`wwwroot`, 8 páginas HTML) | Interfaz web de uso diario | En desarrollo | Media — funcional pero sin CSS/JS compartido entre páginas | API |
| **Gastos fijos / Presupuestos / Inversiones** | Módulos declarados en la visión de largo plazo (README, Fases 2 y 4) | Pendiente | — no iniciado | Cuentas financieras |

---

## 3. Funcionalidades implementadas (solo lo que existe hoy, agrupado por módulo)

**Importación**
- Importación de extractos BBVA Caja de Ahorro (.xls) con idempotencia real (`ExternalId` + índice único).
- Importación catch-all de PDF/CSV/XLSX (tarjeta de crédito Visa/Mastercard, otros formatos) con idempotencia por contenido.
- Enriquecimiento de movimientos de tarjeta de débito contra extractos de Caja de Ahorro.
- Registro de trazabilidad de cada corrida (`ImportBatch`/`ImportBatchLine`), consultable desde `imports.html`.
- Subida manual de archivos desde la UI, reutilizando el mismo motor que usa el Worker automático.
- Manejo de filas parcialmente inválidas sin abortar el resto del archivo, con diagnóstico por fila.

**Revisión y clasificación**
- Detección de movimientos sospechosos (posibles duplicados o splits) dentro de un período.
- Clasificación manual de movimientos en las 4 dimensiones del dominio: `MovementType`, `FinancialImpact`, `Category`, `Counterparty`.
- Sugerencias automáticas de clasificación por coincidencia exacta de descripción histórica y por valores por defecto de contraparte, con motivo legible y nivel de confianza.
- Clasificación en lote desde `movements.html`.

**Centro de Auditoría**
- Reporte de movimientos sospechosos y de movimientos potencialmente mal clasificados (comparando lo persistido contra lo que el motor sugeriría hoy).
- Flujo de revisión humana de decisiones de auditoría (`MovementAuditDecision`).
- Selector de período y resumen estructurado en `audit.html`.

**Planificación Mensual**
- Modelo de mes planificado con ítems de gasto/ingreso esperado.
- Copia de un mes a otro (preservando montos/fechas, reiniciando estado de pago).
- Resumen de disponible/pendiente/pagado del mes.
- Sugerencias de coincidencia (solo lectura) entre ítems planificados y movimientos ya clasificados.

**Cuentas, Categorías, Contrapartes**
- CRUD completo de las 3 entidades, con desactivación lógica en vez de borrado físico.
- Valores por defecto en Contraparte (categoría/tipo/impacto) para acelerar clasificación de movimientos recurrentes.

**Investigaciones / memoria MCP**
- Crear investigaciones, vincular movimientos, agregar hallazgos, actualizar estado.
- Preguntar sobre una investigación en lenguaje natural (`AskInvestigation`), usando un modelo local (Ollama) con el contexto real de la investigación.

**Métricas y MCP**
- Resumen mensual, gasto por categoría, tendencia mensual, comparación con el mes anterior — expuestos tanto en el Dashboard como en tools MCP.
- Herramientas MCP de búsqueda/explicación de movimientos, consulta de investigaciones y preguntas libres sobre el conocimiento del proyecto (documentación).

**Frontend**
- 8 pantallas funcionales: Dashboard, Movimientos, Cuentas, Contrapartes, Importaciones, Auditoría, Planificación, (más el redirect de `index.html`).

---

## 4. Funcionalidades pendientes

### Alta prioridad
- Autenticación/autorización en la API (hoy inexistente).
- Corrección del ruteo ambiguo de PDF Visa/Mastercard (riesgo activo de pérdida silenciosa de movimientos).
- ~~Indicador de "% de movimientos clasificados" visible en el Dashboard (visibilidad de cobertura).~~ **Resuelto (PATCH-019/PATCH-020, Épica L)**: endpoint de cobertura + indicador visual en `dashboard.html` + badge `#navPending`.
- Guía de UX para distinguir pago de resumen de tarjeta vs. consumo (evitar doble conteo de gasto) — **parcialmente resuelto (PATCH-022)**: precarga de `FinancialImpact=DebtPayment` + hint al elegir contraparte `OwnCard` (ver ADR-003). Sigue pendiente como guía general del campo (Épica N), no solo para ese caso puntual.

### Prioridad media
- Módulo de gastos fijos con vencimientos (parte de la visión original del producto, nunca construido).
- Presupuestos por categoría con alertas de desvío.
- Asignación automática de `FinancialAccount` al importar cuando el archivo no trae número de cuenta o hay ambigüedad — **ya se asigna sola cuando el número resuelve sin ambigüedad** (banco y pipeline catch-all de tarjeta, ver tabla de épicas, fila J); solo ese caso restante sigue siendo manual.
- Simplificación del formulario de clasificación (reducir de 4 campos a la decisión real que el usuario toma).
- Infraestructura de UI compartida (CSS/JS comunes entre las 8 páginas).
- Reglas de clasificación configurables (hoy son 2 heurísticas hardcodeadas en código).

### Prioridad baja
- Soporte multi-banco (hoy hardcodeado a BBVA).
- Cuentas de inversión y seguimiento de rendimientos.
- Multiusuario / finanzas compartidas.
- Exportación de reportes (PDF/Excel).

---

## 5. Épicas

| Épica | Estado | Qué implementó realmente | Qué quedó fuera |
|---|---|---|---|
| **Review & Classification Engine v2** | Archivada | Motor de matching original (`IMatchScorer`, `IMatchingRule`) construido y luego retirado por completo al no tener consumidor real tras la Épica K. | — (correctamente archivado en `docs/Archive/`) |
| **K — Nueva UX de clasificación** | Terminada | `movements.html` como pantalla central de clasificación, sin matching contra Excel. | — (su documento de diseño, `ClassificationUX.md`, se actualizó en PATCH-032). |
| **I — Confiabilidad de importación** | Activa | `ImportBatch`, idempotencia por contenido en el pipeline catch-all. | Corrección del fingerprint Visa/Mastercard (I7) — pendiente. |
| **J — Modelo de Cuentas Financieras** | Terminada (núcleo) | `FinancialAccount` como entidad explícita, CRUD completo (`accounts.html`), asignación automática de cuenta al importar cuando el número resuelve sin ambigüedad (banco y tarjeta). | Asignación manual solo para el caso restante: archivo sin número de cuenta o con ambigüedad. |
| **L — Visibilidad de cobertura** | Terminada | Badge de pendientes en el nav; endpoint de cobertura de clasificación + indicador visual de 3 estados en el Dashboard (PATCH-019/PATCH-020). | — |
| **M — Cuentas de inversión** | Pendiente | — no iniciada. | Todo — `InvestmentAccount` no existe en el código. |
| **Mejoras al flujo de importación** (documento con nombre colisionado con la Épica M de inversión) | Activa | Corrección de desfasaje de fila, autoasignación de cuenta en enriquecimiento de débito. | Mostrar "Enriquecidos" en `imports.html`, limpiar estado "Confirmado" inalcanzable, diagnóstico de cuenta sin match. |
| **N — Simplificación del formulario de clasificación** | Pendiente | — no iniciada. | Todo — `MovementType` sigue siendo un campo obligatorio. |
| **O — Importación Manual e Historial** | Terminada | Endpoint de subida manual, reutilizando el mismo motor que el Worker. | — (la documentación de la épica está desactualizada, no la funcionalidad). |
| **S — Motor de sugerencias** (sin doc de épica formal) | Terminada | Las dos heurísticas activas (histórica + valores por defecto de contraparte), con múltiples rondas de corrección de bugs. | Mejoras estructurales de bajo riesgo recomendadas y nunca ejecutadas (extraer normalización a su propia clase). |
| **U — UX de un clic** | Terminada (mayormente) | Chips de confianza, aceptación rápida de sugerencias. | Algunos puntos menores no verificados en detalle. |
| **UI — Arquitectura de UI compartida** | Pendiente | — no iniciada. | Extracción de CSS/JS común (`wwwroot/shared/`) — el problema que buscaba resolver empeoró con más páginas agregadas. |
| **Planificación Mensual** | Terminada | Modelo completo, CRUD, copia de mes, resumen. | Su propio documento de alcance excluye explícitamente el matching contra Movimientos — se implementó igual (scope creep documentado, no destructivo). |
| **Centro de Auditoría** (sin épica formal) | Terminada (funcionalmente) | Reporte completo de sospechosos/mal clasificados, flujo de revisión humana. | — (documentado en PATCH-033, `docs/Architecture/CentroDeAuditoria.md`). |

---

## 6. ADRs

| ADR | Tema | Estado | Por qué |
|---|---|---|---|
| **ADR-001** | Modelo de clasificación en 4 dimensiones fijas | Parcialmente vigente | Implementado tal cual en el código, pero un análisis posterior encontró evidencia de que una de las 4 dimensiones (`MovementType`) no tiene consumidor real verificado — sin que exista todavía el ADR de reemplazo que el propio ADR-001 exige para poder modificarse. |
| **ADR-002** | Excel como mecanismo histórico de migración | Parcialmente vigente | El diagnóstico general (Excel no es la fuente de verdad) sigue vigente; la decisión específica de "mantener `group-reconciliation.html`" fue revertida (se eliminó la pantalla y su backend), revisión documentada dentro del propio archivo. |
| **ADR-003** | Separar consumo de tarjeta de pago de resumen | Vigente (dominio); UX parcialmente resuelta | El modelo de dominio (`FinancialImpact.DebtPayment` + `Counterparty.OwnCard`) está completo y correcto; la guía de UX que el propio ADR reconocía como pendiente ya tiene una primera pieza construida (PATCH-022: precarga de `DebtPayment` + hint al elegir contraparte `OwnCard`), acotada a ese caso puntual — el riesgo general que motivó el ADR (resto de `MovementType`) sigue latente hasta que se retome como parte de Épica N. |
| **ADR-004** | `FinancialAccount` antes que cuentas de inversión | Vigente | El orden de dependencia se respetó: `FinancialAccount` existe, `InvestmentAccount` todavía no. |
| **ADR-005** | `ImportBatch` como mecanismo de trazabilidad | Vigente (texto corregido en PATCH-030) | La entidad está completamente implementada y en uso; el documento ya refleja esto — antes decía "entidad no implementada". |
| **ADR-006** | Roadmap del MCP como compañero de investigación | Parcialmente vigente | Fases 1-2 implementadas; Fase 3 (IA local) implementada pero con nombres de herramientas distintos a los que sugería el ADR original. |
| **ADR-007** | Memoria del MCP (investigaciones) | Vigente (declaración de estado corregida en PATCH-030) | El diseño se respeta y está bien implementado (Fases 2-4: persistencia, tools CRUD, integración con Ollama); el documento ya lo refleja — antes declaraba explícitamente "ninguna fase implementada". Fase 5 sigue pendiente, correctamente marcada como tal. |

---

## 7. Documentación

**Fuente de verdad (vigentes, consultar primero):**
- **`docs/PROJECT_STATUS.md`** (este documento) — estado real y punto de entrada.
- `docs/RoadMaps/FinancialMcp-vNext.md` — roadmap por épicas (I-O); actualizado en PATCH-029 contra el estado real del código (antes marcaba J/L/O como planificadas y varios problemas de la sección 6 como abiertos, estando ya resueltos).
- `docs/Architecture/Architecture.md` — arquitectura formal (una línea puntual desactualizada, resto vigente).
- `docs/Architecture/EstadoMVP.md` — estado del MVP original, reemplaza explícitamente a tres documentos anteriores.
- `docs/Architecture/SimplificacionModeloClasificacion.md` (PATCH-028) — consolida `analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md` y `redisenoflujofuncional.md` (archivados), insumo base para la futura Épica N.
- `docs/Decisions/ADR-001` a `ADR-006`, `docs/Architecture/Decisions/ADR-007` — ver sección 6 para vigencia de cada uno.
- `docs/Epics/EpicaI-Importacion.md`, `EpicaO-ImportacionManual.md`, `Epica-PlanificacionMensual.md` — diseño de épicas concretas (con notas de estado desactualizadas en algunos casos, contenido técnico vigente).
- `docs/UX/ClassificationUX.md` — vigente; actualizado en PATCH-032 (campo de cuenta financiera de solo lectura, resolución automática de Épica J, controles del modal de clasificación no documentados hasta ahora).
- `docs/UserGuide/McpUserGuide.md` — vigente, catálogo de tools un paso atrás del código.
- `docs/Architecture/CentroDeAuditoria.md` (PATCH-033, nuevo) — arquitectura, flujo y limitaciones reales del Centro de Auditoría; no tenía documento de diseño propio hasta este patch.

**Históricos (tienen valor de referencia, no de verdad activa):**
- `docs/Archive/*` — correctamente archivado.
- Serie `PRS1/6/8/11/12` (motor de sugerencias, ya implementado).
- Serie `PRU1/3/4` y `analisisentidadcounterparty.md`, `PRUI1analisisarquitecturaui.md` (este último sigue siendo un plan vigente, no ejecutado).
- `docs/patch/enriquecimiento-tarjeta-debito.md`.

**No deberían archivarse todavía (verificado contra el código en PATCH-027 — corrige la evaluación anterior de esta misma tabla):**
- `docs/Architecture/analisisnavegacion.md` — propone unificar el shell con sidebar de `dashboard.html` en las pantallas secundarias; siguen usando un topbar aislado con "← Dashboard" (`movements.html`, `accounts.html`, `imports.html`, `counterparties.html`, `audit.html`, `planning.html`) — no implementado.
- `docs/Architecture/analisisproximaepicausabilidad.md` — de sus 6 ítems de alcance, al menos 3 siguen sin implementar: ruteo de `.xls` por contenido (`BbvaBankStatementImportHandler.CanHandle` sigue siendo por nombre de archivo), pantalla de administración de Categorías (no existe) y checkbox "recordar como default de esta contraparte" en el modal de clasificación (no existe en `movements.html`).

---

## 8. Arquitectura

**Capas declaradas:** Domain → Application → Infrastructure → Api/hosts (Clean Architecture). **En la práctica:**

- **Domain** — entidades y enums puros, sin dependencias externas. Se respeta estrictamente.
- **Application** — contiene los contratos (`IApplicationDbContext`, interfaces de servicios) y los Command/Handler de los módulos más nuevos (Planning, Investigations, Review, Audit). **No está aislada del ORM**: referencia `Microsoft.EntityFrameworkCore` completo (no solo abstracciones) y también `ClosedXML`/`PdfPig` directamente, porque los parsers de importación viven acá en vez de en Infrastructure.
- **Infrastructure** — implementaciones concretas (EF Core, parsers, motor de sugerencias, servicio de auditoría).
- **Api** — pensada como capa delgada, pero **no lo es de forma uniforme**: los módulos de Cuentas/Categorías/Contrapartes/Transacciones/Extractos tienen su lógica de negocio (validaciones, unicidad, normalización) escrita directamente en los Endpoints, sin pasar por Application.
- **No hay MediatR real** pese a que el nombre "Command/Handler" lo sugiere — son clases invocadas directamente, no un mediador (`ISender`/`IRequestHandler<T>`). Funciona igual, pero es un nombre engañoso para quien llega esperando MediatR de verdad.
- Sin patrón Repository (decisión deliberada), sin bus de eventos (decisión deliberada), sugerencias de matching efímeras y recalculadas en cada request (decisión deliberada) — las tres documentadas y razonables para el tamaño actual.

**Flujo de importación:**
```
Worker (carpeta vigilada) o API (subida manual)
        │
        ▼
IFileImportRouter.RouteAsync — mismo motor para ambos disparadores
        │
        ▼
IFileImportHandler (banco XLS / enriquecimiento débito XLSX / catch-all PDF-CSV-XLSX)
        │
        ▼
Parser específico (por nombre de archivo o, en PDF, por fingerprint de contenido)
        │
        ▼
Importer — idempotencia (ExternalId + índice único) + persistencia
        │
        ▼
ImportBatch / ImportBatchLine — trazabilidad de la corrida
```

**Flujo de clasificación:**
```
BankStatement / Transaction (ya normalizados)
        │
        ▼
IMovementLoader — adapta a FinancialMovement, excluye lo ya clasificado
        │
        ▼
IReviewEngine / ISuspicionDetector — detecta duplicados/splits sospechosos
        │
        ▼
IClassificationSuggestionService — sugiere clasificación (historial + defaults de contraparte)
        │
        ▼
ClassifyMovementCommand — el usuario confirma/ajusta → ClassifiedMovement (única fuente de verdad)
```

**Flujo de auditoría:** `AuditReportService` (compartido literalmente entre el servidor MCP y la API, para no duplicar lógica) orquesta `IReviewEngine` + `IMovementsQueryService` + `IClassificationSuggestionService` para producir el reporte de sospechosos y mal clasificados, más el flujo de revisión humana (`MovementAuditDecision`).

**Flujo de planificación:** independiente del resto — `PlanningMonth`/`PlanningItem` no tienen FK hacia `Category`/`Counterparty`/`FinancialAccount` por diseño. Opcionalmente cruza (solo lectura) contra `ClassifiedMovement` para sugerir coincidencias, sin escribir nunca de forma automática.

---

## 9. Base de datos

**Entidades principales:** `BankStatement`/`Transaction` (movimientos crudos por fuente), `ImportBatch`/`ImportBatchLine` (trazabilidad de importación), `FinancialAccount` (cuenta bancaria/tarjeta/inversión/efectivo), `Category` y `Counterparty` (catálogos administrables), `ClassifiedMovement`/`ClassifiedMovementItem` (única fuente de verdad para métricas y MCP), `MovementAuditDecision` (revisión humana de auditoría), `PlanningMonth`/`PlanningItem` (planificación mensual), `Investigation`/`InvestigationFinding`/`InvestigationReference` (memoria del MCP).

**Relaciones importantes:**
- `ClassifiedMovement` no referencia directamente `BankStatement`/`Transaction` por FK dura — usa un patrón polimórfico (`SourceEntityType` + `SourceId`), igual que `MovementAuditDecision` e `InvestigationReference`. Decisión deliberada (evita cascadas, permite agregar fuentes nuevas sin migrar), a costa de no tener integridad referencial garantizada por la base.
- `BankStatement`/`Transaction` tienen un `FinancialAccountId` **opcional** — hoy nada lo completa automáticamente al importar, se asigna a mano desde la UI.
- `Category`/`Counterparty` son catálogos planos (sin jerarquía real hoy, pese a que `Category` tiene una columna `ParentId` preparada para eso a futuro).

**Conceptos del dominio a entender antes de tocar código:**
- **Modelo de 4 dimensiones**: todo movimiento clasificado se describe por `MovementType`, `FinancialImpact`, `Category` y `Counterparty` — independientes entre sí.
- **`Confirmed` vs. `Reviewed`**: los dos únicos estados de un `ClassifiedMovement`, sin jerarquía entre ellos — solo trazabilidad de cómo se llegó a la clasificación.
- **Banco/tarjeta son la fuente de verdad**; el Excel histórico es solo un mecanismo de migración, nunca el modelo de datos de largo plazo.

---

## 10. MVP actual

**Forma parte del MVP hoy:**
- Importación de extractos BBVA (banco + débito + tarjeta de crédito PDF), con las salvedades de robustez ya conocidas (sección 12).
- Revisión y clasificación completa de movimientos en las 4 dimensiones, con sugerencias automáticas.
- Centro de Auditoría (sospechosos + mal clasificados + revisión humana).
- Planificación Mensual simple.
- Catálogos de Cuentas, Categorías y Contrapartes.
- Métricas mensuales (resumen, por categoría, tendencia, comparación) vía Dashboard y MCP.
- Servidor MCP funcional con herramientas de búsqueda, explicación, investigación y preguntas en lenguaje natural sobre el proyecto.

**Quedó explícitamente fuera (y no debería agregarse sin revisar primero la sección 12):**
- Soporte para bancos distintos de BBVA.
- Autenticación/multiusuario.
- Gastos fijos, presupuestos, cuentas de inversión (Fases 2 y 4 de la visión de largo plazo del README — todavía no iniciadas).
- Reglas de clasificación configurables sin recompilar.
- Cualquier UI/UX que no sea de escritorio.

---

## 11. Deuda técnica (solo lo realmente relevante)

- **Seguridad ausente**: sin autenticación en la API, credenciales de base de datos versionadas en el repositorio.
- **Robustez de importación desigual**: el pipeline catch-all tiene idempotencia sólida por contenido; el de extractos bancarios sigue siendo posicional (nombre de archivo + fila) y el ruteo de PDF Visa/Mastercard tiene un riesgo de colisión conocido y no corregido.
- **Performance del Centro de Auditoría**: el reporte completo recalcula el mismo resultado varias veces por invocación en vez de reutilizarlo.
- **Arquitectura declarada vs. real**: "CQRS + MediatR" es más un estilo que un hecho (no hay MediatR real) y no se aplica de forma uniforme (mitad de los módulos con Command/Handler, mitad con lógica de negocio directo en los Endpoints).
- **Frontend sin infraestructura compartida**: 8 páginas HTML con CSS/JS duplicado, con una divergencia de comportamiento ya demostrada entre páginas.
- **Cobertura de tests desequilibrada**: sólida en el motor de Sugerencias e Importación; prácticamente inexistente en Auditoría e Investigaciones — justo los dos módulos más nuevos y menos maduros.
- **Documentación que se desactualiza más rápido de lo que se corrige**: varios documentos que se autodeclaran "fuente de verdad" quedaron obsoletos por código escrito el mismo día — riesgo de proceso más que de código, pero ya generó al menos un caso real de scope creep (Planificación Mensual implementó algo que su propia épica excluía).

---

## 12. Riesgos conocidos (ordenados por impacto)

1. **Sin autenticación en la API** — cualquiera con acceso de red puede leer, modificar o borrar todos los datos financieros, incluida la importación de archivos arbitrarios.
2. **Credenciales de base de datos versionadas en git** — mala práctica que, si el proyecto se despliega alguna vez fuera de un entorno estrictamente local, se vuelve una exposición real.
3. **Pérdida silenciosa de movimientos por ambigüedad de parser PDF** (Visa vs. Mastercard) — un extracto real puede procesarse sin extraer ningún movimiento, sin ningún error visible.
4. ~~**Métricas potencialmente engañosas sin advertencia** — el Dashboard puede mostrar un resumen calculado sobre una fracción minoritaria de los movimientos reales del período, sin ningún indicador de cobertura.~~ **Mitigado (Épica L)**: el Dashboard ya muestra un indicador de cobertura de clasificación.
5. **Doble conteo de gasto (parcialmente mitigado)** — la UI ya guía el caso `OwnCard`/pago de resumen (PATCH-022, ver ADR-003), pero no el resto de la distinción entre pago de resumen de tarjeta y consumo para otros `MovementType`.
6. **Documentación "fuente de verdad" desactualizada** — riesgo de que futuras sesiones de trabajo (humanas o de IA) tomen decisiones sobre información que ya no es cierta.
7. **Cero cobertura de tests en Auditoría e Investigaciones** — riesgo de regresión silenciosa al seguir iterando sobre las piezas más nuevas del sistema.
8. **Envío de datos financieros a un servicio externo (OpenAI)** sin mecanismo de consentimiento visible en la UI, solo por configuración de servidor.

---

## 13. Próximas prioridades

Según el plan de implementación priorizado ya elaborado (`FinancialMcp-Plan-Implementacion.md`), en este orden:

1. **Integridad de datos e importación** — corregir el ruteo Visa/Mastercard y la fragilidad del `ExternalId` posicional de `BankStatement`. Es lo primero porque compromete la premisa fundacional del sistema (banco/tarjeta como fuente de verdad).
2. **Seguridad y endurecimiento** — autenticación de la API y saneamiento de credenciales. Bloqueante para cualquier uso fuera de una máquina local.
3. **Consistencia y confianza del producto** — ~~indicador de cobertura de clasificación~~ (hecho, Épica L), guía de UX para pago de tarjeta (parcial, ver ADR-003; falta el caso general de Épica N), ~~corrección de los documentos que hoy mienten sobre su propio estado (ADR-007;~~ `vNext.md` corregido en PATCH-029; `PROJECT_STATUS.md` sincronizado en PATCH-0078A; **ADR-005 y ADR-007 corregidos en PATCH-030**).
4. **Consolidación documental** — archivar lo obsoleto, actualizar lo vigente. ~~documentar el Centro de Auditoría (hoy sin ningún documento de diseño)~~ — Hecho (PATCH-033): `docs/Architecture/CentroDeAuditoria.md`.
5. **Cobertura de tests en módulos críticos** — Auditoría e Investigaciones antes de seguir construyendo sobre ellos.
6. **Deuda de arquitectura y performance** — eliminar la recomputación redundante del Centro de Auditoría, homogeneizar el patrón CQRS.
7. **Consolidación de frontend** — infraestructura de CSS/JS compartida entre las 8 páginas.
8. **Limpieza de código muerto** — bajo riesgo, se intercala en cualquier momento.
9. **Nuevas funcionalidades** (gastos fijos, presupuestos, multi-banco) — explícitamente al final, recién después de cerrar los ocho puntos anteriores.

---

## 14. Estado final

```
Proyecto:              FinancialMcp
Estado:                MVP funcional avanzado — no apto para producción
Versión aproximada:    Sin versionado formal (equivalente a pre-1.0 / v0.x)

Módulos terminados:    Revisión y clasificación, Motor de sugerencias, Planificación Mensual,
                        Cuentas/Categorías/Contrapartes, Métricas, Servidor MCP, Importación
                        (núcleo banco + catch-all), Centro de Auditoría (funcionalmente)

Módulos en desarrollo: Importación de tarjeta de crédito (riesgo de ruteo conocido),
                        Dashboard/Frontend (sin infraestructura compartida)

Módulos experimentales: Centro de Auditoría (sin tests; documentación de diseño ya
                        existe, ver docs/Architecture/CentroDeAuditoria.md),
                        Investigaciones/memoria del MCP (sin tests), Worker de insights (sin consumidor)

Épicas cerradas:        K, J (núcleo), L, O, S, U, Planificación Mensual

Épicas activas:         I (Confiabilidad de importación), Mejoras al flujo de importación

Épicas pendientes:      M (Inversiones), N (Simplificación de formulario),
                        UI (Arquitectura de UI compartida)

Principales riesgos:    Sin autenticación en la API · Credenciales de DB versionadas ·
                        Riesgo de pérdida silenciosa de movimientos (Visa/Mastercard) ·
                        Doble conteo de gasto sin guía de UX general (parcial, ver ADR-003)

Próximo objetivo:       Cerrar Integridad de datos + Seguridad (Epics P y Q del plan de
                        implementación) antes de considerar cualquier despliegue fuera de
                        un entorno local de un solo usuario.
```
