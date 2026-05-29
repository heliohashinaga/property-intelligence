#!/usr/bin/env sh
# INEP IDEB — School Records Import
# Imports school records with IDEB scores into the school_records table.
#
# Usage:
#   IDEB_CSV=/path/to/ideb.csv sh data/import/inep_ideb_import.sh
#   sh data/import/inep_ideb_import.sh /path/to/ideb.csv
#
# Pre-processing:
#   INEP distributes IDEB data as .xlsx.  Convert to CSV first:
#     ssconvert ideb_escolas_2023.xlsx ideb.csv       # via gnumeric
#     libreoffice --headless --convert-to csv ideb_escolas_2023.xlsx
#
# Expected CSV columns (semicolon-separated):
#   co_entidade  — INEP school code (8 digits)
#   no_entidade  — school name
#   no_municipio — municipality
#   co_uf        — 2-char state code (e.g. SP)
#   vl_observado_sf — IDEB score (0.0–10.0, may be empty)
#   nu_ano_censo — census year (e.g. 2021)
#   lat          — latitude  (optional; add manually or via Nominatim batch)
#   lng          — longitude (optional)
#
# Geocoding:
#   lat/lng columns are optional in the CSV.  If absent (or NULL) the script
#   still imports the record; run the Nominatim batch update afterwards:
#
#     psql "$DATABASE_URL" -c "
#       UPDATE school_records
#       SET    location = ST_SetSRID(ST_MakePoint(lng, lat), 4326)
#       WHERE  lat IS NOT NULL AND lng IS NOT NULL AND location IS NULL;"
#
# Requirements:
#   - psql (postgresql-client)
#   - DATABASE_URL env var
#
# The script is idempotent: uses ON CONFLICT (inep_code) DO UPDATE.

set -e

IDEB_CSV="${1:-${IDEB_CSV:-}}"

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is required." >&2
    exit 1
fi

if [ -z "$IDEB_CSV" ]; then
    echo "ERROR: CSV path required." >&2
    echo "Usage: IDEB_CSV=/path/to/ideb.csv $0" >&2
    echo "  or:  $0 /path/to/ideb.csv" >&2
    exit 1
fi

if [ ! -f "$IDEB_CSV" ]; then
    echo "ERROR: file not found: $IDEB_CSV" >&2
    exit 1
fi

echo "Importing INEP school records from: $IDEB_CSV"

psql "$DATABASE_URL" <<SQL
CREATE TEMP TABLE ideb_import_tmp (
    co_entidade      TEXT,
    no_entidade      TEXT,
    no_municipio     TEXT,
    co_uf            VARCHAR(2),
    vl_observado_sf  NUMERIC(4,1),
    nu_ano_censo     SMALLINT,
    lat              NUMERIC(9,6),
    lng              NUMERIC(9,6)
) ON COMMIT DROP;

COPY ideb_import_tmp
FROM '${IDEB_CSV}'
DELIMITER ';'
CSV HEADER;

INSERT INTO school_records (
    inep_code, name, municipality, state,
    ideb_score, ideb_year,
    lat, lng, location,
    imported_at
)
SELECT
    co_entidade,
    COALESCE(NULLIF(no_entidade, ''), 'Escola sem nome'),
    no_municipio,
    co_uf,
    NULLIF(vl_observado_sf, 0),
    nu_ano_censo,
    lat,
    lng,
    CASE
        WHEN lat IS NOT NULL AND lng IS NOT NULL
        THEN ST_SetSRID(ST_MakePoint(lng, lat), 4326)
        ELSE NULL
    END,
    NOW()
FROM ideb_import_tmp
WHERE co_entidade IS NOT NULL
ON CONFLICT (inep_code) DO UPDATE SET
    ideb_score   = EXCLUDED.ideb_score,
    ideb_year    = EXCLUDED.ideb_year,
    lat          = COALESCE(EXCLUDED.lat, school_records.lat),
    lng          = COALESCE(EXCLUDED.lng, school_records.lng),
    location     = COALESCE(EXCLUDED.location, school_records.location);
SQL

COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM school_records")
GEO_COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM school_records WHERE location IS NOT NULL")
echo "Done. school_records: ${COUNT} rows total (${GEO_COUNT} geocoded)."

if [ "$GEO_COUNT" -lt "$COUNT" ]; then
    MISSING=$((COUNT - GEO_COUNT))
    echo "NOTE: ${MISSING} records lack coordinates."
    echo "      After adding lat/lng to the CSV, re-run this script."
    echo "      Or update in-place:"
    echo "        psql \"\$DATABASE_URL\" -c \\"
    echo "          \"UPDATE school_records SET location = ST_SetSRID(ST_MakePoint(lng,lat),4326) WHERE lat IS NOT NULL AND location IS NULL;\""
fi
