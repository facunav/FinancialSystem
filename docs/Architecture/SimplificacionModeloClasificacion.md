# Simplificación del modelo de clasificación — documento base para la Épica N

Consolida en una única fuente de verdad tres documentos de análisis
independientes que llegaron, por caminos distintos, al mismo diagnóstico:
`analisissimplificacionmodelodominio.md`, `auditoriaflujoclasificacion.md` y
`redisenoflujofuncional.md` (PATCH-028). Los tres se archivaron en
`docs/Archive/` — su contenido íntegro sigue disponible ahí, sin editar;
este documento no es un resumen de ellos, es la versión consolidada,
depurada de duplicaciones y verificada contra el estado actual del código.

**Alcance:** exclusivamente el análisis "¿el modelo de interacción de
clasificación pide más decisiones al usuario de las que hacen falta?".
No es un documento de arquitectura ni de dominio — el modelo de *datos*
(las 4 dimensiones de `ClassifiedMovement`, ADR-001) no está en discusión
en ninguno de los tres documentos originales ni en este.

---

## 1. Los tres documentos originales, y qué aportaba cada uno

| Documento | Enfoque | Aporte específico, no repetido en los otros |
|---|---|---|
| `auditoriaflujoclasificacion.md` | Auditoría campo por campo del flujo de interacción, contra la métrica "menor cantidad de clics/decisiones para clasificar 500 movimientos" | Las 3 alternativas concretas de UX para alta de Contraparte en contexto (combobox con autocompletado, mini-formulario, diferir a lote), con recomendación explícita por la primera |
| `redisenoflujofuncional.md` | Síntesis a nivel de producto, sobre lo ya verificado por el documento anterior — explícitamente no técnico | Propuesta de fusionar las 3 pantallas de catálogo en una; qué hacer con el campo Comentario; señala como deuda el detector de sospechosos sin acción de resolución y el modo lote que asume homogeneidad; el flujo ideal completo en 9 pasos; exige que el Dashboard indique explícitamente qué porcentaje de lo mostrado es confiable |
| `analisissimplificacionmodelodominio.md` | Intento explícito de **refutar** la conclusión de los dos anteriores, con evidencia de código más exhaustiva (`git grep` completo sobre consumidores reales de `MovementType`/`FinancialImpact`) | La tabla campo-por-campo con "quién lo consume realmente" (ningún consumidor de `MovementType` en `FinancialMetricsService.cs`/`FinancialTools.cs`, los dos componentes que el propio ADR-001 cita como razón para no tocar el modelo); el modelo mínimo de `CounterpartyType` razonado desde cero, sin mirar el código, llegando igual a 2 valores |

No hubo refutación real: el tercer documento confirmó, con evidencia más
fuerte, el diagnóstico de los dos primeros. Los tres coinciden en la
conclusión de fondo — se consolidan, no se promedian.

## 2. Contradicciones/asimetrías encontradas, y cómo se resolvieron

**No hay contradicciones directas entre los tres** (ningún documento
afirma lo contrario de lo que afirma otro). Sí hay una asimetría real que
los tres documentos tratan de forma distinta y que este consolidado
preserva explícitamente en vez de emparejar artificialmente:

- **`MovementType`**: los tres documentos coinciden en que la *columna*
  debería seguir existiendo (mismo contenido semántico), calculada, sin
  perder ningún valor del enum — cambia únicamente quién decide el valor
  (el sistema, no el usuario).
- **`CounterpartyType`**: `redisenoflujofuncional.md` (§7) y
  `analisissimplificacionmodelodominio.md` (Parte 4) van más allá y
  proponen reducir el **enum en sí** de 10 a 2 valores (`OwnAccount`/
  `OwnCard`), retirando los otros 8 — no solo dejar de preguntarlo.

Criterio para conservar esta diferencia tal cual, en vez de unificar el
tratamiento: verificado contra el código, `CounterpartyType` no tiene
ningún consumidor que dependa de los 8 valores genéricos (`Person`,
`Business`, `Company`, `Bank`, `Service`, `Government`, `Investment`,
`Other` — ninguno filtra ni condiciona ningún cálculo), mientras que
`MovementType` sí se persiste y se muestra como dato consultable en
`ClassifiedMovement`. Son decisiones de distinto alcance por una razón
real, no por descuido de los documentos originales.

## 3. Diagnóstico consolidado, campo por campo — verificado contra el código actual de este patch

| Campo | Veredicto de los 3 documentos | Estado real verificado en este patch |
|---|---|---|
| **Cuenta financiera** | No debería elegirse por movimiento — es un dato de la importación, resuelto una vez por cuenta real | **Parcialmente implementado.** El wiring automático para banco (`BankStatement.AccountNumber` vs. `FinancialAccount`) ya existe (Épica J, M5). Para tarjeta, sigue sin resolverse por contenido del PDF — sigue como brecha abierta (mismo hallazgo ya registrado en el Patch 0080 para `analisisproximaepicausabilidad.md`). |
| **Categoría** | Debería seguir siendo una pregunta real, pero solo para Gasto/Ingreso genuino — no para transferencias internas ni pagos de deuda | **No implementado.** `movements.html` sigue pidiendo Categoría como campo obligatorio (`class="req"`) sin condicionarlo al valor de Impacto financiero elegido. |
| **Contraparte** | Alta en contexto sin abandonar la pantalla, vía combobox con "crear nueva" como una opción más de la lista | **Parcialmente implementado, con un patrón distinto al recomendado.** Existe alta en contexto (`btnNewCounterparty` abre un modal secundario sin salir de la pantalla de clasificación) — pero es la alternativa 2 de `auditoriaflujoclasificacion.md` (botón + mini-formulario), no la 1 (combobox con "crear" como ítem de la lista), que el documento marcaba como la de menor fricción. |
| **Tipo de movimiento** | Debería dejar de pedirse como pregunta independiente; el dato sigue existiendo, calculado | **No implementado.** `movements.html` mantiene `#cMovementType` como `<select>` independiente y obligatorio, sin ningún cálculo automático. |
| **Impacto financiero** | Debería inferirse casi siempre — a partir del signo, de si la contraparte es una cuenta propia, y de patrones de texto — preguntándose solo en el residuo genuino | **Parcialmente implementado.** El caso concreto que los tres documentos señalan como la pieza que falta (marcar una Contraparte como "cuenta propia" para derivar Impacto automáticamente) se implementó para `OwnCard` → `DebtPayment` (Patch 0075/PATCH-022, sobre la base ya sentada por ADR-003) — precarga inmediata, editable, en `movements.html`. **No** se implementó el caso simétrico para `OwnAccount` → `InternalMovement`, ni la inferencia por patrones de texto del banco (vocabulario cerrado tipo "TRANSFERENCIA"/"INTERESES GANADOS"). |
| **`CounterpartyType`** | Reducir de 10 a 2 valores reales (`OwnAccount`/`OwnCard`) más un estado por defecto | **No implementado.** El enum sigue con sus 10 valores originales (verificado en `Counterparty.cs` en este mismo patch). La obligatoriedad de elegir un valor al alta sí se retiró (PR-O7, ya implementado, patch anterior a esta serie) — pero eso resuelve la fricción de tener que elegir, no la existencia de los 8 valores sin consumidor. |
| **Comentario** | Debería desaparecer o conectarse a algo que el usuario vuelva a ver | **Parcialmente vigente, dato corregido en este patch.** Verificado: `movements.html` nunca vuelve a mostrarlo (se manda al guardar, no se lee de vuelta en ningún lado de esa pantalla) — pero sí se muestra hoy en las herramientas MCP de investigación (`MovementTools.GetMovement`/`ExplainMovement`/`ExplainClassification`, `InvestigationTools`), que si lo leen y lo devuelven. La premisa "ningún lugar del sistema vuelve a mostrarlo" de `redisenoflujofuncional.md` ya no es exacta tal cual — el campo tiene un consumidor real, aunque no en la pantalla donde se carga. |
| **Indicador de cobertura/confiabilidad del Dashboard** | Debería mostrar explícitamente qué porcentaje de lo mostrado es confiable, desde el día uno | **Implementado.** Patch 0073/PATCH-020 — tarjeta "Cobertura de clasificación" en el Dashboard, consumiendo el endpoint del Patch 0071/0072. |
| **Pantalla única de catálogos** (fusionar Cuentas/Categorías/Contrapartes) | Debería ser una sola experiencia, no 3 pantallas top-level | **No implementado.** Siguen siendo pantallas separadas (`accounts.html`, `counterparties.html`; no existe una pantalla de administración de Categorías). |
| **Ruteo de import por contenido, no por nombre de archivo** | El sistema debería identificar banco/tipo de documento por contenido | **No implementado para el `.xls` de banco** (`BbvaBankStatementImportHandler.CanHandle` sigue siendo por patrón de nombre de archivo — mismo hallazgo ya registrado en el Patch 0080). Sí implementado para el catch-all de tarjeta (PDF, por contenido vía `IStatementParser.CanHandle`). |
| **Detección de sospechosos sin acción de resolución** / **Modo lote que asume homogeneidad** | Señalados como deuda de producto, sin una recomendación concreta de qué construir en su lugar | **Temas abiertos, no verificados en detalle en este patch** — se preservan tal cual estaban en `redisenoflujofuncional.md` §6-7, como observaciones a resolver cuando se retome la Épica N, no como conclusiones cerradas. |

## 4. Qué queda como base para la futura Épica N

Sin inventar alcance nuevo — exactamente lo que los tres documentos
originales dejaron como pendiente y que la verificación de este patch
confirma que sigue sin resolverse:

1. Dejar de pedir `MovementType` como pregunta independiente; calcularlo
   por vocabulario del banco + `FinancialImpact` ya resuelto.
2. Reducir `CounterpartyType` a `OwnAccount`/`OwnCard`/ninguno; retirar los
   8 valores restantes del flujo de alta (quedan como decisión de
   arquitectura si conservarlos en el enum por compatibilidad histórica,
   igual que se hizo con `SourceEntityType.LegacyImport`/`MovementRole.Candidate`).
3. Derivar `FinancialImpact` automáticamente también para `OwnAccount` →
   `InternalMovement` (simétrico a lo ya hecho para `OwnCard` en el Patch
   0075) y por patrones de texto del banco.
4. No pedir Categoría para movimientos que son mecánicamente transferencia
   interna o pago de deuda.
5. Resolver cuenta financiera de tarjeta automáticamente en el import
   (extracción del número de cuenta del texto del PDF).
6. Evaluar el patrón de alta de Contraparte en contexto (combobox con
   "crear" como ítem de la lista) contra el actual (botón + modal
   secundario) — no urgente, ambos evitan abandonar la pantalla.
7. Decidir el destino del campo Comentario, con el dato actualizado de que
   ya tiene un consumidor real (MCP), aunque no en `movements.html`.
8. Evaluar la fusión de las 3 pantallas de catálogo en una sola experiencia.
9. Detección de sospechosos sin resolución y modo lote que asume
   homogeneidad — quedan como observaciones abiertas de `redisenoflujofuncional.md`,
   sin decisión tomada.

## 5. Qué NO cambia (conclusión que los tres documentos comparten y este ratifica)

Las 4 dimensiones de dominio de `ClassifiedMovement` (Categoría, Impacto,
Tipo, Contraparte — ADR-001) no se cuestionan como *dato almacenado*, en
ninguno de los tres documentos ni en este consolidado. Lo que está en
discusión, exclusivamente, es cuántas de esas 4 dimensiones deberían
seguir siendo una pregunta activa para el usuario en el flujo normal de
clasificación, y cuáles deberían resolverse solas.
