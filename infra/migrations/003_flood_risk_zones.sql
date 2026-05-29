-- 003_flood_risk_zones.sql
-- ANA SNIRH flood-risk zone polygons imported via ogr2ogr (data/import/ana_shapefile_import.sh)

CREATE TABLE flood_risk_zones (
    id          BIGSERIAL   PRIMARY KEY,
    geometry    GEOMETRY(MultiPolygon, 4326) NOT NULL,
    risk_level  TEXT        NOT NULL CHECK (risk_level IN ('low', 'moderate', 'high', 'critical')),
    description TEXT,
    source_file TEXT,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ON flood_risk_zones USING GIST (geometry);
