# DEDUPE-004-CONV — Auditoría exhaustiva del Preview real post-0010

> Fuente única de datos: la corrida real de `dotnet run --project tools/DedupePreviewCli` pegada por el usuario, post-aplicación de 0010, contra la base real (`financialsystem`, 500 `BankStatements`, sesión Postgres read-only). Ningún número de este documento fue inventado, estimado o recordado de memoria — todo IDs/fechas/importes/conceptos provienen literalmente de esa salida. Donde la evidencia documental del repositorio no alcanza para una afirmación, se marca explícitamente **NO DISPONIBLE** en vez de inferirse.
>
> Motor: congelado (0001–0010). Este documento es auditoría de resultados, no ingeniería. No se tocó código, base, migración, ni `ApplyAsync`/`SaveChangesAsync` para producirlo.

---

## 0. Resumen cuantitativo

| | Cantidad |
|---|---|
| Total candidatos (Preview) | 93 |
| **FUERTE** | **81** |
| POSIBLE | 3 |
| INDETERMINADO | 9 |
| — de los cuales, degradados por 0010 (red `-17401`) | 3 |
| — de los cuales, preexistentes (vía L, sin relación con 0010) | 6 |

### Los 81 FUERTE por tipo de evidencia

| Vía | Cantidad | % de 81 | Descripción |
|---|---|---|---|
| **B** (duplicado exacto) | **62** | 76,5% | `(Fecha, Importe, ConceptNormalized)` idénticos en ≥2 `SourceFile` — DEDUPE-003-CONV apéndice #16, agregada en 0007 |
| **D+E** (Nro→OP) | **14** | 17,3% | Transformación demostrada del sufijo numérico embebido en el concepto (`Nro:XXXXXX` → `OPXXXX`) |
| **F+K+L** | **5** | 6,2% | Único candidato + cadena de Balance exacta en ambos lados, sin número, frecuencia de importe = 1 |
| **Total** | **81** | 100% | |

Sub-desglose de la vía B, por cantidad de archivos físicos involucrados en el cluster:

| B / cant. archivos | Cantidad de resultados FUERTE |
|---|---|
| 2 archivos | 33 |
| 3 archivos | 29 |

---

## 1. Inventario — vía B (duplicado exacto), 62 resultados

`#` = número original en el Preview (1-indexado, para trazabilidad directa contra la salida pegada).

| # | PendienteId | LiquidadoId | Fecha (P→L) | Importe | Concepto | Archivos | CarryForward extra |
|---|---|---|---|---|---|---|---|
| 8 | 0ad56abb | 1344d209 | 07-14→07-14 | -27081,08 | PAGO CON VISA DEBITO 96477108 OP2920 | 2 | — |
| 9 | 89b13a4d | e8a5820c | 07-14→07-14 | -31754,84 | PAGO CON VISA DEBITO 96477108 OP3158 | 2 | — |
| 10 | 3eda3e47 | 4e69496d | 07-15→07-15 | -1900,00 | PAGO CON VISA DEBITO 96477108 OP5182 | 2 | — |
| 11 | 7f1b18e0 | c931fa61 | 07-15→07-15 | -13000,00 | PAGO CON VISA DEBITO 96477108 OP7563 | 2 | — |
| 12 | 102c0c02 | 16a363a5 | 07-15→07-15 | -23510,76 | PAGO CON VISA DEBITO 96477108 OP9675 | 3 | d708bc5b |
| 13 | ca179e82 | d14b1f39 | 07-16→07-16 | -16000,00 | TRANSFERENCIA | 3 | f98b652b |
| 14 | 1821adef | 21039e28 | 07-16→07-16 | -39950,00 | PAGO CON VISA DEBITO 96477108 OP5623 | 3 | 4e991660 |
| 15 | 1600f9be | 3465d231 | 07-16→07-16 | -5000,00 | PAGO CON VISA DEBITO 96477108 OP6857 | 3 | c7c73670 |
| 16 | 0c18ffeb | f25616e2 | 07-17→07-17 | -23700,00 | Transferencia inmediata | 3 | fb39588c |
| 17 | 21e775b9 | a374e40a | 07-17→07-17 | -19865,00 | PAGO CON VISA DEBITO 96477108 OP1652 | 3 | e7c28dcb |
| 19 | 40db13ff | 4b814aa6 | 07-20→07-20 | -15900,00 | PAGO CON VISA DEBITO 96477108 OP1883 | 3 | 974bf3a6 |
| 20 | 4591db4d | 529c8752 | 07-20→07-20 | -6100,00 | PAGO CON VISA DEBITO 96477108 OP6653 | 3 | a8f58479 |
| 21 | 8ef05519 | bf99c2f7 | 07-20→07-20 | -19199,92 | PAGO CON VISA DEBITO 96477108 OP2162 | 3 | e76d92d3 |
| 22 | 71543657 | c1f183b3 | 07-20→07-20 | -2200,00 | PAGO CON VISA DEBITO 96477108 OP1235 | 3 | ea055f1a |
| 23 | 5ca6d529 | 9b8d7ddc | 07-20→07-20 | -3600,00 | PAGO CON VISA DEBITO 96477108 OP9471 | 3 | d2cd8a59 |
| 24 | 50f22b78 | a30b1b13 | 07-20→07-20 | -18100,00 | PAGO CON VISA DEBITO 96477108 OP2606 | 3 | d4094cc3 |
| 25 | 1643be00 | 714e1776 | 07-21→07-21 | -19700,00 | PAGO CON VISA DEBITO 96477108 OP7336 | 2 | — |
| 26 | bebc8d07 | d55d4507 | 07-21→07-21 | -4500,00 | PAGO CON VISA DEBITO 96477108 OP7789 | 2 | — |
| 27 | 18dacbb7 | af95ce8d | 07-22→07-22 | -15500,00 | PAGO CON VISA DEBITO 96477108 OP2079 | 2 | — |
| 28 | 6333670b | d4ccebce | 07-22→07-22 | -12000,00 | Transferencia inmediata | 2 | — |
| 29 | a000ab89 | ec178d94 | 07-22→07-22 | -15000,00 | TRANSFERENCIA | 2 | — |
| 30 | 76a0b6a1 | f9a4b864 | 07-23→07-23 | -10900,00 | PAGO CON VISA DEBITO 96477108 OP4102 | 2 | — |
| 31 | 44516cb6 | da080172 | 07-23→07-23 | -11900,00 | PAGO CON VISA DEBITO 96477108 OP2594 | 2 | — |
| 32 | 6de72ed1 | faf2af7d | 07-23→07-23 | -10340,00 | PAGO CON VISA DEBITO 96477108 OP3741 | 2 | — |
| 34 | 056ea487 | 6ac6b57c | 07-24→07-24 | -14000,00 | PAGO CON VISA DEBITO 96477108 OP2800 | 2 | — |
| 35 | 272971df | f6d429c7 | 07-24→07-24 | -6400,00 | PAGO CON VISA DEBITO 96477108 OP3174 | 2 | — |
| 44 | 07e1e93e | 2826f721 | 07-27→07-27 | +100000,00 | PAGO A TERCEROS OP.1106435 | 2 | — |
| 45 | 9dd95e93 | c2c22daa | 07-27→07-27 | +147000,00 | Cambio de moneda extranjera | 2 | — |
| 46 | b4bcec84 | f9ee9d12 | 07-27→07-27 | -2500,00 | PAGO CON VISA DEBITO 96477108 OP6201 | 2 | — |
| 47 | 05eaf973 | cb5bbbcd | 07-27→07-27 | -19500,00 | PAGO CON VISA DEBITO 96477108 OP4824 | 2 | — |
| 48 | 20033a48 | fa5d3748 | 07-28→07-28 | -10000,00 | TRANSFERENCIA | 2 | — |
| 49 | 4cfdc107 | be060e01 | 07-28→07-28 | -12000,00 | TRANSFERENCIA | 2 | — |
| 50 | 2a0107dc | 3f8fcf56 | 07-29→07-29 | -10000,00 | TRANSFERENCIA | 2 | — |
| 51 | 48673110 | fae25761 | 07-29→07-29 | -29140,00 | PAGO CON VISA DEBITO 96477108 OP8503 | 2 | — |
| 52 | 43fc0d8d | fbcb4af4 | 07-29→07-29 | -4900,00 | PAGO CON VISA DEBITO 96477108 OP5926 | 2 | — |
| 53 | 0c7bc8cc | c78ec6a2 | 07-30→07-30 | -1900,00 | PAGO CON VISA DEBITO 96477108 OP5459 | 2 | — |
| 56 | 37084d38 | 74e57e79 | 07-31→07-31 | -400000,00 | TRANSFERENCIA | 3 | bd13c727 |
| 57 | 799cf63d | e802756d | 07-31→07-31 | +5604791,00 | PAGO DE HABERES Nro:99999999 | 2 | — |
| 58 | 43f8a14a | 9c149dee | 07-31→07-31 | -31991,53 | PAGO CON VISA DEBITO 96477108 OP5395 | 3 | b02a35f9 |
| 59 | aec89826 | b318abc4 | 07-31→07-31 | -4800,00 | PAGO CON VISA DEBITO 96477108 OP6298 | 3 | b44c2588 |
| 60 | 05d5f34d | aeeb3a3f | 07-31→07-31 | -2300,00 | PAGO CON VISA DEBITO 96477108 OP7686 | 2 | — |
| 66 | 16ba49df | 5c05dfec | 08-03→08-03 | +11,20 | INTERESES GANADOS | 3 | bf0a69af |
| 67 | 44a041ba | c3fe5bee | 08-03→08-03 | -1980214,95 | PAGO DE TARJETA VISA Nro:00045099 | 2 | — |
| 68 | 511867e7 | ef1af5c5 | 08-03→08-03 | -31793,61 | PAGO DE TARJETA MASTERCARD Nro:00045099 | 2 | — |
| 69 | 5dd91138 | af041164 | 08-03→08-03 | -16000,00 | TRANSFERENCIA | 3 | b133517d |
| 70 | 78e5e746 | d39ce6a5 | 08-03→08-03 | -95000,00 | Transferencia inmediata | 2 | — |
| 71 | 17b24d3c | 55d820e4 | 08-03→08-03 | -1264000,00 | TRANSFERENCIA | 3 | 9f868ee3 |
| 72 | 3217d306 | 44911b53 | 08-03→08-03 | -386549,00 | TRANSFERENCIA | 3 | a3abab2c |
| 73 | 87b125f8 | bc05aff9 | 08-03→08-03 | -29932,00 | TRANSFERENCIA | 3 | c0a8b43b |
| 74 | 67d1f480 | cd1f524f | 08-03→08-03 | -315000,00 | TRANSFERENCIA | 3 | cdcf2cf1 |
| 75 | 4e3e2e84 | cddcc591 | 08-03→08-03 | -35160,00 | TRANSFERENCIA | 3 | dc6f4f4e |
| 76 | 01e661e8 | 13cbf0ff | 08-03→08-03 | -44872,00 | TRANSFERENCIA | 3 | de457f72 |
| 77 | 5810723b | c10a3113 | 08-03→08-03 | -2000,00 | PAGO CON VISA DEBITO 96477108 OP5399 | 2 | — |
| 78 | 1dbe6fca | c5cd78d8 | 08-03→08-03 | -10955,00 | PAGO CON VISA DEBITO 96477108 OP2585 | 2 | — |
| 79 | 43c9423b | d180c816 | 08-03→08-03 | -32735,13 | PAGO CON VISA DEBITO 96477108 OP3407 | 2 | — |
| 82 | 137c55da | 2633e854 | 08-04→08-04 | -37250,00 | PAGO CON VISA DEBITO 96477108 OP1579 | 3 | d9a7e92c |
| 84 | 0d27075f | 9d2acd27 | 08-05→08-05 | -10000,00 | TRANSFERENCIA | 3 | ce1070e9 |
| 85 | 3f5b7710 | 79a376cc | 08-05→08-05 | -6000,00 | TRANSFERENCIA | 3 | ede2011a |
| 86 | 21c4f5e0 | 786eb99f | 08-05→08-05 | -36855,00 | PAGO CON VISA DEBITO 96477108 OP7273 | 3 | d4483776 |
| 87 | 885bd35e | 8ecb8eab | 08-05→08-05 | -10100,00 | PAGO CON VISA DEBITO 96477108 OP0731 | 3 | c8cb3e26 |
| 88 | 56cc2676 | 98200ecc | 08-05→08-05 | -7800,00 | PAGO CON VISA DEBITO 96477108 OP2573 | 3 | cd07ca79 |
| 90 | 47f08ed8 | 9566b6d5 | 08-07→08-07 | -38901,80 | PAGO DE SERVICIOS TARJETA 96477108 OP3309 | 2 | — |

**Nota de reconciliación (#57 y #67):** estos dos casos coinciden, por **importe exacto y concepto**, con los dos outliers económicos citados literalmente en `IMPORT-003-auditoria-duplicados-resultados.md` §8 (`PAGO DE HABERES` = máximo del rango, +$5.604.791,00; `PAGO DE TARJETA VISA` = mínimo, -$1.980.214,95). Esto es una coincidencia de valor verificable, no una inferencia por conteo.

---

## 2. Inventario — vía D+E (Nro→OP), 14 resultados

| # | PendienteId | LiquidadoId | Fecha (P→L) | Importe | Concepto Pendiente | Concepto Liquidado | Sufijo | CarryForward extra |
|---|---|---|---|---|---|---|---|---|
| 7 | df799060 | 5a9c18a5 | 07-12→07-13 | -16555,00 | PAGO CON VISA DEBITO Nro:311482 | PAGO CON VISA DEBITO 96477108 OP1482 | 1482 | 03b23f92 |
| 18 | f3f6ca2b | dc9cce51 | 07-20→07-21 | -7200,00 | PAGO CON VISA DEBITO Nro:186757 | PAGO CON VISA DEBITO 96477108 OP6757 | 6757 | aff13df9 |
| 40 | f2505241 | d73619e2 | 07-27→07-28 | -35788,08 | PAGO CON VISA DEBITO Nro:824290 | PAGO CON VISA DEBITO 96477108 OP4290 | 4290 | 5053e18d |
| 41 | 4d931034 | 5a03ed83 | 07-27→07-28 | -7200,00 | PAGO CON VISA DEBITO Nro:829314 | PAGO CON VISA DEBITO 96477108 OP9314 | 9314 | c1770a22 |
| 43 | 71439d32 | 867822ea | 07-27→07-27 | -4700,00 | PAGO CON VISA DEBITO Nro:119613 | PAGO CON VISA DEBITO 96477108 OP9613 | 9613 | — |
| 54 | 835ce74e | 7796b4f2 | 07-31→08-03 | -5410,00 | PAGO CON VISA DEBITO Nro:432039 | PAGO CON VISA DEBITO 96477108 OP2039 | 2039 | 3fa5a492 |
| 55 | c3547f68 | 6da6ef1d | 07-31→08-03 | -31989,83 | PAGO CON VISA DEBITO Nro:233619 | PAGO CON VISA DEBITO 96477108 OP3619 | 3619 | bd4400a5 |
| 61 | 034a2f95 | a5ed5799 | 08-01→08-03 | -3300,00 | PAGO CON VISA DEBITO Nro:695696 | PAGO CON VISA DEBITO 96477108 OP5696 | 5696 | 540d89ff |
| 62 | 5d99f4c7 | 80d04b64 | 08-01→08-03 | -7200,00 | PAGO CON VISA DEBITO Nro:633718 | PAGO CON VISA DEBITO 96477108 OP3718 | 3718 | c0bc64bd |
| 63 | bc181868 | 9352b445 | 08-01→08-03 | -7200,00 | PAGO CON VISA DEBITO Nro:632680 | PAGO CON VISA DEBITO 96477108 OP2680 | 2680 | 64345f62 |
| 64 | e5b3e39b | 2a87f891 | 08-01→08-03 | -28645,00 | PAGO CON VISA DEBITO Nro:567576 | PAGO CON VISA DEBITO 96477108 OP7576 | 7576 | 27628e32 |
| 83 | b76c37aa | 4ee19b4e | 08-05→08-06 | -36597,52 | PAGO CON VISA DEBITO Nro:575114 | PAGO CON VISA DEBITO 96477108 OP5114 | 5114 | 1f443e61 |
| 89 | a0b1c61a | 1616438b | 08-06→08-06 | -5900,00 | PAGO CON VISA DEBITO Nro:963946 | PAGO CON VISA DEBITO 96477108 OP3946 | 3946 | f170ab68 |
| 91 | e2d87164 | 34695db6 | 08-08→08-10 | -7200,00 | PAGO CON VISA DEBITO Nro:550153 | PAGO CON VISA DEBITO 96477108 OP0153 | 0153 | — |

Los 14 son homogéneos: 100% "PAGO CON VISA DEBITO", 100% transición de un archivo con formato `Nro:XXXXXX` a otro con formato `OPXXXX`, sufijo confirmado idéntico en cada par.

**Reconciliación IMPORT-003:** estos 14 son **estructuralmente invisibles** a IMPORT-003 (ver §3.1) — no pueden aparecer como PROBABLE/POSIBLE/AMBIGUO ni en ninguna categoría de esa auditoría, porque el texto de concepto difiere entre pendiente y liquidado (`Nro:X` vs `OPY`) y el script agrupa por igualdad literal de concepto.

---

## 3. Inventario — vía F+K+L, 5 resultados

| # | PendienteId | LiquidadoId | Fecha (P→L) | Importe | Concepto Pendiente | Concepto Liquidado | Caso conocido | CarryForward extra |
|---|---|---|---|---|---|---|---|---|
| 2 | da22d1c4 | 0b1ceeca | 07-10→07-13 | -43000,00 | TRANSF DEBITO Nro:337206 | TRANSFERENCIA | **ADVERSARIAL 337206** | b5ece93c |
| 33 | d8fc85ff | 5aa3e9ef | 07-24→07-27 | -3400,00 | TRANSF DEBITO Nro:136644 | TRANSFERENCIA | 136644 (mencionado, no marcado adversarial por el CLI) | 657a781d |
| 38 | 778a7a33 | a3a9658f | 07-25→07-27 | +6500,00 | TRANSF CREDITO Nro:904607 | TRANSFERENCIA | **ADVERSARIAL 904607** | — |
| 39 | 3783293b | 7fd3f623 | 07-26→07-27 | -90000,00 | DB TRF INM COE Nro:899728 | Transferencia inmediata | **ADVERSARIAL 899728** | — |
| 42 | 6085cf89 | be8bd525 | 07-27→07-27 | -200,00 | TRANSF DEBITO Nro:684228 | TRANSFERENCIA | **ADVERSARIAL 684228** | ca1fd35c |

De los 5 casos adversariales conocidos que el propio CLI marca (`026888, 337206, 684228, 904607, 899728`), **4/5 son FUERTE** (todos vía F+K+L). El quinto (`026888`) es POSIBLE — ver §5.

**Reconciliación IMPORT-003:** los 5 son igualmente invisibles a IMPORT-003 por el mismo motivo que D+E — concepto literal distinto entre pendiente y liquidado.

---

## 4. Reconciliación contra documentación existente (Paso C)

| Documento buscado | Estado |
|---|---|
| IMPORT-003 (resultados agregados) | **DISPONIBLE**, sin commitear — usado arriba |
| IMPORT-003 (lista fila-por-fila de los 98 grupos) | **NO DISPONIBLE** — el documento solo reporta agregados, no el detalle de cada grupo |
| DEDUPE-001 (documento propio) | **NO DISPONIBLE** — solo existe como tarjeta de roadmap de 3 líneas en `Mapa de confianza de datos.md`, sin casos |
| DEDUPE-002 (documento propio) | **NO DISPONIBLE** — ídem |
| DEDUPE-003 (documento propio) | **NO DISPONIBLE** — ídem |
| DEDUPE-004 (documento propio) | **NO DISPONIBLE** — ídem |
| Lista de los "22 casos originales" | **NO DISPONIBLE** — no existe en ningún archivo del repo; solo lo que el propio CLI marca en tiempo de ejecución (`*** CASO ADVERSARIAL CONOCIDO ***`, 5 casos) |
| Casos 026888, 337206, 684228, 904607, 899728 | **DISPONIBLE** — vía el marcador del propio CLI en el Preview real (no reconstruido) |
| Casos 136644, 148054, 013329, 421889, -17401 | **DISPONIBLE** solo como número visible en el Concepto dentro del Preview real — sin marcador adversarial, sin fuente documental que diga qué representan |

### 4.1 Por qué 19 de los 81 FUERTE son invisibles a IMPORT-003, por diseño

Verificado leyendo `import-003-auditoria-duplicados.sql` línea por línea (sección 1, CTE `bs_norm`/`grupos`): el script agrupa por `(fecha, importe, upper(trim(regexp_replace(Concept,...))))` — **igualdad literal de texto normalizado**, sin ningún paso de transformación semántica.

Los 14 D+E y los 5 F+K+L dependen exactamente de que el concepto **no** sea literalmente igual entre las dos filas (`Nro:311482` vs `OP1482`; `TRANSF DEBITO Nro:337206` vs `TRANSFERENCIA`) — es la señal misma que el motor usa para vincularlos. Bajo el criterio de agrupación de IMPORT-003, cada mitad de estos 19 pares cae en su propio grupo de una sola fila y **nunca aparece como "grupo con más de una fila"** — el script las descarta silenciosamente por diseño (`HAVING COUNT(*) > 1`), no por error.

**Conclusión verificable:** los 19 casos D+E/F+K+L representan evidencia de identidad que **IMPORT-003 no podía ver estructuralmente**, no una corrección de algo que IMPORT-003 hubiera clasificado distinto.

### 4.2 Los 62 casos B — comparables en principio, no reconciliados fila-por-fila

Los 62 casos B usan el mismo criterio de igualdad de concepto que IMPORT-003, así que **en principio** son el subconjunto comparable. Sin el detalle fila-por-fila de los 98 grupos de IMPORT-003 (no disponible en este documento), no puedo afirmar cuántos de los 62 coinciden exactamente con los 43 PROBABLE / 15 POSIBLE / 37 AMBIGUO / 3 NO_DUPLICADO de esa auditoría — sería inferencia por conteo, que pediste explícitamente evitar.

Lo que sí puedo afirmar con evidencia directa (no inferencia):
- Los 33 casos B de 2 archivos son estructuralmente del mismo tipo que la definición PROBABLE/POSIBLE de IMPORT-003 (`cant_filas = 2`, `archivos_distintos > 1`) — la vía B del motor **no** aplica el umbral de frecuencia (≤5 vs >5) que separa PROBABLE de POSIBLE en IMPORT-003; el motor los trata igual (ambos FUERTE), IMPORT-003 los hubiera separado.
- Los 29 casos B de 3 archivos son estructuralmente del mismo tipo que AMBIGUO en IMPORT-003 (`cant_filas > 2`) — categoría que IMPORT-003 declaró explícitamente **fuera de su alcance para apareo automático** ("no hay forma automática de saber qué subconjunto... se marca para revisión manual"). La vía B (0007/0008) sí los resuelve automáticamente, siempre que el importe+fecha+concepto coincidan exactamente en todas las filas del cluster — es una extensión real de lo que IMPORT-003 dejó pendiente, no una duplicación de trabajo ya hecho.
- #57 y #67 coinciden por valor exacto con los 2 outliers económicos citados en IMPORT-003 §8 (ver nota en §1).

### 4.3 Para reconciliación exacta (opcional, no ejecutado)

Si querés la reconciliación fila-por-fila real contra IMPORT-003, el camino más directo es re-ejecutar la **sección 1** del script ya existente (`docs/imports/import-003-auditoria-duplicados.sql`, ya es de solo lectura, ya está en el repo, no hace falta SQL nuevo) y pegarme el resultado completo — con eso puedo cruzar literalmente `(Fecha, Importe, Concepto, ids)` contra los 62 casos B de este documento, entrada por entrada, sin inferencia.

Si preferís algo más dirigido — solo los grupos que contengan alguno de los 122 `Statement.Id` físicos que aparecen en mis 81 FUERTE — puedo darte esa consulta con los IDs ya embebidos, dado que ya los tengo todos de este Preview. Decime cuál de las dos preferís y te doy el SQL exacto.

---

## 5. Los 3 POSIBLE y 9 INDETERMINADO — por qué no son FUERTE

| # | PendienteId | LiquidadoId | Fecha (P→L) | Importe | Concepto Pendiente | Concepto Liquidado | Clasificación | Motivo exacto (evidencia real) |
|---|---|---|---|---|---|---|---|---|
| 1 | 655420df | 73752577 | 07-09→07-13 | -1100000,00 | TRANSF DEBITO Nro:026888 | TRANSFERENCIA | POSIBLE | F: cadena de Balance no confirma en ambos lados (pendiente=True, liquidado=False) — **caso adversarial conocido, 026888** |
| 3 | 64086507 | 0956a4cf | 07-11→07-14 | -15000,00 | TRANSF DEBITO Nro:421889 | TRANSFERENCIA | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 4 | 64086507 | e8401b40 | 07-11→07-13 | -15000,00 | TRANSF DEBITO Nro:421889 | TRANSFERENCIA | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 5 | d4e5c1b3 | 0242846b | 07-11→07-13 | -30000,00 | TRANSF DEBITO Nro:013329 | TRANSFERENCIA | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 6 | d4e5c1b3 | 6db31b59 | 07-11→07-16 | -30000,00 | TRANSF DEBITO Nro:013329 | TRANSFERENCIA | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 36 | 5e6fb1d6 | 29ce8c8a | 07-25→07-15 | -20000,00 | DB TRF INM COE Nro:148054 | TRANSFERENCIA | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 37 | 5e6fb1d6 | 018a4d09 | 07-25→07-27 | -20000,00 | DB TRF INM COE Nro:148054 | Transferencia inmediata | INDETERMINADO | L: 2 candidatos igualmente plausibles tras colapsar carry-forward |
| 65 | b891f28a | fba8f8ec | 08-03→08-04 | -17401,00 | Transferencia inmediata | DEBITO DIRECTO | INDETERMINADO | **CONFLICTO (0010)** — fila física compartida con otros 2 FUERTE, red `-17401` |
| 80 | 411fe073 | ff513b4f | 08-04→08-03 | -17401,00 | DEBITO DIRECTO | Transferencia inmediata | INDETERMINADO | **CONFLICTO (0010)** — ídem |
| 81 | 99778628 | b891f28a | 08-04→08-03 | -17401,00 | DEBITO DIRECTO | Transferencia inmediata | INDETERMINADO | **CONFLICTO (0010)** — ídem |
| 92 | 3a268587 | b4c52486 | 08-14→08-04 | -12000,00 | TRANSFERENCIA CAP094 375310 2 Nro:00010008 | TRANSFERENCIA | POSIBLE | K: frecuencia de importe=8 (>1) bloquea la vía única+cadena |
| 93 | de9c39f3 | 6fea5190 | 08-15→08-05 | -30000,00 | TRANSF DEBITO Nro:075829 | TRANSFERENCIA | POSIBLE | K: frecuencia de importe=16 (>1) bloquea la vía única+cadena |

Patrones que emergen de esta tabla (observación directa, no inferencia):

- **Los 4 INDETERMINADO de `421889`/`013329`** (#3-6) y **los 2 de `148054`** (#36-37) comparten la misma causa: el mismo Pendiente tiene 2 Liquidados candidatos igualmente plausibles tras carry-forward (vía L) — **no relacionado con 0010**, preexistente desde antes de esta ventana de trabajo.
- **Los 3 de la red `-17401`** (#65, 80, 81) son exactamente el conflicto que 0010 resolvió — confirmado ya en el turno anterior.
- **`026888`** (POSIBLE) y **`075829`/`00010008`** (#92/93, POSIBLE) son los 3 únicos POSIBLE — dos por alta frecuencia de importe (K bloquea F+K+L), uno por cadena de Balance no confirmada (F).
- **Ninguno de estos 12 casos fue tocado por 0010** salvo los 3 de `-17401` — confirmado por el propio patch (solo actúa sobre componentes con 2+ resultados FUERTE compartiendo `Statement.Id`; ninguno de los otros 9 tenía siquiera un resultado FUERTE que degradar, porque nunca llegaron a serlo).

---

## 6. Confirmación de restricciones (Paso E)

- No se modificó código.
- No se modificó la base.
- No se aplicó ninguna migración.
- No se ejecutó `ApplyAsync` ni `SaveChangesAsync`.
- No hubo commit ni push.
- Este documento es 100% derivado de: (a) la salida real ya pegada del Preview post-0010, (b) lectura directa de archivos ya existentes en el repositorio (`IMPORT-003-*`, `Mapa de confianza de datos.md`, `import-003-auditoria-duplicados.sql`). Ningún dato fue estimado o reconstruido de memoria.

---

## 7. Lo que todavía falta para decidir sobre los 81 FUERTE

No es una recomendación de acción — es el inventario de lo que este documento **no** puede resolver todavía:

1. Reconciliación fila-por-fila de los 62 casos B contra los 98 grupos reales de IMPORT-003 (ver §4.3 — requiere una corrida SQL adicional, ya ofrecida).
2. Identidad económica real de la red `-17401` (1, 2, 3 o 4 identidades distintas) — sigue sin Balance/RowNumber de las 3 filas "DEBITO DIRECTO" para decidirlo; siguen INDETERMINADO por diseño, correctamente.
3. Cuáles de los 81 (o de los 233 involucrados en IMPORT-003) corresponden a los "22 casos originales" que mencionás — no determinable sin que me pases esa lista o su fuente.
4. Si alguno de los 81 FUERTE, al aplicarse como `MovementIdentityLink`, generaría un conflicto con clasificaciones ya existentes (`ClassifiedMovementItem`) — IMPORT-003 §9 ya reportó que **las dos copias de cada uno de los 43 PROBABLE ya tienen `ClassifiedMovementItem` propio**; si eso también es cierto para estos 81, aplicar los links tiene una implicación de doble contabilización que todavía no se auditó para este conjunto específico.

---

*Fuente: Preview real post-0010 pegado por el usuario en esta conversación (dotnet run --project tools/DedupePreviewCli, base `financialsystem`, 500 BankStatements, sesión Postgres read-only) + lectura directa de `docs/imports/IMPORT-003-auditoria-duplicados-resultados.md`, `docs/imports/IMPORT-003-auditoria-duplicados.md`, `docs/imports/import-003-auditoria-duplicados.sql`, `docs/Mapa de confianza de datos.md` (todos existentes en el working tree de esta sesión, sin commitear). Ningún número fue inventado, estimado o recordado de memoria.*
