<#
.SYNOPSIS
    Restauración de la base de datos PostgreSQL de FinancialMcp (Patch 0049).
.DESCRIPTION
    Recrea por completo la base de datos indicada en PGDATABASE (backup.env) y
    restaura un dump generado por backup-database.ps1 / backup-database.sh.
    Si no se indica -DumpFile, usa el más reciente encontrado en BACKUP_DIR.
.PARAMETER DumpFile
    Archivo .dump a restaurar. Opcional: si se omite, se usa el más reciente de BACKUP_DIR.
.PARAMETER ConfigFile
    Ruta al archivo de configuración. Por defecto, "backup.env" en esta carpeta.
.PARAMETER Force
    Omite la confirmación interactiva (uso en scripts/automatización).
#>
param(
    [string]$DumpFile,
    [string]$ConfigFile = (Join-Path $PSScriptRoot "backup.env"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ConfigFile)) {
    Write-Error "No se encontró el archivo de configuración '$ConfigFile'. Copiá 'backup.env.example' a 'backup.env' y completá los valores."
    exit 1
}

$config = @{}
Get-Content $ConfigFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith("#") -and $line.Contains("=")) {
        $parts = $line.Split("=", 2)
        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        $config[$key] = $value
        [Environment]::SetEnvironmentVariable($key, $value, "Process")
    }
}

function Get-RequiredConfig([string]$key) {
    if (-not $config.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($config[$key])) {
        Write-Error "'$key' no está definido en $ConfigFile"
        exit 1
    }
    return $config[$key]
}

$PgDatabase = Get-RequiredConfig "PGDATABASE"
$BackupDir = Get-RequiredConfig "BACKUP_DIR"
$PgHostValue = if ($config.ContainsKey("PGHOST")) { $config["PGHOST"] } else { "localhost" }
$PgPortValue = if ($config.ContainsKey("PGPORT")) { $config["PGPORT"] } else { "5432" }

if ($config.ContainsKey("PG_BIN_DIR") -and $config["PG_BIN_DIR"]) {
    $env:Path = "$($config['PG_BIN_DIR']);$env:Path"
}

foreach ($tool in @("pg_restore", "dropdb", "createdb")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Error "'$tool' no está disponible en PATH. Instalá los clientes de PostgreSQL o definí PG_BIN_DIR en $ConfigFile."
        exit 1
    }
}

if (-not $DumpFile) {
    $latest = Get-ChildItem -Path $BackupDir -Filter "${PgDatabase}_*.dump" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) {
        Write-Error "No se indicó -DumpFile y no se encontró ningún backup en '$BackupDir'."
        exit 1
    }
    $DumpFile = $latest.FullName
    Write-Host "No se indicó -DumpFile: usando el más reciente encontrado: $DumpFile"
}

if (-not (Test-Path $DumpFile)) {
    Write-Error "El archivo de dump '$DumpFile' no existe."
    exit 1
}

Write-Host "Se va a RECREAR completamente la base de datos '$PgDatabase' en '${PgHostValue}:${PgPortValue}'"
Write-Host "a partir de '$DumpFile'. Esta operación borra todos los datos actuales de esa base."

if (-not $Force) {
    $confirm = Read-Host "Escribí 'restaurar' para continuar"
    if ($confirm -ne "restaurar") {
        Write-Host "Cancelado por el usuario."
        exit 1
    }
}

Write-Host "[$(Get-Date -Format o)] Recreando base de datos '$PgDatabase'..."
& dropdb --if-exists $PgDatabase
& createdb $PgDatabase

Write-Host "[$(Get-Date -Format o)] Restaurando '$DumpFile' -> '$PgDatabase'..."
& pg_restore --no-owner --no-privileges --dbname=$PgDatabase "$DumpFile"
if ($LASTEXITCODE -ne 0) {
    Write-Error "pg_restore terminó con código $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "[$(Get-Date -Format o)] Restauración finalizada."
Write-Host "Verificá el resultado con los pasos de 'Cómo validar la restauración' en docs/BACKUP_AND_RESTORE.md antes de darla por buena."
