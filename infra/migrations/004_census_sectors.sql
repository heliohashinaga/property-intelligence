-- 004_census_sectors.sql
-- IBGE Censo 2022 census sectors with socioeconomic data and PostGIS boundaries
-- Imported via data/import/ibge_cnefe_import.sh

CREATE TABLE census_sectors (
    id                 BIGSERIAL    PRIMARY KEY,
    cd_setor           TEXT         NOT NULL UNIQUE,
    geometry           GEOMETRY(MultiPolygon, 4326),
    municipality       TEXT         NOT NULL,
    state              VARCHAR(2)   NOT NULL,
    median_income_group SMALLINT    CHECK (median_income_group BETWEEN 1 AND 10),
    population_density NUMERIC(12,2),
    working_age_pct    NUMERIC(5,2),
    total_population   INTEGER,
    imported_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX ON census_sectors USING GIST (geometry);
CREATE INDEX ON census_sectors (municipality, state);
