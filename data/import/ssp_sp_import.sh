#!/usr/bin/env sh
# SSP-SP Crime Data Importer
# Imports the São Paulo State Security Secretariat crime CSV into the
# crime_records PostgreSQL table. Run monthly via cron after SSP-SP publishes.
#
# Usage:
#   sh data/import/ssp_sp_import.sh [path-to-csv]
#
# If no path is given, attempts to download the latest CSV from SSP-SP portal.
# Requires: psql, curl (for auto-download mode)
#
# Environment:
#   DATABASE_URL  — postgres connection string (default: from .env)
#
# CSV format (SSP-SP monthly export):
#   ANO_BO,MES_BO,MUNICIPIO_CIRC,NATUREZA_APURADA,TOTAL_OCORRENCIAS

set -e

# ── Load DATABASE_URL from .env if not already set ───────────────────────────
if [ -z "$DATABASE_URL" ] && [ -f "$(dirname "$0")/../../.env" ]; then
  # shellcheck disable=SC1091
  . "$(dirname "$0")/../../.env"
fi

DATABASE_URL="${DATABASE_URL:-postgres://property_intelligence:property_intelligence@localhost:5432/property_intelligence}"

# ── Determine CSV source ─────────────────────────────────────────────────────
CSV_PATH="${1:-}"

if [ -z "$CSV_PATH" ]; then
  echo "[ssp_sp_import] No CSV path given — attempting auto-download from SSP-SP..."
  CSV_PATH="/tmp/ssp_sp_$(date +%Y%m).csv"
  # SSP-SP publishes updated CSVs at this URL pattern (adjust year/month as needed)
  YEAR=$(date +%Y)
  MONTH=$(date +%m)
  URL="https://www.ssp.sp.gov.br/estatistica/files/Delegacias_${YEAR}_${MONTH}.csv"
  if curl -fsSL "$URL" -o "$CSV_PATH"; then
    echo "[ssp_sp_import] Downloaded: $URL"
  else
    echo "[ssp_sp_import] ERROR: Could not download from $URL"
    echo "  Download manually from https://www.ssp.sp.gov.br/estatistica"
    echo "  and pass the path as: sh data/import/ssp_sp_import.sh <path-to-csv>"
    exit 1
  fi
fi

if [ ! -f "$CSV_PATH" ]; then
  echo "[ssp_sp_import] ERROR: CSV file not found: $CSV_PATH"
  exit 1
fi

echo "[ssp_sp_import] Importing: $CSV_PATH"

# ── Create temp table, load CSV, upsert into crime_records ──────────────────
psql "$DATABASE_URL" <<SQL
BEGIN;

-- Staging table for raw CSV load
CREATE TEMP TABLE ssp_staging (
    ano_bo              INTEGER,
    mes_bo              INTEGER,
    municipio_circ      TEXT,
    natureza_apurada    TEXT,
    total_ocorrencias   INTEGER
) ON COMMIT DROP;

-- Load CSV (skip header row)
\COPY ssp_staging FROM '$CSV_PATH' CSV HEADER DELIMITER ',' ENCODING 'UTF8';

-- Upsert into crime_records (idempotent — ON CONFLICT DO NOTHING)
INSERT INTO crime_records (year, month, municipality, crime_type, count, source, imported_at)
SELECT
    ano_bo,
    mes_bo,
    LOWER(TRIM(municipio_circ)),
    LOWER(TRIM(natureza_apurada)),
    total_ocorrencias,
    'ssp_sp',
    NOW()
FROM ssp_staging
WHERE total_ocorrencias IS NOT NULL
  AND total_ocorrencias >= 0
ON CONFLICT (year, month, municipality, crime_type, source) DO NOTHING;

COMMIT;

SELECT
    'Rows in crime_records for source ssp_sp: ' ||
    COUNT(*)::text AS result
FROM crime_records
WHERE source = 'ssp_sp';
SQL

echo "[ssp_sp_import] Done."
