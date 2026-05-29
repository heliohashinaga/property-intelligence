#!/usr/bin/env sh
# run.sh — apply all SQL migrations in order against $DATABASE_URL
# Usage: DATABASE_URL=postgres://... sh infra/migrations/run.sh
set -e

if [ -z "$DATABASE_URL" ]; then
    echo "ERROR: DATABASE_URL is not set." >&2
    exit 1
fi

DIR="$(cd "$(dirname "$0")" && pwd)"
echo "Applying migrations from $DIR..."

for f in "$DIR"/0*.sql; do
    echo "  -> $(basename "$f")"
    psql "$DATABASE_URL" -f "$f"
done

echo "All migrations applied successfully."
