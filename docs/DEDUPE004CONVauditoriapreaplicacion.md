# DEDUPE-004-CONV — Auditoría de pre-aplicación (Evaluate → ApplyAsync → MovementIdentityLink)

> Método: clon descartable de `/home/user/FinancialSystem` en `scratchpad/audit-verify/`, patches 0001→0010 aplicados en secuencia real vía `git am` (verificado con `git log --oneline`, cada commit aplicó limpio salvo un patch cosmético duplicado de "eliminar variable sin usar" que se saltó — irrelevante para esta auditoría, no toca lógica). Todo lo que sigue es lectura directa del código resultante, no memoria ni inferencia. No se tocó el repo real. No se ejecutó nada — ni build, ni test, ni SQL, ni migración.

---

## 1. ¿Qué significa exactamente FUERTE?

**`DedupeCandidateResult`** (record, `IDedupeEngine.cs`) — lo que contiene cada resultado:

```
PendienteId, PendienteConcept, PendienteDate, PendienteAmount, PendienteSourceFile,
LiquidadoId, LiquidadoConcept, LiquidadoDate, LiquidadoAmount, LiquidadoSourceFile,
Classification, Evidence, CarryForwardMemberIds (IReadOnlyList<Guid>)
```

Referencia entre **2 y N** filas físicas de `BankStatement`: `PendienteId` + `LiquidadoId` + 0 o más `CarryForwardMemberIds`.

**Traza `Evaluate` → `ApplyAsync` → `MovementIdentityLink`** (`DedupeEngine.cs:65-157`):

1. `ApplyAsync` filtra `results.Where(r => r.Classification == Fuerte)` — cualquier otra clasificación se descarta en la primera línea, nunca se evalúa nada más sobre ella.
2. Calcula `candidateSourceIds` = unión de todos los `Statement.Id` (Pendiente+Liquidado+CarryForward) de **todos** los resultados FUERTE recibidos.
3. Consulta `MovementIdentityLinks` **una sola vez**, buscando cuáles de esos IDs ya tienen un link — `alreadyLinked` (HashSet en memoria).
4. Por cada resultado FUERTE, en orden de lista:
   - `memberIds` = Pendiente+Liquidado+CarryForward, deduplicados.
   - **Si CUALQUIER miembro ya está en `alreadyLinked` → se saltea el grupo ENTERO** (`continue`, sin insertar nada, sin excepción).
   - Si no: genera `IdentityGroupId = Guid.NewGuid()` (nuevo en cada corrida, no determinístico, no derivado de nada).
   - Inserta una fila `MovementIdentityLink` por cada miembro: `Role=Pendiente|Liquidado|CarryForward` según corresponda, `Classification=Fuerte` (hardcodeado, no copiado del resultado), `Evidence=result.Evidence` (mismo texto en las 2-N filas del grupo), `CreatedAtUtc`, `CreatedBy`.
   - Marca esos IDs como `alreadyLinked` **en memoria, dentro de la misma corrida** — protege contra que dos resultados de la misma llamada usen la misma fila física (ver §6).
5. Después de procesar TODOS los resultados FUERTE, **una sola** llamada a `SaveChangesAsync` (no una por grupo).

**Controles antes de insertar:** (a) query única de `alreadyLinked` al principio: (b) chequeo por grupo "si algún miembro ya está linkeado, saltear todo el grupo"; (c) actualización en memoria de `alreadyLinked` a medida que se procesa, para blindar contra colisiones dentro de la misma llamada.

**Si ya existe un vínculo para algún SourceId:** el resultado completo se saltea, sin error, sin duplicado, sin tocar el grupo existente. Confirmado literal en el código (`if (memberIds.Any(alreadyLinked.Contains)) continue;`).

---

## 2. ¿Los 81 FUERTE son realmente aptos para ApplyAsync?

| Vía | Qué evidencia lo hace FUERTE | Garantía de unicidad | Riesgo residual | ¿Cumple el contrato de `MovementIdentityLink`? |
|---|---|---|---|---|
| **B** (62) | Fecha+ConceptNormalized+Importe idénticos, literal, en ≥2 `SourceFile` | Estructural por `GroupBy` (ver §3) + guardián global 0010 | Exclusión gruesa por `yaCubiertos` puede **omitir** (no corromper) un cluster si cualquier miembro ya apareció en el pipeline principal con **cualquier** clasificación, no solo FUERTE — ver §3 | Sí — la cardinalidad 1↔1 por fila física la garantiza el guardián final (0010), no B por sí sola |
| **D+E** (14) | Transformación demostrada del sufijo de 4 dígitos (`Right4`) entre `Nro:` y `OP` | `realCompetitorBuckets<=1` (L) + guardián M + guardián D (contradicción) + guardián global 0010 | `Right4()` compara solo los **últimos 4 dígitos**, no el número completo — riesgo de colisión de sufijo entre operaciones distintas, mitigado por exigir Importe exacto + ventana ≤10 días + rol correcto, pero no eliminado — es el mecanismo de diseño documentado (DEDUPE-003-CONV), no un defecto nuevo | Sí — mismo guardián global |
| **F+K+L** (5) | Único candidato (L) + frecuencia de importe≤1 tras colapsar identidad económica (K, fix 0006) + cadena de Balance local (F) en ambos lados | `realCompetitorBuckets==1` local por pendiente + guardián global 0010 (necesario: ver nota abajo) | **"Cadena de Balance" es una verificación LOCAL** (fila siguiente en el MISMO archivo por `RowNumber`, nunca compara Balance entre pendiente y liquidado) — es un chequeo de coherencia interna del archivo, no una prueba cruzada de identidad; el nombre puede inducir a lectura errónea si no se lee el código | Sí — verificado además empíricamente (validación #2 del CLI sobre datos reales) |

**Nota importante sobre F+K+L (por qué el guardián global de 0010 es necesario, no redundante):** `realCompetitorBuckets==1` se calcula **por pendiente**, mirando solo los candidatos de ESE pendiente — es una condición local. La red `-17401` (previa a 0010) demostró que 3 pendientes distintos pueden, cada uno desde su propia perspectiva, ver "un único candidato" local, y aun así terminar compartiendo filas físicas entre sí — la unicidad local no implica unicidad global. 0010 corrige exactamente esa brecha, y es el mecanismo que hace que la afirmación "sin competidores, sin solapamiento con otro FUERTE" sea cierta para el resultado final, no solo para cada pendiente aislado.

---

## 3. Revisión de B — ¿garantiza que cada fila física aparezca en un único resultado B?

**Sí, estructuralmente, dentro de la vía B misma.** `clustersExactos = rows.GroupBy(r => (Date, ConceptNormalized, Amount))` — por definición de `GroupBy`, cada fila física pertenece a **exactamente un grupo** para esa clave (una fila tiene una sola Fecha/Concepto/Importe). Es imposible, por construcción, que la misma fila aparezca en dos clusters B distintos.

**3 archivos con la misma identidad económica:** el `GroupBy` no produce pares — produce **un cluster con las N filas**. `ordenados = miembros.OrderBy(Statement.Id)`; `ordenados[0]`=Pendiente, `ordenados[1]`=Liquidado, `ordenados.Skip(2)`=`CarryForwardMemberIds`. Al aplicar: **un único `IdentityGroupId`** para las 3 filas (no 3 pares, no 3 grupos). Confirmado línea por línea — no hay ninguna ruta que produzca 2 filas físicas → 2 grupos, ni 3 filas físicas → más de 1 grupo.

**Sin grupos superpuestos — la garantía real es del motor completo, no solo de B.** El invariante que pediste demostrar ("sin producir grupos superpuestos") no depende únicamente de B: `DegradarConflictosDeIdentidadFisica` corre **al final de `Evaluate`, sobre `results` completo** (pipeline principal + vía B juntos), agrupando por componentes conexas cualquier `Statement.Id` compartido entre **cualquier par** de resultados FUERTE, sin importar de qué vía provengan, y degrada todo el componente si tiene 2+. Esto es lo que 0010 garantiza de forma incondicional, no solo para el caso `-17401` — es la razón por la que puedo afirmar, con evidencia de código (no solo de los 81 datos reales), que ningún `Statement.Id` puede terminar en más de un resultado FUERTE al salir de `Evaluate`.

**Riesgo residual real, no corrupción:** `yaCubiertos` (línea 356-358) se construye desde `results` **antes** del filtro por clasificación — incluye Pendiente/Liquidado/CarryForward de resultados Posible, Indeterminado y Descartado del pipeline principal, no solo Fuerte. Si una fila que pertenece a un cluster B exacto ya apareció en **cualquier** resultado del pipeline principal (aunque sea un Posible débil), el cluster B completo se saltea (línea 367) — perdiendo también las otras filas del cluster, que podrían no tener ninguna otra vía de detección. Esto es un **hueco de cobertura silencioso** (algunos duplicados económicos reales podrían no aparecer nunca como candidato), **no un riesgo de seguridad de datos** — nunca produce un resultado incorrecto ni viola la cardinalidad; solo puede producir menos resultados de los que existen. No hay evidencia de que esto haya ocurrido en los 81 actuales (no es observable desde el Preview — por definición, lo que se omite no aparece), y no lo convierto en un hallazgo positivo sin esa evidencia.

---

## 4. Revisión de D+E

- **Cómo se obtiene Nro:** regex `\bNRO\.?:?\s*([0-9]{3,})\b` sobre `Concept` → `r.Nro`. `EsFormaNro = (Nro is not null)`.
- **Cómo se obtiene OP:** regex `\bOP\.?:?\s*([0-9]{3,})\b` sobre `Concept`. `Numero = Nro ?? OP` (prioriza Nro; si no hay Nro, usa OP).
- **Cómo se evita que una misma fila física participe en dos identidades:**
  - Localmente: `realCompetitorBuckets` cuenta los buckets NO contradictorios para el mismo pendiente — si hay más de 1, se degrada a Indeterminado (L) en vez de emitir 2 FUERTE.
  - Globalmente: guardián 0010 (mismo mecanismo que para F+K+L y B — es el mismo `DegradarConflictosDeIdentidadFisica` para todo `results`).
- **Qué ocurre si hay más de una representación candidata:** clasificación `Indeterminado`, evidencia `"L: N candidatos igualmente plausibles tras colapsar carry-forward"`.
- **Qué guardián evita un falso FUERTE:**
  - **D** (`NumeroContradice`): si el sufijo de 4 dígitos de pendiente y liquidado **difieren**, `Descartado` — explícitamente excluido, además, del conteo de competidores de L (fix E.6).
  - **M** (`genericNros`): si el `Nro` completo del pendiente está asociado a **más de un Importe distinto** en toda la cuenta, bloquea la vía D+E aunque el sufijo coincida — degrada a `Posible` con evidencia explícita.

**Riesgo residual (documentado, no nuevo):** `Right4()` compara solo los últimos 4 dígitos, no el número completo — es el mecanismo de diseño (el banco trunca `Nro:` largo a `OP` de 4 dígitos), no una aproximación accidental. El riesgo de colisión de sufijo entre operaciones económicamente distintas existe en teoría, mitigado por la exigencia simultánea de Importe exacto + ventana de 10 días + rol correcto — no eliminado, pero acotado. No es un bug a corregir ahora; es una propiedad conocida del diseño ya aprobado en DEDUPE-003-CONV.

---

## 5. Revisión de F+K+L (los 5 actuales: 337206, 904607, 899728, 684228, 136644)

Condición exacta para llegar a FUERTE por esta vía (línea 294): `liquidadoSinNumero && freq<=1 && pendiente.ChainOk && anyChainOk`.

- **Candidato único:** garantizado por haber llegado a esta rama del código — solo se alcanza cuando `realCompetitorBuckets<=1` (verificado antes, en la rama `else if (realCompetitorBuckets > 1)`).
- **ChainOk:** verificación **local** — `ComputeLocalChainOk` compara, dentro del **mismo archivo**, si `Balance_fila_actual - Importe_fila_actual == Balance_fila_siguiente` (por `RowNumber`). Nunca compara Balance entre pendiente y liquidado (archivos distintos) — es coherencia interna de cada archivo, no una prueba cruzada.
- **Frecuencia económica compatible (K, post-0006):** `frequencyByAmount` colapsa reexportaciones idénticas (mismo Fecha+Concepto+Importe+Balance+Balance-siguiente) antes de contar — así que la frecuencia mide identidades económicas reales, no copias físicas de una misma reexportación.
- **Sin competidores:** `realCompetitorBuckets==1`, local por pendiente.
- **Sin solapamiento con otro FUERTE:** garantizado por el guardián global 0010 — y **confirmado empíricamente**, no solo en teoría: la corrida real post-0010 mostró estos 5 sin conflicto (la red `-17401`, que sí violaba esto, quedó fuera de estos 5 — degradada a Indeterminado), y la validación #2 del propio CLI ("ningún SourceId aparece en más de un vínculo potencial FUERTE") lo confirma sobre datos reales, no solo sobre el código.

Los 5 cumplen lo pedido — con la precisión de que "cadena de Balance" es un chequeo local, no cruzado, y que la garantía "sin competidores" es local-por-pendiente, cerrada globalmente por 0010, no por F+K+L en sí misma.

---

## 6. Auditoría de seguridad de ApplyAsync (sin ejecutar)

- **Idempotencia:** sí — un segundo `ApplyAsync` con los mismos resultados no inserta nada nuevo (todos los miembros ya están en `alreadyLinked` desde la consulta inicial).
- **`alreadyLinked`:** una sola consulta al principio de la llamada, sobre TODOS los `SourceId` candidatos de TODOS los resultados FUERTE pasados — no una consulta por resultado.
- **Índice único (SourceEntityType, SourceId):** existe (`MovementIdentityLinkConfiguration.cs:36-38`), pero es el **backstop de última instancia** — el propio código comenta explícitamente que no confía solo en el índice ("nunca confiar solo en el índice único para evitar una excepción de constraint en corridas repetidas"). El chequeo de aplicación (`alreadyLinked`) es la defensa primaria.
- **Si un resultado ya fue aplicado:** se saltea completo, sin error.
- **Si dos resultados intentan usar el mismo SourceId (dentro de la MISMA llamada):** el primero en el orden de la lista se aplica; el segundo se saltea (porque el primero ya marcó `alreadyLinked` en memoria antes de procesar el segundo) — **esto es order-dependent**, pero es una red de seguridad para un input malformado ("carry-forward mal armado", según el propio comentario), no una ruta esperada: si `results` es exactamente lo que devolvió `PreviewAsync` sin modificar, esta rama nunca debería activarse, porque 0010 ya garantiza que ningún `Statement.Id` aparece en 2 resultados FUERTE al salir de `Evaluate`. **Importante: `ApplyAsync` no vuelve a correr el guardián de 0010 ni ninguna validación de `Evaluate` — confía ciegamente en el campo `Classification` del resultado que recibe.** Si alguna vez se le pasara una lista construida a mano (no la salida directa de `PreviewAsync`), esta protección de "primero gana" sería la única defensa contra un solapamiento — y es silenciosa, no falla ni avisa.
- **Si hay un resultado Posible/Indeterminado en la lista:** se descarta en la primera línea (`results.Where(r => r.Classification == Fuerte)`), nunca llega a evaluarse ni a tocar la base.
- **Si falla a mitad del batch:** todos los `Add()` son solo en memoria (change tracker de EF) hasta la única llamada final a `SaveChangesAsync`. Una excepción del CLR antes de esa llamada deja **cero filas** persistidas. Si `SaveChangesAsync` mismo falla (ej. constraint violado por una condición de carrera real entre dos `ApplyAsync` concurrentes), la llamada entera revierte — **todo o nada para el batch completo, no por grupo individual.**
- **¿Puede producir una identidad parcialmente persistida?** No, dentro de una corrida exitosa — cada grupo se persiste con todas sus filas o ninguna, porque todos los `Add()` viajan en el mismo `SaveChangesAsync`. **Efecto secundario a tener en cuenta:** esa misma atomicidad de batch significa que un solo grupo en conflicto (condición de carrera con otro `ApplyAsync` corriendo en paralelo) puede abortar el `SaveChangesAsync` completo y bloquear la persistencia de OTROS grupos del mismo batch que no tenían ningún conflicto — no es corrupción de datos, es un riesgo operativo (recomendación: no correr dos `ApplyAsync` concurrentes sobre la misma cuenta).

---

## 7. Revisión de la migración

- **La migración `AddMovementIdentityLink` sigue sin estar en Git** — confirmado de nuevo (`git log --all` sobre patrones `*MovementIdentity*`/`*AddMovementIdentityLink*`: vacío). No la inventé, no la generé.
- **Qué necesita estructuralmente `MovementIdentityLinks`** (leído de `MovementIdentityLink.cs` + `MovementIdentityLinkConfiguration.cs`, ambos sin cambios desde 0001):
  - `Id` (uuid, PK, `ValueGeneratedNever` — el valor lo genera la app, no la base).
  - `IdentityGroupId` (uuid, requerido, índice no-único).
  - `SourceEntityType` (int, requerido).
  - `SourceId` (uuid, requerido).
  - `Role` (int, requerido).
  - `Classification` (int, requerido).
  - `Evidence` (varchar(2048), requerido).
  - `CreatedAtUtc` (timestamptz, requerido).
  - `CreatedBy` (varchar(128), requerido).
  - Índice único compuesto `(SourceEntityType, SourceId)`.
  - **Sin FK hacia `BankStatements`** — referencia blanda deliberada, mismo patrón que `ClassifiedMovementItem`.
- **¿El modelo actual exige alguna migración adicional respecto del diseño 0001?** **NO.** Verificado con `git show --stat` sobre los 5 commits de 0006-0010: los cinco tocan **exclusivamente** `DedupeEngine.cs` y `DedupeEngineTests.cs`. Cero cambios en `MovementIdentityLink.cs`, `MovementIdentityLinkConfiguration.cs`, `AppDbContext.cs` o `IApplicationDbContext.cs` en ningún patch posterior a 0001.
- **Condiciones para que la migración sea aplicable:** (a) generada con el SDK real de .NET contra el modelo actual de `AppDbContext` (no escrita a mano) — no disponible en este entorno, confirmado de nuevo; (b) debe crear exactamente la tabla/columnas/índices de arriba; (c) no debe tocar ninguna tabla existente (`BankStatements` incluida); (d) debe generarse y aplicarse en tu máquina, en un paso separado y explícitamente autorizado.
- **Indicio de que 0006-0010 cambiaron el esquema:** ninguno — confirmado por `git diff --stat`, no por inferencia.

---

## 8. Resultado final

| Componente | Resultados | ¿Apto conceptualmente para Apply? | Riesgo residual | Acción necesaria |
|---|---|---|---|---|
| Vía B | 62 | Sí | Omisión silenciosa (no corrupción) por exclusión gruesa de `yaCubiertos` | Ninguna antes de Apply — documentado como límite conocido |
| Vía D+E | 14 | Sí | Colisión de sufijo de 4 dígitos, acotada por Importe+ventana+rol — inherente al diseño | Ninguna — no es un defecto, es el mecanismo aprobado |
| Vía F+K+L | 5 | Sí | "Cadena de Balance" es local, no cruzada — ya considerado en el diseño, no afecta la unicidad real | Ninguna — verificado además con datos reales (validación #2) |
| Invariante de cardinalidad (0010) | Todo el motor | Sí | Ninguno nuevo — validado por código y por datos reales | Ninguna |
| `ApplyAsync` | — | Sí | Atomicidad por batch completo (no por grupo) — una colisión de concurrencia puede abortar grupos sanos del mismo batch | Recomendación operativa: no correr `ApplyAsync` concurrente sobre la misma cuenta; considerar batches chicos si se corre por primera vez sobre 81 grupos |
| Migración | — | Pendiente de generar (no de rediseñar) | Ninguno — el esquema no cambió desde 0001 | Generarla con el SDK real en tu máquina y aplicarla en paso separado, explícitamente autorizado |

### Conclusión

**A) El motor está listo para pasar a la etapa de persistencia — solo falta generar/aplicar la migración.**

No encontré ningún bug de motor que deba corregirse antes de persistir. La invariante de cardinalidad (el único bug real encontrado en toda esta investigación, el solapamiento F+K+L de la red `-17401`) está corregida y verificada dos veces — por lectura de código (0010 corre incondicionalmente sobre `results` completo, cualquier vía) y por datos reales (validación #2 del CLI, post-0010). `ApplyAsync` es idempotente, atómico por batch, y nunca persiste nada que no sea `Classification == Fuerte`. Los riesgos residuales identificados en B, D+E y la concurrencia de `ApplyAsync` son reales y quedan documentados, pero ninguno de ellos puede producir un dato incorrecto o una identidad parcial si el motor se ejecuta como está diseñado (con la salida directa de `PreviewAsync`, sin edición manual de resultados, sin llamadas concurrentes).

---

## Confirmación de restricciones

Solo lectura. No se modificó código real, ni SQL, ni se creó ni aplicó ninguna migración, ni se ejecutó `ApplyAsync`/`SaveChangesAsync`, ni se insertó/actualizó/borró ningún dato. No hubo commit ni push. El clon usado para verificar (`scratchpad/audit-verify/`) es descartable, generado en esta misma sesión, y no fue empujado a ningún remoto.
