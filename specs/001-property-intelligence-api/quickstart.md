# Quickstart: Property Intelligence API

**Date**: 2026-05-28

Validated path from zero to a running **mock-provider-first** analysis response, with an optional follow-up path for real public datasets.

---

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` → `10.x`)
- Docker + Docker Compose installed only if you want to run local PostgreSQL/Redis or integration tests
- Node.js 22+ (for frontend)
- `OPENROUTER_API_KEY` — optional for live `insight` generation; without it the API may return `insight: null` / `insight_unavailable: true`
- `CLOUDFLARE_TUNNEL_TOKEN` — optional for local tunnel usage

---

## Step 1 — Clone & configure

```bash
git clone https://github.com/heliohashinaga/property-intelligence.git
cd property-intelligence
cp .env.example .env
```

Edit `.env`:

```env
DATABASE_URL=postgres://property_intelligence:property_intelligence@localhost:5432/property_intelligence
REDIS_URL=redis://localhost:6379
OPENROUTER_API_KEY=...        # optional for live insight generation
LLM_MODEL=meta-llama/llama-3.1-8b-instruct:free
API_KEY_SALT=change-me-random-32-chars
CLOUDFLARE_TUNNEL_TOKEN=...   # optional for local dev; leave blank to skip tunnel
```

> Recommended local path: start with the **mock provider profile** first. It
> gives you a complete end-to-end response without depending on external APIs,
> imports, geocoding, or public dataset availability.

---

## Step 2 — Optional: start local infrastructure

Use this only if you want PostgreSQL and Redis available locally for the API and integration tests.

```bash
docker compose -f infra/docker-compose.yml up -d postgres redis
```

Wait for PostgreSQL to be ready:
```bash
docker compose -f infra/docker-compose.yml exec postgres pg_isready -U property_intelligence
# output: /var/run/postgresql:5432 - accepting connections
```

---

## Step 3 — Run migrations

```bash
sh infra/migrations/run.sh
# Applies 001_initial_schema.sql through 005_health_facilities.sql
```

After migrations:
- ensure the provider registry (`provider_catalog` or configured equivalent) is available
- seed or configure the **mock provider profile** first
- keep real/public providers disabled locally until you explicitly want to test them

---

## Step 4 — Enable the mock provider profile

Use the mock runtime profile/configuration described in the task plan (for example
`appsettings.Mock.json` plus a registry seed such as `mock-mvp.providers.json`).
The exact wiring may vary by branch, but the goal is always the same:

- enabled providers = `mock_*`
- disabled providers = real public providers until Phase 3B
- deterministic fixture-backed responses for address, mobility, environment,
  security, infrastructure, appreciation, and urban context

Typical local run pattern:

```bash
ASPNETCORE_ENVIRONMENT=Mock dotnet run --project src/PropertyIntelligence.Api
```

If your branch wires the mock profile through Aspire, use:

```bash
dotnet run --project src/PropertyIntelligence.AppHost
```

and confirm the API is running with the mock registry profile enabled.

---

## Step 5 — Optional later: import real public datasets

Only do this when you want to start Phase 3B / real-source hardening.
These are **not required** for the first end-to-end API milestone.

```bash
# Flood risk zones (ANA SNIRH shapefile)
sh data/import/ana_shapefile_import.sh

# IBGE Census 2022 sectors
sh data/import/ibge_cnefe_import.sh

# INEP IDEB schools
sh data/import/inep_ideb_import.sh

# CNES health facilities
sh data/import/cnes_import.sh

# SSP-SP crime CSV (São Paulo, latest available year)
sh data/import/ssp_sp_import.sh
```

Each script prints import counts on completion. Expected counts depend on the
source snapshot used in your branch/environment.

---

## Step 6 — Create an API consumer (test key)

For the MVP, create the consumer directly in PostgreSQL:

```sql
-- Hash the key yourself: echo -n "lcc_test_mykey" | sha256sum
INSERT INTO api_consumers (name, api_key_hash)
VALUES ('local-test', '<sha256hex>');
```

Save the cleartext key you choose (`lcc_test_mykey` in this example); the API only stores the hash.

> Note: the active source set is registry-driven. In local/mock mode, validation
> should be done against the **currently enabled** providers for that profile
> (typically `mock_*` first), never against a hardcoded provider count.

---

## Step 7 — Run the API

If you are using the mock profile directly:

```bash
ASPNETCORE_ENVIRONMENT=Mock dotnet run --project src/PropertyIntelligence.Api
```

If your branch is already wired through Aspire:

```bash
dotnet run --project src/PropertyIntelligence.AppHost
# Aspire dashboard: http://localhost:15000
```

If the API service is not yet wired into the AppHost in your branch, start it in a second terminal:

```bash
ASPNETCORE_ENVIRONMENT=Mock dotnet run --project src/PropertyIntelligence.Api
```

---

## Step 8 — Send a test request

```bash
curl -s -X POST http://localhost:5276/v1/property/analyze \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: lcc_test_mykey" \
  -d '{"address": "Rua Augusta, 1500, São Paulo"}' \
  | jq .
```

Expected in the **mock-first** path:
- HTTP 200
- `score.composite` in 0–1000
- six dimension objects
- `providers_used` containing mock provider IDs
- `analyzed_at` timestamp
- `insight` populated **or** `insight_unavailable: true` if no live LLM key is configured

---

## Step 9 — Run the tests

```bash
# Unit tests (fast, no I/O)
dotnet test tests/PropertyIntelligence.Tests.Unit

# Contract tests (WireMock.Net stubs, no real HTTP)
dotnet test tests/PropertyIntelligence.Tests.Contract

# Integration tests (Testcontainers — needs Docker)
dotnet test tests/PropertyIntelligence.Tests.Integration
```

All three suites should pass before opening a PR.

---

## Step 10 — Run the frontend demo

```bash
cd frontend
npm install
npm run dev
# Open http://localhost:5173
```

Enter any Brazilian address → radar chart and score should appear.

---

## Validation Checklist

After completing the **mock-first** steps above, verify:

- [ ] `GET /health` returns `{"status":"healthy"}`
- [ ] `POST /v1/property/analyze` returns HTTP 200 with all 6 dimensions
- [ ] `providers_used` contains only enabled `mock_*` providers for the selected profile
- [ ] `cached: false` on first request, `cached: true` on second identical request
- [ ] `property_analyses` table has one new row after the request
- [ ] `data_provider_raw_logs` table has one row per enabled provider actually consulted after the request
- [ ] `insight` is either a non-empty PT-BR paragraph or the response clearly indicates `insight_unavailable`
- [ ] Radar chart renders in the frontend with correct dimension values

### Optional Phase 3B Validation

When you enable real public providers later, additionally verify:

- [ ] the registry can switch from `mock_*` providers to selected real providers without endpoint code changes
- [ ] imported datasets (ANA/IBGE/INEP/CNES/SSP-SP) are readable by the corresponding providers
- [ ] mock providers remain available for local/dev/test fallback

---

## Common Issues

**Compose PostgreSQL not ready**: Wait 10–15 seconds after `docker compose -f infra/docker-compose.yml up` before running migrations.

**Mock profile not taking effect**: Check `ASPNETCORE_ENVIRONMENT=Mock`, registry seed/config loading, and whether only `mock_*` providers are enabled.

**Aspire only shows the dashboard**: If the AppHost is not yet wired to the API and infrastructure in your branch, `dotnet run --project src/PropertyIntelligence.AppHost` will still open the dashboard, but the analysis endpoint will need the API and local services started separately.

**Insight is unavailable**: This is acceptable in the mock-first path when `OPENROUTER_API_KEY` is not configured. If you want live insight generation, set `OPENROUTER_API_KEY` and `LLM_MODEL` in `.env`.

**ANA shapefile import fails**: Install GDAL (`sudo apt install gdal-bin` or `brew install gdal`) for `ogr2ogr`.

**Public provider instability**: Keep mocks enabled for development and CI even after real-provider onboarding. Real-source imports and public APIs should harden the system, not block local development.
