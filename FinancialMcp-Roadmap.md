FinancialMcp — Documento de Contexto del Proyecto


Este documento es la fuente de verdad del proyecto.
Antes de proponer cualquier cambio, leer este archivo completo.
Toda decisión de arquitectura, modelo de datos o flujo debe alinearse con la visión aquí definida.
Si una propuesta la contradice, explicarlo explícitamente antes de generar código.




Visión del Proyecto

FinancialMcp no es un registrador de gastos ni un conciliador bancario.

Es un asistente financiero personal inteligente cuyo objetivo es ayudar a una persona a entender qué pasó con su dinero, controlar lo que está pasando y planificar lo que viene.

La conciliación bancaria es solo el mecanismo para obtener datos financieros limpios, clasificados y confiables. Esos datos son la materia prima de todo lo demás.

El sistema tiene tres fuentes de datos reales hoy:


Tarjetas de crédito: extractos PDF del BBVA (Visa y Mastercard). Entidad: Transaction.
Cuenta bancaria: extractos XLS del BBVA (caja de ahorros / cuenta corriente). Entidad: BankStatement.
Registro manual: Excel personal con gastos dinámicos y fijos. Entidad: ManualExpense.


Las dos primeras son la fuente de verdad financiera. El Excel es auxiliar: enriquece clasificación, ayuda al matching y detecta olvidos.


Filosofía

Toda funcionalidad nueva debe responder al menos una de estas preguntas:


¿Qué pasó con mi dinero?
¿Qué está pasando con mi dinero?
¿Qué va a pasar con mi dinero?
¿Qué debería hacer con mi dinero?


Si una funcionalidad no aporta valor a ninguna de estas preguntas, no tiene lugar en el sistema.


Objetivo a Largo Plazo: Financial Copilot

El sistema debe evolucionar hacia un copiloto financiero personal capaz de:


Registrar y clasificar gastos automáticamente
Conciliar movimientos bancarios y de tarjeta
Administrar gastos fijos y presupuestos mensuales
Proyectar el flujo de caja futuro
Administrar inversiones y calcular rendimientos
Calcular el patrimonio neto en tiempo real
Ayudar a ahorrar y cumplir objetivos financieros
Sugerir decisiones financieras basadas en el historial
Responder preguntas en lenguaje natural usando IA



Roadmap por Fases

Fase 1 — Fundación de datos (EN PROGRESO)

Objetivo: tener datos limpios, clasificados y persistidos correctamente.


✅ Importación de extractos bancarios BBVA (XLS)
✅ Importación de resúmenes de tarjeta BBVA Visa y Mastercard (PDF)
✅ Importación de Excel manual (gastos dinámicos y fijos)
✅ Idempotencia de importaciones (SHA256 ExternalId)
✅ Motor de conciliación greedy con scoring compuesto (monto, fecha, descripción, método de pago)
✅ Conciliación N↔M manual
✅ Estados procesados: Confirmed y Reviewed
✅ Entidad Category con 11 categorías del sistema
✅ Enum FinancialImpact (RealExpense / InternalTransfer / DebtPayment / Income)
✅ ProcessedExpense con CategoryId y FinancialImpact obligatorios
✅ Descartar candidatos Excel sin eliminarlos físicamente
✅ Revisión en lote de múltiples movimientos
⬜ Watcher de carpeta de imports (Worker en background — parcialmente implementado)
⬜ Categorías creadas por el usuario (IsSystem = false)
⬜ Criterio de "mes cerrado" (todos los bancarios procesados)
⬜ Indicador de progreso del período


Fase 2 — Visibilidad financiera

Objetivo: el usuario puede ver su situación financiera de un vistazo.


⬜ Dashboard mensual (ingresos, gastos, ahorro, porcentaje de ahorro)
⬜ Gastos fijos: flujo de asociación a movimientos bancarios
⬜ Presupuesto mensual por categoría
⬜ Alertas de desvío de presupuesto
⬜ Evolución histórica de gastos por categoría
⬜ Gráficos de distribución de gastos


Fase 3 — Planificación

Objetivo: el usuario puede anticipar su situación financiera.


⬜ Proyección de flujo de caja (próximos 30/60/90 días)
⬜ Vencimientos próximos
⬜ Objetivos financieros (monto objetivo + fecha)
⬜ Cálculo de patrimonio neto
⬜ "Dinero realmente disponible" (líquido menos gastos fijos no pagados)


Fase 4 — Inversiones y patrimonio


⬜ Registro de inversiones (plazos fijos, FCI, acciones, crypto)
⬜ Rendimientos y performance
⬜ Distribución de activos
⬜ Evolución del patrimonio neto en el tiempo


Fase 5 — IA y MCP


⬜ MCP Server con herramientas financieras reales (ver sección MCP)
⬜ Integración con Ollama (modelo local)
⬜ Respuestas a consultas en lenguaje natural
⬜ Memoria persistente de objetivos y preferencias
⬜ Recomendaciones proactivas
⬜ Aprendizaje de reglas de clasificación



Dashboard Ideal (Fase 2)

Cuando el sistema madure, la pantalla principal debe mostrar en menos de un minuto:

┌─────────────────────────────────────────────────────────┐
│  Junio 2026                                             │
│                                                         │
│  Ingresos       $450.000    Gastos          $312.400    │
│  Ahorro          $137.600   % Ahorro           30,6%    │
│                                                         │
│  Pendientes de procesar:  8 movimientos                 │
│  Próximos vencimientos:   Internet $8.500 (15/07)       │
│                           Seguro   $15.200 (20/07)      │
│                                                         │
│  Dinero disponible:      $89.300                        │
│  Patrimonio:             $1.240.000                     │
│                                                         │
│  [ Alimentación 35% ][ Servicios 18% ][ Salud 12% ]    │
│  [ Transporte 9% ][ Otros 26% ]                         │
│                                                         │
│  ↗ Gastos +12% vs mes anterior                          │
└─────────────────────────────────────────────────────────┘


Modelo de Datos Central

Entidades de importación (inmutables post-import)

EntidadTablaOrigenRolTransactionTransactionsPDF tarjeta BBVAFuente de verdad (crédito)BankStatementBankStatementsXLS banco BBVAFuente de verdad (débito)ManualExpenseManualExpensesExcel personalAuxiliar (clasificación y matching)

Estas tablas nunca se modifican después de la importación. Son el registro histórico inmutable.

ManualExpense.IsDiscarded es la única excepción: permite marcar candidatos como descartados sin eliminarlos.

Entidad central para el MCP

ProcessedExpense
├── EffectiveDate        — fecha del movimiento original
├── TotalAmount          — Math.Abs(monto original), siempre positivo
├── Currency             — "ARS"
├── Description          — descripción del movimiento original
├── CategoryId           — FK a Category (OBLIGATORIO)
├── Category             — navegación
├── FinancialImpact      — RealExpense | InternalTransfer | DebtPayment | Income (OBLIGATORIO)
├── Status               — Confirmed | Reviewed
├── ProcessingSource     — ManualMatch | ConfirmedFromSuggestion | ManualReview
├── ReviewReason         — solo si Status = Reviewed
├── ReviewNotes          — texto libre opcional
├── MatchScore           — score del motor (null si fue match manual)
├── AmountDelta          — diferencia entre referencia y candidato
├── CreatedAt / ProcessedAt / ProcessedBy
└── Items[]              — ProcessedExpenseItem (snapshot inmutable)

ProcessedExpenseItem
├── SourceEntityType     — Transaction | BankStatement | ManualExpense
├── SourceId             — Guid del registro original
├── Role                 — Reference (banco/tarjeta) | Candidate (Excel)
└── Original*            — snapshot: Amount, Date, Description, Currency, SourceFile

Categorías

11 categorías del sistema (IsSystem = true, no eliminables):

Name (técnico)DisplayName (UI)FoodAlimentaciónHealthSaludTransportTransporteServicesServiciosInsuranceSegurosEducationEducaciónEntertainmentEntretenimientoSubscriptionSuscripcionesTransferTransferenciasIncomeIngresosOtherOtros

El usuario puede crear categorías propias (IsSystem = false). Pendiente de implementar en UI.

FinancialImpact — la clasificación más importante para el MCP

RealExpense      → dinero que salió del patrimonio definitivamente
                   Ejemplos: supermercado, farmacia, servicios, alquiler
                   Es el ÚNICO tipo que cuenta para "¿cuánto gasto?"

InternalTransfer → no modifica el patrimonio neto
                   Ejemplos: transferencia a cónyuge, entre cuentas propias

DebtPayment      → pago de deuda ya registrada (evita doble contabilización)
                   Ejemplos: pago del resumen de tarjeta, cuota de préstamo

Income           → ingreso al patrimonio
                   Ejemplos: sueldo, cobro freelance, reintegro


Módulos del Sistema

Conciliación (implementado)

Propósito: transformar movimientos bancarios crudos en gastos clasificados y persistidos.

Implementado:


Motor de matching greedy con 4 reglas: monto (45%), fecha (25%), descripción (20%), método de pago (10%)
Tolerancias configurables: 50 ARS absoluto, 2% relativo, ventana de 3 días
Matching N↔M manual (múltiples movimientos de cada lado)
ReconciliationOrchestrator: carga movimientos, ejecuta motor, devuelve sugerencias en memoria (sin persistir)
ReconciliationConfirmationService: persiste matches confirmados (batch y unitario)
ConfirmGroupHandler: confirma grupos N↔M
ReviewMovementHandler: marca movimientos sin contraparte (copia datos reales del movimiento original)
Detección de movimientos ya procesados antes de confirmar
Snapshot inmutable en ProcessedExpenseItem


Pendiente:


Matching inteligente por similitud avanzada (Levenshtein, embeddings)
Aprendizaje de reglas: si "TRANSFERENCIA A TATI" siempre termina en "Alimentación", sugerirlo
Confirmación automática de alta confianza (score > 0.85)
ReconciliationSuggestion como tabla de staging (diseñada, no implementada)


Importación (implementado)

Propósito: ingestar archivos del banco y del Excel personal de forma idempotente.

Implementado:


Parser BBVA banco: XLS → BankStatement
Parser BBVA Visa: PDF → Transaction
Parser BBVA Mastercard: PDF → Transaction
Parser Excel manual: hojas "Gastos Dinámicos" y "Gastos Fijos" → ManualExpense
FileImportRouter: detecta tipo de archivo y delega al handler correcto
FileIngestionOptions.BbvaBankStatementFilePatterns: lista de patrones glob para archivos del banco
Watcher de carpeta (Worker en background): detecta archivos nuevos automáticamente
Idempotencia por SHA256: re-importar el mismo archivo no genera duplicados


Patrones configurados para banco BBVA:

"Caja*.xls", "*ahorros*.xls", "*corriente*.xls", "Detalle_mov*.xls"

Pendiente:


Soporte de múltiples bancos
Importación vía API (upload desde UI)
Feedback visual del resultado de importación en la UI


Gastos Fijos (parcialmente implementado)

Propósito: planificar y hacer seguimiento de gastos recurrentes mensuales.

Implementado:


ManualExpense con campos MonthLabel, PaymentStatus, PaidAt para gastos fijos
Hoja "Gastos Fijos" del Excel se importa correctamente


Pendiente:


Flujo de vinculación a movimiento bancario real
Listado de gastos fijos del mes con estado Pagado/Pendiente
Integración en el Dashboard


IA / Insights (prototipo)

Propósito: análisis automático de transacciones con IA local.

Implementado:


OllamaFinancialInsightsService: llama a Ollama con las últimas N transacciones
OpenAIFinancialInsightsService: alternativa con OpenAI
TransactionInsightsWorker: worker en background que genera insights periódicamente
System prompt en español para análisis de patrones de gasto


Problema conocido: el worker opera sobre Transactions (tarjetas) crudas, sin clasificación. Debe migrar a ProcessedExpense para producir análisis útiles.

Pendiente:


Migrar insights a ProcessedExpense con CategoryId y FinancialImpact
Integrar resultados en la UI
Memoria persistente de conclusiones relevantes


MCP Server (esqueleto)

Propósito: exponer herramientas financieras a modelos LLM.

Implementado:


Servidor MCP funcional con ModelContextProtocol SDK
Un único tool: GetTransactionCountAsync (solo cuenta transacciones)


Pendiente: ver sección MCP más abajo.

UI de Conciliación (implementado)

Propósito: interfaz para procesar movimientos manualmente.

Implementado:


Doble grilla: Banco/Tarjeta (izquierda) ↔ Excel/Manual (derecha)
Selección múltiple para grupos N↔M
Modal de Review con categoría, impacto financiero y motivo
Modal de Confirm con categoría e impacto financiero
Revisión en lote: seleccionar varios movimientos y clasificar con un solo modal
Descartar candidatos Excel (sin eliminar físicamente)
Barra contextual de acciones que aparece al seleccionar
Filtro por texto en ambas columnas
Balance en tiempo real con indicador de diferencia



MCP (Model Context Protocol)

Principio fundamental

El MCP no contiene lógica de negocio. Solo expone herramientas que un LLM puede llamar. La lógica permanece en los servicios de aplicación.

LLM (Claude / Ollama)
    └── llama herramientas del MCP Server
           └── MCP Server delega a servicios de Application
                    └── Application accede a ProcessedExpenses (DB)

Herramientas que deben implementarse (Fase 5)

GetMonthlyExpenses(month, year)
  → SUM(TotalAmount) WHERE FinancialImpact = RealExpense AND period = month/year

GetExpensesByCategory(from, to)
  → GROUP BY Category.DisplayName WHERE FinancialImpact = RealExpense

GetCategoryTrend(categoryName, months)
  → evolución mensual de una categoría

GetMonthlyNetBalance(month, year)
  → Income - RealExpense del período

GetPendingFixedExpenses(month, year)
  → gastos fijos con PaymentStatus = Pendiente

GetTopExpenses(month, year, limit)
  → los N gastos más grandes del mes

GetSavingsRate(month, year)
  → (Income - RealExpense) / Income * 100

GetPatrimonySnapshot()
  → suma de todos los activos conocidos

Preguntas que el MCP debe poder responder


¿Cuánto gasté en Salud este año?
¿Cuánto gasto en cigarrillos por mes?
¿Cuánto aumentaron mis gastos?
¿Cuáles son mis categorías más costosas?
¿Qué gastos olvidé registrar?
¿Cuánto necesito para mantener mi nivel de vida?
¿Puedo ahorrar $200.000 este mes?
¿Cuánto gasté el mes pasado vs el anterior?



IA

Principios


La IA se ejecuta mediante Ollama (modelo local, sin datos en la nube) o OpenAI como fallback
El modelo no accede directamente a la DB: solo usa herramientas del MCP
El modelo responde en español
Las respuestas deben ser concretas, basadas en datos reales del usuario


Memoria persistente (Fase 5)

No se almacenan conversaciones completas. Solo resúmenes estructurados:


Objetivos financieros declarados ("quiero ahorrar $1M en 12 meses")
Preferencias ("prefiero invertir en plazos fijos")
Hábitos detectados ("gasta más los viernes")
Decisiones importantes ("refinancié el préstamo en junio")
Notas personales ("el seguro del auto vence en agosto")


La memoria se consulta via herramientas del MCP y enriquece el contexto de cada conversación.


Arquitectura

Stack tecnológico


Runtime: .NET 9/10
DB: PostgreSQL (Npgsql + EF Core)
PDF: PdfPig
Excel: NPOI (XLS) + ClosedXML (XLSX)
IA local: Ollama (HTTP)
IA cloud: OpenAI (fallback)
MCP: ModelContextProtocol SDK


Estructura de proyectos

src/
  FinancialSystem.Domain          → Entidades, enums, modelos de dominio
  FinancialSystem.Application     → Orquestador, motor, handlers, interfaces
  FinancialSystem.Infrastructure  → EF Core, repositorios, importadores, parsers
  FinancialMcp.Api                → Minimal API endpoints + UI estática HTML

hosts/
  FinancialSystem.McpServer       → Servidor MCP (stdio)
  FinancialSystem.Worker          → Worker: watcher de imports + insights en background

Principios que deben respetarse siempre


Clean Architecture: el Domain no conoce Infrastructure, Application no conoce Api
SOLID: cada clase tiene una única responsabilidad
Bajo acoplamiento: cambiar un parser no debe afectar el motor de conciliación
Idempotencia: las importaciones pueden repetirse sin efectos secundarios
Inmutabilidad de fuentes: Transaction, BankStatement y ManualExpense no se modifican post-import
Sin lógica en endpoints: los endpoints solo orquestan, la lógica vive en handlers y servicios
Código mantenible: evitar duplicación, preferir composición sobre herencia


Decisiones de modelo de datos congeladas


Los movimientos bancarios y de tarjeta son la fuente de verdad financiera
ManualExpense.Description es texto libre del usuario, no una categoría normalizada
CategoryId y FinancialImpact son obligatorios en todo ProcessedExpense
ProcessedExpense es la única tabla que el MCP consulta para métricas
ProcessedExpenseItem guarda snapshots inmutables (no FKs a las tablas fuente)
Las categorías son entidades propias con Name (técnico) y DisplayName (UI)
PeriodStart/PeriodEnd no existen — el MCP filtra siempre por EffectiveDate
No existe estado "Pending" en ProcessedExpense — toda fila es verdad financiera verificada



Flujo Operativo Mensual

El flujo que el usuario ejecuta a fin de mes:

1. IMPORTAR
   └── Copiar extracto bancario .xls en carpeta de imports
   └── Copiar resumen de tarjeta .pdf en carpeta de imports
   └── Copiar Excel personal en carpeta de imports
   └── El Worker detecta los archivos y los importa automáticamente

2. PROCESAR DÉBITOS (pestaña Débito en la UI)
   └── Revisar sugerencias automáticas del motor
   └── Confirmar matches (con categoría e impacto financiero)
   └── Marcar como Reviewed los que no tienen contraparte Excel
   └── Descartar candidatos Excel que no corresponden

3. PROCESAR TARJETA (pestaña Tarjeta en la UI)
   └── Los gastos de tarjeta raramente tienen contraparte Excel
   └── Seleccionar en lote y marcar como Reviewed (Gasto real)
   └── Matchear manualmente solo en casos excepcionales

4. VERIFICAR GASTOS FIJOS
   └── Confirmar cuáles fueron pagados
   └── Vincular al movimiento bancario correspondiente

5. CERRAR EL MES
   └── Todos los bancarios procesados (ninguno en Pending)
   └── Todos los datos listos para el MCP y el Dashboard

→ DATOS LISTOS PARA ANÁLISIS


Regla de Consistencia para Futuras Sesiones

Antes de proponer cualquier cambio:


Leer este documento completo
Verificar que la propuesta responde al menos una pregunta de la filosofía
Verificar que no contradice ninguna decisión congelada de modelo de datos
Priorizar cambios que acerquen al sistema a la visión del Financial Copilot
No proponer refactors masivos sin beneficio claro y medible
Preferir PRs pequeños, seguros y verificables
Si algo en el código contradice este documento, señalarlo antes de continuar


Cuando se detecten oportunidades de mejora alineadas con la visión, proponerlas aunque no hayan sido solicitadas explícitamente.