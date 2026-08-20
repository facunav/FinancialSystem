# IMPORT-003 — Resultados de la auditoría de duplicados (cerrado)

> Continuación de `docs/imports/IMPORT-003-auditoria-duplicados.md` (metodología y script) y de `docs/Mapa de confianza de datos.md` (IMPORT-001). Este documento registra los **resultados reales**, obtenidos ejecutando `import-003-auditoria-duplicados.sql` e `import-003b-impacto-economico.sql` contra la base de datos real del usuario — no contra el dataset sintético usado para validar los scripts. Ningún dato fue modificado, borrado ni saneado como parte de este trabajo: las dos corridas fueron 100% de solo lectura (`default_transaction_read_only=on`, `-v ON_ERROR_STOP=1`, ambas terminaron con `FIN — ningún dato fue modificado por este script`).

---

## 1. Objetivo

Cuantificar, sin modificar nada, cuántos movimientos en `BankStatements`/`Transactions` de la base real parecen ser duplicados producidos por la causa raíz confirmada en IMPORT-001 (`BankStatement.ExternalId` depende del nombre de archivo, así que dos exportaciones con período solapado no se reconocen como el mismo movimiento) — y, en una segunda pasada, cuantificar el impacto económico y el impacto en clasificaciones de los casos con mejor evidencia (PROBABLE).

## 2. Fecha/contexto de ejecución

- Corrida 1 (`import-003-auditoria-duplicados.sql`): ejecutada por el usuario, resultado recibido y analizado en esta misma investigación (archivo `import-003-resultado.txt`).
- Corrida 2 (`import-003b-impacto-economico.sql`): ejecutada por el usuario como segunda auditoría de validación, resultado recibido y analizado (archivo `import-003b-resultado.txt`).
- Ambas corridas: mismo entorno, misma base, mismo criterio de solo lectura forzado a nivel de sesión de Postgres.

## 3. Base auditada

- Motor: **PostgreSQL 17**.
- Base: `financialsystem`, `localhost:5432`.
- Alcance: `BankStatements` (banco, foco principal) y `Transactions` (tarjeta, alcance reducido — ver `IMPORT-002` en `docs/Mapa de confianza de datos.md`).
- Cuenta financiera involucrada en el 100% de los grupos con evidencia: **BBVA Caja de ahorro pesos** (única cuenta con algún grupo PROBABLE/POSIBLE/AMBIGUO).

## 4. Metodología

Sin repetir el detalle completo (ver `IMPORT-003-auditoria-duplicados.md`): se agrupan los movimientos de `BankStatements` por `(Fecha, Importe, Concepto normalizado)` y cada grupo con más de una fila se clasifica en cuatro niveles según dos señales — si las filas vienen de archivos (`ImportBatch.SourceFile`) distintos o del mismo, y qué tan frecuente es esa descripción en el resto de la cuenta:

- **PROBABLE**: 2 filas, 2 archivos distintos, descripción con frecuencia total ≤5 en toda la cuenta.
- **POSIBLE**: mismo patrón, pero descripción muy frecuente (>5 apariciones).
- **AMBIGUO**: 3 o más filas comparten el grupo — no se aparea automáticamente.
- **NO_DUPLICADO_MISMO_ARCHIVO**: 2+ filas, todas del mismo archivo.

La segunda corrida (`import-003b`) usó exactamente la misma definición de PROBABLE, re-derivada de forma independiente (consulta escrita por separado, no reutilizando el resultado de la primera corrida) para: (a) cuantificar el impacto económico, y (b) volver a verificar contra `ClassifiedMovementItems`/`ClassifiedMovements`/`Categories` si cada mitad de cada par ya está clasificada.

## 5. Resultados reales

| | Total |
|---|---|
| `BankStatements` | 500 |
| `Transactions` | 213 |

| Clasificación | Grupos | Movimientos |
|---|---|---|
| PROBABLE | 43 | 86 |
| POSIBLE | 15 | 30 |
| AMBIGUO | 37 | 111 |
| NO_DUPLICADO_MISMO_ARCHIVO | 3 | 6 |
| **Total en algún grupo** | **98** | **233** |

`Transactions` (tarjeta): un único grupo de 2 filas en 213 totales — consistente con que `Transaction.ExternalId` ya es content-based (IMPORT-001/IMPORT-002), no posicional.

## 6. Evidencia de solapamiento de `ImportBatch`

**HECHO VERIFICADO.** Los 43 grupos PROBABLE están, sin excepción, respaldados por dos `ImportBatch` con `SourceFile` distinto — nunca el mismo archivo dos veces. Los archivos involucrados en el 96% de los 98 grupos totales (PROBABLE+POSIBLE+AMBIGUO) son, en su enorme mayoría, solo 6 nombres reutilizados en distintas combinaciones: `Detalle_mov_cuenta_21_07_2026.xls`, `_27_07_2026`, `_01_08_2026`, `_06_08_2026`, `_08_08_2026`, `_16_08_2026` (más un caso aislado de `_12_07_2026`). Nombres que coinciden con el patrón `Detalle_mov_cuenta*.xls` ya configurado en `FileIngestionOptions.BbvaBankStatementFilePatterns`, subidos manualmente vía `imports.html` (rutas `...\TempImports\...`).

**INFERENCIA, no hecho probado matemáticamente por el SQL:** que cada par PROBABLE sea el mismo movimiento bancario real, y no una coincidencia — la evidencia (concepto poco frecuente + archivos distintos + fechas de archivo espaciadas 5-8 días, consistente con el patrón de importación ya descrito en IMPORT-001) hace esto altamente probable, pero el script nunca compara un identificador de operación real del banco — no existe tal identificador confiable (ver IMPORT-001).

## 7. Distribución temporal

| Período | AMBIGUO | POSIBLE | PROBABLE |
|---|---|---|---|
| 2026-05 | 1 grupo / 3 mov. | — | — |
| 2026-07 | 19 grupos / 57 mov. | 13 grupos / 26 mov. | 28 grupos / 56 mov. |
| 2026-08 | 17 grupos / 51 mov. | 2 grupos / 4 mov. | 15 grupos / 30 mov. |

**HECHO VERIFICADO:** julio y agosto concentran 96 de los 98 grupos (98%) — coincide con el período de solapamiento de archivos descrito en la sección 6.

## 8. Impacto económico

Calculado exclusivamente sobre los 43 grupos PROBABLE (segunda corrida, `import-003b`):

| Métrica | Con signo | Valor absoluto |
|---|---|---|
| Suma de las 86 filas | $6.625.978,32 | $16.781.185,68 |
| Suma de una copia por grupo (43) | $3.312.989,16 | $8.390.592,84 |
| **Importe potencialmente duplicado** | **$3.312.989,16** | **$8.390.592,84** |

El "importe potencialmente duplicado" coincide, exacto, con "suma de una copia por grupo" en ambas variantes — identidad matemática verificada por la propia consulta (86 filas = 2× cada grupo, así que lo "de más" es una copia completa), no asumida.

**Estadística de los 43 grupos:**

| | Con signo | Valor absoluto |
|---|---|---|
| Mínimo | -$1.980.214,95 | $1.900,00 |
| Máximo | $5.604.791,00 | $5.604.791,00 |
| Promedio | $77.046,26 | $195.130,07 |
| Mediana | -$7.200,00 | $10.955,00 |

**INFERENCIA:** la mediana muy por debajo del promedio indica una distribución asimétrica — la mayoría de los 43 duplicados son montos chicos/medianos (débitos de tarjeta cotidianos), y un puñado de casos grandes (el "PAGO DE HABERES" de $5.604.791,00 y un "PAGO DE TARJETA VISA" de -$1.980.214,95) domina el promedio y el resultado neto con signo. Si cualquiera de esos dos casos puntuales resultara no ser un duplicado real, el número neto con signo cambiaría mucho más que si se descartara cualquier otro de los 43.

## 9. Impacto sobre `ClassifiedMovementItems`

**HECHO VERIFICADO, dos veces, con dos consultas escritas por separado, contra la base real — sin ninguna excepción:** de los 43 grupos PROBABLE, **43 tienen ambas copias con `ClassifiedMovementItem` propio** (`AMBAS_CLASIFICADAS = 43`). Cero grupos con solo una copia clasificada. Cero grupos sin ninguna copia clasificada.

**INFERENCIA (no hecho universal — ver riesgo):** las dos copias de cada grupo PROBABLE ya poseen `ClassifiedMovementItem` por separado. Por lo tanto, existe evidencia de que los duplicados ya penetraron la capa de clasificación y existe riesgo de doble contabilización en cualquier métrica que agregue ambas clasificaciones sin una deduplicación previa por identidad del movimiento. **La auditoría IMPORT-003 no demuestra por sí sola qué métricas concretas están afectadas** — no se consultó `FinancialMetricsService`, el Dashboard, ni ningún reporte real; solo se confirmó la existencia de dos `ClassifiedMovement` distintos por cada par.

**RIESGO derivado, no confirmado como materializado:** si estos 43 pares son duplicados reales y las métricas existentes no filtran por identidad de movimiento antes de sumar, cualquier cifra agregada (gasto mensual, ingreso, comparación entre períodos) que incluya julio/agosto de esta cuenta podría estar inflada hasta en ~$8,39M de volumen de movimiento (valor absoluto) o ~$3,31M en términos netos — un rango, no un número confirmado, porque depende de que la inferencia de la sección 6 sea correcta para cada uno de los 43 casos.

## 10. Distribución de `BankStatements` involucrados

**HECHO VERIFICADO:** de 500 `BankStatements` totales, **233 son únicos y están involucrados en algún grupo** (46,6%) — validado explícitamente por conteo directo (`COUNT(DISTINCT "Id")`), no por suma de contadores de grupo.

| Clasificación | BankStatements únicos |
|---|---|
| PROBABLE | 86 |
| POSIBLE | 30 |
| AMBIGUO | 111 |
| NO_DUPLICADO_MISMO_ARCHIVO | 6 |
| **Total único** | **233** |

**HECHO VERIFICADO — sin solapamiento entre categorías:** se ejecutó una consulta específica para detectar si algún `BankStatement.Id` aparece en más de una clasificación a la vez — **devolvió 0 filas**. Cada movimiento pertenece a exactamente un grupo y una clasificación; los 233 no se cuentan dos veces entre categorías.

## 11. Limitaciones

- No se validó el valor real de `ExternalId` de cada par PROBABLE en esta corrida — la explicación (nombre de archivo distinto) es la única consistente con que ambas filas coexistan bajo el índice único de la columna, pero no se releyó explícitamente.
- No se calculó el impacto económico de los grupos POSIBLE ni AMBIGUO — quedan fuera del alcance de esta auditoría a propósito, dado que la evidencia de identidad es más débil (POSIBLE: descripciones genéricas; AMBIGUO: 3+ filas sin apareo automático).
- No se consultó ningún endpoint, servicio ni reporte real del sistema (`FinancialMetricsService`, Dashboard, MCP) para confirmar si las cifras ya publicadas están efectivamente infladas — solo se confirmó la existencia de clasificaciones duplicadas a nivel de datos.
- El resultado corresponde a un único punto en el tiempo (la fecha de estas dos corridas) — nuevas importaciones podrían cambiar estos números.

## 12. Conclusión

IMPORT-003 produjo evidencia real, verificada dos veces de forma independiente, consistente con la causa raíz confirmada en IMPORT-001:

- **HECHO VERIFICADO:** existen 43 grupos (86 movimientos) con evidencia fuerte de duplicación (archivos distintos + descripción poco frecuente + coincidencia exacta de fecha/importe/descripción), concentrados en julio/agosto de 2026 y respaldados por `ImportBatch` real con `SourceFile` distinto en cada caso.
- **HECHO VERIFICADO:** el impacto económico de esos 43 grupos es cuantificable — $8.390.592,84 en valor absoluto, $3.312.989,16 en términos netos.
- **HECHO VERIFICADO:** las dos copias de cada uno de los 43 grupos ya tienen `ClassifiedMovementItem` propio, sin excepción.
- **INFERENCIA, no hecho:** que estos 43 pares sean, cada uno, el mismo movimiento bancario real duplicado.
- **RIESGO, no confirmado como materializado:** doble contabilización en métricas agregadas — la auditoría no demostró qué métricas concretas están afectadas.

## 13. Decisión de cierre

**IMPORT-003 queda cerrada como investigación concluida.**

Existe evidencia suficiente para avanzar a **DEDUPE-001**, con este alcance inicial y ningún otro: investigar cómo demostrar la identidad de los 43 grupos PROBABLE (más allá de la inferencia metodológica ya reunida) y diseñar un criterio seguro de identidad/saneamiento para ese conjunto específico.

**DEDUPE-001 NO debe, en su alcance inicial:**
- borrar ningún duplicado;
- modificar ningún dato;
- implementar todavía la nueva identidad de `BankStatement.ExternalId`;
- resolver automáticamente los grupos POSIBLE;
- resolver automáticamente los grupos AMBIGUO.

Estos cuatro puntos quedan explícitamente fuera del alcance inicial — no porque no importen, sino porque la evidencia reunida hasta acá es la más fuerte para PROBABLE específicamente, y no debe extenderse por default al resto.

---

*Fuente: `import-003-resultado.txt` e `import-003b-resultado.txt` (salidas reales de `import-003-auditoria-duplicados.sql` e `import-003b-impacto-economico.sql`, ejecutadas por el usuario contra PostgreSQL 17, base `financialsystem`), analizados en esta conversación. Ningún número de este documento fue estimado, inventado o re-derivado de otra fuente — todos provienen directamente de esas dos corridas reales.*
