#!/usr/bin/env sh
# Local ANA bootstrap import for Aspire-based development.
#
# One-command developer workflow from the repo root:
#   sh data/import/run_local_ana_import.sh
#
# What it does:
#   1. Resolves the ANA shapefile under data/import/raw/ana/SNIRH_Inundacao.shp
#   2. Validates required companion files exist
#   3. Waits for the local PostgreSQL/PostGIS instance to be reachable
#   4. Applies SQL migrations
#   5. Runs the ANA shapefile import

set -e

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname "$0")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)"
DEFAULT_DATABASE_URL="host=localhost port=5432 dbname=property-intelligence user=property_intelligence password=property_intelligence"
DATABASE_URL="${DATABASE_URL:-$DEFAULT_DATABASE_URL}"
ANA_SHAPEFILE="${ANA_SHAPEFILE:-$REPO_ROOT/data/import/raw/ana/SNIRH_Inundacao.shp}"

wait_for_postgres() {
    attempts=60
    while [ "$attempts" -gt 0 ]; do
        if psql "$DATABASE_URL" -tAc 'SELECT 1' >/dev/null 2>&1; then
            return 0
        fi
        attempts=$((attempts - 1))
        sleep 2
    done

    echo "ERROR: PostgreSQL is not reachable at $DATABASE_URL" >&2
    exit 1
}

if [ ! -f "$ANA_SHAPEFILE" ]; then
    echo "ERROR: shapefile not found: $ANA_SHAPEFILE" >&2
    exit 1
fi

for companion in \
    "${ANA_SHAPEFILE%.shp}.dbf" \
    "${ANA_SHAPEFILE%.shp}.prj" \
    "${ANA_SHAPEFILE%.shp}.shx"
do
    if [ ! -f "$companion" ]; then
        echo "ERROR: required shapefile companion missing: $companion" >&2
        exit 1
    fi
done

if ! command -v psql >/dev/null 2>&1; then
    echo "ERROR: psql not found. Install postgresql-client." >&2
    exit 1
fi

if ! command -v ogr2ogr >/dev/null 2>&1; then
    echo "ERROR: ogr2ogr not found. Install gdal-bin." >&2
    exit 1
fi

echo "Waiting for PostgreSQL to be ready at: $DATABASE_URL"
wait_for_postgres

echo "Applying migrations..."
DATABASE_URL="$DATABASE_URL" sh "$REPO_ROOT/infra/migrations/run.sh"

echo "Importing ANA shapefile..."
DATABASE_URL="$DATABASE_URL" ANA_SHAPEFILE="$ANA_SHAPEFILE" sh "$REPO_ROOT/data/import/ana_shapefile_import.sh" "$ANA_SHAPEFILE"

echo "ANA import complete."
