# Épica I — Confiabilidad de importación

> Estado: 📋 planificada, no implementada. Este documento define el alcance y el orden de PRs antes de escribir código, siguiendo el mismo protocolo usado en Review & Classification Engine v2 (un PR por vez, objetivo único, compila independientemente).

## 1. Problemas actuales detectados

Los 3 hallazgos siguientes están verificados directamente contra el código, no son hipótesis:

### 1.1 — La importación de tarjeta no es idempotente

`ImportFileProcessingSink.HandleFileAsync` (el pipeline que procesa PDFs de tarjeta) solo deduplica dentro de la misma corrida, con un `HashSet` en memoria (`dedupKey = Date|Amount|Description`) que nunca se compara contra lo que ya existe en la base:

```csharp
var seen = new HashSet<string>(StringComparer.Ordinal);
...
if (!seen.Add(dedupKey)) { duplicates++; continue; }
db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), ... });
```

`TransactionConfiguration` (EF Core) solo define un índice no-único sobre `Date` — no hay ningún constraint que impida duplicar. Reimportar el mismo PDF dos veces duplica cada movimiento.

### 1.2 — El diagnóstico de líneas descartadas se calcula y se descarta

`PdfStatementParserBase.ParseAsync(IReadOnlyList<string>, ...)` (el parser interno) calcula `skippedLines` y `failedLines` con su motivo, y los loguea. Pero el overload que expone `IFileParser.ParseAsync(string filePath, ...)` — el que realmente consume el pipeline — descarta esa información:

```csharp
return new FileParseResult(extracted, 0, [], sw.Elapsed);
//                                    ↑ SkippedRows       ↑ Diagnostics
```

`FileParseResult` ya tiene los campos `SkippedRows`/`Diagnostics` en su contrato — nunca se completan con los valores reales. Además, las líneas descartadas se logean con `LogTrace`, que la configuración por defecto (`LogLevel.Default = "Information"`) filtra — ni siquiera quedan en el log del proceso.

### 1.3 — Riesgo de ruteo incorrecto entre parsers de PDF

`FileParserFactory.ResolvePdfParser` prueba los parsers PDF **en el orden de registro en DI** y usa el primero cuyo `CanHandle()` sea verdadero (el propio comentario del código lo advierte). El fingerprint de `BbvaVisaStatementParser` es `\bBBVA\b` — matchea cualquier extracto BBVA, incluido uno BBVA Mastercard. `BbvaVisaStatementParser` se registra antes que `BbvaMastercardStatementParser` en `DependencyInjection.cs`. Resultado posible: un extracto BBVA Mastercard se procesa con el parser de línea de Visa, cuyo regex de cupón/monto no coincide con el layout real → cada línea individual falla `IsTransactionLine()` sin ningún error visible (la sección "se encuentra" porque el fingerprint de banco pasó, pero las líneas no matchean).

## 2. Estado actual de cada pipeline

| Fuente | Idempotencia | Trazabilidad de errores | Constraint en DB |
|---|---|---|---|
| Banco (`BbvaBankStatementImporter`) | ✅ `ExternalId = SHA256(archivo\|hoja\|fila)`, consulta previa contra DB | ✅ `ImportResult` con `Inserted`/`Duplicates`/`ParseErrors`/`SkippedRows`/`Diagnostics` | ✅ índice único en `ExternalId` |
| Excel legacy (`ExcelLegacyExpenseImporter`) | ✅ `ExternalId = SHA256(...)` | ✅ mismo patrón que banco | ✅ índice único en `ExternalId` |
| Tarjeta PDF (`ImportFileProcessingSink`) | ❌ solo dedup intra-archivo en memoria | ❌ `SkippedRows`/`Diagnostics` se descartan (1.2) | ❌ solo índice no-único en `Date` |

El pipeline de banco es la referencia a seguir — no hay que diseñar un patrón nuevo, hay que llevar tarjeta al mismo nivel.

## 3. Estrategia objetivo

### `ImportBatch`

Entidad nueva, común a las 3 fuentes: registra cada corrida de importación (archivo, hash de contenido, handler, timestamp, insertados/duplicados/fallidos). Hoy esa información existe fugazmente como variables locales (`ImportResult` en el caso de banco) y se pierde apenas termina el log — `ImportBatch` la persiste.

### `ExternalId` en `Transaction`

Mismo patrón que `BankStatement`/`LegacyImportedExpense`: hash determinístico. Clave natural preferida: `CouponNumber` cuando existe (es el identificador de operación real que da el banco); fallback a hash de `Date+Amount+Description+SourceFile` cuando no está disponible.

### Idempotencia

Antes de insertar, consultar `ExternalId`s ya existentes en una sola query batch (patrón exacto de `BbvaBankStatementImporter.PersistAsync`) e insertar solo los que faltan. El índice único actúa como red de seguridad final, no como mecanismo primario.

### Diagnóstico de errores

Completar `FileParseResult.SkippedRows`/`Diagnostics` con los valores que `PdfStatementParserBase` ya calcula internamente — no hace falta tocar ningún regex de parsing para esto.

### Persistencia de líneas ignoradas

Cada línea descartada o fallida de una corrida queda asociada a su `ImportBatch` (línea, texto crudo, motivo), consultable después — hoy esa información nunca sobrevive más allá del proceso que la generó.

## 4. Orden exacto de implementación

| PR | Objetivo único | Depende de |
|---|---|---|
| **I1** | Completar `FileParseResult` con los contadores reales (`skippedLines`/`failedLines`) que `PdfStatementParserBase` ya calcula. Sin tocar ningún regex. | — |
| **I2** | Entidad `ImportBatch` + configuración EF + migración. Sin persistir nada todavía desde los importadores. | — |
| **I3** | `ExternalId` único en `Transaction` + migración + `PersistAsync` con chequeo contra DB, siguiendo el patrón de `BbvaBankStatementImporter`. | — |
| **I4** | Los 3 importadores (banco, tarjeta, Excel) persisten un `ImportBatch` por corrida, con el detalle de líneas fallidas/saltadas. | I2 (+ I1 para que tarjeta tenga datos reales que persistir) |
| **I5** | Endpoint de solo lectura `GET /api/imports/history` sobre `ImportBatch`. Sin UI todavía. | I4 |
| **I6** | Pantalla mínima de errores de importación, consume I5. Cierra la parte de observabilidad de la épica. | I5 |
| **I7** | Investigar con PDFs reales, usando la observabilidad de I1/I6, si el fingerprint BBVA Visa vs. Mastercard es la causa real de líneas faltantes, y corregirlo si se confirma. | I1, I6 (no adivinar la causa sin la observabilidad activa) |

I1 e I2 e I3 no tienen dependencias entre sí y pueden implementarse en cualquier orden o en paralelo; se listan en este orden porque I1 es el cambio de menor riesgo y el más urgente de los tres (ya hay síntomas reportados).
