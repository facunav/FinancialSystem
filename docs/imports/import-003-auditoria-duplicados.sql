-- ============================================================================
-- IMPORT-003 — Auditoría de duplicados existentes (solo lectura)
-- ============================================================================
-- Cuantifica posibles movimientos duplicados en BankStatements/Transactions.
--
-- GARANTÍA DE SOLO LECTURA: cada sentencia de este archivo es un SELECT (o un
-- WITH .. SELECT). No hay ningún INSERT/UPDATE/DELETE/DDL. No crea tablas ni
-- vistas -- todo vive en CTEs que se descartan al terminar cada consulta.
-- Podés ejecutarlo con un usuario de solo lectura si tu base lo permite.
--
-- METODOLOGÍA Y LÍMITES: ver docs/imports/IMPORT-003-auditoria-duplicados.md
-- antes de interpretar los resultados -- en particular, por qué "PROBABLE"
-- no es lo mismo que "confirmado", y qué significa "AMBIGUO".
--
-- CÓMO EJECUTARLO:
--   psql "<tu connection string>" -f docs/imports/import-003-auditoria-duplicados.sql
--
-- Alcance: esta corrida audita BankStatements (banco). La sección 8 hace el
-- mismo ejercicio, más liviano, sobre Transactions (tarjeta) -- ver
-- IMPORT-002 en el roadmap para por qué el riesgo ahí es distinto.
-- ============================================================================

\pset pager off
\timing off

\echo '=============================================================='
\echo '0. MOVIMIENTOS TOTALES'
\echo '=============================================================='

SELECT 'BankStatements' AS fuente, COUNT(*) AS total FROM "BankStatements"
UNION ALL
SELECT 'Transactions', COUNT(*) FROM "Transactions";


\echo '=============================================================='
\echo '1. GRUPOS DE COINCIDENCIA EXACTA (Fecha + Importe + Concepto) — BANCO'
\echo '=============================================================='
-- Normaliza Concept (trim + mayúsculas + espacios colapsados) y agrupa por
-- (fecha del movimiento, importe, concepto normalizado). Un grupo con más de
-- una fila es un candidato -- la clasificación (columna "clasificacion") es
-- la parte importante, no el solo hecho de aparecer acá.
--
-- Por qué NO alcanza con "aparece más de una vez" para llamarlo duplicado:
--   - Si las dos filas vienen del MISMO archivo (SourceFile), lo más probable
--     es que sean dos movimientos reales distintos que coinciden en fecha,
--     importe y texto (ej. dos retiros de efectivo del mismo monto el mismo
--     día) -- el propio archivo ya los trajo como filas separadas, y nada
--     indica que el banco haya duplicado nada. Se marcan aparte, nunca como
--     PROBABLE.
--   - Si el grupo tiene MÁS DE 2 filas, no hay forma automática de saber cuál
--     par (si alguno) es el duplicado real y cuál es coincidencia -- se marca
--     AMBIGUO explícitamente en vez de asumir.
--   - Si el texto del concepto es muy frecuente en toda la cuenta (umbral
--     configurable, GREATEST(5, ...) más abajo), la coincidencia pesa menos
--     -- es exactamente el patrón real ya documentado (115 filas de
--     "TRANSFERENCIA" en un extracto real de este proyecto, ver investigación
--     de IMPORT-001) -- se marca POSIBLE en vez de PROBABLE.

WITH bs_norm AS (
    SELECT
        "Id",
        "Date"::date                                              AS fecha,
        "Amount"                                                  AS importe,
        upper(trim(regexp_replace("Concept", '\s+', ' ', 'g')))   AS concepto_norm,
        "SourceFile"                                              AS archivo,
        "FinancialAccountId"                                      AS cuenta_id,
        "ImportBatchId"                                           AS import_batch_id
    FROM "BankStatements"
),
frecuencia_concepto AS (
    -- cuántas veces aparece cada descripción normalizada en TODA la cuenta,
    -- sin importar fecha/importe -- mide qué tan "genérico" es el texto.
    SELECT concepto_norm, COUNT(*) AS frecuencia_total
    FROM bs_norm
    GROUP BY concepto_norm
),
grupos AS (
    SELECT
        fecha, importe, concepto_norm,
        COUNT(*)                              AS cant_filas,
        COUNT(DISTINCT archivo)               AS archivos_distintos,
        COUNT(DISTINCT cuenta_id)             AS cuentas_distintas,
        array_agg("Id" ORDER BY archivo)      AS ids,
        array_agg(DISTINCT archivo)           AS archivos
    FROM bs_norm
    GROUP BY fecha, importe, concepto_norm
    HAVING COUNT(*) > 1
)
SELECT
    g.fecha,
    g.importe,
    g.concepto_norm                                                AS concepto,
    g.cant_filas,
    g.archivos_distintos,
    f.frecuencia_total                                             AS frecuencia_concepto_en_cuenta,
    CASE
        WHEN g.cant_filas > 2
            THEN 'AMBIGUO — grupo de ' || g.cant_filas || ' filas, no se puede aparear automáticamente'
        WHEN g.archivos_distintos = 1
            THEN 'NO DUPLICADO — mismo archivo, probable coincidencia real'
        WHEN g.archivos_distintos > 1 AND f.frecuencia_total > GREATEST(5, 0)
            THEN 'POSIBLE — descripción muy frecuente en la cuenta (' || f.frecuencia_total || ' apariciones), baja la confianza'
        WHEN g.archivos_distintos > 1
            THEN 'PROBABLE — mismo día+importe+descripción, en archivos distintos, descripción poco frecuente'
        ELSE 'INDETERMINADO'
    END AS clasificacion,
    g.archivos,
    g.ids
FROM grupos g
JOIN frecuencia_concepto f USING (concepto_norm)
ORDER BY
    CASE
        WHEN g.cant_filas > 2 THEN 1
        WHEN g.archivos_distintos > 1 AND f.frecuencia_total <= GREATEST(5, 0) THEN 2
        WHEN g.archivos_distintos > 1 THEN 3
        ELSE 4
    END,
    g.fecha;


\echo '=============================================================='
\echo '2. RESUMEN POR CLASIFICACIÓN — BANCO'
\echo '=============================================================='

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm,
           "SourceFile" AS archivo
    FROM "BankStatements"
),
frecuencia_concepto AS (
    SELECT concepto_norm, COUNT(*) AS frecuencia_total FROM bs_norm GROUP BY concepto_norm
),
grupos AS (
    SELECT fecha, importe, concepto_norm,
           COUNT(*) AS cant_filas,
           COUNT(DISTINCT archivo) AS archivos_distintos
    FROM bs_norm
    GROUP BY fecha, importe, concepto_norm
    HAVING COUNT(*) > 1
),
clasificados AS (
    SELECT
        g.*,
        CASE
            WHEN g.cant_filas > 2 THEN 'AMBIGUO'
            WHEN g.archivos_distintos = 1 THEN 'NO_DUPLICADO_MISMO_ARCHIVO'
            WHEN g.archivos_distintos > 1 AND f.frecuencia_total > GREATEST(5, 0) THEN 'POSIBLE'
            WHEN g.archivos_distintos > 1 THEN 'PROBABLE'
            ELSE 'INDETERMINADO'
        END AS clasificacion
    FROM grupos g
    JOIN frecuencia_concepto f USING (concepto_norm)
)
SELECT
    clasificacion,
    COUNT(*)                    AS grupos,
    SUM(cant_filas)             AS movimientos_involucrados
FROM clasificados
GROUP BY clasificacion
ORDER BY clasificacion;


\echo '=============================================================='
\echo '3. POR CUENTA FINANCIERA (solo grupos PROBABLE/POSIBLE)'
\echo '=============================================================='

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm,
           "SourceFile" AS archivo, "FinancialAccountId" AS cuenta_id
    FROM "BankStatements"
),
frecuencia_concepto AS (
    SELECT concepto_norm, COUNT(*) AS frecuencia_total FROM bs_norm GROUP BY concepto_norm
),
grupos AS (
    SELECT fecha, importe, concepto_norm,
           COUNT(*) AS cant_filas,
           COUNT(DISTINCT archivo) AS archivos_distintos,
           -- cuenta representativa del grupo (debería ser una sola; si hay más
           -- de una, el propio grupo ya es raro y cae en AMBIGUO igual)
           (array_agg(cuenta_id))[1] AS cuenta_id
    FROM bs_norm
    GROUP BY fecha, importe, concepto_norm
    HAVING COUNT(*) > 1
),
clasificados AS (
    SELECT g.*,
        CASE
            WHEN g.cant_filas > 2 THEN 'AMBIGUO'
            WHEN g.archivos_distintos = 1 THEN 'NO_DUPLICADO_MISMO_ARCHIVO'
            WHEN g.archivos_distintos > 1 AND f.frecuencia_total > GREATEST(5, 0) THEN 'POSIBLE'
            WHEN g.archivos_distintos > 1 THEN 'PROBABLE'
            ELSE 'INDETERMINADO'
        END AS clasificacion
    FROM grupos g JOIN frecuencia_concepto f USING (concepto_norm)
)
SELECT
    COALESCE(fa."Name", 'Sin cuenta asignada') AS cuenta,
    c.clasificacion,
    COUNT(*)        AS grupos,
    SUM(c.cant_filas) AS movimientos_involucrados
FROM clasificados c
LEFT JOIN "FinancialAccounts" fa ON fa."Id" = c.cuenta_id
WHERE c.clasificacion IN ('PROBABLE', 'POSIBLE', 'AMBIGUO')
GROUP BY cuenta, c.clasificacion
ORDER BY cuenta, c.clasificacion;


\echo '=============================================================='
\echo '4. POR PERÍODO (mes) — solo grupos PROBABLE/POSIBLE/AMBIGUO'
\echo '=============================================================='

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm,
           "SourceFile" AS archivo
    FROM "BankStatements"
),
frecuencia_concepto AS (
    SELECT concepto_norm, COUNT(*) AS frecuencia_total FROM bs_norm GROUP BY concepto_norm
),
grupos AS (
    SELECT fecha, importe, concepto_norm,
           COUNT(*) AS cant_filas,
           COUNT(DISTINCT archivo) AS archivos_distintos
    FROM bs_norm
    GROUP BY fecha, importe, concepto_norm
    HAVING COUNT(*) > 1
),
clasificados AS (
    SELECT g.*,
        CASE
            WHEN g.cant_filas > 2 THEN 'AMBIGUO'
            WHEN g.archivos_distintos = 1 THEN 'NO_DUPLICADO_MISMO_ARCHIVO'
            WHEN g.archivos_distintos > 1 AND f.frecuencia_total > GREATEST(5, 0) THEN 'POSIBLE'
            WHEN g.archivos_distintos > 1 THEN 'PROBABLE'
            ELSE 'INDETERMINADO'
        END AS clasificacion
    FROM grupos g JOIN frecuencia_concepto f USING (concepto_norm)
)
SELECT
    to_char(fecha, 'YYYY-MM') AS periodo,
    clasificacion,
    COUNT(*)          AS grupos,
    SUM(cant_filas)   AS movimientos_involucrados
FROM clasificados
WHERE clasificacion IN ('PROBABLE', 'POSIBLE', 'AMBIGUO')
GROUP BY periodo, clasificacion
ORDER BY periodo, clasificacion;


\echo '=============================================================='
\echo '5. EVIDENCIA DE SOLAPAMIENTO DE IMPORTACIONES (grupos PROBABLE)'
\echo '=============================================================='
-- Para cada grupo PROBABLE, muestra qué corridas de ImportBatch trajeron cada
-- fila -- si son corridas distintas y con fecha de corrida separada por unos
-- días, es evidencia directa de "archivo re-exportado con solapamiento de
-- período" (el escenario ya confirmado en la investigación de IMPORT-001).
-- ImportBatchId puede ser NULL en filas anteriores a Patch 0105 -- esos casos
-- quedan marcados como "sin trazabilidad de importación", no se pierden.

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm,
           "SourceFile" AS archivo, "ImportBatchId" AS import_batch_id
    FROM "BankStatements"
),
frecuencia_concepto AS (
    SELECT concepto_norm, COUNT(*) AS frecuencia_total FROM bs_norm GROUP BY concepto_norm
),
grupos AS (
    SELECT fecha, importe, concepto_norm,
           COUNT(*) AS cant_filas,
           COUNT(DISTINCT archivo) AS archivos_distintos
    FROM bs_norm
    GROUP BY fecha, importe, concepto_norm
    HAVING COUNT(*) > 1
),
probables AS (
    SELECT g.fecha, g.importe, g.concepto_norm
    FROM grupos g
    JOIN frecuencia_concepto f USING (concepto_norm)
    WHERE g.cant_filas = 2 AND g.archivos_distintos > 1 AND f.frecuencia_total <= GREATEST(5, 0)
)
SELECT
    p.fecha, p.importe, p.concepto_norm AS concepto,
    b."Id" AS movimiento_id,
    b.archivo,
    ib."SourceFile" AS import_batch_source_file,
    ib."CompletedAtUtc" AS import_batch_completado,
    CASE WHEN b.import_batch_id IS NULL THEN 'SIN TRAZABILIDAD (fila anterior a Patch 0105)' END AS aviso
FROM probables p
JOIN bs_norm b ON b.fecha = p.fecha AND b.importe = p.importe AND b.concepto_norm = p.concepto_norm
LEFT JOIN "ImportBatches" ib ON ib."Id" = b.import_batch_id
ORDER BY p.fecha, p.importe, b.archivo;


\echo '=============================================================='
\echo '6. IMPACTO YA EFECTIVO — ¿alguno de los duplicados ya está clasificado?'
\echo '=============================================================='
-- SourceEntityType.BankStatement = 2 (ver Domain/Enums/SourceEntityType.cs).
-- Si una fila del grupo PROBABLE ya tiene un ClassifiedMovementItem, ese
-- duplicado ya está afectando una métrica real (dashboard, MCP), no es solo
-- un dato crudo sin usar todavía.

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm,
           "SourceFile" AS archivo
    FROM "BankStatements"
),
frecuencia_concepto AS (
    SELECT concepto_norm, COUNT(*) AS frecuencia_total FROM bs_norm GROUP BY concepto_norm
),
grupos AS (
    SELECT fecha, importe, concepto_norm, COUNT(*) AS cant_filas, COUNT(DISTINCT archivo) AS archivos_distintos
    FROM bs_norm GROUP BY fecha, importe, concepto_norm HAVING COUNT(*) > 1
),
probables AS (
    SELECT g.fecha, g.importe, g.concepto_norm
    FROM grupos g JOIN frecuencia_concepto f USING (concepto_norm)
    WHERE g.cant_filas = 2 AND g.archivos_distintos > 1 AND f.frecuencia_total <= GREATEST(5, 0)
)
SELECT
    p.fecha, p.importe, p.concepto_norm AS concepto,
    b."Id" AS movimiento_id,
    (cmi."Id" IS NOT NULL) AS ya_clasificado,
    cmi."ClassifiedMovementId" AS classified_movement_id
FROM probables p
JOIN "BankStatements" b ON b."Date"::date = p.fecha AND b."Amount" = p.importe
    AND upper(trim(regexp_replace(b."Concept", '\s+', ' ', 'g'))) = p.concepto_norm
LEFT JOIN "ClassifiedMovementItems" cmi
    ON cmi."SourceEntityType" = 2 AND cmi."SourceId" = b."Id"
ORDER BY p.fecha, p.importe, b."Id";


\echo '=============================================================='
\echo '7. COINCIDENCIAS APROXIMADAS — mismo día y monto, descripción distinta'
\echo '=============================================================='
-- Mismo criterio que ya usa el detector de sospechosos actual del sistema
-- (SuspicionDetector: monto ± tolerancia, fecha ± ventana -- ver
-- src/FinancialSystem.Infrastructure/Review/SuspicionDetector.cs), pero
-- separando explícitamente los casos donde el concepto SÍ coincide (ya
-- cubiertos arriba) de los casos donde NO coincide. Esto es la categoría más
-- débil -- coincidencia normal la mayoría de las veces (dos gastos distintos,
-- mismo importe, mismo día) -- se entrega solo para que quede contado, no
-- para que se trate como sospecha real sin revisión humana.

WITH bs_norm AS (
    SELECT "Id", "Date"::date AS fecha, "Amount" AS importe,
           upper(trim(regexp_replace("Concept", '\s+', ' ', 'g'))) AS concepto_norm
    FROM "BankStatements"
)
SELECT
    a.fecha, a.importe,
    a."Id" AS movimiento_a, a.concepto_norm AS concepto_a,
    b."Id" AS movimiento_b, b.concepto_norm AS concepto_b
FROM bs_norm a
JOIN bs_norm b ON a.fecha = b.fecha AND a.importe = b.importe AND a."Id" < b."Id"
WHERE a.concepto_norm <> b.concepto_norm
ORDER BY a.fecha, a.importe;


\echo '=============================================================='
\echo '8. TARJETA (Transactions) — mismo análisis, alcance reducido'
\echo '=============================================================='
-- Transaction.ExternalId ya es un hash de contenido (fecha+importe+
-- descripción, o CouponNumber -- ver SheetParserHelpers.BuildTransactionExternalId)
-- y ya tiene índice único, así que dos filas con exactamente el mismo
-- contenido no deberían poder coexistir salvo un caso ya señalado en
-- IMPORT-002: el fallback sin CouponNumber podría, en teoría, fusionar dos
-- operaciones reales distintas con igual fecha+monto+descripción bajo el
-- MISMO ExternalId -- en ese caso la segunda nunca se inserta, y este query
-- no la va a encontrar (no hay dos filas para comparar: una de las dos ya se
-- perdió en el import). Sirve para ver coincidencias de fecha+importe con
-- descripción distinta, igual que la sección 7, no para repetir el análisis
-- de banco -- la tarjeta no tiene el problema de ExternalId posicional.

SELECT
    "Date"::date AS fecha, "Amount" AS importe, COUNT(*) AS cant_filas,
    array_agg(DISTINCT "Description") AS descripciones,
    array_agg("Id") AS ids
FROM "Transactions"
GROUP BY fecha, importe
HAVING COUNT(*) > 1
ORDER BY fecha;

\echo '=============================================================='
\echo 'FIN — ningún dato fue modificado por este script.'
\echo '=============================================================='
