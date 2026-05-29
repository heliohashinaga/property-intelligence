#!/usr/bin/env sh
# IBGE Censo 2022 — Census Sectors Import
# Imports census sector boundaries (shapefile) and socioeconomic data (CSV)
# into the census_sectors table.
#
# Usage:
#   CNEFE_CSV=/path/to/cnefe.csv SECTOR_SHP=/path/to/setores.shp \
#       sh data/import/ibge_cnefe_import.sh
#
# Both CNEFE_CSV and SECTOR_SHP are optional; supply one or both.
# Running with only SECTOR_SHP imports geometry; only CNEFE_CSV imports statistics.
# Running both imports everything and joins geometry to statistics.
#
# Requirements:
#   - ogr2ogr (gdal-bin)        — for shapefile import
#   - psql (postgresql-client)
#   - DATABASE_URL env var
#
# CNEFE CSV expected columns (semicolon-separated, UTF-8 or LATIN1):
#   cd_setor, nm_municipio, nm_uf, v005 (median income proxy), v001 (total population)
#
# Sector shapefile must contain cd_setor attribute.
#
# The script is idempotent: uses ON CONFLICT DO UPDATE.

set -e

CNEFE_CSV="${CNEFE_CSV:-}"
SECTOR_SHP="${SECTOR_SHP:-}"

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is required." >&2
    exit 1
fi

if [ -z "$CNEFE_CSV" ] && [ -z "$SECTOR_SHP" ]; then
    echo "ERROR: set CNEFE_CSV and/or SECTOR_SHP." >&2
    echo "  CNEFE_CSV=/path/to/cnefe.csv SECTOR_SHP=/path/to/setores.shp $0" >&2
    exit 1
fi

# ── Step 1: import sector boundaries (optional) ───────────────────────────────
if [ -n "$SECTOR_SHP" ]; then
    if [ ! -f "$SECTOR_SHP" ]; then
        echo "ERROR: shapefile not found: $SECTOR_SHP" >&2; exit 1
    fi
    echo "Importing census sector boundaries from: $SECTOR_SHP"
    ogr2ogr \
        -f "PostgreSQL" \
        PG:"$DATABASE_URL" \
        "$SECTOR_SHP" \
        -nln census_sectors_geom_tmp \
        -overwrite \
        -t_srs EPSG:4326 \
        -nlt PROMOTE_TO_MULTI \
        -lco GEOMETRY_NAME=wkb_geometry \
        -lco FID=ogc_fid
    echo "Sector shapefile loaded into staging table."
fi

# ── Step 2: import socioeconomic CSV (optional) ───────────────────────────────
if [ -n "$CNEFE_CSV" ]; then
    if [ ! -f "$CNEFE_CSV" ]; then
        echo "ERROR: CSV not found: $CNEFE_CSV" >&2; exit 1
    fi
    echo "Importing CNEFE socioeconomic data from: $CNEFE_CSV"
    psql "$DATABASE_URL" <<SQL
CREATE TEMP TABLE cnefe_import_tmp (
    cd_setor     TEXT,
    nm_municipio TEXT,
    nm_uf        VARCHAR(2),
    v005         NUMERIC,   -- proxy for median income group (1–10)
    v001         INTEGER    -- total resident population
) ON COMMIT DROP;

COPY cnefe_import_tmp
FROM '${CNEFE_CSV}'
DELIMITER ';'
CSV HEADER;

INSERT INTO census_sectors (cd_setor, municipality, state, median_income_group, total_population, imported_at)
SELECT
    cd_setor,
    nm_municipio,
    nm_uf,
    LEAST(10, GREATEST(1, ROUND(v005)::INT)),
    v001,
    NOW()
FROM cnefe_import_tmp
ON CONFLICT (cd_setor) DO UPDATE SET
    municipality        = EXCLUDED.municipality,
    state               = EXCLUDED.state,
    median_income_group = EXCLUDED.median_income_group,
    total_population    = EXCLUDED.total_population;
SQL
    echo "Census sector statistics merged."
fi

# ── Step 3: join geometry to statistics ───────────────────────────────────────
if [ -n "$SECTOR_SHP" ]; then
    echo "Joining geometry to census_sectors..."
    psql "$DATABASE_URL" <<'SQL'
UPDATE census_sectors cs
SET    geometry = g.wkb_geometry
FROM   census_sectors_geom_tmp g
WHERE  g.cd_setor = cs.cd_setor
  AND  cs.geometry IS NULL;

DROP TABLE IF EXISTS census_sectors_geom_tmp;
SQL
    echo "Geometry joined."
fi

COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM census_sectors")
echo "Done. census_sectors: ${COUNT} rows total."
