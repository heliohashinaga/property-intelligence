# Data Model: Property Intelligence API

**Date**: 2026-05-28

---

## Domain Entities (Persisted)

### `property_addresses`

Normalized canonical addresses. Immutable after creation.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default gen_random_uuid() | |
| `raw_input` | `text` | NOT NULL | Original user string |
| `normalized` | `text` | NOT NULL | Full normalized string |
| `street` | `text` | NOT NULL | |
| `number` | `text` | | May be null for lots |
| `neighborhood` | `text` | | |
| `city` | `text` | NOT NULL | |
| `state` | `char(2)` | NOT NULL | ISO 3166-2:BR state code |
| `postal_code` | `char(8)` | | CEP without hyphen |
| `latitude` | `numeric(10,7)` | NOT NULL | WGS84 |
| `longitude` | `numeric(10,7)` | NOT NULL | WGS84 |
| `created_at` | `timestamptz` | NOT NULL, default now() | |

**Indexes**: `(latitude, longitude)`, `(postal_code)`, `(city, neighborhood)`.
**Uniqueness**: No unique constraint on raw_input (same address can be submitted
differently); deduplication happens via normalized form in the cache layer.

---

### `property_analyses`

Append-only audit record of every completed analysis. Never updated or deleted.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default gen_random_uuid() | |
| `address_id` | `uuid` | NOT NULL, FK → property_addresses | |
| `api_consumer_id` | `uuid` | NOT NULL, FK → api_consumers | |
| `composite_score` | `smallint` | NOT NULL, CHECK (0–1000) | Sum of available dimension scores |
| `composite_max` | `smallint` | NOT NULL, CHECK (0–1000) | 200 × N available dimensions |
| `grade` | `varchar(2)` | NOT NULL | A+, A, B+, B, C+, C, D, F |
| `dimension_scores` | `jsonb` | NOT NULL | Array of DimensionScore objects |
| `risk_flags` | `text[]` | NOT NULL, default '{}' | |
| `opportunity_flags` | `text[]` | NOT NULL, default '{}' | |
| `insight` | `text` | nullable | PT-BR AI explanation; null if Claude unavailable |
| `insight_unavailable` | `boolean` | NOT NULL, default false | True when LLM failed |
| `llm_model` | `varchar(100)` | NOT NULL | OpenRouter model used, e.g. `anthropic/claude-3-haiku` |
| `rules_version` | `varchar(20)` | NOT NULL | NRules scoring version tag, e.g. `1.0.0` |
| `warnings` | `jsonb` | NOT NULL, default '[]' | Array of AnalysisWarning objects |
| `providers_used` | `text[]` | NOT NULL | Provider IDs successfully consulted from the enabled registry |
| `providers_unavailable` | `text[]` | NOT NULL, default '{}' | Enabled provider IDs that failed during this analysis |
| `cached` | `boolean` | NOT NULL | Were all providers served from cache? |
| `request_ip` | `inet` | | CF-Connecting-IP |
| `created_at` | `timestamptz` | NOT NULL, default now() | |

**Indexes**: `(address_id)`, `(api_consumer_id)`, `(created_at DESC)`.
**Constraint**: No UPDATE or DELETE — enforced at application level and
optionally via row-level security rule.

---

### `api_consumers`

One row per registered API key. Key is stored hashed (SHA-256).

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default gen_random_uuid() | |
| `name` | `text` | NOT NULL | Human label (e.g., "demo-frontend") |
| `api_key_hash` | `char(64)` | NOT NULL, UNIQUE | SHA-256 hex of the raw key |
| `is_active` | `boolean` | NOT NULL, default true | Soft revocation |
| `created_at` | `timestamptz` | NOT NULL, default now() | |

---

### `provider_catalog`

Declarative registry of available data sources. This table can be seeded from
configuration at deploy/startup time and is the source of truth for which
providers are enabled.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `provider_id` | `text` | PK | Stable identifier, e.g. `viacep`, `ssp_sp` |
| `display_name` | `text` | NOT NULL | Human-friendly source name |
| `enabled` | `boolean` | NOT NULL, default true | Disable without code changes |
| `capabilities` | `text[]` | NOT NULL | Supported dimensions/capabilities |
| `cache_ttl_seconds` | `integer` | NOT NULL | Runtime cache TTL |
| `timeout_seconds` | `integer` | NOT NULL | Per-provider timeout budget |
| `source_type` | `text` | NOT NULL | `real_time`, `imported`, `local_db` |
| `version` | `text` | NOT NULL | Adapter/source version for auditability |
| `created_at` | `timestamptz` | NOT NULL, default now() | |
| `updated_at` | `timestamptz` | NOT NULL, default now() | |
| `last_healthcheck_at` | `timestamptz` | nullable | Optional operational visibility |

**Indexes**: `(enabled)`, GIN on `(capabilities)` if query patterns justify it.

---

### `data_provider_raw_logs`

Stores raw external provider payloads before transformation. Supports
auditability and re-processing without re-fetching. Append-only.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default gen_random_uuid() | |
| `analysis_id` | `uuid` | nullable, FK → property_analyses | null if fetch happens before analysis ID is assigned |
| `provider_id` | `text` | NOT NULL, FK → provider_catalog.provider_id | Stable source identifier |
| `provider_version` | `text` | NOT NULL | Captured from registry at fetch time |
| `address_id` | `uuid` | NOT NULL, FK → property_addresses | |
| `raw_payload` | `jsonb` | NOT NULL | Serialized raw response |
| `fetched_at` | `timestamptz` | NOT NULL, default now() | |
| `from_cache` | `boolean` | NOT NULL | |

---

## Geospatial / Imported Datasets (Persisted, Read-Heavy)

### `flood_risk_zones`

Imported from ANA SNIRH shapefiles. Static; re-imported when ANA publishes
updates (typically annual).

| Column | Type | Notes |
|---|---|---|
| `id` | `bigserial` | PK |
| `geometry` | `geometry(MultiPolygon, 4326)` | PostGIS, WGS84 |
| `risk_level` | `text` | low, moderate, high, critical |
| `description` | `text` | nullable |
| `source_file` | `text` | Original shapefile name |
| `imported_at` | `timestamptz` | |

**Spatial index**: `CREATE INDEX ON flood_risk_zones USING GIST (geometry);`

---

### `census_sectors`

IBGE Censo 2022 aggregated data joined with setor censitário boundaries.

| Column | Type | Notes |
|---|---|---|
| `id` | `bigserial` | PK |
| `sector_code` | `char(15)` | UNIQUE — IBGE `cd_setor` (15 digits) |
| `geometry` | `geometry(MultiPolygon, 4326)` | Sector boundary |
| `median_income_group` | `smallint` | 1–10 IBGE income bracket |
| `population_density` | `numeric(10,2)` | People per km² |
| `median_age` | `numeric(5,2)` | |
| `total_population` | `integer` | |
| `census_year` | `smallint` | 2022 |

**Spatial index**: `CREATE INDEX ON census_sectors USING GIST (geometry);`

---

### `crime_records`

Imported from SSP-SP monthly CSV. Append-only — new months inserted, old
records never modified.

| Column | Type | Notes |
|---|---|---|
| `id` | `bigserial` | PK |
| `municipality` | `text` | Normalized name |
| `year` | `smallint` | |
| `month` | `smallint` | 1–12 |
| `crime_type` | `text` | Canonical type (see enum below) |
| `count` | `integer` | Absolute count |
| `per_100k` | `numeric(8,2)` | nullable — computed after population join |
| `imported_at` | `timestamptz` | |

**Unique constraint**: `(municipality, year, month, crime_type)` — idempotent
re-import.

**Crime type canonical enum**:
`furto`, `roubo`, `homicidio_doloso`, `latrocinio`, `lesao_corporal`, `estupro`, `outros`

---

### `school_records`

Imported from INEP IDEB. Updated every 2 years when INEP publishes.

| Column | Type | Notes |
|---|---|---|
| `id` | `bigserial` | PK |
| `inep_code` | `varchar(8)` | UNIQUE |
| `name` | `text` | |
| `location` | `geometry(Point, 4326)` | PostGIS |
| `ideb_score` | `numeric(4,2)` | nullable (not all schools have IDEB) |
| `ideb_year` | `smallint` | |
| `level` | `text` | fundamental_i, fundamental_ii, medio |
| `type` | `text` | public, private |

**Spatial index**: `CREATE INDEX ON school_records USING GIST (location);`

---

### `health_facilities`

Imported from CNES/DataSUS. Refreshed monthly.

| Column | Type | Notes |
|---|---|---|
| `id` | `bigserial` | PK |
| `cnes_code` | `varchar(7)` | UNIQUE |
| `name` | `text` | |
| `facility_type` | `text` | hospital, ubs, upa, clinica, farmacia, outros |
| `location` | `geometry(Point, 4326)` | |
| `municipality` | `text` | |
| `imported_at` | `timestamptz` | |

**Spatial index**: `CREATE INDEX ON health_facilities USING GIST (location);`

---

## Value Objects (In-Memory / JSON)

### `DimensionScore` (stored as JSONB in `property_analyses.dimension_scores`)

```json
{
  "dimension": "security",
  "score": 118,
  "max": 200,
  "trend": "stable",
  "status": "available"
}
```

When unavailable:

```json
{
  "dimension": "security",
  "score": null,
  "max": 200,
  "trend": null,
  "status": "unavailable"
}
```

**Trend historical windows**:
- `security`, `mobility` → 24 months
- `infrastructure`, `environment`, `appreciation`, `urban_context` → 36 months

---

### `AnalysisWarning` (stored as JSONB in `property_analyses.warnings`)

```json
{
  "dimension": "security",
  "provider": "ssp_sp",
  "message": "Dados de criminalidade indisponíveis no momento. Score de segurança não incluído nesta análise."
}
```

---

## Grade Mapping

| Composite % of available max | Grade |
|---|---|
| ≥ 90% | A+ |
| 80–89% | A |
| 70–79% | B+ |
| 60–69% | B |
| 50–59% | C+ |
| 40–49% | C |
| 30–39% | D |
| < 30% | F |

Grade is computed as `composite_score / composite_max` (not out of 1000),
so partial analyses are graded fairly.

---

## Redis Cache Schema

Keys follow the pattern: `{provider_id}:{addressHash}` where `addressHash` is
the first 16 hex characters of `SHA256(normalized_address.ToLowerInvariant())`.

TTL is resolved from `provider_catalog.cache_ttl_seconds`, not from a hardcoded
switch statement or fixed provider table.

| Key pattern | TTL source | Value |
|---|---|---|
| `{provider_id}:{hash}` | `provider_catalog.cache_ttl_seconds` | Serialized raw or normalized provider result |
| `{provider_id}:{hash}:meta` | `provider_catalog.cache_ttl_seconds` | Optional metadata snapshot (version, fetched_at, trend inputs) |

---

## Entity Relationships

```
api_consumers ──┐
                ├── property_analyses ──── property_addresses
                │         │
                │         └── data_provider_raw_logs ──── provider_catalog
                │
provider_catalog ────────────────────────────────────────┘
                │
                └── (future: provider health snapshots, rollout policies)

api_consumers ── (future: usage_logs, quotas)

property_analyses.dimension_scores → DimensionScore[] (JSONB)
property_analyses.warnings         → AnalysisWarning[] (JSONB)

PostGIS lookup tables (read-only at query time):
  flood_risk_zones    ← ST_Intersects(address.point, zone.geometry)
  census_sectors      ← ST_Intersects(address.point, sector.geometry)
  school_records      ← ST_DWithin(address.point, school.location, radius)
  health_facilities   ← ST_DWithin(address.point, facility.location, radius)
  crime_records       ← WHERE municipality = address.city
```

---

## State Transitions

**PropertyAnalysis** is append-only with no state machine — it is either
created (success, full or partial) or not created (HTTP 503 when <3 dimensions
available). There are no draft, pending, or failed states in the database.

**Data freshness** (when a re-analysis of the same address is requested):
- Cache hit → return same provider data, new `PropertyAnalysis` record created
  with `cached: true`
- Cache miss (TTL expired) → re-fetch providers, new `PropertyAnalysis` record
  created with `cached: false`
- Historical analyses are never modified or deleted regardless of cache state.
