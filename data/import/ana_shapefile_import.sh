#!/usr/bin/env sh
# ANA SNIRH Flood Risk Zones Import
# Imports flood-risk zone polygons from an ANA shapefile into the flood_risk_zones table.
#
# Site: https://metadados.snirh.gov.br/geonetwork/srv/por/catalog.search#/search?any=alagamentos
#
# Usage:
#   sh data/import/ana_shapefile_import.sh /path/to/flood_risk.shp
#   ANA_SHAPEFILE=/path/to/flood_risk.shp sh data/import/ana_shapefile_import.sh
#
# Requirements:
#   - ogr2ogr (gdal-bin)
#   - psql (postgresql-client)
#   - DATABASE_URL env var pointing to the PostGIS database (defaults to the
#     local Aspire database when not provided)
#
# The script is idempotent: rows are inserted with ON CONFLICT DO NOTHING,
# so re-running after a partial import is safe.

set -e

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname "$0")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)"
DEFAULT_SHAPEFILE="$REPO_ROOT/data/import/raw/ana/SNIRH_Inundacao.shp"
SHAPEFILE="${1:-${ANA_SHAPEFILE:-$DEFAULT_SHAPEFILE}}"

if [ ! -f "$SHAPEFILE" ]; then
    echo "ERROR: shapefile not found: $SHAPEFILE" >&2
    echo "Usage: $0 <path-to-shapefile.shp>" >&2
    echo "  or:  ANA_SHAPEFILE=/path/to/file.shp $0" >&2
    echo "  or:  $0   # defaults to $DEFAULT_SHAPEFILE" >&2
    exit 1
fi

for companion in \
    "${SHAPEFILE%.shp}.dbf" \
    "${SHAPEFILE%.shp}.prj" \
    "${SHAPEFILE%.shp}.shx"
do
    if [ ! -f "$companion" ]; then
        echo "ERROR: required shapefile companion missing: $companion" >&2
        exit 1
    fi
done

DEFAULT_DATABASE_URL="host=localhost port=5432 dbname=property-intelligence user=property_intelligence password=property_intelligence"
DATABASE_URL="${DATABASE_URL:-$DEFAULT_DATABASE_URL}"

if ! command -v ogr2ogr >/dev/null 2>&1; then
    echo "ERROR: ogr2ogr not found. Install gdal-bin." >&2
    exit 1
fi

if ! command -v psql >/dev/null 2>&1; then
    echo "ERROR: psql not found. Install postgresql-client." >&2
    exit 1
fi

echo "Importing ANA flood risk zones from: $SHAPEFILE"

# Step 1 — load shapefile into a staging table, re-projecting to WGS84
ogr2ogr \
    -f "PostgreSQL" \
    PG:"$DATABASE_URL" \
    "$SHAPEFILE" \
    -nln flood_risk_zones_import_tmp \
    -overwrite \
    -t_srs EPSG:4326 \
    -nlt PROMOTE_TO_MULTI \
    -lco GEOMETRY_NAME=wkb_geometry \
    -lco FID=ogc_fid

echo "Shapefile loaded into staging table. Merging into flood_risk_zones..."

# Step 2 — merge staging → target, normalising the risk_level field
psql "$DATABASE_URL" <<SQL
INSERT INTO flood_risk_zones (geometry, risk_level, description, source_file, imported_at)
SELECT
    wkb_geometry,
    LOWER(COALESCE(
        NULLIF(risco,      ''),
        NULLIF(risco_inun, ''),
        NULLIF(risk_level, ''),
        'low'
    )),
    descricao,
    '${SHAPEFILE}',
    NOW()
FROM flood_risk_zones_import_tmp
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS flood_risk_zones_import_tmp;
SQL

COUNT=$(psql "$DATABASE_URL" -tAc "SELECT COUNT(*) FROM flood_risk_zones")
echo "Done. flood_risk_zones: ${COUNT} rows total."
