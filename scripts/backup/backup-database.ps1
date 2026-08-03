<#
.SYNOPSIS
    Backup de la base de datos PostgreSQL de FinancialMcp (Patch 0049).
.DESCRIPTION
    Lee la configuración de backup.env (misma carpeta por defecto), genera un dump
    con pg_dump en formato custom y aplica la política de retención configurada.
    Ver backup.env.example para las variables requeridas y docs/BACKUP_AND_RESTORE.md
    para el procedimiento completo.
.PARAMETER ConfigFile
    Ruta al archivo de configuración. Por defecto, "backup.env" en esta carpeta.
#>
param(
    [string]$ConfigFile = (Join-Path $PSScriptRoot "backup.env")
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
$RetentionDays = 14
if ($config.ContainsKey("RETENTION_DAYS") -and $config["RETENTION_DAYS"]) {
    $RetentionDays = [int]$config["RETENTION_DAYS"]
}

if ($config.ContainsKey("PG_BIN_DIR") -and $config["PG_BIN_DIR"]) {
    $env:Path = "$($config['PG_BIN_DIR']);$env:Path"
}

if (-not (Get-Command pg_dump -ErrorAction SilentlyContinue)) {
    Write-Error "pg_dump no está disponible en PATH. Instalá los clientes de PostgreSQL o definí PG_BIN_DIR en $ConfigFile."
    exit 1
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$DumpFile = Join-Path $BackupDir "${PgDatabase}_${Timestamp}.dump"

Write-Host "[$(Get-Date -Format o)] Iniciando backup de '$PgDatabase' -> '$DumpFile'"

& pg_dump --format=custom --no-owner --no-privileges --file="$DumpFile" $PgDatabase
if ($LASTEXITCODE -ne 0) {
    Write-Error "pg_dump terminó con código $LASTEXITCODE"
    exit $LASTEXITCODE
}

$sizeKb = [math]::Round((Get-Item $DumpFile).Length / 1KB, 1)
Write-Host "[$(Get-Date -Format o)] Backup completado: $DumpFile ($sizeKb KB)"

if ($RetentionDays -gt 0) {
    Write-Host "[$(Get-Date -Format o)] Aplicando retención: eliminando backups de más de $RetentionDays días en '$BackupDir'"
    $cutoff = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem -Path $BackupDir -Filter "${PgDatabase}_*.dump" -File |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        ForEach-Object {
            Write-Host "  eliminando $($_.FullName)"
            Remove-Item $_.FullName -Force
        }
}

Write-Host "[$(Get-Date -Format o)] Proceso de backup finalizado sin errores."
