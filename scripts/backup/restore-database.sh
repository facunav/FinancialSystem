#!/usr/bin/env bash
# Restauración de la base de datos PostgreSQL de FinancialMcp (Patch 0049).
#
# Uso:
#   ./restore-database.sh [archivo.dump] [ruta-a-backup.env]
#
# Si no se indica un archivo de dump, se usa el más reciente encontrado en
# BACKUP_DIR (ver backup.env). La base de datos indicada en PGDATABASE se
# RECREA por completo (se borra y se vuelve a crear antes de restaurar).
#
# Variables de entorno:
#   FORCE=1   omite la confirmación interactiva (uso en scripts/automatización).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DUMP_FILE="${1:-}"
CONFIG_FILE="${2:-$SCRIPT_DIR/backup.env}"

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

if [[ -n "${PG_BIN_DIR:-}" ]]; then
  export PATH="$PG_BIN_DIR:$PATH"
fi

for tool in pg_restore dropdb createdb; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "ERROR: '$tool' no está disponible en PATH." >&2
    echo "Instalá los clientes de PostgreSQL o definí PG_BIN_DIR en $CONFIG_FILE." >&2
    exit 1
  fi
done

if [[ -z "$DUMP_FILE" ]]; then
  DUMP_FILE="$(find "$BACKUP_DIR" -maxdepth 1 -name "${PGDATABASE}_*.dump" -type f -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -n1 | cut -d' ' -f2-)"
  if [[ -z "$DUMP_FILE" ]]; then
    echo "ERROR: no se indicó un archivo de dump y no se encontró ninguno en '$BACKUP_DIR'." >&2
    exit 1
  fi
  echo "No se indicó archivo de dump: usando el más reciente encontrado: $DUMP_FILE"
fi

if [[ ! -f "$DUMP_FILE" ]]; then
  echo "ERROR: el archivo de dump '$DUMP_FILE' no existe." >&2
  exit 1
fi

echo "Se va a RECREAR completamente la base de datos '$PGDATABASE' en '${PGHOST:-localhost}:${PGPORT:-5432}'"
echo "a partir de '$DUMP_FILE'. Esta operación borra todos los datos actuales de esa base."

if [[ "${FORCE:-}" != "1" ]]; then
  read -r -p "Escribí 'restaurar' para continuar: " CONFIRM
  if [[ "$CONFIRM" != "restaurar" ]]; then
    echo "Cancelado por el usuario."
    exit 1
  fi
fi

echo "[$(date -Iseconds)] Recreando base de datos '$PGDATABASE'..."
dropdb --if-exists "$PGDATABASE"
createdb "$PGDATABASE"

echo "[$(date -Iseconds)] Restaurando '$DUMP_FILE' -> '$PGDATABASE'..."
pg_restore --no-owner --no-privileges --dbname="$PGDATABASE" "$DUMP_FILE"

echo "[$(date -Iseconds)] Restauración finalizada."
echo "Verificá el resultado con los pasos de 'Cómo validar la restauración' en docs/BACKUP_AND_RESTORE.md antes de darla por buena."
