#!/usr/bin/env sh
# ANA SNIRH Flood Risk Zones Import
# Imports flood-risk zone polygons from an ANA shapefile into the flood_risk_zones table.
#
# Usage:
#   sh data/import/ana_shapefile_import.sh /path/to/flood_risk.shp
#   ANA_SHAPEFILE=/path/to/flood_risk.shp sh data/import/ana_shapefile_import.sh
#
# Requirements:
#   - ogr2ogr (gdal-bin)
#   - psql (postgresql-client)
#   - DATABASE_URL env var pointing to the PostGIS database
#
# The script is idempotent: rows are inserted with ON CONFLICT DO NOTHING,
# so re-running after a partial import is safe.

set -e

SHAPEFILE="${1:-${ANA_SHAPEFILE:-}}"

if [ -z "$SHAPEFILE" ]; then
    echo "ERROR: shapefile path required." >&2
    echo "Usage: $0 <path-to-shapefile.shp>" >&2
    echo "  or:  ANA_SHAPEFILE=/path/to/file.shp $0" >&2
    exit 1
fi

if [ ! -f "$SHAPEFILE" ]; then
    echo "ERROR: file not found: $SHAPEFILE" >&2
    exit 1
fi

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is required." >&2
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
