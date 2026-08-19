# IMPORT-003 — Auditoría de duplicados existentes

> Continuación de IMPORT-001 (identidad e idempotencia de movimientos bancarios, ver `docs/Mapa de confianza de datos.md`). Documento de investigación — describe una herramienta de **solo lectura**, no una corrección. No se borró, modificó ni consolidó ningún movimiento como parte de este trabajo.

## 1. Objetivo

Cuantificar, sin modificar nada, cuántos movimientos en `BankStatements`/`Transactions` parecen ser duplicados producidos por el bug ya confirmado en IMPORT-001 (`BankStatement.ExternalId` depende del nombre de archivo, así que dos exportaciones con período solapado no se reconocen como el mismo movimiento). Es medición, no resolución — la idempotencia y el saneamiento histórico son tareas posteriores del roadmap (Fases 1 y 6), que dependen de este número, no lo reemplazan.

## 2. Qué se entrega

- **`import-003-auditoria-duplicados.sql`** — script de solo lectura (únicamente sentencias `SELECT`/`WITH .. SELECT`, sin ningún `INSERT`/`UPDATE`/`DELETE`/DDL) que corre 8 análisis contra la base real.
- Este documento — metodología, criterios de clasificación, limitaciones y **el estado real de la ejecución** (sección 6 — importante, léela antes de asumir que hay resultados reales disponibles).

## 3. Modelo de datos usado (verificado contra el código, no asumido)

El script opera sobre las tablas y columnas reales, confirmadas contra las EF Core configurations:

- `BankStatements` (`BankStatementConfiguration.cs`): `Date`, `Concept`, `Amount`, `SourceFile`, `FinancialAccountId`, `ImportBatchId`. `ExternalId` **no se usa como criterio de comparación** — ya se demostró en IMPORT-001 que no identifica el movimiento, identifica archivo+fila.
- `Transactions` (`TransactionConfiguration.cs`): mismo criterio, alcance reducido (sección 8 del script) — ver por qué en la sección 5 de este documento.
- `FinancialAccounts`: para la columna "por cuenta" del reporte (`Name`, join por `FinancialAccountId`).
- `ImportBatches` (`ImportBatchConfiguration.cs`, entidad en `ImportBatch.cs`): para cruzar cada mitad de un grupo duplicado con la corrida que la insertó (`SourceFile`, `CompletedAtUtc`) — evidencia directa de solapamiento de importaciones. `BankStatement.ImportBatchId` es nullable (Patch 0105) — filas anteriores a ese patch no tienen este cruce disponible, el script las marca explícitamente ("SIN TRAZABILIDAD") en vez de omitirlas en silencio.
- `ClassifiedMovementItems` (`SourceEntityType`+`SourceId`, sin FK real — ver investigación IMPORT-001/DATA-001 anterior): para saber si un duplicado ya contaminó una clasificación existente. `SourceEntityType.BankStatement = 2` (`Domain/Enums/SourceEntityType.cs`).

No se inventó ningún campo. Donde faltaba información para responder algo con certeza (ver sección 5), se dice explícitamente, no se completa con una estimación.

## 4. Criterios de clasificación

La auditoría separa cada grupo de movimientos con `(Fecha, Importe, Concepto normalizado)` idéntico en cuatro categorías, no dos:

| Categoría | Condición | Por qué |
|---|---|---|
| **PROBABLE** | Exactamente 2 filas, en **archivos distintos**, con una descripción cuya frecuencia total en la cuenta es baja (umbral por defecto: ≤5 apariciones) | Es el patrón exacto ya confirmado en IMPORT-001: el mismo movimiento, mismo texto poco genérico, apareciendo en dos exportaciones distintas. |
| **POSIBLE** | Mismo caso, pero la descripción es muy frecuente en toda la cuenta (>5 apariciones) | Evidencia real de IMPORT-001: descripciones genéricas (`TRANSFERENCIA`, 115 apariciones en un extracto real citado en la documentación del proyecto) tienen riesgo real de coincidencia entre movimientos genuinamente distintos — no alcanza para tratarlas igual que un caso con descripción específica. |
| **AMBIGUO** | 3 o más filas comparten el mismo `(Fecha, Importe, Concepto)` | No hay forma automática de saber qué subconjunto (si alguno) es el duplicado real y cuál es una coincidencia legítima (ej. dos retiros de efectivo del mismo monto el mismo día, más un tercero que sí vino de un archivo repetido) — se marca para revisión manual en vez de asumir un apareo. |
| **NO DUPLICADO** | 2+ filas, todas del **mismo archivo** | El propio archivo ya las trajo como filas separadas — no hay ninguna señal de que el banco haya duplicado nada; lo más probable es que sean dos movimientos reales coincidentes (ver IMPORT-001, sección de casos ambiguos). |

Una quinta sección (7) reporta, aparte y sin clasificar como sospecha, pares con mismo día+importe pero **descripción distinta** — mismo criterio que ya usa `SuspicionDetector` hoy (`src/FinancialSystem.Infrastructure/Review/SuspicionDetector.cs`), separado explícitamente para no mezclarlo con los grupos de concepto idéntico.

**El umbral de frecuencia (5) es un valor de partida conservador, no una decisión de diseño final** — ajustable según lo que muestren los datos reales; queda como parámetro explícito (`GREATEST(5, 0)`) en el script para que sea fácil de cambiar sin tocar el resto de la lógica.

## 5. Limitaciones conocidas — qué NO puede responder este script

- **No usa `Balance`** como señal adicional — la investigación de IMPORT-001 dejó esto como candidato no confirmado (no se pudo validar si es estable entre exportaciones distintas por falta de archivos reales solapados disponibles). Queda fuera de este script a propósito, no por descuido.
- **No usa el "Nro:" embebido en `Concept`** — ya descartado como identificador confiable en IMPORT-001 (dos "PAGO DE HABERES" reales de meses distintos comparten el mismo `Nro:99999999`).
- **No hace fuzzy matching de descripciones** — coincidencia de concepto es siempre exacta (tras normalizar espacios/mayúsculas). Es una decisión deliberada, coherente con el roadmap general ("no empezar agregando fuzzy matching antes de entender el problema real").
- **`PROBABLE` no es lo mismo que "confirmado".** Ningún nivel de esta auditoría alcanza por sí solo para autorizar un borrado — coherente con Fase 2/6 del roadmap general (`DEDUPE-001`: la taxonomía de confianza final es una tarea aparte, esta auditoría es su primer insumo de datos reales, no su versión definitiva).
- **Alcance de tarjeta reducido a propósito.** `Transaction.ExternalId` ya es contenido-based con índice único — un caso donde el fallback sin `CouponNumber` fusionara dos operaciones reales bajo el mismo `ExternalId` (IMPORT-002, riesgo no confirmado) haría que la segunda **nunca llegue a insertarse** — este script no puede detectar una fila que nunca existió. La sección 8 solo cubre coincidencias entre filas que sí están, ambas, en la tabla.

## 6. Estado real de la ejecución — importante

**Este script todavía no corrió contra la base de datos real.** El entorno donde se preparó esta auditoría no tiene acceso a ningún Postgres con datos reales (sin conexión configurada, sin Docker disponible) — verificado explícitamente antes de asumir que había datos para consultar.

Para no entregar un script sin ninguna prueba de que funciona, **se validó contra una base de datos Postgres 16 local, vacía, con datos 100% sintéticos** construidos a mano para reproducir exactamente el escenario de IMPORT-001 (dos archivos `Debito_01_08_2026_al_05_08_2026.xls` / `Debito_01_08_2026_al_10_08_2026.xls`, con movimientos repetidos entre ambos, incluyendo casos ambiguos deliberados). El esquema de esa base de prueba se reconstruyó a mano a partir de las EF Core configurations reales (mismas tablas/columnas/tipos que el script consulta) — no es la migración oficial del proyecto, solo lo necesario para probar esta auditoría.

**Resultado de esa validación** (dataset sintético, NO son datos reales de ninguna cuenta):

```
0. Movimientos totales: BankStatements=12, Transactions=0

2. Resumen por clasificación:
   AMBIGUO   → 1 grupo  (3 movimientos) — "OPERACION EN EFECTIVO..." x2 en el
               mismo archivo + 1 en el archivo solapado: correctamente NO
               apareado automáticamente.
   PROBABLE  → 2 grupos (4 movimientos) — "PAGO DE HABERES..." y
               "TRANSFERENCIA", cada uno repetido una vez entre los dos
               archivos con período solapado.

5. Evidencia de solapamiento: para ambos grupos PROBABLE, el script identificó
   correctamente los dos ImportBatch distintos (mismo nombre que su
   SourceFile, fechas de corrida separadas 5 días) que insertaron cada mitad.

6. Impacto ya efectivo: detectó correctamente que una de las dos filas de
   "PAGO DE HABERES" ya tenía un ClassifiedMovementItem asociado (simulando
   que el duplicado ya había contaminado una métrica) y la otra no.
```

Esto confirma que el script es sintácticamente correcto contra el esquema real y que la lógica de clasificación hace lo que dice que hace — **no confirma nada sobre el estado real de tu base**, porque no corrió contra ella. El script de validación sintética no forma parte de esta entrega (era solo para probar, se descartó).

## 7. Cómo ejecutarlo contra la base real

```bash
psql "Host=localhost;Port=5432;Database=financialsystem;Username=postgres;Password=postgres" \
  -f docs/imports/import-003-auditoria-duplicados.sql
```
(ajustar la connection string a la real — la de arriba es el default documentado en `docs/UserGuide/McpUserGuide.md`). El script es de solo lectura: puede ejecutarse con un usuario que solo tenga permiso `SELECT`, si tu base lo permite.

## 8. Próximo paso recomendado

Ejecutar el script contra la base real y traer los resultados de vuelta a esta investigación — recién ahí se puede completar la sección "Movimientos totales" / "Por cuenta" / "Por período" con números reales, y decidir si el volumen encontrado justifica avanzar ya a `DEDUPE-001` (taxonomía de confianza definitiva) o si alcanza con lo que esta auditoría ya clasificó como punto de partida.

---

*Fuente: `BankStatementConfiguration.cs`, `TransactionConfiguration.cs`, `FinancialAccountConfiguration.cs`, `ImportBatchConfiguration.cs`, `ClassifiedMovementItemConfiguration.cs`, `SourceEntityType.cs`, `SuspicionDetector.cs`, cruzado contra la investigación de IMPORT-001 (`docs/Mapa de confianza de datos.md`). Ningún dato real fue leído, modificado ni borrado para producir este documento.*
