#!/usr/bin/env bash
# Backup de la base de datos PostgreSQL de FinancialMcp (Patch 0049).
#
# Uso:
#   ./backup-database.sh [ruta-a-backup.env]
#
# Si no se indica una ruta de configuración, usa "backup.env" en esta misma carpeta.
# Ver backup.env.example para las variables requeridas y docs/BACKUP_AND_RESTORE.md
# para el procedimiento completo.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="${1:-$SCRIPT_DIR/backup.env}"

if [[ ! -f "$CONFIG_FILE" ]]; then
  echo "ERROR: no se encontró el archivo de configuración '$CONFIG_FILE'." >&2
  echo "Copiá '$SCRIPT_DIR/backup.env.example' a 'backup.env' y completá los valores." >&2
  exit 1
fi

# shellcheck disable=SC1090
set -a
source "$CONFIG_FILE"
set +a

: "${PGDATABASE:?PGDATABASE no está definido en $CONFIG_FILE}"
: "${BACKUP_DIR:?BACKUP_DIR no está definido en $CONFIG_FILE}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

if [[ -n "${PG_BIN_DIR:-}" ]]; then
  export PATH="$PG_BIN_DIR:$PATH"
fi

if ! command -v pg_dump >/dev/null 2>&1; then
  echo "ERROR: pg_dump no está disponible en PATH." >&2
  echo "Instalá los clientes de PostgreSQL o definí PG_BIN_DIR en $CONFIG_FILE." >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
DUMP_FILE="$BACKUP_DIR/${PGDATABASE}_${TIMESTAMP}.dump"

echo "[$(date -Iseconds)] Iniciando backup de '$PGDATABASE' -> '$DUMP_FILE'"

pg_dump --format=custom --no-owner --no-privileges --file="$DUMP_FILE" "$PGDATABASE"

DUMP_SIZE="$(du -h "$DUMP_FILE" | cut -f1)"
echo "[$(date -Iseconds)] Backup completado: $DUMP_FILE ($DUMP_SIZE)"

if [[ "$RETENTION_DAYS" -gt 0 ]]; then
  echo "[$(date -Iseconds)] Aplicando retención: eliminando backups de más de $RETENTION_DAYS días en '$BACKUP_DIR'"
  find "$BACKUP_DIR" -maxdepth 1 -name "${PGDATABASE}_*.dump" -type f -mtime "+$RETENTION_DAYS" -print -delete
fi

echo "[$(date -Iseconds)] Proceso de backup finalizado sin errores."
