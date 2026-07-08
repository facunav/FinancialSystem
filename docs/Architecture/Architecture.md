# Arquitectura — FinancialMcp

> Documento vivo. Describe las capas y entidades tal como existen en el código a la fecha de esta versión, y las entidades futuras planificadas por `docs/RoadMaps/FinancialMcp-vNext.md`. Las entidades futuras están marcadas explícitamente como no implementadas — no se deben confundir con el estado actual.

---

## 1. Capas

El proyecto sigue Clean Architecture (Domain → Application → Infrastructure → Api), sin capas adicionales.

### Domain (`FinancialSystem.Domain`)

Entidades y modelos neutros. No depende de ninguna otra capa del proyecto ni de infraestructura (EF Core, HTTP, etc.).

Contiene:
* Entidades persistidas (`Transaction`, `BankStatement`, `LegacyImportedExpense`, `Category`, `Counterparty`, `ClassifiedMovement`, `ClassifiedMovementItem`).
* Enums de dominio (`MovementType`, `FinancialImpact`, `ClassificationStatus`, `ProcessingSource`, `MovementRole`, `SourceEntityType`).
* Modelos neutros de proceso (`FinancialMovement`, `ReviewResult` y su familia — `MatchedPair`, `UnmatchedMovement`, `SuspiciousGroup`, `MatchScore`).

**Qué NO pertenece acá:** acceso a base de datos, HTTP, lectura de archivos, cualquier dependencia de un paquete de infraestructura.

### Application (`FinancialSystem.Application`)

Contratos (interfaces), comandos, queries, handlers, y opciones de configuración. Depende solo de Domain.

Contiene:
* Contratos de servicios (`IMovementLoader`, `IMatchScorer`, `IMatchingRule`, `ISuspicionDetector`, `IReviewEngine`, `IApplicationDbContext`, `IDateTimeProvider`, `ITransactionNormalizer`, `IFileParser`, etc.).
* Comandos y sus handlers (`ClassifyMovementCommand`/`Handler`, `ConfirmMatchCommand`/`Handler`, `DiscardLegacyCandidatesCommand`/`Handler`, `RestoreLegacyCandidatesCommand`/`Handler`).
* Queries y sus handlers (`GetUnclassifiedMovementsQuery`/`Handler`).
* Opciones de configuración (`ReviewEngineOptions`, `FileIngestionOptions`, `OllamaOptions`, `OpenAIOptions`, `InsightsWorkerOptions`).

**Convención establecida (sin patrón Repository):** los handlers usan `IApplicationDbContext` directamente. No se reintroduce un `IRepository` intermedio — fue una decisión explícita al reconstruir el motor de revisión (ver `docs/Archive/ReviewClassificationEnginev2ADR.md`, sección 17), y se mantiene para todo lo nuevo.

**Qué NO pertenece acá:** implementaciones concretas de EF Core, parsers reales, lógica de HTTP/endpoints.

### Infrastructure (`FinancialSystem.Infrastructure`)

Implementaciones concretas de los contratos de Application.

Contiene:
* `AppDbContext` + configuraciones EF Core por entidad.
* Parsers de importación (PDF, XLS, CSV, Excel legacy).
* `MovementLoader`, `MatchScorer` + 4 `IMatchingRule`, `SuspicionDetector`, `ReviewEngine`.
* `FinancialMetricsService`.
* Registro de DI (`AddInfrastructure`).

**Qué NO pertenece acá:** endpoints HTTP, DTOs de request/response.

### Api (`FinancialMcp.Api`)

Endpoints Minimal API delgados.

Contiene:
* Endpoints agrupados por dominio (`CategoryEndpoints`, `CounterpartyEndpoints`, `MetricsEndpoints`, `MovementReviewEndpoints`).
* DTOs de request/response.
* `Program.cs` — composición de la aplicación.
* `wwwroot/` — UI estática servida directamente (sin SPA framework).

**Regla:** un endpoint arma un command/query desde el request, lo pasa al handler, y traduce el resultado a un status HTTP. Ninguna decisión de negocio (qué es válido, qué se persiste, cómo se calcula algo) vive en un archivo de `Endpoints/`.

### Hosts adicionales

* `FinancialSystem.Worker` — `ImportsFolderWatcherHostedService` (importación en background) y `TransactionInsightsWorker`. No consume el motor de revisión — clasificar es siempre una acción del usuario vía Api.
* `FinancialSystem.McpServer` — expone `FinancialTools` (4 herramientas), que delegan a `IFinancialMetricsService` sin lógica propia.

---

## 2. Entidades principales actuales

### `ClassifiedMovement`

Movimiento financiero ya clasificado. Única fuente de verdad para métricas y MCP. Cada fila representa una decisión de clasificación completa y verificada por el usuario — no hay estados intermedios ("pendiente") en esta tabla, porque lo pendiente simplemente no tiene fila todavía.

Campos clave: `EffectiveDate`, `TotalAmount` (siempre positivo, magnitud), `Currency`, `Description`, y las 4 dimensiones de clasificación (`MovementType`, `FinancialImpact`, `CategoryId`, `CounterpartyId` opcional). Estado (`ClassificationStatus`: `Confirmed`/`Reviewed`) y trazabilidad (`ProcessingSource`, `MatchScore` opcional, `AmountDelta` opcional).

### `ClassifiedMovementItem`

Referencia inmutable (snapshot) a un movimiento crudo que participó en una clasificación. `SourceEntityType` + `SourceId` identifican la fila original sin FK explícita (evita cascadas indeseadas sobre datos de importación). `Role` distingue `Reference` (banco/tarjeta, verdad contable) de `Candidate` (legacy, auxiliar).

### `Transaction`

Movimiento de tarjeta de crédito, extraído de PDF. Campos: `Date`, `Description`, `Amount` (ya en convención "positivo = gasto"), `Currency`, `CouponNumber`, `RawLine`, `SourceFile`.

**Brecha conocida (Épica I):** no tiene `ExternalId` ni ningún constraint de unicidad — reimportar el mismo PDF duplica cada fila. Ver `docs/Epics/EpicaI-Importacion.md`.

### `BankStatement`

Movimiento de cuenta bancaria, extraído de XLS. Convención de signo invertida respecto a `Transaction` (positivo = crédito/ingreso; se adapta al cargar en `FinancialMovement`). Tiene `ExternalId = SHA256(archivo|hoja|fila)` con índice único — es idempotente.

### `Counterparty`

Contraparte de un movimiento — "¿con quién o qué se relaciona?". Administrable por CRUD. Tiene valores sugeridos (`DefaultCategoryId`, `DefaultMovementType`, `DefaultFinancialImpact`) pensados para pre-cargarse al elegir la contraparte durante la clasificación — mecanismo ya modelado, todavía sin wiring en la UI (Épica K, PR K4). `CounterpartyType` ya incluye `OwnCard` (tarjeta propia, para pagos de resumen) e `Investment` (vehículo de inversión), relevantes para ADR-003 y ADR-004 respectivamente.

### `Category`

Categoría financiera — "¿para qué se usó el dinero?". Administrable por CRUD, con un set de categorías de sistema (`IsSystem=true`) sembradas por seed. `Name` es la clave técnica invariante; `DisplayName` es el label editable.

---

## 3. Entidades futuras (planificadas, no implementadas)

> Ninguna de las entidades de esta sección existe en el código todavía. Se documentan acá para fijar su diseño antes de implementarlas, no como referencia de algo ya construido.

### `ImportBatch` — Épica I

Registro de una corrida de importación: archivo, hash de contenido, handler que la procesó, timestamp, cantidad de filas insertadas/duplicadas/fallidas. Común a las 3 fuentes (banco, tarjeta, Excel), no específica de tarjeta — hoy ninguna de las 3 persiste este historial, aunque banco y Excel sí calculan la información equivalente en memoria (`ImportResult`) antes de descartarla.

### `FinancialAccount` — Épica J

Cuenta financiera explícita: `Type` (`Bank`/`Card`/`Investment`/`Cash`), nombre, y (eventualmente) FK opcional desde `BankStatement`/`Transaction`. Resuelve la brecha de no poder distinguir, a nivel de datos, de qué cuenta o tarjeta vino un movimiento.

### `InvestmentAccount` — Épica M

Extensión de `FinancialAccount` (`Type=Investment`) con saldo/valuación y sus propios movimientos internos (dividendos, intereses, compra/venta de activos). Estos movimientos internos **no** son `ClassifiedMovement` — son un dominio distinto con su propia semántica (tenencia, rendimiento), y solo la transferencia banco→inversión (que sí es un `ClassifiedMovement` con `FinancialImpact=InternalMovement`) cruza la frontera entre ambos modelos.

---

## 4. Qué lógica pertenece al dominio y qué no

**Pertenece a Domain/Application:**
* Qué combinación de las 4 dimensiones es válida.
* Cómo se calcula un score de matching, cómo se decide un umbral de confianza.
* Cómo se agregan movimientos para dar un resumen financiero (`FinancialMetricsService`, aunque vive en Infrastructure por ser la implementación concreta de `IFinancialMetricsService`, el contrato y las reglas de agregación son de negocio).
* Idempotencia de importación como *regla* ("un mismo `ExternalId` no se inserta dos veces") — la regla es de negocio aunque su implementación (índice único de Postgres) sea de infraestructura.

**No pertenece a Domain/Application — es infraestructura o presentación:**
* Cómo se extrae texto de un PDF (`IPdfTextExtractor`/`PdfPigTextExtractor`).
* El regex específico que reconoce una línea de transacción de un banco puntual.
* Cómo se serializa un DTO a JSON, o qué status HTTP corresponde a un error de negocio (eso lo decide el endpoint, traduciendo un resultado ya calculado por el handler).
* Formato de fecha/moneda para mostrar en pantalla.

**Zona gris explícita:** las reglas de scoring del motor de matching (pesos, umbrales) viven en `ReviewEngineOptions`, configurable — son parámetros de negocio pero externalizados como configuración en vez de hardcodeados, para poder ajustarlos sin recompilar.
