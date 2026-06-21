-- 006_provider_catalog.sql
-- Declarative provider registry + raw-log registry linkage (see data-model.md
-- §provider_catalog and §data_provider_raw_logs).
-- Idempotent: safe to re-run. Does NOT rewrite 001_initial_schema.sql.

-- ── provider_catalog ───────────────────────────────────────────────────────────
-- Source of truth for which data sources are enabled. Seeded from config at
-- deploy/startup time.
CREATE TABLE IF NOT EXISTS provider_catalog (
    provider_id          TEXT        PRIMARY KEY,
    display_name         TEXT        NOT NULL,
    enabled              BOOLEAN     NOT NULL DEFAULT TRUE,
    capabilities         TEXT[]      NOT NULL,
    cache_ttl_seconds    INTEGER     NOT NULL,
    timeout_seconds      INTEGER     NOT NULL,
    source_type          TEXT        NOT NULL CHECK (source_type IN ('real_time','imported','local_db')),
    version              TEXT        NOT NULL,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_healthcheck_at  TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_provider_catalog_enabled ON provider_catalog (enabled);

-- ── data_provider_raw_logs: registry linkage + cache provenance ───────────────
-- Augment (do NOT drop provider_name — kept for back-compat during migration).
ALTER TABLE data_provider_raw_logs ADD COLUMN IF NOT EXISTS provider_id      TEXT;
ALTER TABLE data_provider_raw_logs ADD COLUMN IF NOT EXISTS provider_version TEXT;
ALTER TABLE data_provider_raw_logs ADD COLUMN IF NOT EXISTS from_cache      BOOLEAN NOT NULL DEFAULT FALSE;