-- 002_crime_records.sql
-- Monthly crime statistics imported from SSP-SP CSV (data/import/ssp_sp_import.sh)

CREATE TABLE crime_records (
    id           BIGSERIAL    PRIMARY KEY,
    municipality TEXT         NOT NULL,
    state        VARCHAR(2)   NOT NULL DEFAULT 'SP',
    year         SMALLINT     NOT NULL,
    month        SMALLINT     NOT NULL CHECK (month BETWEEN 1 AND 12),
    crime_type   TEXT         NOT NULL,
    count        INTEGER      NOT NULL CHECK (count >= 0),
    population   BIGINT,
    per_100k     NUMERIC(10,2),
    imported_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (municipality, year, month, crime_type)
);
CREATE INDEX ON crime_records (municipality, year, month);
CREATE INDEX ON crime_records (year DESC, month DESC);
