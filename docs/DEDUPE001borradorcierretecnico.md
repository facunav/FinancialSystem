# DEDUPE-001 — Borrador de cierre técnico (PARA REVISIÓN — no está en el repo)

> Este documento es un borrador. No fue escrito en `docs/`. No se generó ningún patch. No se modificó código, datos, ni se hizo commit/push. Es exclusivamente para que lo revises antes de decidir si pasa al repositorio.

---

## 1. Estado final de la investigación

| Categoría | Cantidad | Estado |
|---|---:|---|
| Casos originales confirmados (8 iniciales + 14 adicionales, ya cerrados antes de DEDUPE-001j) | 22 | CONFIRMADO |
| Nuevos FUERTE (auditados adversarialmente en DEDUPE-001j) | 5 | CONFIRMADO |
| **Total confirmado** | **27** | — |
| POSIBLE | 2 | NO CONFIRMADO |
| INDETERMINADO | 2 | NO DETERMINABLE |
| **Total movimientos investigados relevantes** | **31** | — |

Aritmética: 22 + 5 = 27 confirmados. 27 + 2 + 2 = 31 total. Verificado.

---

## 2. Qué quedó demostrado — separación estricta

**HECHOS DEMOSTRADOS** (verificados directamente contra datos reales, sin inferencia):
- BBVA puede representar el mismo movimiento real en dos formas de texto distintas (`Concept`) y con `Date` distinta, en exportaciones sucesivas.
- El criterio de IMPORT-003 (`Fecha + Importe + Concepto` exactos) no detecta estas 27 ocurrencias porque, por definición, ni la fecha ni el concepto coinciden entre las dos representaciones.
- El sufijo `OPxxxx` es, en los 22 casos originales, siempre los últimos 4 dígitos del `Nro:` correspondiente.
- La fórmula de cadena de `Balance` correcta es `saldo(fila actual) − Amount(fila actual) = saldo(fila siguiente, RowNumber mayor)`, verificada al centavo.

**EVIDENCIA FUERTE** (27 casos — combinación de múltiples señales independientes, sin explicación alternativa encontrada tras auditoría adversarial):
- Los 27 movimientos confirmados.

**EVIDENCIA COMPATIBLE PERO INSUFICIENTE** (2 casos — la hipótesis no está contradicha, pero tampoco demostrada):
- `136644` y `148054`.

**NO DETERMINABLE CON LOS DATOS ACTUALES** (2 casos):
- `013329` y `421889`.

No se usa "probablemente" para los 27 confirmados. No se usa "confirmado" para los 2 POSIBLE.

---

## 3. Hallazgo central

IMPORT-003, basado exclusivamente en coincidencia exacta de `Fecha + Importe + Concepto`, tiene un **falso negativo estructural**: no es un defecto puntual ni un caso raro — es una consecuencia directa de que BBVA puede representar un mismo movimiento en dos etapas (pendiente → liquidado) cambiando simultáneamente `Date` y `Concept`. Esto dejó de ser hipótesis: son 27 movimientos confirmados (22 ya cerrados + 5 que resistieron auditoría adversarial completa en DEDUPE-001j).

---

## 4. Patrones de Concept demostrados

Solo los realmente observados en datos reales:

| Patrón | Identificador numérico sobreviviente | Casos |
|---|---|---|
| `Nro:XXXXXX → OPXXXX` | Sí — últimos 4 dígitos | 22 |
| `Nro:XXXXXX → TRANSFERENCIA` | No | incluido en los 5 FUERTE (`026888`, `904607`, `337206`, `684228`) |
| `DB TRF INM COE Nro:XXXXXX → Transferencia inmediata` | No | incluido en los 5 FUERTE (`899728`) |

No se documentan otros patrones — no se observaron.

---

## 5. Importancia del número embebido

`Nro:XXXXXX → OPXXXX` conserva los últimos 4 dígitos del número — es un identificador parcial verificable dentro del propio `Concept`.

`Nro:XXXXXX → TRANSFERENCIA` (y `DB TRF INM COE → Transferencia inmediata`) **no conserva ningún identificador equivalente visible** en la forma liquidada. Por lo tanto, **estos dos mecanismos no tienen el mismo nivel de demostrabilidad**: el primero se apoya en un dato textual verificable; el segundo se apoyó, en cambio, en la combinación de unicidad de importe en toda la cuenta, ausencia de identificador contradictorio, cadena de Balance exacta, y reconciliación cruzada sin residuo — evidencia igualmente sólida para los 5 casos concretos auditados, pero que no es generalizable automáticamente a cualquier otro candidato futuro sin repetir el mismo nivel de escrutinio.

---

## 6. Balance — qué demuestra y qué no

La fórmula corregida (`saldo − importe = saldo_siguiente`) tuvo **471/478 = 98,5% de recall** sobre el conjunto de control.

**Sí demuestra:** que una fila individual es contablemente consistente con su vecino inmediato dentro del mismo archivo — es decir, que la fila no es un dato corrupto ni una anomalía aislada.

**No demuestra por sí sola:** que dos filas de archivos distintos sean el mismo movimiento real. La cadena local válida en A y en B, por separado, no equivale a identidad cruzada — eso solo se estableció, para los 5 FUERTE, combinándola con la reconciliación cruzada (sección 7) y la ausencia de competidores (sección 8).

---

## 7. Reconciliación angosta

La primera reconciliación (contra todos los conceptos, sin filtrar familia) no discriminó: la densidad real de movimientos —particularmente `PAGO CON VISA DEBITO`, con cientos de filas— y las ventanas de varios días superpuestas entre archivos generaban demasiadas filas "exclusivas" no relacionadas con el candidato.

Al restringir la comparación a la familia de transferencias (`TRANSF DEBITO/CREDITO`, `DB TRF INM COE`, `TRANSFERENCIA`, `Transferencia inmediata`), y al documentar explícitamente la cobertura real de cada archivo (fecha mínima/máxima), se estableció que **lo que parecía ruido era, en su mayor parte, el borde de cobertura de cada archivo** — filas fuera del rango de fechas que ese archivo específico cubre, no evidencia contradictoria. Una vez separado correctamente ese borde:

- Los 5 casos FUERTE quedaron con reconciliación **1:1** dentro del rango de fechas común entre archivo A y archivo B (o, en el caso de `026888`/`337206`, sin superposición de fechas en absoluto entre `12_07` y `21_07`, lo que elimina cualquier zona gris posible).
- No quedaron residuos sin explicar.
- Se verificó explícitamente, fila por fila, que ninguna de las filas excluidas por borde de cobertura podía ser una contraparte alternativa (auditoría adversarial, DEDUPE-001j).

---

## 8. Controles negativos

Se encontraron, en datos reales, importes de `TRANSFERENCIA` suelta genuinamente recurrentes en fechas muy separadas (hasta 14 ocurrencias distintas a lo largo de meses): `-10.000`, `-30.000`, `-20.000`, `-15.000`, `-12.000`, `-5.000`, `-50.000`, `-95.000`.

**Qué demuestran exactamente:** que "mismo importe + concepto de transferencia" no es, por sí solo, evidencia de identidad — existen movimientos genuinamente independientes que comparten importes redondos con regularidad.

**Por qué los 5 FUERTE no caen en esta categoría** (no es una afirmación general de que el método sea infalible — es específico de estos 5):
- Ninguno de los 5 importes FUERTE aparece en la lista de importes recurrentes.
- Los 5 requieren, estructuralmente, que el lado pendiente tenga un `Nro:`/`COE` — ninguno de los controles negativos lo tiene en ambos lados, por lo que el método ni siquiera los habría propuesto como candidatos.
- Cada uno de los 5 pasó, además, verificación de ausencia de competidores en toda la cuenta, ausencia de colisión de identificador, y cadena de Balance exacta con vecinos revisados.

Los controles negativos no validan "todo el algoritmo" — validan específicamente que el requisito de número embebido en el lado pendiente es una salvaguarda real, no cosmética.

---

## 9. Los 2 POSIBLE

`136644` (-3.400) y `148054` (-20.000): reconciliación compatible, transformación de Concept compatible con el patrón ya demostrado, pero el importe es demasiado frecuente en el resto de la cuenta como para descartar coincidencia por sí sola, y no existe un identificador independiente adicional que lo resuelva. La validación sintética (DEDUPE-001i) demostró explícitamente que una reconciliación limpia con importe frecuente puede producir un falso positivo — por eso no se elevan a FUERTE.

No se descartan. No se confirman.

---

## 10. Los 2 INDETERMINADOS

`013329` (-30.000) y `421889` (-15.000): existen dos familias de liquidación plausibles para cada uno (`07-12` vs `07-15` para el primero; `07-12` vs `07-13` para el segundo), y los datos disponibles no permiten determinar cuál corresponde al pendiente. No se intentó resolverlos artificialmente en ninguna etapa.

---

## 11. Qué significa esto para IMPORT-003

Sin modificar código: IMPORT-003 **no puede seguir considerándose exhaustivo** si su criterio de identidad depende exclusivamente de `Fecha + Importe + Concepto` exactos. El problema no es "hay algunos duplicados sueltos" — es que **el mismo movimiento real puede cambiar de representación entre exportaciones**, lo cual es una limitación de identidad/reconciliación del modelo de datos, no un problema de parser ni un bug puntual corregible con un ajuste menor.

---

## 12. Qué NO hacer todavía

Esta investigación **no** determina cuál debe ser el algoritmo definitivo de identidad. No se decide acá:
- si usar el sufijo de `Nro:` como identificador primario;
- si usar `Balance` como señal (y con qué peso);
- qué ventana de fecha usar;
- si combinar `Importe` + `Concept` similarity + otras señales;
- ni ninguna combinación de las anteriores.

Eso corresponde a una etapa de **diseño**, posterior y separada de esta investigación.

---

## 13. Recomendación de estado

**DEDUPE-001 = INVESTIGACIÓN COMPLETADA / LISTA PARA CIERRE.**

Esto significa: la investigación produjo evidencia suficiente para responder la pregunta original (¿existe un mecanismo real de falso negativo en IMPORT-003, y cuánto mide?), y no tiene sentido seguir buscando candidatos indefinidamente con las mismas técnicas — se agotó el margen razonable de lo que este dataset puede demostrar con los métodos aplicados.

Esto **no** significa que todos los candidatos estén resueltos — los 2 POSIBLE y los 2 INDETERMINADO quedan explícitamente abiertos, caracterizados, y sin forzar.

---

## 14. Conclusión

- La hipótesis de falso negativo estructural de IMPORT-003 quedó demostrada, no es una hipótesis.
- El fenómeno pendiente → liquidado es recurrente, no aislado.
- Hay **27 movimientos confirmados** afectados.
- Existen **2 casos adicionales compatibles pero no confirmados**.
- Existen **2 casos que no pueden determinarse** con los datos actuales.
- No corresponde seguir ampliando la búsqueda indefinidamente con las mismas técnicas.
- La siguiente etapa, si se decide continuar, debe ser **diseño del criterio de identidad/deduplicación** — no otra ronda de búsqueda indiscriminada.

---

*Fin del borrador. No fue escrito en `docs/`. Ningún dato ni código fue modificado. Pendiente tu revisión antes de decidir A) cerrar, B) diseñar el criterio, o C) abrir una investigación específica para los 4 casos restantes.*
