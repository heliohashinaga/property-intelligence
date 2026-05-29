-- 001_initial_schema.sql
-- Core tables: property_addresses, api_consumers, property_analyses, data_provider_raw_logs

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "postgis";

-- ── property_addresses ────────────────────────────────────────────────────────
CREATE TABLE property_addresses (
    id                 UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    normalized_address TEXT         NOT NULL UNIQUE,
    street_name        TEXT,
    street_number      TEXT,
    neighborhood       TEXT,
    city               TEXT         NOT NULL,
    state              VARCHAR(2)   NOT NULL,
    postal_code        VARCHAR(8),
    lat                NUMERIC(9,6),
    lng                NUMERIC(9,6),
    location           GEOMETRY(Point, 4326),
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX ON property_addresses (city, state);
CREATE INDEX ON property_addresses USING GIST (location);

-- ── api_consumers ─────────────────────────────────────────────────────────────
CREATE TABLE api_consumers (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name         TEXT        NOT NULL,
    api_key_hash VARCHAR(64) NOT NULL UNIQUE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ,
    is_active    BOOLEAN     NOT NULL DEFAULT TRUE
);

-- ── property_analyses (append-only) ──────────────────────────────────────────
CREATE TABLE property_analyses (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    address_id          UUID        NOT NULL REFERENCES property_addresses(id),
    api_consumer_id     UUID        NOT NULL REFERENCES api_consumers(id),
    composite_score     SMALLINT    NOT NULL CHECK (composite_score BETWEEN 0 AND 1000),
    composite_max       SMALLINT    NOT NULL CHECK (composite_max BETWEEN 0 AND 1000),
    grade               VARCHAR(2)  NOT NULL,
    dimension_scores    JSONB       NOT NULL,
    risk_flags          TEXT[]      NOT NULL DEFAULT '{}',
    opportunity_flags   TEXT[]      NOT NULL DEFAULT '{}',
    insight             TEXT,
    insight_unavailable BOOLEAN     NOT NULL DEFAULT FALSE,
    llm_model           VARCHAR(100) NOT NULL,
    rules_version       VARCHAR(20) NOT NULL,
    warnings            JSONB       NOT NULL DEFAULT '[]',
    providers_used      TEXT[]      NOT NULL,
    providers_unavailable TEXT[]    NOT NULL DEFAULT '{}',
    cached              BOOLEAN     NOT NULL DEFAULT FALSE,
    request_ip          INET,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ON property_analyses (address_id);
CREATE INDEX ON property_analyses (api_consumer_id);
CREATE INDEX ON property_analyses (created_at DESC);

-- ── data_provider_raw_logs ────────────────────────────────────────────────────
CREATE TABLE data_provider_raw_logs (
    id            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    address_id    UUID        NOT NULL REFERENCES property_addresses(id),
    analysis_id   UUID        REFERENCES property_analyses(id),
    provider_name TEXT        NOT NULL,
    raw_payload   TEXT        NOT NULL,
    fetched_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ON data_provider_raw_logs (address_id);
CREATE INDEX ON data_provider_raw_logs (analysis_id);
