# Backup y restauración — FinancialMcp

> Patch 0049 — Backup y Recuperación. Este documento y los scripts de `scripts/backup/`
> protegen la información del sistema ante una falla del equipo. No modifican el
> comportamiento funcional de la aplicación ni requieren cambios de código.

---

## 1. Qué datos deben respaldarse

- **La base de datos PostgreSQL completa** (`ConnectionStrings:Postgres` en los `appsettings.json` de `FinancialMcp.Api`, `FinancialSystem.Worker` y `FinancialSystem.McpServer`, todos apuntando a la misma base). Es la única fuente de verdad del sistema: `BankStatement`, `Transaction`, `ImportBatch`, `ClassifiedMovement`, `FinancialAccount`, `Category`, `Counterparty`, `PlanningMonth`/`PlanningItem`, `MovementAuditDecision`, `Investigation` y todo lo demás vive ahí. Respaldar la base es, en la práctica, respaldar el sistema entero.
- **(Recomendado, no obligatorio) los archivos originales de extractos bancarios**, es decir el contenido de la carpeta configurada en `FileIngestion:ImportsPath` (Worker) si todavía se conservan ahí. No son estrictamente necesarios porque su contenido ya está persistido en la base tras la importación, pero conservarlos facilita reprocesar desde cero ante una corrupción de datos, o auditar un archivo fuente puntual.
- **El archivo `scripts/backup/backup.env`** (la copia local con valores reales, no el `.example`) — sin él no se puede volver a ejecutar los scripts de backup/restore en un equipo nuevo. Guardarlo en un lugar seguro fuera del repositorio (no se versiona, ver sección 8).

## 2. Qué NO es necesario respaldar

- Código fuente y migraciones de Entity Framework — ya están versionados en git.
- `TempImports/` (archivos temporales de la subida manual vía API) — se limpian solos al terminar cada importación.
- Artefactos de build (`bin/`, `obj/`) — se regeneran con `dotnet build`.
- Logs de aplicación — no son necesarios para reconstruir el estado del sistema.
- Backups anteriores que ya excedieron la política de retención (sección 6).

## 3. Procedimiento completo para generar un backup

**Configuración (una sola vez por equipo):**

1. Copiar `scripts/backup/backup.env.example` a `scripts/backup/backup.env`.
2. Completar `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` y `BACKUP_DIR` con los valores reales del equipo (ver sección 8 para la contraseña).

**Generar un backup:**

- Linux/macOS:
  ```
  scripts/backup/backup-database.sh
  ```
- Windows (PowerShell):
  ```
  scripts/backup/backup-database.ps1
  ```

Ambos scripts leen la misma configuración de `backup.env`, generan un dump en formato *custom* de PostgreSQL (`pg_dump --format=custom`) con nombre `<PGDATABASE>_<fecha>_<hora>.dump` dentro de `BACKUP_DIR`, y aplican la retención configurada (sección 6) al finalizar. Ambos terminan con código de salida distinto de cero si algo falla — no dan por exitoso un backup que no se generó.

## 4. Procedimiento completo para restaurar

Pensado para el escenario de "falla del equipo": una instalación de PostgreSQL nueva o vacía, y un dump generado por el paso anterior.

1. **Detener la aplicación** (API, Worker y, si está corriendo, el servidor MCP) para evitar escrituras concurrentes durante la restauración.
2. Confirmar que hay una instancia de PostgreSQL accesible y que `scripts/backup/backup.env` apunta a ella (`PGHOST`/`PGPORT`/`PGUSER`).
3. Ejecutar el script de restauración indicando el archivo de dump, o dejar que tome el más reciente de `BACKUP_DIR`:
   - Linux/macOS:
     ```
     scripts/backup/restore-database.sh [archivo.dump]
     ```
   - Windows (PowerShell):
     ```
     scripts/backup/restore-database.ps1 [-DumpFile <archivo.dump>]
     ```
4. El script pide confirmación explícita antes de continuar (escribir `restaurar`), porque **recrea por completo** la base indicada en `PGDATABASE`: la borra si existe y la vuelve a crear vacía antes de aplicar el dump. Para automatizar sin confirmación interactiva: `FORCE=1` (bash) o `-Force` (PowerShell) — usar con cuidado, solo en procedimientos ya validados.
5. Al terminar, seguir la sección 5 antes de dar la restauración por buena.
6. Volver a iniciar la aplicación.

## 5. Cómo validar que la restauración fue correcta

No dar la restauración por terminada solo porque el script no mostró errores. Verificar, en este orden:

1. **Migraciones aplicadas**: la tabla `__EFMigrationsHistory` debe tener tantas filas como archivos de migración existen en `src/FinancialSystem.Infrastructure/Migrations/`. Si falta alguna, la restauración corresponde a un dump más viejo que el esquema actual del código.
2. **Volumen de datos razonable**: comparar la cantidad de filas de las tablas centrales (`Transactions`, `BankStatements`, `ClassifiedMovements`, `ImportBatches`) contra lo esperado (por ejemplo, contra un backup de referencia anterior, o contra el conteo que se tenía antes de la falla). Una tabla vacía donde no debería estarlo es la señal de alarma más común.
3. **La aplicación arranca**: levantar `FinancialMcp.Api` contra la base restaurada y confirmar que responde sin errores de conexión ni de esquema.
4. **Datos visibles end-to-end**: abrir el Dashboard y confirmar que el resumen del último mes conocido muestra números coherentes con lo esperado, y que `movements.html`/`audit.html`/`planning.html` cargan sin error.

Solo después de estos cuatro pasos la restauración se considera validada.

## 6. Frecuencia recomendada de backup

- **Diario**, automatizado (ver sección 7), dado que el Worker importa movimientos de forma continua mientras la carpeta vigilada tenga archivos nuevos — un solo día de pérdida ya puede significar movimientos reales sin forma sencilla de recuperar.
- **Antes de cualquier operación de riesgo manual** (por ejemplo, antes de aplicar una nueva migración de base de datos, o antes de una restauración de prueba) — backup manual puntual además del automático diario.

## 7. Estrategia recomendada de retención

- **Backups diarios**: conservar los últimos `RETENTION_DAYS` días (por defecto 14, configurable en `backup.env`). El propio script de backup elimina automáticamente los `.dump` más viejos que ese umbral en cada corrida — no requiere una tarea de limpieza separada.
- **Copia mensual fuera de la máquina**: el día 1 de cada mes, copiar manualmente el backup más reciente a un almacenamiento distinto del equipo (disco externo, almacenamiento en la nube personal, etc.) y conservarlo por 12 meses. Esto está fuera del alcance de este patch (no se agrega integración con almacenamiento externo) — es una tarea manual recomendada, documentada acá para que no se pierda.
- Este esquema (14 días de detalle + instantáneas mensuales de largo plazo) cubre tanto "recuperarse de un error detectado esta semana" como "recuperarse de una falla total del equipo detectada mucho después".

## 8. Configuración y credenciales

Toda la configuración de estos scripts está centralizada en **un único archivo**: `scripts/backup/backup.env` (a partir de `scripts/backup/backup.env.example`). Ningún script tiene rutas ni credenciales hardcodeadas — todo se lee de ahí.

- **Contraseña de PostgreSQL**: se recomienda **no** guardarla en `backup.env`. Usar en su lugar el mecanismo estándar de libpq:
  - Linux/macOS: archivo `~/.pgpass` (`host:port:database:usuario:contraseña`, permisos `600`).
  - Windows: `%APPDATA%\postgresql\pgpass.conf` (mismo formato).
  Si no es posible usar `.pgpass`, se puede definir `PGPASSWORD` directamente en `backup.env` como último recurso — el archivo ya está excluido de git, pero queda en texto plano en el disco local.
- **Herramientas de PostgreSQL** (`pg_dump`, `pg_restore`, `dropdb`, `createdb`): deben ser de una versión igual o superior a la del servidor en uso. Si no están en el `PATH` del sistema (común en Windows), indicar la carpeta en `PG_BIN_DIR` dentro de `backup.env`.

## 9. Diferencias Windows / Linux

Los scripts existen en ambas variantes con el mismo comportamiento:

| Acción | Linux/macOS | Windows |
|---|---|---|
| Backup | `scripts/backup/backup-database.sh` | `scripts/backup/backup-database.ps1` |
| Restore | `scripts/backup/restore-database.sh` | `scripts/backup/restore-database.ps1` |
| Configuración | `scripts/backup/backup.env` (mismo archivo para ambos SO) | |
| Permisos | dar permiso de ejecución: `chmod +x scripts/backup/*.sh` | ejecutar con `powershell -ExecutionPolicy Bypass -File ...` si la política de ejecución del sistema lo bloquea |

## 10. Automatización (tarea programada)

Este patch **no instala** ninguna tarea programada — automatizarla depende del sistema operativo y del entorno de cada usuario. Comandos sugeridos para la frecuencia diaria recomendada (sección 6):

- **Linux/macOS (cron)**, todos los días a las 3 AM:
  ```
  0 3 * * * /ruta/al/repo/scripts/backup/backup-database.sh >> /ruta/al/log/backup.log 2>&1
  ```
- **Windows (Programador de tareas)**: crear una tarea diaria a las 3 AM que ejecute
  ```
  powershell -ExecutionPolicy Bypass -File "C:\ruta\al\repo\scripts\backup\backup-database.ps1"
  ```

Ver la sección "Acciones manuales" de este patch para el detalle paso a paso de cómo configurarla.
