#!/usr/bin/env sh
# SSP-SP Crime Data — Monthly Import
# Downloads (or uses a local copy of) the SSP-SP crime statistics CSV,
# parses municipality/year/month/crime_type/count columns, and imports
# into the crime_records table.
#
# Usage:
#   SSP_CSV=/path/to/ssp_dados.csv sh data/import/ssp_sp_import.sh
#   sh data/import/ssp_sp_import.sh /path/to/ssp_dados.csv
#
# If SSP_CSV is not provided, the script attempts to download the latest
# dataset from SSP-SP / Dados Abertos SP portal.
#
# SSP-SP CSV encoding: LATIN1 (ISO-8859-1); the script handles this automatically.
#
# Expected CSV columns (semicolon or comma-separated):
#   MUNICIPIO (or similar)   — municipality name
#   ANO (or similar)         — year (YYYY)
#   MES (or similar)         — month (1-12 or MM)
#   TIPO_CRIME (or similar)  — crime type description
#   OCORRENCIAS (or similar) — count of occurrences
#
# Requirements:
#   - psql (postgresql-client)
#   - wget + unzip (only if auto-download is used)
#   - DATABASE_URL env var
#
# The script is idempotent: uses ON CONFLICT (municipality, year, month, crime_type) DO NOTHING.
# Recommended: run monthly via cron to keep data current.

set -e

SSP_CSV="${1:-${SSP_CSV:-}}"

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is required." >&2
    exit 1
fi

# ── Auto-download if no local file provided ───────────────────────────────────
if [ -z "$SSP_CSV" ]; then
    echo "SSP_CSV not set — attempting download from SSP-SP / Dados Abertos SP..."
    if ! command -v wget >/dev/null 2>&1; then
        echo "ERROR: wget not found. Install wget or set SSP_CSV=/path/to/file.csv" >&2
        exit 1
    fi
    
    TMP_DIR=$(mktemp -d)
    YEAR=$(date +%Y)
    
    # SSP-SP publishes data at:
    # https://www.ssp.sp.gov.br/estatistica/dados-mensais
    # or through São Paulo Dados Abertos:
    # https://dados.sp.gov.br/dataset/ocorrencias-criminais
    # 
    # The exact URL structure may vary. Here we try the most common pattern:
    SSP_URL="https://www.ssp.sp.gov.br/Estatistica/Dados/DadosCriminais_${YEAR}.csv"
    
    echo "Downloading ${SSP_URL}..."
    wget -q -O "${TMP_DIR}/ssp_crimes.csv" "$SSP_URL" || {
        # Fallback: try Dados Abertos SP portal (common alternative)
        echo "Primary URL failed. Trying Dados Abertos SP..."
        SSP_URL="https://dados.sp.gov.br/storage/f/2024-01-01T00%3A00%3A00.000Z/ocorrencias-criminais-${YEAR}.csv"
        wget -q -O "${TMP_DIR}/ssp_crimes.csv" "$SSP_URL" || {
            echo "ERROR: download failed. Set SSP_CSV=/path/to/local/file.csv and rerun." >&2
            echo "Manual download: https://www.ssp.sp.gov.br/estatistica/dados-mensais" >&2
            rm -rf "$TMP_DIR"
            exit 1
        }
    }
    
    SSP_CSV="${TMP_DIR}/ssp_crimes.csv"
    echo "Using downloaded file: $SSP_CSV"
fi

if [ ! -f "$SSP_CSV" ]; then
    echo "ERROR: file not found: $SSP_CSV" >&2
    exit 1
fi

echo "Importing SSP-SP crime records from: $SSP_CSV"

# Determine delimiter (try semicolon first, then comma)
FIRST_LINE=$(head -1 "$SSP_CSV")
if echo "$FIRST_LINE" | grep -q ";"; then
    DELIMITER=";"
else
    DELIMITER=","
fi

echo "Detected delimiter: '$DELIMITER'"

# Import using PostgreSQL COPY into temp table, then INSERT with normalization
psql "$DATABASE_URL" <<SQL
CREATE TEMP TABLE ssp_import_tmp (
    municipio    TEXT,
    ano          TEXT,
    mes          TEXT,
    tipo_crime   TEXT,
    ocorrencias  TEXT
) ON COMMIT DROP;

-- Import raw data (all columns as TEXT for flexibility)
\COPY ssp_import_tmp FROM '${SSP_CSV}' WITH (FORMAT csv, HEADER true, DELIMITER '${DELIMITER}', ENCODING 'LATIN1');

-- Normalize and insert into crime_records
INSERT INTO crime_records (
    municipality, state, year, month, crime_type, count, imported_at
)
SELECT
    TRIM(UPPER(municipio)),
    'SP',
    CAST(NULLIF(TRIM(ano), '') AS SMALLINT),
    CAST(NULLIF(TRIM(mes), '') AS SMALLINT),
    TRIM(tipo_crime),
    CAST(NULLIF(TRIM(ocorrencias), '') AS INTEGER),
    NOW()
FROM ssp_import_tmp
WHERE municipio IS NOT NULL
  AND NULLIF(TRIM(ano), '') IS NOT NULL
  AND NULLIF(TRIM(mes), '') IS NOT NULL
  AND NULLIF(TRIM(ocorrencias), '') IS NOT NULL
  AND TRIM(ano) ~ '^[0-9]+\$'
  AND TRIM(mes) ~ '^[0-9]+\$'
  AND TRIM(ocorrencias) ~ '^[0-9]+\$'
ON CONFLICT (municipality, year, month, crime_type) DO NOTHING;

-- Report imported row count
SELECT COUNT(*) AS imported_rows FROM crime_records;
SQL

TOTAL_COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM crime_records")
LATEST_MONTH=$(psql "$DATABASE_URL" -tAc "SELECT CONCAT(year, '-', LPAD(month::TEXT, 2, '0')) FROM crime_records ORDER BY year DESC, month DESC LIMIT 1")

echo ""
echo "Done. crime_records: ${TOTAL_COUNT} rows total"
echo "Latest data month: ${LATEST_MONTH}"
echo ""
echo "To keep data current, run this script monthly:"
echo "  sh data/import/ssp_sp_import.sh"
echo ""
echo "Or schedule via cron (example for first day of each month at 2 AM):"
echo "  0 2 1 * * cd /path/to/property-intelligence && sh data/import/ssp_sp_import.sh"
SQL
