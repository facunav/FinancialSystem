# DEDUPE-004-CONV §4.3 — Reconciliación de los 62 casos B contra IMPORT-003

> Cierre del §4.3 pendiente de `DEDUPE-004-CONV-auditoria-81-fuerte.md`. Solo lectura, sin cambios de código/SQL/base. No se ejecutó ninguna consulta nueva — no tengo acceso a la base real en este entorno (confirmado repetidas veces esta investigación: sin `dotnet`, sin conexión a Postgres). Todo lo de abajo es lectura directa de: (a) el Preview real ya pegado, (b) `docs/imports/IMPORT-003-auditoria-duplicados-resultados.md` (agregados), (c) `docs/imports/import-003-auditoria-duplicados.sql` (metodología, inspeccionada, no ejecutada por mí).

---

## 0. Qué dato falta, exactamente, y por qué

Para marcar un caso B como **CONFIRMADO** contra IMPORT-003 necesito evidencia de que **las mismas filas físicas (`BankStatement.Id`)** aparecen en un grupo de IMPORT-003 — no alcanza con que fecha/importe/concepto coincidan.

Tengo, de los 62 casos B: `PendienteId`, `LiquidadoId`, y (para 29 de ellos) 1 `CarryForwardMemberId` extra — **153 `Statement.Id` físicos conocidos, con certeza, del Preview real.**

No tengo: el `array_agg("Id")` por grupo que produce IMPORT-003 — el documento de resultados (`IMPORT-003-auditoria-duplicados-resultados.md`) solo reporta **agregados** (43/15/37/3, conteos y sumas), nunca la lista de IDs de cada uno de los 98 grupos. Sin esa lista no puedo cruzar IDs contra IDs — cualquier intento de hacerlo por fecha/importe/concepto sería exactamente la inferencia que me pediste evitar.

**No hace falta escribir SQL nueva.** La sección 1 de `docs/imports/import-003-auditoria-duplicados.sql` (ya existe, ya es de solo lectura, ya la ejecutaste una vez para producir el documento de resultados) **ya selecciona `array_agg("Id" ORDER BY archivo) AS ids`** por cada grupo — es exactamente el dato que falta. No la ejecuté (no tengo acceso a tu Postgres). Si volvés a correrla y me pegás la salida completa de la sección 1 (98 filas, con su columna `ids`), cruzo esos arrays literalmente contra los 153 `Statement.Id` de este documento — sin inferencia, en un solo pase.

Hasta que eso pase, el único resultado honesto es el de abajo: **0 confirmados, 0 no encontrados, 62 no determinables** — no porque el trabajo esté incompleto por descuido, sino porque el dato necesario no está disponible en ningún documento ya escrito.

---

## 1. Tabla de reconciliación

`SourceIds` = Pendiente / Liquidado / [extra CarryForward] (primeros 8 caracteres, consistente con el documento anterior). `SourceFiles` = código corto por fecha de archivo (`F21-07`=`Detalle_mov_cuenta_21_07_2026.xls`, `F27-07`, `F01-08`, `F06-08`, `F08-08`, `F16-08`, `F12-07` — los mismos 7 nombres que IMPORT-003 §6 cita textualmente). `?` = el Preview no imprimió el `SourceFile` del miembro extra de carry-forward.

| Caso B (#) | Fecha | Importe | Concepto | SourceIds | SourceFiles | ¿IMPORT-003? | Clasificación IMPORT-003 | Evidencia | Estado |
|---|---|---|---|---|---|---|---|---|---|
| 8 | 07-14 | -27081,08 | OP2920 | 0ad56abb/1344d209 | F01-08/F21-07 | — | — | Sin array de IDs de IMPORT-003 disponible | NO DETERMINABLE |
| 9 | 07-14 | -31754,84 | OP3158 | 89b13a4d/e8a5820c | F21-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 10 | 07-15 | -1900,00 | OP5182 | 3eda3e47/4e69496d | F21-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 11 | 07-15 | -13000,00 | OP7563 | 7f1b18e0/c931fa61 | F21-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 12 | 07-15 | -23510,76 | OP9675 | 102c0c02/16a363a5/d708bc5b | F21-07/F01-08/? | — | — | 3 archivos → candidato estructural a AMBIGUO en IMPORT-003, sin confirmar por ID | NO DETERMINABLE |
| 13 | 07-16 | -16000,00 | TRANSFERENCIA | ca179e82/d14b1f39/f98b652b | F01-08/F27-07/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 14 | 07-16 | -39950,00 | OP5623 | 1821adef/21039e28/4e991660 | F27-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 15 | 07-16 | -5000,00 | OP6857 | 1600f9be/3465d231/c7c73670 | F27-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 16 | 07-17 | -23700,00 | Transferencia inmediata | 0c18ffeb/f25616e2/fb39588c | F21-07/F27-07/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 17 | 07-17 | -19865,00 | OP1652 | 21e775b9/a374e40a/e7c28dcb | F21-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 19 | 07-20 | -15900,00 | OP1883 | 40db13ff/4b814aa6/974bf3a6 | F21-07/F27-07/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 20 | 07-20 | -6100,00 | OP6653 | 4591db4d/529c8752/a8f58479 | F21-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 21 | 07-20 | -19199,92 | OP2162 | 8ef05519/bf99c2f7/e76d92d3 | F21-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 22 | 07-20 | -2200,00 | OP1235 | 71543657/c1f183b3/ea055f1a | F27-07/F21-07/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 23 | 07-20 | -3600,00 | OP9471 | 5ca6d529/9b8d7ddc/d2cd8a59 | F27-07/F01-08/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 24 | 07-20 | -18100,00 | OP2606 | 50f22b78/a30b1b13/d4094cc3 | F01-08/F27-07/? | — | — | ídem (3 archivos) | NO DETERMINABLE |
| 25 | 07-21 | -19700,00 | OP7336 | 1643be00/714e1776 | F01-08/F27-07 | — | — | 2 archivos → candidato estructural a PROBABLE/POSIBLE, sin confirmar por ID | NO DETERMINABLE |
| 26 | 07-21 | -4500,00 | OP7789 | bebc8d07/d55d4507 | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 27 | 07-22 | -15500,00 | OP2079 | 18dacbb7/af95ce8d | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 28 | 07-22 | -12000,00 | Transferencia inmediata | 6333670b/d4ccebce | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 29 | 07-22 | -15000,00 | TRANSFERENCIA | a000ab89/ec178d94 | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 30 | 07-23 | -10900,00 | OP4102 | 76a0b6a1/f9a4b864 | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 31 | 07-23 | -11900,00 | OP2594 | 44516cb6/da080172 | F01-08/F27-07 | — | — | ídem | NO DETERMINABLE |
| 32 | 07-23 | -10340,00 | OP3741 | 6de72ed1/faf2af7d | F01-08/F27-07 | — | — | ídem | NO DETERMINABLE |
| 34 | 07-24 | -14000,00 | OP2800 | 056ea487/6ac6b57c | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 35 | 07-24 | -6400,00 | OP3174 | 272971df/f6d429c7 | F01-08/F27-07 | — | — | ídem | NO DETERMINABLE |
| 44 | 07-27 | +100000,00 | PAGO A TERCEROS OP.1106435 | 07e1e93e/2826f721 | F01-08/F27-07 | — | — | ídem | NO DETERMINABLE |
| 45 | 07-27 | +147000,00 | Cambio de moneda extranjera | 9dd95e93/c2c22daa | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 46 | 07-27 | -2500,00 | OP6201 | b4bcec84/f9ee9d12 | F27-07/F01-08 | — | — | ídem | NO DETERMINABLE |
| 47 | 07-27 | -19500,00 | OP4824 | 05eaf973/cb5bbbcd | F01-08/F27-07 | — | — | ídem | NO DETERMINABLE |
| 48 | 07-28 | -10000,00 | TRANSFERENCIA | 20033a48/fa5d3748 | F01-08/F06-08 | — | — | ídem | NO DETERMINABLE |
| 49 | 07-28 | -12000,00 | TRANSFERENCIA | 4cfdc107/be060e01 | F06-08/F01-08 | — | — | ídem | NO DETERMINABLE |
| 50 | 07-29 | -10000,00 | TRANSFERENCIA | 2a0107dc/3f8fcf56 | F06-08/F01-08 | — | — | ídem | NO DETERMINABLE |
| 51 | 07-29 | -29140,00 | OP8503 | 48673110/fae25761 | F01-08/F06-08 | — | — | ídem | NO DETERMINABLE |
| 52 | 07-29 | -4900,00 | OP5926 | 43fc0d8d/fbcb4af4 | F06-08/F01-08 | — | — | ídem | NO DETERMINABLE |
| 53 | 07-30 | -1900,00 | OP5459 | 0c7bc8cc/c78ec6a2 | F06-08/F01-08 | — | — | ídem | NO DETERMINABLE |
| 56 | 07-31 | -400000,00 | TRANSFERENCIA | 37084d38/74e57e79/bd13c727 | F01-08/F06-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 57 | 07-31 | +5604791,00 | PAGO DE HABERES Nro:99999999 | 799cf63d/e802756d | F01-08/F06-08 | — | — | **Coincide en importe+concepto con el outlier máximo citado en IMPORT-003 §8 — NO tratado como confirmación de identidad física sin el array de IDs real** | NO DETERMINABLE |
| 58 | 07-31 | -31991,53 | OP5395 | 43f8a14a/9c149dee/b02a35f9 | F01-08/F16-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 59 | 07-31 | -4800,00 | OP6298 | aec89826/b318abc4/b44c2588 | F01-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 60 | 07-31 | -2300,00 | OP7686 | 05d5f34d/aeeb3a3f | F06-08/F01-08 | — | — | 2 archivos → candidato PROBABLE/POSIBLE, sin confirmar | NO DETERMINABLE |
| 66 | 08-03 | +11,20 | INTERESES GANADOS | 16ba49df/5c05dfec/bf0a69af | F01-08/F06-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 67 | 08-03 | -1980214,95 | PAGO DE TARJETA VISA Nro:00045099 | 44a041ba/c3fe5bee | F06-08/F16-08 | — | — | **Coincide en importe+concepto con el outlier mínimo citado en IMPORT-003 §8 — NO tratado como confirmación de identidad física sin el array de IDs real** | NO DETERMINABLE |
| 68 | 08-03 | -31793,61 | PAGO DE TARJETA MASTERCARD Nro:00045099 | 511867e7/ef1af5c5 | F06-08/F16-08 | — | — | 2 archivos → candidato PROBABLE/POSIBLE, sin confirmar | NO DETERMINABLE |
| 69 | 08-03 | -16000,00 | TRANSFERENCIA | 5dd91138/af041164/b133517d | F06-08/F08-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 70 | 08-03 | -95000,00 | Transferencia inmediata | 78e5e746/d39ce6a5 | F06-08/F16-08 | — | — | 2 archivos → candidato PROBABLE/POSIBLE, sin confirmar | NO DETERMINABLE |
| 71 | 08-03 | -1264000,00 | TRANSFERENCIA | 17b24d3c/55d820e4/9f868ee3 | F16-08/F08-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 72 | 08-03 | -386549,00 | TRANSFERENCIA | 3217d306/44911b53/a3abab2c | F16-08/F08-08/? | — | — | ídem | NO DETERMINABLE |
| 73 | 08-03 | -29932,00 | TRANSFERENCIA | 87b125f8/bc05aff9/c0a8b43b | F16-08/F06-08/? | — | — | ídem | NO DETERMINABLE |
| 74 | 08-03 | -315000,00 | TRANSFERENCIA | 67d1f480/cd1f524f/cdcf2cf1 | F08-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 75 | 08-03 | -35160,00 | TRANSFERENCIA | 4e3e2e84/cddcc591/dc6f4f4e | F08-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 76 | 08-03 | -44872,00 | TRANSFERENCIA | 01e661e8/13cbf0ff/de457f72 | F08-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 77 | 08-03 | -2000,00 | OP5399 | 5810723b/c10a3113 | F16-08/F06-08 | — | — | 2 archivos → candidato PROBABLE/POSIBLE, sin confirmar | NO DETERMINABLE |
| 78 | 08-03 | -10955,00 | OP2585 | 1dbe6fca/c5cd78d8 | F16-08/F06-08 | — | — | ídem | NO DETERMINABLE |
| 79 | 08-03 | -32735,13 | OP3407 | 43c9423b/d180c816 | F16-08/F06-08 | — | — | ídem | NO DETERMINABLE |
| 82 | 08-04 | -37250,00 | OP1579 | 137c55da/2633e854/d9a7e92c | F16-08/F06-08/? | — | — | 3 archivos → candidato AMBIGUO, sin confirmar | NO DETERMINABLE |
| 84 | 08-05 | -10000,00 | TRANSFERENCIA | 0d27075f/9d2acd27/ce1070e9 | F16-08/F08-08/? | — | — | ídem | NO DETERMINABLE |
| 85 | 08-05 | -6000,00 | TRANSFERENCIA | 3f5b7710/79a376cc/ede2011a | F08-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 86 | 08-05 | -36855,00 | OP7273 | 21c4f5e0/786eb99f/d4483776 | F06-08/F08-08/? | — | — | ídem | NO DETERMINABLE |
| 87 | 08-05 | -10100,00 | OP0731 | 885bd35e/8ecb8eab/c8cb3e26 | F06-08/F16-08/? | — | — | ídem | NO DETERMINABLE |
| 88 | 08-05 | -7800,00 | OP2573 | 56cc2676/98200ecc/cd07ca79 | F16-08/F08-08/? | — | — | ídem | NO DETERMINABLE |
| 90 | 08-07 | -38901,80 | PAGO DE SERVICIOS TARJETA OP3309 | 47f08ed8/9566b6d5 | F08-08/F16-08 | — | — | 2 archivos → candidato PROBABLE/POSIBLE, sin confirmar | NO DETERMINABLE |

---

## 2. Resumen cuantitativo

| Estado | Cantidad |
|---|---|
| CONFIRMADO | **0/62** |
| NO ENCONTRADO | **0/62** |
| NO DETERMINABLE | **62/62** |

Ninguno de los dos conteos "CONFIRMADO" o "NO ENCONTRADO" tiene evidencia suficiente para poblarse todavía — ambos requieren el array de `Statement.Id` por grupo de IMPORT-003, que no está disponible en ningún documento ya escrito (ver §0). No convertí la ausencia de dato en "NO ENCONTRADO" ni en "PROBABLE" — se mantiene explícitamente como falta de evidencia.

---

## 3. Discrepancias (verificables sin datos nuevos, por lectura directa del SQL/motor)

Estas son diferencias **metodológicas**, confirmadas leyendo `import-003-auditoria-duplicados.sql` y `DedupeEngine.cs` — no dependen del array de IDs faltante:

1. **B no aplica el umbral de frecuencia que separa PROBABLE de POSIBLE en IMPORT-003.** IMPORT-003 clasifica un grupo de 2 archivos como POSIBLE (no PROBABLE) si la descripción tiene frecuencia total >5 en toda la cuenta (`GREATEST(5,0)` en el script). La vía B del motor no calcula ni usa esa frecuencia — un caso B de 2 archivos es FUERTE sin importar cuántas veces se repita el concepto en la cuenta. **Consecuencia estructural:** algunos de los 33 casos B de "2 archivos" podrían corresponder, si se confirmaran por ID, a grupos que IMPORT-003 hubiera clasificado POSIBLE (confianza más baja) en vez de PROBABLE — sin el array de IDs no puedo decir cuáles.
2. **B resuelve automáticamente los clusters de 3 archivos; IMPORT-003 los deja AMBIGUO sin aparear.** Los 29 casos B de "3 archivos" son, por definición de IMPORT-003, del tipo que esa auditoría declaró explícitamente fuera de su capacidad de apareo automático (`docs/imports/IMPORT-003-auditoria-duplicados.md` §4: *"no hay forma automática de saber qué subconjunto... se marca para revisión manual"*). La vía B (0007/0008) sí los resuelve, condicionado a que las 3 filas compartan exactamente `(Fecha, Importe, ConceptNormalized)`.
3. **Los 19 casos D+E/F+K+L (no forman parte de esta tabla, ya cerrados en §4.1 del documento anterior) son estructuralmente invisibles a IMPORT-003** — reafirmado, no repetido en detalle acá.

No hay evidencia todavía (ni la puede haber sin el array de IDs) de una discrepancia en sentido contrario: **ningún caso donde IMPORT-003 detecte algo que B no detecte** — no determinado, no descartado.

---

## 4. Conclusión

> ¿Los 62 B son realmente una nueva categoría de duplicados o son, en esencia, la materialización como identidad de duplicados que IMPORT-003 ya había detectado?

**HECHO VERIFICADO** (por lectura de código/SQL, no depende del array de IDs faltante):
- La vía B y la agrupación de IMPORT-003 comparten el mismo criterio base: `(Fecha, Importe, ConceptNormalized)` idéntico en ≥2 `SourceFile` distintos. Estructuralmente, B opera sobre el mismo fenómeno que IMPORT-003 ya había definido y cuantificado (43+15+37=95 grupos con archivos distintos, de un total de 98).
- B es una extensión, no una redefinición: quita el umbral de frecuencia (discrepancia #1) y resuelve automáticamente lo que IMPORT-003 dejó como AMBIGUO sin aparear (discrepancia #2).
- 2 de los 62 (#57, #67) coinciden en importe+concepto, exactamente, con los 2 outliers económicos que IMPORT-003 cita textualmente en su §8.

**NO DETERMINADO** (requiere el array de IDs de IMPORT-003, no disponible en ningún documento):
- Si cada uno de los 62 casos B corresponde, fila física por fila física, a uno de los 98 grupos ya detectados por IMPORT-003, o si alguno de los 62 es un grupo que IMPORT-003 nunca vio (posible si, por ejemplo, la base cambió entre la corrida de IMPORT-003 y la corrida de este Preview, aunque ambas reportan 500 `BankStatements` — coincidencia de conteo total, no prueba de que sea exactamente el mismo conjunto de filas).
- Si el importe/concepto exacto de #57 y #67 corresponde a las mismas filas físicas que IMPORT-003 citó, o a una coincidencia entre movimientos distintos con igual valor.

**No colapso esto en una única respuesta sí/no** — la evidencia disponible hoy alcanza para el hallazgo estructural (B es una extensión metodológica de IMPORT-003, verificado), no para la identidad física caso por caso (no determinado).

---

## 5. Próximo paso (ofrecido, no ejecutado)

Re-correr la **sección 1**, sin modificar, de `docs/imports/import-003-auditoria-duplicados.sql` contra la base real, y pegarme la salida completa (incluye la columna `ids`). Con eso cierro este documento con conteos reales de CONFIRMADO/NO ENCONTRADO en un solo pase, sin inferencia.

No se requiere ninguna consulta nueva, ninguna migración, ningún cambio de código.

---

## 6. Confirmación de restricciones

No se modificó código. No se modificó SQL existente. No se creó ni aplicó ninguna migración. No se ejecutó `ApplyAsync` ni `SaveChangesAsync`. No se insertó, actualizó ni borró ningún dato. No hubo commit ni push. Solo lectura y análisis, sobre datos ya entregados en esta conversación y documentos ya existentes en el repositorio.
