namespace FinancialSystem.McpServer;

/// <summary>
/// Registro explícito, escrito a mano, de las tools MCP existentes hasta 0023 —
/// preparación para que un modelo (Ollama u otro) pueda en el futuro saber qué tools
/// existen y cuándo usarlas. Esto NO es tool calling: nada acá ejecuta una tool, elige
/// una tool, ni encadena llamadas. Es solo metadata estática, consumida hoy únicamente
/// por ListAvailableTools (RegistryTools.cs).
///
/// SIN REFLEXIÓN, SIN ESCANEO DE ENSAMBLADOS, SIN GENERACIÓN AUTOMÁTICA (a propósito):
///   Reflejar [McpServerTool]/[Description] por reflexión evitaría mantener esta lista
///   a mano, pero acoplaría este registro a la forma exacta de los atributos del SDK
///   ModelContextProtocol y produciría descripciones pensadas para el protocolo (texto
///   largo, para un LLM que ya está "dentro" de la tool), no un resumen curado de
///   "cuándo usar cada una" pensado para que otro modelo decida entre tools sin haber
///   leído el código. Se prefiere explícito y editable a mano, tal como se pidió.
///
/// Si se agrega o cambia una tool, esta lista se actualiza a mano en el mismo PR --
/// no hay ningún mecanismo que la mantenga sincronizada automáticamente.
/// </summary>
public static class ToolRegistry
{
    public sealed record ToolEntry(
        string ClassName,
        string Name,
        string ShortDescription,
        string WhenToUse,
        string Parameters,
        string Returns);

    public static readonly IReadOnlyList<ToolEntry> Tools = new List<ToolEntry>
    {
        // ── SystemTools ──────────────────────────────────────────────────────
        new(
            "SystemTools", "Ping",
            "Verifica que el servidor MCP está corriendo y responde.",
            "Confirmar la conexión desde un cliente MCP antes de invocar cualquier otra tool.",
            "(ninguno)",
            "El texto literal 'pong'."),
        new(
            "SystemTools", "Version",
            "Versión de la app, commit de origen y fecha de compilación.",
            "Confirmar qué versión del MCP está corriendo antes de reportar un bug.",
            "(ninguno)",
            "Texto con AssemblyVersion, InformationalVersion, Commit y fecha de build."),
        new(
            "SystemTools", "Health",
            "Estado básico del servidor: conectividad a la base y última migración aplicada.",
            "Diagnosticar si el MCP puede leer datos antes de investigar un problema puntual.",
            "(ninguno)",
            "Texto con estado de conexión, proveedor, migración aplicada y hora UTC."),

        // ── MovementTools ────────────────────────────────────────────────────
        new(
            "MovementTools", "SearchMovements",
            "Busca movimientos (banco y tarjeta, pendientes y clasificados) en un rango de fechas, con filtros opcionales.",
            "Encontrar uno o varios movimientos cuando no se conoce su Id exacto.",
            "from?, to? (yyyy-MM-dd, máx. 90 días), text?, categoryId?, counterpartyId?, " +
            "financialImpact?, movementType?, status?, currency?, minAmount?, maxAmount?",
            "Lista de movimientos que matchean los filtros, en texto estructurado."),
        new(
            "MovementTools", "GetMovement",
            "Detalle completo de un movimiento identificado por su origen y su Id.",
            "Ya se conoce el SourceEntityType+SourceId (ej. lo devolvió SearchMovements) y se " +
            "necesita el detalle completo, con su clasificación si la tiene.",
            "sourceEntityType (Transaction|BankStatement), sourceId (Guid)",
            "Datos del movimiento, información técnica, clasificación y grupo de matching."),
        new(
            "MovementTools", "ExplainMovement",
            "Explicación estructurada y estable de un movimiento -- mismas secciones siempre.",
            "Necesitar el mismo detalle que GetMovement pero organizado por tema, con una " +
            "sección de observaciones derivadas de datos ya existentes (sin IA).",
            "sourceEntityType (Transaction|BankStatement), sourceId (Guid)",
            "Texto con secciones Movimiento/Clasificación/Procesamiento/Matching/Observaciones."),
        new(
            "MovementTools", "ExplainClassification",
            "Responde puntualmente por qué un movimiento terminó con su clasificación actual.",
            "La pregunta es específicamente sobre el origen de la clasificación, no sobre el " +
            "movimiento en general (para eso, ExplainMovement).",
            "sourceEntityType (Transaction|BankStatement), sourceId (Guid)",
            "Texto explicando el origen de la clasificación, con la salvedad si no está disponible."),

        // ── AuditTools ───────────────────────────────────────────────────────
        new(
            "AuditTools", "FindSuspiciousMovements",
            "Grupos de movimientos sospechosos detectados por el motor de revisión existente (ISuspicionDetector).",
            "Auditar un período buscando duplicados, montos inusuales u otras señales ya cubiertas por el motor.",
            "from?, to? (yyyy-MM-dd, máx. 90 días), financialAccountId?",
            "Lista de grupos sospechosos con sus movimientos, en texto estructurado."),
        new(
            "AuditTools", "FindMisclassifiedMovements",
            "Movimientos ya clasificados que podrían estar mal clasificados, con motivos concretos.",
            "Auditar un período buscando clasificaciones que no coinciden con lo esperado " +
            "(defaults de contraparte, sugerencias del motor de clasificación).",
            "from?, to? (yyyy-MM-dd, máx. 90 días), financialAccountId?",
            "Lista de movimientos con sus motivos (Origen/Dimensión/Valor actual/Valor sugerido)."),

        // ── ProjectTools ─────────────────────────────────────────────────────
        new(
            "ProjectTools", "ListArchitectureDocuments",
            "Lista los documentos .md de docs/Architecture/.",
            "Saber qué documentos existen antes de pedir uno con ReadArchitectureDocument.",
            "(ninguno)",
            "Lista de nombres de archivo relativos a docs/Architecture/."),
        new(
            "ProjectTools", "ReadArchitectureDocument",
            "Contenido crudo (sin interpretar) de un documento de docs/Architecture/.",
            "Ya se sabe el nombre del documento (por ListArchitectureDocuments) y se necesita su contenido completo.",
            "fileName (nombre relativo a docs/Architecture/, ej. 'Architecture.md')",
            "El contenido del archivo tal cual está guardado."),
        new(
            "ProjectTools", "SearchDocumentation",
            "Búsqueda de texto literal (sin IA, sin ranking) en todos los .md de docs/.",
            "No se sabe en qué documento buscar y se quiere encontrar dónde se menciona un término.",
            "query (texto a buscar, contiene, sin distinguir mayúsculas)",
            "Coincidencias por archivo y número de línea, como un grep simple."),
        new(
            "ProjectTools", "GetRoadmap",
            "Contenido crudo de docs/RoadMaps/FinancialMcp-vNext.md, la fuente de verdad del roadmap.",
            "Se necesita el estado y la visión general del proyecto tal como está documentada.",
            "(ninguno)",
            "El contenido del roadmap tal cual está guardado."),
        new(
            "ProjectTools", "AskProjectKnowledge",
            "Responde una pregunta sobre el proyecto usando Ollama, con el roadmap como contexto.",
            "La pregunta requiere una respuesta en lenguaje natural sobre el proyecto, no solo " +
            "el texto crudo de un documento (para eso, GetRoadmap/ReadArchitectureDocument).",
            "question (pregunta en lenguaje natural)",
            "La respuesta de Ollama, o un error si Ollama no está disponible."),

        // ── ConfigurationTools ───────────────────────────────────────────────
        new(
            "ConfigurationTools", "ListFinancialAccounts",
            "Lista todas las cuentas financieras configuradas (activas e inactivas).",
            "Saber qué cuentas existen, ej. antes de filtrar otras tools por financialAccountId.",
            "(ninguno)",
            "Lista de cuentas con Id/Nombre/Tipo/Moneda/Estado."),
        new(
            "ConfigurationTools", "ListCategories",
            "Lista todas las categorías configuradas (activas e inactivas).",
            "Saber qué categorías existen antes de filtrar otras tools por categoryId.",
            "(ninguno)",
            "Lista de categorías con Id/Nombre/Tipo (Sistema o Usuario)/Estado."),
        new(
            "ConfigurationTools", "ListCounterparties",
            "Lista todas las contrapartes configuradas (activas e inactivas), sin joins.",
            "Saber qué contrapartes existen antes de filtrar otras tools por counterpartyId.",
            "(ninguno)",
            "Lista de contrapartes con Id/Nombre/Tipo/Estado."),
        new(
            "ConfigurationTools", "GetCounterparty",
            "Detalle completo de una contraparte, incluyendo sus defaults configurados.",
            "Ya se conoce el Id de la contraparte y se necesitan sus defaults de clasificación.",
            "id (Guid de la contraparte)",
            "Datos de la contraparte y sus defaults (categoría/tipo/impacto)."),
        new(
            "ConfigurationTools", "SearchCounterparties",
            "Busca contrapartes por nombre (contiene, sin distinguir mayúsculas).",
            "No se conoce el Id exacto de la contraparte, solo parte de su nombre.",
            "text (texto a buscar en el nombre)",
            "Lista de contrapartes que matchean, con Id/Nombre/Tipo/Estado."),

        // ── InvestigationTools ───────────────────────────────────────────────
        new(
            "InvestigationTools", "CreateInvestigation",
            "Crea una investigación nueva en estado Open.",
            "Empezar a registrar memoria sobre una pregunta o hipótesis nueva.",
            "question, tags? (etiquetas separadas por coma)",
            "InvestigationId, Status y CreatedAt de la investigación creada."),
        new(
            "InvestigationTools", "LinkMovement",
            "Asocia un movimiento existente a una investigación ya creada (idempotente).",
            "La investigación necesita referenciar un movimiento puntual como parte de su contexto.",
            "investigationId, sourceEntityType (Transaction|BankStatement), sourceId",
            "InvestigationId/SourceEntityType/SourceId y el resultado (Linked o AlreadyLinked)."),
        new(
            "InvestigationTools", "AddFinding",
            "Registra un hallazgo (texto libre) en una investigación ya creada.",
            "Se encontró algo puntual durante la investigación que vale la pena dejar registrado.",
            "investigationId, text (texto del hallazgo)",
            "InvestigationId, FindingId y CreatedAt del hallazgo creado."),
        new(
            "InvestigationTools", "GetInvestigation",
            "Información completa de una investigación: datos generales, referencias y hallazgos.",
            "Se necesita revisar todo el estado actual de una investigación puntual.",
            "investigationId",
            "Datos generales, lista de referencias y lista de hallazgos ordenados por fecha."),
        new(
            "InvestigationTools", "UpdateInvestigationStatus",
            "Cambia el estado de una investigación (Open/InProgress/Resolved/Discarded).",
            "La investigación avanzó de etapa, o se llegó a una conclusión (Resolved exige conclusion).",
            "investigationId, status (Open|InProgress|Resolved|Discarded), conclusion? (obligatoria si status=Resolved)",
            "InvestigationId, Status y UpdatedAt."),
        new(
            "InvestigationTools", "SearchInvestigations",
            "Busca investigaciones por status exacto, tag (contiene) y/o texto libre en Question/Conclusion.",
            "No se conoce el Id de la investigación, o se quiere ver varias a la vez (ej. todas las abiertas).",
            "status?, tag?, text? -- todos opcionales, sin ninguno devuelve todas",
            "Lista de investigaciones con datos generales y conteo de hallazgos/referencias."),
        new(
            "InvestigationTools", "AskInvestigation",
            "Responde una pregunta sobre una investigación usando Ollama, con la investigación y " +
            "sus movimientos referenciados como contexto real.",
            "La pregunta requiere razonar sobre los datos reales de la investigación (ej. '¿por qué " +
            "este movimiento parece mal clasificado?'), no solo consultarlos tal cual (para eso, GetInvestigation).",
            "investigationId, question (pregunta en lenguaje natural)",
            "La respuesta de Ollama, 'InvestigationNotFound', o un error si Ollama no está disponible."),
    };
}
