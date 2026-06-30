# FinancialMcp — Documento de Contexto del Proyecto

> **Este documento es la fuente de verdad del proyecto.**
> Antes de proponer cualquier cambio, leer este archivo completo.
> Toda decisión de arquitectura, modelo de datos o flujo debe alinearse con la visión aquí definida.
> Si una propuesta la contradice, explicarlo explícitamente antes de generar código.
>
> **Versión 2.0** — Reemplaza el enfoque centrado en "conciliación bancaria" por el enfoque
> centrado en "revisión y clasificación de movimientos financieros". Ver sección de Migración
> al final para el historial de este cambio.

---

## Visión del Proyecto

FinancialMcp no es un registrador de gastos ni un conciliador bancario.

Es un **asistente financiero personal inteligente** cuyo objetivo es ayudar a una persona a entender qué pasó con su dinero, controlar lo que está pasando y planificar lo que viene.

### Cambio de concepto principal (v2.0)

La aplicación deja de estar centrada en la **conciliación** (emparejar un movimiento bancario con un registro externo). A partir de ahora el concepto central es la **revisión y clasificación de movimientos financieros**.

Todo movimiento que entra al sistema —sin importar su origen— pasa por un proceso de revisión donde el usuario lo clasifica en sus dimensiones correspondientes (ver "Nuevo Modelo de Clasificación"). El matching contra un registro externo (Excel u otra fuente) es solo una **ayuda opcional** para sugerir esa clasificación más rápido cuando existe una coincidencia. Nunca es el fin del proceso — el fin del proceso es siempre un movimiento clasificado.

Esto significa: un movimiento revisado sin ninguna contraparte externa es un resultado tan válido y completo como uno que sí tuvo coincidencia. No hay una jerarquía entre "Confirmado por match" y "Revisado manualmente" — ambos son, en esencia, **movimientos clasificados**. La distinción técnica de cómo llegaron a estarlo (`ProcessingSource`) se mantiene solo para trazabilidad, no como diferencia funcional de cara al usuario.

### Eliminar la dependencia del Excel (v2.0)

El Excel personal **deja de ser parte de la visión futura del sistema**. Su rol pasa a ser exclusivamente el de un **mecanismo de migración o compatibilidad temporal**, útil mientras el usuario transiciona datos históricos hacia el sistema.

El objetivo de mediano plazo es que toda la información viva dentro de FinancialMcp: movimientos bancarios, tarjeta, gastos fijos y cualquier registro manual se cargan y gestionan directamente en la aplicación, sin pasar por un Excel intermedio.

Esto no implica eliminar inmediatamente la capacidad de importar Excel — implica que el diseño del sistema no debe asumir su existencia como dependencia estructural. Cualquier flujo nuevo (gastos fijos, contrapartes, categorías) se diseña sin depender de datos provenientes de Excel.

---

## Filosofía

Toda funcionalidad nueva debe responder al menos una de estas preguntas:

- ¿Qué **pasó** con mi dinero?
- ¿Qué **está pasando** con mi dinero?
- ¿Qué **va a pasar** con mi dinero?
- ¿Qué **debería hacer** con mi dinero?

Si una funcionalidad no aporta valor a ninguna de estas preguntas, no tiene lugar en el sistema.

---

## Objetivo a Largo Plazo: Financial Copilot

El sistema debe evolucionar hacia un copiloto financiero personal capaz de:

- Registrar y clasificar movimientos automáticamente
- Revisar y clasificar movimientos bancarios y de tarjeta (la conciliación es una ayuda, no el fin)
- Administrar gastos fijos y presupuestos mensuales
- Proyectar el flujo de caja futuro
- Administrar inversiones y calcular rendimientos
- Calcular el patrimonio neto en tiempo real
- Ayudar a ahorrar y cumplir objetivos financieros
- Sugerir decisiones financieras basadas en el historial
- Responder preguntas en lenguaje natural usando IA

---

## Roadmap por Fases

### Fase 1 — Fundación de datos (EN PROGRESO)

Objetivo: tener datos limpios, clasificados y persistidos correctamente.

- ✅ Importación de extractos bancarios BBVA (XLS)
- ✅ Importación de resúmenes de tarjeta BBVA Visa y Mastercard (PDF)
- ✅ Importación de Excel manual (solo como mecanismo de migración/compatibilidad — ver nota abajo)
- ✅ Idempotencia de importaciones (SHA256 ExternalId)
- ✅ Motor de sugerencias de matching (ayuda opcional, no obligatoria, para la revisión)
- ✅ Revisión N↔M manual (uno o más movimientos bancarios vinculados a uno o más registros externos)
- ✅ Estados de movimiento clasificado: `Confirmed` (con coincidencia) y `Reviewed` (sin coincidencia)
- ✅ Entidad `Category` con 11 categorías del sistema (seed inicial)
- ✅ Enum `FinancialImpact` (Gasto / Ingreso / Movimiento interno / Financiación-Pago de deuda)
- ✅ `ProcessedExpense` con CategoryId y FinancialImpact obligatorios
- ✅ Descartar candidatos de Excel sin eliminarlos físicamente (uso transitorio, ligado a la fase de migración)
- ✅ Revisión en lote de múltiples movimientos
- ⬜ **(v2.0)** Eliminar `ReviewReason` del modelo — reemplazado por comentario libre
- ⬜ **(v2.0)** Dimensión "Tipo de movimiento" (Compra, Transferencia, Pago, Cobro, Comisión, Interés, Reintegro, Ajuste, Otro)
- ⬜ **(v2.0)** Entidad `Category` administrable por CRUD (crear, editar, desactivar, ordenar)
- ⬜ **(v2.0)** Entidad `Counterparty` (contraparte) administrable por CRUD, con valores sugeridos por defecto
- ⬜ **(v2.0)** Sugerencia automática de clasificación cuando la contraparte ya es conocida
- ⬜ Categorías jerárquicas (futuro, dependiente de CRUD de categorías)
- ⬜ Watcher de carpeta de imports (Worker en background — parcialmente implementado)
- ⬜ Criterio de "mes cerrado" (todos los movimientos del período clasificados)
- ⬜ Indicador de progreso del período

### Fase 2 — Visibilidad financiera

Objetivo: el usuario puede ver su situación financiera de un vistazo.

- ✅ Capa de métricas financieras (`IFinancialMetricsService`)
- ✅ Endpoints de métricas: resumen, por categoría, tendencia mensual, comparación
- ✅ Dashboard principal v1 (KPIs, distribución por categoría, tendencia, comparación)
- ⬜ **(v2.0)** Módulo de Gastos Fijos completo (ver sección dedicada más abajo) — ya no depende del Excel
- ⬜ Presupuesto mensual por categoría
- ⬜ Alertas de desvío de presupuesto
- ⬜ Próximos vencimientos en el Dashboard
- ⬜ Dinero realmente disponible (líquido menos gastos fijos no pagados)

### Fase 3 — Planificación

Objetivo: el usuario puede anticipar su situación financiera.

- ⬜ Proyección de flujo de caja (próximos 30/60/90 días), alimentada por Gastos Fijos
- ⬜ Vencimientos próximos y recordatorios
- ⬜ Objetivos financieros (monto objetivo + fecha)
- ⬜ Cálculo de patrimonio neto

### Fase 4 — Inversiones y patrimonio

- ⬜ Registro de inversiones (plazos fijos, FCI, acciones, crypto)
- ⬜ Rendimientos y performance
- ⬜ Distribución de activos
- ⬜ Evolución del patrimonio neto en el tiempo

### Fase 5 — IA y MCP

- ⬜ MCP Server con herramientas financieras reales (ver sección MCP actualizada)
- ⬜ Integración con Ollama (modelo local)
- ⬜ Respuestas a consultas en lenguaje natural
- ⬜ Memoria persistente de objetivos y preferencias
- ⬜ Recomendaciones proactivas
- ⬜ Aprendizaje de reglas de clasificación basado en contrapartes conocidas

---

## Nuevo Modelo de Clasificación (v2.0)

Todo movimiento clasificado se describe mediante **cuatro dimensiones independientes**. Cada una responde una pregunta distinta y no debe mezclarse con las demás.

### Tipo de Movimiento — "¿Qué ocurrió?"

Describe la naturaleza técnica del movimiento.

```
Compra
Transferencia
Pago
Cobro
Comisión
Interés
Reintegro
Ajuste
Otro
```

### Impacto Financiero — "¿Cómo afecta mi patrimonio?"

Se mantiene el concepto ya implementado, con nomenclatura ajustada:

```
Gasto                        (antes: RealExpense)
Ingreso                      (antes: Income)
Movimiento interno           (antes: InternalTransfer)
Financiación / Pago de deuda (antes: DebtPayment)
```

Solo "Gasto" cuenta para las métricas de gasto neto. "Ingreso" se suma al patrimonio. "Movimiento interno" y "Financiación/Pago de deuda" no modifican el patrimonio neto (evitan doble contabilización).

### Categoría — "¿Para qué se usó el dinero?"

Deja de estar hardcodeada como conjunto fijo. Pasa a ser una **entidad administrable**:

- El sistema inicializa un conjunto de categorías por defecto mediante seed de datos (las 11 actuales: Alimentación, Salud, Transporte, Servicios, Seguros, Educación, Entretenimiento, Suscripciones, Transferencias, Ingresos, Otros)
- El usuario puede **crear, editar, desactivar y ordenar** categorías propias
- Las categorías del sistema (`IsSystem = true`) no son eliminables, pero sí pueden desactivarse
- A futuro: soporte de categorías jerárquicas (categoría padre / subcategoría)

### Contraparte — "¿Con quién o qué se relaciona el movimiento?"

Reemplaza cualquier concepto previo de "beneficiario". Una contraparte es la entidad del otro lado del movimiento.

Tipos de contraparte:

```
Persona
Comercio
Empresa
Banco
Servicio
Gobierno
Cuenta propia
Tarjeta
Inversión
Otro
```

Las contrapartes son administrables mediante CRUD. Cada contraparte puede tener **valores sugeridos por defecto**:

- Categoría por defecto
- Tipo de movimiento por defecto
- Impacto financiero por defecto

**Comportamiento esperado:** durante la revisión de un movimiento, si el sistema reconoce (o el usuario selecciona) una contraparte ya existente, la UI debe pre-cargar esos valores sugeridos automáticamente, reduciendo la fricción de clasificar movimientos recurrentes (ej. "Farmacia Amancay" siempre sugiere Categoría=Salud, Tipo=Compra, Impacto=Gasto).

### Eliminación del concepto de "Motivo" (v2.0)

El combo de `ReviewReason` (Transferencia personal, Regalo, Movimiento interno, Comisión bancaria, Interés, Otro) **deja de existir como enum cerrado**. Sus valores quedan cubiertos por la combinación de Tipo de Movimiento + Impacto Financiero + Contraparte, que es información más rica y reutilizable.

En su lugar:

- **Comentario libre**: campo de texto opcional para contexto adicional, igual que `ReviewNotes` ya funciona hoy
- **(Futuro) Etiquetas opcionales**: sistema de tags libres para casos que ninguna dimensión estructurada cubre bien

---

## Dashboard Ideal

El Dashboard es la **pantalla principal del sistema**. Es el punto de entrada y debe permitir entender la situación financiera completa en menos de un minuto.

Implementado en v1:

```
┌─────────────────────────────────────────────────────────┐
│  Junio 2026                                             │
│  Ingresos · Gastos · Balance · % Ahorro                 │
│  Distribución por categoría (donut + lista)              │
│  Tendencia de los últimos 6 meses                        │
│  Comparación contra el mes anterior                      │
└─────────────────────────────────────────────────────────┘
```

Preparado para incorporar sin rediseño (placeholders ya definidos en el layout):

- Resumen mensual (✅ implementado)
- Presupuesto por categoría y desvíos
- Gastos fijos pendientes y próximos vencimientos
- Dinero realmente disponible
- Patrimonio neto
- Inversiones y rendimientos
- Objetivos financieros y progreso
- Recomendaciones generadas por IA
- Insights del MCP (respuestas a preguntas frecuentes, alertas proactivas)

---

## Módulos del Sistema

### Revisión de Movimientos (antes "Conciliación")

**Propósito:** transformar movimientos financieros crudos en movimientos clasificados, con o sin ayuda de coincidencias externas.

**Implementado (bajo el nombre técnico "Reconciliation", pendiente de rename — ver sección Migración):**
- Motor de sugerencias con scoring compuesto: monto, fecha, descripción, método de pago
- Sugerencias N↔M (varios movimientos de cada lado pueden agruparse)
- Confirmación con coincidencia (`Status = Confirmed`)
- Revisión sin coincidencia (`Status = Reviewed`)
- Revisión en lote de múltiples movimientos
- Detección de movimientos ya clasificados antes de procesar
- Snapshot inmutable del movimiento original en cada clasificación

**Pendiente (v2.0):**
- Reemplazar `ReviewReason` por Tipo de Movimiento + Contraparte + Comentario libre
- Sugerencia automática de clasificación basada en contraparte conocida
- Matching inteligente por similitud avanzada (Levenshtein, embeddings)
- Aprendizaje de reglas basado en historial de contrapartes

### Importación

**Propósito:** ingestar movimientos desde fuentes externas de forma idempotente.

**Implementado:**
- Parser BBVA banco (XLS), tarjeta Visa y Mastercard (PDF)
- Parser Excel manual — **rol redefinido en v2.0: solo migración/compatibilidad, no parte del flujo principal**
- `FileImportRouter` con detección de tipo de archivo
- Watcher de carpeta (Worker en background)
- Idempotencia por SHA256

**Pendiente:**
- Soporte de múltiples bancos
- Importación vía API (upload desde UI)
- Reducir progresivamente la superficie de código dedicada a Excel a medida que el usuario migra su historial

### Gastos Fijos (módulo nuevo — v2.0)

**Propósito:** gestionar gastos recurrentes de forma nativa, sin depender del Excel.

**Funcionalidades requeridas:**
- Crear gastos fijos (monto, descripción, contraparte, categoría)
- Editar gastos fijos existentes
- Desactivar gastos fijos (sin eliminarlos del historial)
- Definir periodicidad (mensual, bimestral, anual, etc.)
- Definir fecha de vencimiento
- Marcar como pagado
- Asociar el gasto fijo al movimiento bancario real cuando ocurre el pago

**Por qué es la base de todo lo que sigue:**

Este módulo es el prerequisito de:
- Flujo de caja proyectado (saber qué va a salir y cuándo)
- "Dinero realmente disponible" (líquido menos compromisos pendientes)
- Próximos vencimientos en el Dashboard
- Recordatorios y alertas
- Herramientas del MCP relacionadas con planificación

### Categorías (módulo nuevo — v2.0)

**Propósito:** CRUD administrable de categorías, reemplazando el listado hardcodeado.

**Funcionalidades requeridas:**
- Listar categorías (sistema + usuario)
- Crear categoría nueva
- Editar nombre visible
- Desactivar (no eliminar) categorías
- Reordenar categorías
- (Futuro) Jerarquía categoría padre / subcategoría

### Contrapartes (módulo nuevo — v2.0)

**Propósito:** CRUD administrable de contrapartes, con valores sugeridos por defecto.

**Funcionalidades requeridas:**
- Listar contrapartes
- Crear contraparte (nombre, tipo, valores sugeridos por defecto)
- Editar contraparte
- Desactivar contraparte
- Durante la revisión de movimientos: sugerir clasificación automática si la contraparte es reconocida

### IA / Insights (prototipo)

Sin cambios respecto a la versión anterior del documento, salvo que las fuentes de datos para insights deben evolucionar junto con el nuevo modelo de clasificación (Tipo de Movimiento, Contraparte) en lugar de depender solo de Categoría e Impacto Financiero.

### MCP Server (esqueleto)

Ver sección MCP actualizada más abajo.

### Dashboard (UI de Revisión, antes "UI de Conciliación")

**Implementado:**
- Doble grilla: movimientos bancarios/tarjeta ↔ registros externos (hoy Excel, en el futuro solo durante migración)
- Selección múltiple para grupos N↔M
- Modal de clasificación con Categoría e Impacto Financiero
- Revisión en lote
- Descartar candidatos externos
- Barra contextual de acciones
- Filtro por texto
- Balance en tiempo real

**Pendiente (v2.0):**
- Reemplazar el campo "Motivo" por Tipo de Movimiento + Contraparte + Comentario libre
- Autocompletado de contraparte con sugerencia de valores por defecto
- Renombrar textos de la UI ("Conciliación" → "Revisión de Movimientos", "Confirmar Match" → "Confirmar Clasificación")

---

## MCP (Model Context Protocol)

### Principio fundamental

El MCP no contiene lógica de negocio. Solo expone herramientas que un LLM puede llamar. La lógica permanece en los servicios de aplicación.

### Herramientas implementadas (Fase 5, parcial)

```
GetMonthlySummary(year, month)
GetExpensesByCategory(from, to)
GetMonthlyTrend(months)
CompareWithPreviousMonth(year, month)
```

### Herramientas futuras a incorporar (v2.0)

```
── Contrapartes ──
GetTopCounterparties(from, to, limit)
GetCounterpartyHistory(counterpartyId)
SuggestCounterpartyClassification(description)

── Gastos fijos ──
GetUpcomingFixedExpenses(days)
GetFixedExpensesStatus(month, year)
GetOverdueFixedExpenses()

── Presupuestos ──
GetBudgetStatus(month, year)
GetBudgetDeviations(month, year)

── Flujo de caja ──
ProjectCashFlow(days)
GetAvailableMoney()

── Patrimonio ──
GetNetWorth()
GetNetWorthEvolution(months)

── Inversiones ──
GetInvestmentPortfolio()
GetInvestmentReturns(from, to)

── Objetivos financieros ──
GetFinancialGoalsProgress()
SuggestSavingsCapacity(month, year)
```

### Preguntas que el MCP debe poder responder

Las ya existentes, más las habilitadas por el nuevo modelo:

- ¿Cuánto le pagué a [contraparte] este año?
- ¿Qué gastos fijos vencen esta semana?
- ¿Cuánto dinero tengo realmente disponible?
- ¿Estoy dentro de mi presupuesto de [categoría]?
- ¿Cuál es mi patrimonio neto actual?
- ¿Puedo ahorrar $200.000 este mes considerando mis gastos fijos pendientes?

---

## Arquitectura

### Stack tecnológico

Sin cambios: .NET 9/10, PostgreSQL (Npgsql + EF Core), PdfPig, NPOI/ClosedXML, Ollama, OpenAI (fallback), ModelContextProtocol SDK.

### Estructura de proyectos

Sin cambios en la macro-estructura. El rename de "Reconciliation" a un nombre que refleje "Revisión/Clasificación" es un cambio de naming interno, no de estructura de capas.

### Principios que deben respetarse siempre

Sin cambios: Clean Architecture, SOLID, bajo acoplamiento, idempotencia, inmutabilidad de fuentes, sin lógica en endpoints, código mantenible.

### Decisiones de modelo de datos — actualizadas (v2.0)

1. Los movimientos bancarios y de tarjeta son la fuente de verdad financiera
2. El Excel es un mecanismo de migración/compatibilidad, no una fuente estructural del sistema
3. `CategoryId` y `FinancialImpact` son obligatorios en todo movimiento clasificado
4. **(Nuevo)** `MovementType` (Tipo de Movimiento) será obligatorio en todo movimiento clasificado
5. **(Nuevo)** `CounterpartyId` será opcional pero fuertemente recomendado — habilita sugerencias automáticas
6. El registro clasificado (hoy `ProcessedExpense`) es la única tabla que el MCP consulta para métricas
7. Los ítems de detalle guardan snapshots inmutables (sin FKs a las tablas fuente)
8. Las categorías y contrapartes son entidades propias administrables, no enums cerrados
9. No existe estado "Pending" en el registro clasificado — toda fila es verdad financiera verificada
10. **(Eliminado)** `ReviewReason` como enum cerrado — reemplazado por Tipo de Movimiento + Contraparte + Comentario libre

---

## Flujo Operativo Mensual (actualizado v2.0)

```
1. IMPORTAR / REGISTRAR
   └── Importar extractos bancarios y de tarjeta (flujo principal, sin cambios)
   └── Registrar gastos fijos directamente en el sistema (ya no vía Excel)
   └── (Transitorio) Importar Excel solo si hay historial pendiente de migrar

2. REVISAR Y CLASIFICAR MOVIMIENTOS
   └── Revisar sugerencias de coincidencia del motor (ayuda opcional)
   └── Para cada movimiento: asignar Tipo de Movimiento, Impacto Financiero,
       Categoría y, cuando aplique, Contraparte
   └── Si la contraparte es conocida, aceptar o ajustar los valores sugeridos
   └── Movimientos sin coincidencia externa se clasifican igual (Reviewed)

3. VERIFICAR GASTOS FIJOS
   └── Confirmar cuáles vencen en el período
   └── Marcar como pagados y asociar al movimiento bancario real

4. CERRAR EL PERÍODO
   └── Todos los movimientos del período clasificados
   └── Todos los datos listos para el Dashboard y el MCP

→ DATOS LISTOS PARA ANÁLISIS
```

---

## Migración de este Documento (historial)

**v1.0 → v2.0:** Cambio de paradigma de "conciliación bancaria" a "revisión y clasificación de movimientos". Se introduce el modelo de clasificación de 4 dimensiones (Tipo de Movimiento, Impacto Financiero, Categoría, Contraparte). Se elimina `ReviewReason` como enum cerrado. El Excel pasa de ser fuente auxiliar permanente a mecanismo de migración temporal. Se agrega el módulo de Gastos Fijos como entidad nativa del sistema, independiente del Excel.

**Razón del cambio:** la conciliación bancaria tradicional optimiza para "encontrar la pareja de cada movimiento". El Financial Copilot necesita optimizar para "entender qué fue cada movimiento", con o sin pareja. El modelo anterior generaba fricción conceptual entre dos pantallas (Review vs Confirm Match) que en esencia hacían lo mismo (clasificar) con UX inconsistente entre sí.

---

## Regla de Consistencia para Futuras Sesiones

Antes de proponer cualquier cambio:

1. Leer este documento completo
2. Verificar que la propuesta responde al menos una pregunta de la filosofía
3. Verificar que no contradice ninguna decisión congelada de modelo de datos
4. Priorizar cambios que acerquen el sistema a la visión del Financial Copilot
5. No proponer refactors masivos sin beneficio claro y medible
6. Preferir PRs pequeños, seguros y verificables
7. Si algo en el código contradice este documento, señalarlo antes de continuar
8. **(Nuevo)** Si un cambio implica renombrar conceptos ampliamente usados en el código (ej. "Reconciliation"), evaluar el alcance real antes de ejecutar y proponer una estrategia incremental si el blast radius es grande

Cuando se detecten oportunidades de mejora alineadas con la visión, proponerlas aunque no hayan sido solicitadas explícitamente.