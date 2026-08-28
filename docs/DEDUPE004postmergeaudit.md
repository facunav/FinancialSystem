# DEDUPE-004 — Auditoría técnica POST-MERGE

> Método: todo lo verificable sin `dotnet`/Postgres se verificó por lectura directa de `origin/master` (fetch real, `git show`/`git ls-tree`/`git grep` contra el commit real, sin checkout destructivo del working tree). Este entorno **no tiene el SDK de .NET ni acceso de red al Postgres real** (confirmado de nuevo: `which dotnet` vacío, `psql` a `localhost:5432` → *connection refused*) — es la misma limitación arquitectónica de siempre, no cambió. Cada sección marca explícitamente qué se verificó por código y qué **NO EJECUTADO** requiere que lo corras vos.

---

## 1. Estado Git

**Hallazgo importante primero:** el merge que describís **es real y lo verifiqué** — pero vive en `origin/master` (commit `818e407`), **no en la rama designada de este trabajo** (`claude/financialmcp-audit-roadmap-sgzqqi`, que sigue en `dede331`, sin ningún commit de Dedupe-004 posterior). Confirmé con `git branch --all --contains 818e407` → solo `remotes/origin/master`.

- **Branch actual (esta sesión):** `claude/financialmcp-audit-roadmap-sgzqqi` (sin el merge).
- **Working tree de esta sesión:** con cambios staged/no-commiteados preexistentes (los mismos `IMPORT-003-*`/`Mapa de confianza de datos.md` de turnos anteriores) — no relacionados con este merge.
- **Commit del merge (en `origin/master`):** `818e407 Añadir auditoría y soporte para MovementIdentityLink`, autor `facunav <facunav@yahoo.com.ar>`, `2026-08-27 21:36:55 -0300`.
- **Archivos que tocó ese commit:**
  - `docs/DEDUPE004CONVauditoriapreaplicacion.md` (+147)
  - `docs/DEDUPE004CONVreconciliacion62Bvsimport003.md` (+143)
  - `src/FinancialMcp.Api/FinancialMcp.Api.csproj` (+4 — agrega `Microsoft.EntityFrameworkCore.Design`, necesario para que `dotnet ef` funcione con ese proyecto como *startup project*)
  - `src/FinancialSystem.Infrastructure/Migrations/20260828000053_AddMovementIdentityLink.cs` (+52)
  - `src/FinancialSystem.Infrastructure/Migrations/20260828000053_AddMovementIdentityLink.Designer.cs` (+892)
  - `src/FinancialSystem.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (+45, solo el bloque de la nueva entidad — la migración anterior ya tenía el resto del snapshot)
- **Verifiqué también** que el `DedupeEngine.cs` de `origin/master` es **byte-a-byte idéntico** al resultado de aplicar mis 10 patches (0001→0010) en secuencia sobre el estado base — lo comparé línea por línea (`diff`, vacío). Mismo resultado para `MovementIdentityLink.cs` y `MovementIdentityLinkConfiguration.cs`. Esto confirma que lo que está en producción/master es exactamente lo que audité en la sesión anterior, sin desviación.
- **Archivos de migración/snapshot olvidados:** ninguno — listé el directorio `Migrations/` completo de `origin/master`: 16 migraciones + `AppDbContextModelSnapshot.cs`, `20260828000053_AddMovementIdentityLink` es la **última cronológicamente**, ninguna posterior.

**Nota de coherencia (no es un bug, es una decisión pendiente tuya):** si el próximo patch se genera contra la rama designada de esta sesión (como indican las reglas del proyecto), vas a estar generándolo sobre una base **sin** el merge — desalineado de lo que ya corriste en producción. Antes del siguiente patch conviene decidir explícitamente si seguimos contra `origin/master` o si primero sincronizamos la rama designada.

---

## 2. Migraciones EF Core

**NO EJECUTADO** — no tengo `dotnet` en este entorno (`which dotnet` → vacío, confirmado de nuevo).

Lo que sí verifiqué sin el SDK, por lectura directa del árbol de `origin/master`:
- `20260828000053_AddMovementIdentityLink.cs`/`.Designer.cs` existen y están commiteados.
- Es la migración con el timestamp más alto de las 16 — no hay ninguna migración posterior en el árbol de archivos.

Para cerrar este punto con certeza real, corré vos:
```powershell
dotnet ef migrations list `
  --project .\src\FinancialSystem.Infrastructure\FinancialSystem.Infrastructure.csproj `
  --startup-project .\src\FinancialMcp.Api\FinancialMcp.Api.csproj
```
Esperado: `20260828000053_AddMovementIdentityLink` sin `(Pending)`, y ninguna entrada después de esa.

---

## 3. PostgreSQL real

**NO EJECUTADO** — intenté conectar (`psql "host=localhost port=5432 dbname=financialsystem user=postgres" -c "select 1"`) y obtuve `connection refused`, esperable: no hay ruta de red desde este contenedor hacia tu Postgres local. No lo simulé.

Para confirmarlo vos, con solo lectura:
```sql
\d "MovementIdentityLinks"

SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'MovementIdentityLinks';
```
Lo que la migración (ya verificada en el punto 5) dice que deberías ver: 9 columnas (`Id, IdentityGroupId, SourceEntityType, SourceId, Role, Classification, Evidence, CreatedAtUtc, CreatedBy`), índice `IX_MovementIdentityLinks_IdentityGroupId` (no único) y `IX_MovementIdentityLinks_Source_Unique` **UNIQUE** sobre `(SourceEntityType, SourceId)`.

---

## 4. Contaminación de la protección read-only

**NO EJECUTADO** lo que requiere Postgres real o `dotnet ef database update`. Lo que sí verifiqué por código:

- **`git grep` de `ALTER SYSTEM` y `postgresql.conf` en todo `origin/master`** (`.cs`/`.sql`/`.md`): **cero coincidencias.** No hay evidencia en el repo de que se haya tocado la configuración del servidor para resolver el problema de solo-lectura.
- **`DedupePreviewCli` mantiene su aislamiento** — no lo toqué, y `origin/master` tampoco lo modificó desde la última vez que lo audité (mismo `Program.cs`, mismo `SET default_transaction_read_only = on` scoped a su propia conexión, verificado en el punto 6 de abajo vía `grep`).

Para confirmar vos que la capacidad de escritura de Postgres sigue intacta y que `dotnet ef database update` funciona sin `PGOPTIONS`:
```powershell
$env:PGOPTIONS = $null
dotnet ef database update `
  --project .\src\FinancialSystem.Infrastructure\FinancialSystem.Infrastructure.csproj `
  --startup-project .\src\FinancialMcp.Api\FinancialMcp.Api.csproj
```
(si ya está aplicada, este comando no debería hacer nada — es la confirmación de que el mecanismo de escritura funciona, no un riesgo).

---

## 5. Modelo EF — consistencia entre entidad, configuración, migración, Designer y snapshot

**GREEN — verificado byte a byte, sin inferencia.** Extraje el bloque `MovementIdentityLink` de `20260828000053_AddMovementIdentityLink.Designer.cs` y de `AppDbContextModelSnapshot.cs` y los comparé: **idénticos, columna por columna.** Ambos coinciden exactamente con:
- `Up()` de la migración (mismos tipos: `uuid`, `integer`×4, `character varying(2048)`, `timestamp with time zone`, `character varying(128)`).
- `MovementIdentityLinkConfiguration.cs` (mismas longitudes máximas, mismos `IsRequired()`, misma conversión de enums a `int`).
- `MovementIdentityLink.cs` (mismas propiedades, mismos tipos C#).

No encontré:
- Tipos incorrectos.
- Inconsistencias nullable/non-nullable.
- Longitudes distintas entre configuración y migración.
- Índices en la migración ausentes del snapshot, ni viceversa (los 2 índices están en los 3 lugares: migración, Designer, snapshot).
- Enums persistidos de forma distinta a `int` (los 3 enums — `SourceEntityType`, `Role`, `Classification` — están mapeados a `integer` de forma consistente en las 4 fuentes).
- Problemas de `DateTime`/UTC: `CreatedAtUtc` es `timestamp with time zone` en los 4 lugares, coherente con que `IDateTimeProvider.UtcNow` (usado en `DedupeEngine.ApplyAsync`) entrega UTC.

`Id` no tiene ninguna anotación de generación de valor en el Designer/snapshot — es coherente con `ValueGeneratedNever()` en la configuración (comportamiento por convención de EF para PK `Guid` sin atributo adicional, no requiere anotación explícita para reflejarlo).

---

## 6. Integración con el dominio

Grep completo sobre `origin/master` para `MovementIdentityLink`, `IdentityGroupId`, `SourceEntityType`, `Role`, `Classification`, `Evidence`, `CreatedBy`:

1. **Dónde se crean los links hoy:** en ningún lugar de código de producción. `MovementIdentityLink.Add(...)` solo aparece dentro de `DedupeEngine.ApplyAsync` (`src/FinancialSystem.Infrastructure/Dedupe/DedupeEngine.cs`) — y **`ApplyAsync` no tiene ningún llamador en código de producción**, solo en tests (`DedupeEngineTests.cs`, 4 invocaciones).
2. **Dónde se consultan:** `ApplyAsync` mismo (para `alreadyLinked`, antes de insertar). Ningún endpoint, tool MCP, ni servicio de dominio consulta `MovementIdentityLinks` todavía.
3. **¿DEDUPE-004 ya genera links o solo preparó el modelo?** **Solo preparó el modelo y la infraestructura.** `BbvaBankStatementImporter` (el único punto de integración con el flujo real de importación) llama exclusivamente `PreviewAsync` después de persistir — nunca `ApplyAsync` — y solo loguea el conteo de candidatos FUERTE encontrados. No se está escribiendo ni un solo `MovementIdentityLink` en producción hoy, aunque la tabla ya exista.
4. **¿Puede algún flujo insertar 2 links para el mismo `(SourceEntityType, SourceId)`?** No, en el uso actual: el único código que inserta (`ApplyAsync`) no tiene ningún llamador productivo. Si en el futuro se invocara, el propio método hace el chequeo de `alreadyLinked` antes de insertar (ver auditoría de pre-aplicación anterior, sección 6, reconfirmada sin cambios en `origin/master`) — y el índice único de Postgres es el backstop final.

---

## 7. Tests

**NO EJECUTADO** — sin `dotnet` en este entorno, no puedo correr `dotnet test`. No lo simulo ni asumo un resultado.

Lo que sí puedo darte, por lectura directa del árbol de `origin/master`, es el inventario real de archivos de test relevantes para que corras vos y me pegues el resultado:

```text
tests/FinancialSystem.Infrastructure.Tests/Dedupe/DedupeEngineTests.cs   (32 tests conocidos de la sesión anterior)
tests/FinancialSystem.Infrastructure.Tests/Audit/AuditReportServiceTests.cs
tests/FinancialSystem.Infrastructure.Tests/Imports/ImportBatchMovementLinkTests.cs
tests/FinancialSystem.Infrastructure.Tests/Imports/ImportConsistencyVerificationTests.cs
tests/FinancialSystem.Infrastructure.Tests/Imports/ImportFileProcessingSink{Idempotency,Validation}Tests.cs
tests/FinancialSystem.Infrastructure.Tests/Imports/ImportFileRouter{Consistency,Idempotency,Traceability,Validation}Tests.cs
tests/FinancialSystem.Infrastructure.Tests/Imports/Import{FileValidator,HandlerSelection,PipelineDiagnostics,ersContract,sPathResolver}Tests.cs
tests/FinancialSystem.McpServer.Tests/MovementToolsExplainMovementImportOriginTests.cs
tests/FinancialMcp.Api.Tests/Imports/Import{FileSignatureValidator,UploadValidation}Tests.cs
tests/FinancialMcp.Api.Tests/Authentication/PlanningAuditInvestigationsProtectedEndpointsTests.cs
```
Ninguno de estos (salvo `DedupeEngineTests.cs`) referencia `MovementIdentityLink` directamente — coincidieron por la palabra "Import"/"Audit" del filtro pedido, no por relación real con DEDUPE-004. Corré `dotnet test` completo y pegame Passed/Failed/Skipped y los nombres exactos de cualquier falla — con eso completo esta sección con datos reales, no antes.

---

## 8. Auditoría de regresión conceptual

Esta sección tiene el hallazgo más importante de toda la auditoría — lo marco aparte porque es concreto, no genérico.

**Encontré, y pude leer por primera vez, `docs/DEDUPE001borradorcierretecnico.md`** (commiteado en `origin/master`, aunque su propio encabezado dice *"borrador... no está en el repo"* — desactualizado, ver hallazgo #4 más abajo). Es una investigación previa, independiente de esta sesión, que llegó a: **27 confirmados (22 originales + 5 adversariales) + 2 POSIBLE + 2 INDETERMINADO = 31 casos**, con reglas explícitas: *"No se usa 'probablemente' para los 27 confirmados. No se usa 'confirmado' para los 2 POSIBLE."*

**Contraste caso por caso contra el Preview real post-0010 de esta sesión:**

| Caso | DEDUPE-001 (borrador) | Motor actual (Preview post-0010) | ¿Coincide? |
|---|---|---|---|
| `026888` | (uno de los 5 adversariales confirmados) | POSIBLE — "F: cadena de Balance no confirma" | Ver nota¹ |
| `904607`, `337206`, `684228`, `899728` | Confirmados (4 de los 5 adversariales) | FUERTE (F+K+L) | Sí |
| `013329` | INDETERMINADO — 2 familias plausibles | INDETERMINADO (vía L) | **Sí, coincide** |
| `421889` | INDETERMINADO — 2 familias plausibles | INDETERMINADO (vía L) | **Sí, coincide** |
| `148054` | POSIBLE — importe demasiado frecuente | INDETERMINADO (vía L) | Ambas dicen "no seguro", coinciden en espíritu aunque no en la etiqueta exacta |
| **`136644`** | **POSIBLE — "el importe es demasiado frecuente en el resto de la cuenta... no se eleva a FUERTE"** | **FUERTE (F+K+L), evidencia: "frecuencia=1"** | **NO COINCIDE — contradicción directa** |

¹ Nota sobre `026888`: el propio `Program.cs` de `DedupePreviewCli` (línea 112) marca los 5 adversariales como `["026888", "904607", "899728", "337206", "684228"]`, y el comentario de la línea 188 asume que los 5 "siguen FUERTE" — pero en el Preview real, `026888` da **POSIBLE**, no FUERTE. Esto ya lo había señalado yo mismo en la reconciliación de la sesión anterior (no es nuevo), pero vale reconfirmarlo acá: el propio script de verificación interno del CLI (punto 4 de su salida) ya lo marca como `*** ALERTA ***` — es un hallazgo conocido, no oculto, y no contradice a DEDUPE-001 (que no dice que los 5 deban dar FUERTE necesariamente, solo que están "confirmados" — su propio doc en la sección 5 aclara que el mecanismo sin número sobreviviente "no es generalizable automáticamente").

### Hallazgo central: caso `136644`

- **DEDUPE-001** (con verificación manual contra datos reales, sección 9 de su borrador): *"el importe es demasiado frecuente en el resto de la cuenta como para descartar coincidencia por sí sola, y no existe un identificador independiente adicional que lo resuelva"* → clasificado **POSIBLE**, explícitamente **no elevado a FUERTE**.
- **El motor actual** (Preview real post-0010, resultado #33 de 93): clasifica el mismo caso como **FUERTE**, vía F+K+L, con evidencia literal `"frecuencia=1"` — es decir, el guardián K (que cuenta cuántas identidades económicas distintas comparten el importe -3400 dentro de la familia `TRANSFERENCIA`/`TRANSFERENCIA INMEDIATA`) no encontró ningún competidor.

**No puedo determinar, sin ejecutar SQL real, cuál de los dos tiene razón** — y evito convertir esto en una afirmación definitiva en cualquier dirección, como pediste:
- Es posible que DEDUPE-001 haya medido "frecuencia del importe -3400 en TODA la cuenta" (cualquier concepto), mientras que el guardián K de `DedupeEngine.cs` está **deliberadamente acotado** a la familia literal `"TRANSFERENCIA"`/`"TRANSFERENCIA INMEDIATA"` (línea 185) — si `-3400` es frecuente solo en otras familias de concepto (ej. pagos de tarjeta), K correctamente no lo cuenta como competidor, y ambas conclusiones podrían ser compatibles bajo definiciones distintas de "frecuente".
- O puede ser un caso real donde el guardián K, tal como está acotado hoy, es más permisivo de lo que la investigación manual de DEDUPE-001 consideró seguro.

**No lo resuelvo acá — lo marco como el hallazgo #1 de la tabla de abajo, con recomendación de verificación puntual antes de persistir ese caso específico.**

**Sobre los 22 casos originales:** siguen sin identificadores individuales disponibles en ningún archivo del repo (`docs/DEDUPE001borradorcierretecnico.md` da el conteo agregado, 22, pero no la lista) — confirmado de nuevo, no inventado.

**Ninguna evidencia de que DEDUPE-004 haya introducido una definición de identidad *más fuerte* que la disponible**, salvo el caso puntual `136644` — el resto de las vías (B, D+E, y los otros 4 F+K+L) son consistentes con, o más conservadoras que, las conclusiones de DEDUPE-001 e IMPORT-003.

---

## 9. Seguridad de datos

- **`Evidence`:** texto libre, generado por el motor (nunca por input externo/usuario) — contiene fragmentos del propio `Concept` del movimiento (ej. `"Nro pendiente=136644"`), que ya está en texto plano en `BankStatements.Concept` — no introduce ningún dato más sensible del que ya existe en la tabla origen. `MaxLength(2048)`, `IsRequired()`.
- **`CreatedBy`:** string libre, `MaxLength(128)`, `IsRequired()` — hoy solo se le pasan constantes controladas por código (`"DedupeEngine"`/`"Backfill-DEDUPE-004-CONV"`/manual, según el doc-comment de la entidad) — no hay ningún path que lo alimente con input de usuario sin controlar.
- **Posibilidad de duplicación / idempotencia / reejecución:** ya auditado en la sesión anterior y reconfirmado sin cambios — `ApplyAsync` es idempotente (skip completo del grupo si cualquier miembro ya está linkeado), atómico por batch (`SaveChangesAsync` una sola vez).
- **¿El índice único impide que una misma entidad fuente pertenezca a dos grupos?** Sí, a nivel de base — `(SourceEntityType, SourceId)` es `UNIQUE`. A nivel de aplicación, el pre-chequeo evita llegar a violar el índice en el caso normal (single-threaded, sin llamadas concurrentes).
- **¿Una violación produciría una excepción sin control?** **Sí, en el caso narrow de una condición de carrera real** (dos `ApplyAsync` corriendo en paralelo sobre la misma cuenta, insertando entre el momento en que uno consulta `alreadyLinked` y el `SaveChangesAsync` del otro). No hay ningún `try/catch` alrededor de `SaveChangesAsync` en `ApplyAsync` — una violación real del índice único en ese escenario propagaría una `DbUpdateException` sin capturar hacia el llamador. No es corrupción de datos (la transacción de `SaveChangesAsync` revierte atómicamente), pero sí es una excepción no controlada — ver hallazgo #2 de la tabla.

---

## 10. Conclusión — hallazgos y veredicto

| Severidad | Archivo | Línea | Problema | Evidencia | Impacto | Recomendación | ¿Patch inmediato? |
|---|---|---|---|---|---|---|---|
| **MEDIA** | `docs/DEDUPE001borradorcierretecnico.md` §9 vs `DedupeEngine.cs` | doc §9 / código 184-208, 294-299 | Caso `136644`: DEDUPE-001 lo clasifica POSIBLE ("importe demasiado frecuente"); el motor actual lo da FUERTE ("frecuencia=1") | Contraste directo, ambos documentos reales, citado arriba | Si se aplica tal cual, riesgo real de fusionar dos movimientos económicamente distintos bajo una sola identidad — exactamente el riesgo que la especificación busca evitar | Verificar con SQL real cuántas filas `TRANSFERENCIA`/`TRANSFERENCIA INMEDIATA` (o el alcance que usó DEDUPE-001) tienen Importe=-3400 en toda la cuenta, y reconciliar el alcance de "frecuencia" entre ambas investigaciones antes de incluir este caso en cualquier `ApplyAsync` | **No** — es una verificación de datos, no un defecto de código a corregir a ciegas |
| **BAJA** | `DedupeEngine.cs` | 153-154 (`ApplyAsync`) | `SaveChangesAsync` sin `try/catch` — una violación real de índice único por condición de carrera propaga excepción sin controlar | Lectura directa, ausencia confirmada de manejo de excepción | Operativo, no corrompe datos (rollback atómico), pero el batch entero falla de forma no controlada | Documentar "no correr `ApplyAsync` concurrente sobre la misma cuenta"; opcionalmente, patch futuro para capturar la violación de unicidad y devolver resultado parcial | No |
| **INFO** | (estado de git) | — | El merge vive en `origin/master`, no en la rama designada de esta sesión | `git branch --all --contains 818e407` | Ninguno sobre datos; sí sobre coherencia de dónde continuar el siguiente patch | Decidir explícitamente la base del próximo patch (`origin/master` vs sincronizar la rama designada) | No |
| **INFO** | `docs/DEDUPE001borradorcierretecnico.md` | 1-3 | El encabezado dice "no está en el repo" pero ya está commiteado | Lectura directa | Ninguno funcional, posible confusión futura | Actualizar el encabezado si el documento pasa a ser definitivo | No |

**Ningún hallazgo de esta auditoría corrompe datos hoy** — porque no hay ningún flujo activo insertando `MovementIdentityLink` en producción todavía (§6). El riesgo real está en el **próximo paso** (aplicar los 81 FUERTE), no en el estado actual del repositorio.

### Veredicto

## **B. YELLOW**

La infraestructura (esquema, migración, consistencia del modelo EF, aislamiento de `DedupePreviewCli`, ausencia de `ALTER SYSTEM`/cambios al servidor) está correcta y coherente — verificado por código, sin inferencia, donde fue posible. No hay, hoy, ningún link persistido ni ningún flujo activo que pueda generar uno accidentalmente.

Pero hay una cuestión concreta pendiente antes del siguiente patch: **la contradicción real entre DEDUPE-001 y el motor actual sobre el caso `136644`** — no es un defecto de infraestructura, es una discrepancia de clasificación sobre un caso real específico que debe resolverse con datos (no con código) antes de dar por buenos, sin reservas, los 81 FUERTE como listos para `ApplyAsync`.

No hice ningún cambio de código. No propongo ningún patch todavía — la recomendación es una verificación de datos, no una corrección de motor.

---

## Confirmación de restricciones

Solo lectura. No se ejecutó `dotnet`, ni se conectó a Postgres real (intento real, falló por diseño del entorno). No se modificó código, ni migración, ni base. No se hizo checkout destructivo del working tree de `/home/user/FinancialSystem` — toda la inspección de `origin/master` se hizo con `git show`/`git ls-tree`/`git grep` sin tocar el árbol de trabajo real. No hubo commit ni push.
