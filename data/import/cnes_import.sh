#!/usr/bin/env sh
# CNES / DataSUS — Health Facilities Import
# Downloads (or uses a local copy of) the CNES establishment dataset,
# filters to São Paulo state, and imports into the health_facilities table.
#
# Usage:
#   CNES_CSV=/path/to/tbEstabelecimento.csv sh data/import/cnes_import.sh
#   sh data/import/cnes_import.sh /path/to/tbEstabelecimento.csv
#
# If CNES_CSV is not provided, the script attempts to download the current
# month's file from the DataSUS FTP server (requires wget + unzip).
#
# CNES CSV encoding: LATIN1 (ISO-8859-1); the script handles this automatically.
#
# Expected CSV columns (semicolon-separated):
#   co_cnes          — CNES establishment code
#   no_fantasia      — trade name (may be empty)
#   tp_unidade       — facility type code (e.g. 05 = HOSPITAL, 01 = POSTO DE SAÚDE)
#   no_municipio     — municipality name
#   co_estado_gestor — IBGE state code (São Paulo = 35)
#   nu_latitude      — latitude
#   nu_longitude     — longitude
#
# Requirements:
#   - psql (postgresql-client)
#   - wget + unzip  (only if auto-download is used)
#   - DATABASE_URL env var
#
# The script is idempotent: uses ON CONFLICT (cnes_code) DO UPDATE.

set -e

CNES_CSV="${1:-${CNES_CSV:-}}"

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is required." >&2
    exit 1
fi

# ── Auto-download if no local file provided ───────────────────────────────────
if [ -z "$CNES_CSV" ]; then
    echo "CNES_CSV not set — attempting download from DataSUS FTP..."
    if ! command -v wget >/dev/null 2>&1; then
        echo "ERROR: wget not found. Install wget or set CNES_CSV=/path/to/file.csv" >&2
        exit 1
    fi
    TMP_DIR=$(mktemp -d)
    YEAR_MONTH=$(date +%Y%m)
    FTP_URL="ftp://ftp.datasus.gov.br/cnes/BASE_DE_DADOS_CNES_${YEAR_MONTH}.ZIP"
    echo "Downloading ${FTP_URL}..."
    wget -q -O "${TMP_DIR}/cnes.zip" "$FTP_URL" || {
        echo "ERROR: download failed. Set CNES_CSV=/path/to/local/tbEstabelecimento.csv and rerun." >&2
        rm -rf "$TMP_DIR"
        exit 1
    }
    unzip -q "${TMP_DIR}/cnes.zip" -d "$TMP_DIR"
    CNES_CSV=$(find "$TMP_DIR" -name 'tbEstabelecimento*.csv' | head -1)
    if [ -z "$CNES_CSV" ]; then
        echo "ERROR: could not find tbEstabelecimento*.csv in downloaded archive." >&2
        rm -rf "$TMP_DIR"
        exit 1
    fi
    echo "Using extracted file: $CNES_CSV"
fi

if [ ! -f "$CNES_CSV" ]; then
    echo "ERROR: file not found: $CNES_CSV" >&2
    exit 1
fi

echo "Importing CNES health facilities from: $CNES_CSV (São Paulo state only)"

psql "$DATABASE_URL" <<SQL
CREATE TEMP TABLE cnes_import_tmp (
    co_cnes          TEXT,
    no_fantasia      TEXT,
    tp_unidade       TEXT,
    no_municipio     TEXT,
    co_estado_gestor VARCHAR(2),
    nu_latitude      NUMERIC(9,6),
    nu_longitude     NUMERIC(9,6)
) ON COMMIT DROP;

COPY cnes_import_tmp
FROM '${CNES_CSV}'
DELIMITER ';'
CSV HEADER
ENCODING 'LATIN1';

INSERT INTO health_facilities (
    cnes_code, name, facility_type,
    municipality, state,
    lat, lng, location,
    imported_at
)
SELECT
    co_cnes,
    COALESCE(NULLIF(TRIM(no_fantasia), ''), 'Unidade de Saúde'),
    tp_unidade,
    no_municipio,
    'SP',
    nu_latitude,
    nu_longitude,
    CASE
        WHEN nu_latitude  IS NOT NULL
         AND nu_longitude IS NOT NULL
        THEN ST_SetSRID(ST_MakePoint(nu_longitude, nu_latitude), 4326)
        ELSE NULL
    END,
    NOW()
FROM cnes_import_tmp
WHERE co_estado_gestor = '35'       -- IBGE code for São Paulo state
  AND co_cnes IS NOT NULL
ON CONFLICT (cnes_code) DO UPDATE SET
    name          = EXCLUDED.name,
    facility_type = EXCLUDED.facility_type,
    municipality  = EXCLUDED.municipality,
    lat           = EXCLUDED.lat,
    lng           = EXCLUDED.lng,
    location      = EXCLUDED.location;
SQL

COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM health_facilities")
GEO_COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM health_facilities WHERE location IS NOT NULL")
echo "Done. health_facilities: ${COUNT} rows total (${GEO_COUNT} with coordinates)."
