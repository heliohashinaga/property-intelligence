# Quickstart: Property Intelligence API

**Date**: 2026-05-28

Validated path from zero to a running analysis response.

---

## Prerequisites

- Docker + Docker Compose installed
- .NET 10 SDK installed (`dotnet --version` → `10.x`)
- Node.js 22+ (for frontend)
- `ANTHROPIC_API_KEY` — get at console.anthropic.com
- `IPTU_API_KEY` — register at iptuapi.com.br (free tier)
- `CLOUDFLARE_TUNNEL_TOKEN` — create at dash.cloudflare.com → Zero Trust → Tunnels

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
ANTHROPIC_API_KEY=sk-ant-...
IPTU_API_KEY=...
API_KEY_SALT=change-me-random-32-chars
CLOUDFLARE_TUNNEL_TOKEN=...   # optional for local dev; leave blank to skip tunnel
```

---

## Step 2 — Start infrastructure

```bash
docker compose up -d postgres redis
```

Wait for PostgreSQL to be ready:
```bash
docker compose exec postgres pg_isready -U property_intelligence
# output: /var/run/postgresql:5432 - accepting connections
```

---

## Step 3 — Run migrations

```bash
sh infra/migrations/run.sh
# Applies 001_initial_schema.sql through 005_health_facilities.sql
```

---

## Step 4 — Import static datasets

These are one-time imports (re-run when sources publish updates):

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

Each script prints import counts on completion. Expect:
- ANA: ~50k flood zone polygons
- CNEFE: ~72k census sectors (SP state)
- INEP: ~18k schools
- CNES: ~340k facilities (Brazil-wide, filtered to SP)
- SSP-SP: ~8k rows per year/month/type combination

---

## Step 5 — Create an API consumer (test key)

```bash
# Via the admin CLI (or directly in psql for MVP)
dotnet run --project src/PropertyIntelligence.Api -- create-api-key --name "local-test"
# Output: API Key: lcc_test_abc123xyz...  (save this — shown once)
```

Or directly in psql:
```sql
-- Hash the key yourself: echo -n "lcc_test_mykey" | sha256sum
INSERT INTO api_consumers (name, api_key_hash)
VALUES ('local-test', '<sha256hex>');
```

---

## Step 6 — Run the API

```bash
dotnet run --project src/PropertyIntelligence.Api
# Listening on http://localhost:5000
```

---

## Step 7 — Send a test request

```bash
curl -s -X POST http://localhost:5000/v1/property/analyze \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: lcc_test_mykey" \
  -d '{"address": "Rua Augusta, 1500, São Paulo"}' \
  | jq .
```

Expected: HTTP 200 with `score.composite` in 0–1000, six dimension objects,
PT-BR `insight` string, and `analyzed_at` timestamp.

---

## Step 8 — Run the tests

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

## Step 9 — Run the frontend demo

```bash
cd frontend
npm install
npm run dev
# Open http://localhost:5173
```

Enter any Brazilian address → radar chart and score should appear.

---

## Validation Checklist

After completing the steps above, verify:

- [ ] `GET /health` returns `{"status":"healthy"}`
- [ ] `POST /v1/property/analyze` returns HTTP 200 with all 6 dimensions
- [ ] `cached: false` on first request, `cached: true` on second identical request
- [ ] `insight` is a non-empty PT-BR paragraph
- [ ] `property_analyses` table has one new row after the request
- [ ] `data_provider_raw_logs` table has 8 rows (one per provider) after the request
- [ ] Radar chart renders in the frontend with correct dimension values

---

## Common Issues

**Docker postgres not ready**: Wait 10–15 seconds after `docker compose up` before running migrations.

**ANA shapefile import fails**: Install GDAL (`sudo apt install gdal-bin` or `brew install gdal`) for `ogr2ogr`.

**Claude insight is null**: Check `ANTHROPIC_API_KEY` in `.env` and verify the key is valid at console.anthropic.com.

**Overpass timeout**: The public Overpass instance can be slow. The provider has a 5-second timeout; on miss the dimension is marked unavailable. Try again — cache will serve on retry.
