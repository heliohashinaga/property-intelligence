-- 005_health_and_schools.sql
-- Health facilities (CNES/DataSUS) and school records (INEP/IDEB) with PostGIS locations
-- Imported via data/import/cnes_import.sh and data/import/inep_ideb_import.sh

CREATE TABLE health_facilities (
    id            BIGSERIAL   PRIMARY KEY,
    cnes_code     TEXT        UNIQUE,
    name          TEXT        NOT NULL,
    facility_type TEXT        NOT NULL,
    municipality  TEXT        NOT NULL,
    state         VARCHAR(2)  NOT NULL DEFAULT 'SP',
    lat           NUMERIC(9,6),
    lng           NUMERIC(9,6),
    location      GEOMETRY(Point, 4326),
    imported_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ON health_facilities USING GIST (location);
CREATE INDEX ON health_facilities (facility_type);
CREATE INDEX ON health_facilities (municipality, state);

CREATE TABLE school_records (
    id          BIGSERIAL   PRIMARY KEY,
    inep_code   TEXT        UNIQUE,
    name        TEXT        NOT NULL,
    municipality TEXT       NOT NULL,
    state       VARCHAR(2)  NOT NULL,
    lat         NUMERIC(9,6),
    lng         NUMERIC(9,6),
    location    GEOMETRY(Point, 4326),
    ideb_score  NUMERIC(4,1),
    ideb_year   SMALLINT,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ON school_records USING GIST (location);
CREATE INDEX ON school_records (municipality, state);
