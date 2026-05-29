---

description: "Tasks for Property Intelligence API"
---

# Tasks: Property Intelligence API

**Input**: Design documents from `specs/001-property-intelligence-api/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅

**Tests**: Included per Constitution Principle III (Test-First — NON-NEGOTIABLE). Tests are written
and confirmed failing before each implementation task.

**Organization**: Tasks grouped by user story (US1→US2→US3) to enable independent MVP delivery.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Parallelizable (different files, no incomplete dependencies)
- **[US1/2/3]**: Maps to user story in spec.md

---

## Phase 1: Setup

**Purpose**: Solution scaffolding, tooling, and local infrastructure.

- [x] T001 Create .NET 10 solution with 6 projects: `PropertyIntelligence.Api`, `PropertyIntelligence.Core`, `PropertyIntelligence.Providers`, `PropertyIntelligence.Rules`, `PropertyIntelligence.Explainability`, `PropertyIntelligence.AppHost` (Aspire) under `src/`; and 3 test projects `PropertyIntelligence.Tests.Unit`, `PropertyIntelligence.Tests.Contract`, `PropertyIntelligence.Tests.Integration` under `tests/`
- [x] T002 [P] Add NuGet packages per project: `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` + `NRules` (Core/Rules), `StackExchange.Redis` (Providers), `NetTopologySuite.IO.ShapeFile` (Providers, for shapefile import), `OpenTelemetry.Exporter.Otlp` + `OpenTelemetry.Instrumentation.AspNetCore` + `OpenTelemetry.Instrumentation.Http` (Api), `Aspire.Hosting` (AppHost), `xUnit` + `Testcontainers.PostgreSql` + `Testcontainers.Redis` + `WireMock.Net` + `FluentAssertions` (Tests)
- [x] T003 [P] Create `infra/docker-compose.yml` with PostgreSQL 16 + PostGIS 3.4 (`postgis/postgis:16-3.4`) and Redis 7 services; add Docker Compose `healthcheck` for both; create `.env.example` with `DATABASE_URL`, `REDIS_URL`, `OPENROUTER_API_KEY`, `LLM_MODEL`, `IPTU_API_KEY`, `API_KEY_SALT`, `CLOUDFLARE_TUNNEL_TOKEN`, `GRAFANA_OTLP_ENDPOINT`, `GRAFANA_OTLP_TOKEN`
- [x] T004 [P] Create `.github/workflows/ci.yml` with GitHub Actions pipeline: restore → build → unit tests → contract tests; integration tests on push to `main`; **NOTE: K3s deploy job is added separately in T073 (Phase 7) after the cluster is provisioned by T067 — do NOT add kubectl steps here yet**

**Checkpoint**: `dotnet build` succeeds; `docker compose up -d` starts PostgreSQL and Redis healthy.

---

## Phase 2: Foundational

**Purpose**: Core interfaces, domain types, database schema, auth, and health endpoint that ALL user stories depend on.

⚠️ **CRITICAL**: No user story implementation can begin until this phase is complete.

- [x] T005 Define interfaces `IDataProvider<TResult>`, `IPropertyAnalysisEngine`, `IExplainabilityService`, `IAddressNormalizer`, `ICacheService` in `src/PropertyIntelligence.Core/Interfaces/` — write interface contracts first; confirm no implementation exists yet
- [x] T006 [P] Create domain value types `PropertyAddress`, `DimensionScore`, `AnalysisWarning` as C# records in `src/PropertyIntelligence.Core/Domain/`; include all fields from `data-model.md`; `DimensionScore` MUST have `Status` enum (Available/Unavailable), nullable `Score` and `Trend`
- [x] T007 [P] Create entity types `PropertyAnalysis`, `ApiConsumer`, `DataProviderRawLog` as C# records in `src/PropertyIntelligence.Core/Domain/`; `PropertyAnalysis` MUST include `LlmModel string` and `RulesVersion string` fields (constitution Principle II — algorithm/model version traceability); `PropertyAnalysis.DimensionScores` typed as `IReadOnlyList<DimensionScore>` (stored as JSONB)
- [x] T008 [P] Create `PropertyProfile` aggregate record in `src/PropertyIntelligence.Core/Domain/PropertyProfile.cs` with typed sub-records for each provider's data (PoiData, FloodRiskData, CensusData, CrimeData, HealthData, SchoolData, IptuData) plus `IReadOnlyList<string> ProvidersUnavailable`
- [x] T009 Create `PropertyIntelligenceDbContext` with EF Core + `UseNpgsql(..., o => o.UseNetTopologySuite())` in `src/PropertyIntelligence.Core/`; configure entity mappings for `property_addresses`, `property_analyses`, `api_consumers`, `data_provider_raw_logs`; register as scoped in DI
- [x] T010 Create database migrations in `infra/migrations/`: `001_initial_schema.sql` (property_addresses, property_analyses — include `llm_model VARCHAR(100) NOT NULL` and `rules_version VARCHAR(20) NOT NULL` columns, api_consumers, data_provider_raw_logs), `002_crime_records.sql`, `003_flood_risk_zones.sql` (PostGIS), `004_census_sectors.sql` (PostGIS), `005_health_and_schools.sql` (PostGIS); create `infra/migrations/run.sh` to apply all in order
- [x] T011 [P] Implement `CacheService` in `src/PropertyIntelligence.Providers/Shared/CacheService.cs` implementing `ICacheService`; Redis key format `{provider}:{SHA256(normalizedAddress)[..16]}`; generic get/set with per-call `TimeSpan ttl`; return null on miss
- [x] T012 [P] Implement `ApiKeyAuthMiddleware` in `src/PropertyIntelligence.Api/Middleware/ApiKeyAuthMiddleware.cs`; read `X-Api-Key` header → SHA-256 hash → lookup `api_consumers.api_key_hash`; return 401 JSON on missing/invalid; attach `ApiConsumer` to `HttpContext.Items`; extract `CF-Connecting-IP` header and store in `HttpContext.Items["ClientIp"]` for downstream audit use; fall back to `RemoteIpAddress` when header absent
- [x] T058 [P] Add structured JSON logging middleware to `src/PropertyIntelligence.Api/Program.cs`; every request logs `correlation_id` (UUID from `X-Request-Id` header or generated), `operation`, `duration_ms`, `status_code`, `api_consumer_id`, `request_ip`; use `Microsoft.Extensions.Logging` with JSON console formatter; **this MUST be in Foundational — constitution Principle V requires observability before any business logic runs**
- [x] T074 [P] Create `data/import/ana_shapefile_import.sh`; use `ogr2ogr` to import ANA SNIRH flood-risk shapefile into `flood_risk_zones` table with SRID 4326; print row count on success
- [x] T075 [P] Create `data/import/ibge_cnefe_import.sh`; download IBGE Censo 2022 CSV + sector boundary shapefile; join on `cd_setor`; import into `census_sectors` table with PostGIS geometry; print sector count
- [x] T076 [P] Create `data/import/inep_ideb_import.sh`; convert INEP IDEB XLS to CSV; geocode school addresses via Nominatim batch; import into `school_records` with `ST_MakePoint` geometry
- [x] T077 [P] Create `data/import/cnes_import.sh`; download CNES/DataSUS CSV; filter to São Paulo state; import into `health_facilities` table with lat/lng geometry; print facility count
- [x] T013 [P] Implement `GET /health` endpoint in `src/PropertyIntelligence.Api/Endpoints/HealthEndpoint.cs` matching `contracts/health-endpoint.md`; check PostgreSQL connectivity (simple `SELECT 1`) and Redis PING; return 200 healthy/degraded or 503 when DB unreachable; response time < 100ms
- [ ] T014 Run static dataset import scripts (**depends on T074–T077**): `sh data/import/ana_shapefile_import.sh` (ANA flood zones → PostGIS), `sh data/import/ibge_cnefe_import.sh` (census sectors), `sh data/import/inep_ideb_import.sh` (school records), `sh data/import/cnes_import.sh` (health facilities); verify row counts in each table

**Checkpoint**: `GET /health` returns `{"status":"healthy"}`; all migration tables exist; static datasets imported.

---

## Phase 3: User Story 1 — Property Risk Analysis (Priority: P1) 🎯 MVP

**Goal**: `POST /v1/property/analyze` returns HTTP 200 with composite score (0–1000), all 6 dimension scores, grade, risk/opportunity flags, and a PT-BR insight paragraph. Audit log persisted.

**Independent Test**: `curl -X POST /v1/property/analyze -H "X-Api-Key: ..." -d '{"address":"Rua Augusta, 1500, São Paulo"}'` returns 200 with `score.composite` in 0–1000, `score.grade` populated, all 6 entries in `score.dimensions`, non-empty `insight`, and a new row in `property_analyses`. No other phase needed.

### Contract Tests for US1 (write first, confirm failing)

- [ ] T015 [P] [US1] Write contract test for `POST /v1/property/analyze` happy path in `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs`; verify response schema matches `contracts/analyze-endpoint.md`; use WireMock.Net to stub all 8 external providers; confirm test FAILS before implementation
- [ ] T016 [P] [US1] Write provider contract test for `ViaCepProvider` in `tests/PropertyIntelligence.Tests.Contract/ViaCepProviderTests.cs`; stub `viacep.com.br` with known CEP response; verify normalized address fields populated correctly; confirm FAILS
- [ ] T017 [P] [US1] Write provider contract test for `OverpassPoiProvider` in `tests/PropertyIntelligence.Tests.Contract/OverpassProviderTests.cs`; stub Overpass API with POI count fixture; verify radius buckets (500m/1km/2km) parsed correctly; confirm FAILS

### Implementation for US1

- [ ] T018 [P] [US1] Implement `ViaCepProvider` in `src/PropertyIntelligence.Providers/ViaCep/ViaCepProvider.cs`; call `viacep.com.br/ws/{cep}/json/`; parse street, neighborhood, city, state; resolve lat/lng via Nominatim (`nominatim.openstreetmap.org/search?q={address}&format=json`); cache 30 days; `CacheTtl = TimeSpan.FromDays(30)`
- [ ] T019 [P] [US1] Implement `OverpassPoiProvider` in `src/PropertyIntelligence.Providers/Overpass/OverpassPoiProvider.cs`; build Overpass QL query for metro stations, bus stops, hospitals, schools, pharmacies within 500m/1km/2km radii from lat/lng; parse `elements` count per category per radius; cache 7 days
- [ ] T020 [P] [US1] Implement `AnaFloodRiskProvider` in `src/PropertyIntelligence.Providers/Ana/AnaFloodRiskProvider.cs`; execute PostGIS `ST_Intersects(geometry, ST_SetSRID(ST_MakePoint(:lng,:lat),4326))` against `flood_risk_zones`; return highest `risk_level` found (null if no zone); cache 30 days
- [ ] T021 [P] [US1] Implement `IbgeCensusProvider` in `src/PropertyIntelligence.Providers/Ibge/IbgeCensusProvider.cs`; execute PostGIS `ST_Intersects` against `census_sectors`; return `median_income_group`, `population_density`, `median_age`; cache 30 days
- [ ] T022 [P] [US1] Implement `CrimeDataProvider` in `src/PropertyIntelligence.Providers/Crime/CrimeDataProvider.cs`; query `crime_records WHERE municipality = :city AND year >= :cutoffYear`; sum counts by crime_type; return total crimes per 100k; cache 24 hours
- [ ] T023 [P] [US1] Implement `CnesHealthProvider` in `src/PropertyIntelligence.Providers/Cnes/CnesHealthProvider.cs`; execute PostGIS `ST_DWithin(location, ST_SetSRID(ST_MakePoint(:lng,:lat),4326)::geography, :radius)` against `health_facilities`; count by `facility_type` within 1km and 2km; cache 7 days
- [ ] T024 [P] [US1] Implement `InepSchoolProvider` in `src/PropertyIntelligence.Providers/Inep/InepSchoolProvider.cs`; PostGIS `ST_DWithin` against `school_records` within 1km and 2km; return nearest school IDEB score and count of IDEB ≥ 7.0 schools; cache 30 days
- [ ] T025 [P] [US1] Implement `IptuApiProvider` in `src/PropertyIntelligence.Providers/Iptu/IptuApiProvider.cs`; call `iptuapi.com.br` REST API with `IPTU_API_KEY`; parse `valor_venal`, `zoneamento`, historical values list; return typed `IptuData` record; cache 30 days; graceful null on 404
- [ ] T026 [US1] Implement `PropertyEnrichmentModule` in `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`; `Task.WhenAll` all 8 providers with individual 5-second `CancellationToken` timeout per provider; catch per-provider exceptions → record in `ProvidersUnavailable`; aggregate results into `PropertyProfile`; store raw payloads in `data_provider_raw_logs`
- [ ] T027 [US1] Implement NRules facts `PropertyFact` and `ScoringFact` in `src/PropertyIntelligence.Rules/Facts/`; `PropertyFact` wraps `PropertyProfile`; `ScoringFact` holds `(Dimension, Points, Reason)` string fields; no logic in fact classes
- [ ] T078 [P] [US1] Write unit tests for all 6 NRules dimension rule classes in `tests/PropertyIntelligence.Tests.Unit/DimensionRulesTests.cs` **before** T028–T033 implementation (constitution Principle III — confirm all FAIL first); SecurityRules (Per100k < 50 → score ≥ 160, Per100k > 300 → score ≤ 40), MobilityRules (metro within 500m → score ≥ 150, no transit → score ≤ 40), InfrastructureRules (hospital within 2km → score ≥ 100), EnvironmentRules (null risk → 200, critical → 0), AppreciationRules (positive CAGR → score ≥ 120), UrbanContextRules (income group 8+ → score ≥ 130)
- [ ] T028 [P] [US1] Implement `SecurityRules` in `src/PropertyIntelligence.Rules/Dimensions/SecurityRules.cs`; rules score 0–200 based on `CrimeData.Per100k` thresholds (e.g., <50/100k → 180pts, 50-100 → 140pts, etc.); each rule produces one `ScoringFact("security", pts, reason)`
- [ ] T029 [P] [US1] Implement `MobilityRules` in `src/PropertyIntelligence.Rules/Dimensions/MobilityRules.cs`; score based on metro stations within 500m, bus stops within 500m/1km, walkability index from OSM POI density; max 200pts additive
- [ ] T030 [P] [US1] Implement `InfrastructureRules` in `src/PropertyIntelligence.Rules/Dimensions/InfrastructureRules.cs`; score based on hospital within 2km, UBS/UPA counts within 1km, school IDEB ≥ 7.0 within 1km, pharmacy within 500m; max 200pts
- [ ] T031 [P] [US1] Implement `EnvironmentRules` in `src/PropertyIntelligence.Rules/Dimensions/EnvironmentRules.cs`; score based on `FloodRiskData.RiskLevel`: null → 200pts, low → 160pts, moderate → 100pts, high → 40pts, critical → 0pts; no additive — single rule fires per risk level
- [ ] T032 [P] [US1] Implement `AppreciationRules` in `src/PropertyIntelligence.Rules/Dimensions/AppreciationRules.cs`; score based on IPTU CAGR (positive CAGR → higher score) and zoning class density allowance; max 200pts additive
- [ ] T033 [P] [US1] Implement `UrbanContextRules` in `src/PropertyIntelligence.Rules/Dimensions/UrbanContextRules.cs`; score based on `CensusData.MedianIncomeGroup` (1–10 scale) and `PopulationDensity` (optimal density range); max 200pts additive
- [ ] T034 [US1] Implement `PropertyAnalysisEngine` in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs` implementing `IPropertyAnalysisEngine`; create `ISessionFactory` singleton at startup; create `ISession` per request; assert `PropertyFact`; call `session.Fire()`; aggregate `ScoringFact` by dimension with `Math.Min(200, sum)`; compute proportional composite (`sum of available`) and `composite_max` (200 × N available); compute grade from `composite/max` percentage per grade mapping in `contracts/analyze-endpoint.md`; expose `RulesVersion` constant (e.g., `"1.0.0"`) for audit logging
- [ ] T035 [US1] Implement `LlmExplainabilityService` in `src/PropertyIntelligence.Explainability/LlmExplainabilityService.cs` implementing `IExplainabilityService`; POST to `openrouter.ai/api/v1/chat/completions` (OpenAI-compatible format); headers: `Authorization: Bearer {OPENROUTER_API_KEY}`, `HTTP-Referer: https://property-intelligence.hashinaga.dev`, `X-Title: Property Intelligence`; body: `{model, messages, models: [primary, fallback], route: "fallback"}`; PT-BR prompt includes address, composite/max, all dimension scores, flags, unavailable providers; 10-second `HttpClient` timeout; return null on timeout/error (do NOT throw); log failure with correlation ID
- [ ] T036 [US1] Implement `POST /v1/property/analyze` endpoint in `src/PropertyIntelligence.Api/Endpoints/AnalyzeEndpoint.cs`; orchestrate: validate request → normalize address → enrich → analyze → explain → persist audit → return 200/422/503 per `contracts/analyze-endpoint.md`; attach correlation ID to all log entries; return 401 if auth middleware rejected
- [ ] T037 [US1] Implement audit persistence in `src/PropertyIntelligence.Api/Endpoints/AnalyzeEndpoint.cs`; upsert `property_addresses` (ON CONFLICT normalized address); INSERT `property_analyses` (append-only) including `llm_model` (value of `LLM_MODEL` env var) and `rules_version` (from `PropertyAnalysisEngine.RulesVersion`); after INSERT, backfill `data_provider_raw_logs SET analysis_id = :new_analysis_id WHERE address_id = :addr_id AND analysis_id IS NULL AND fetched_at >= :request_start`; all within a single DB transaction; log `analysis_id` at INFO level
- [ ] T038 [US1] Implement SSP-SP monthly import in `data/import/ssp_sp_import.sh`; download latest CSV from `ssp.sp.gov.br/estatistica`; parse municipality/year/month/crime_type/count columns; `INSERT INTO crime_records ... ON CONFLICT DO NOTHING` (idempotent); print imported row count; register as monthly cron via `data/import/README.md` instructions
- [ ] T039 [US1] Register all services in `src/PropertyIntelligence.Api/Program.cs`: `ISessionFactory` singleton (NRules), `ISession` transient, all 8 providers as `IDataProvider<T>` scoped, `CacheService` singleton, `PropertyEnrichmentModule` scoped, `PropertyAnalysisEngine` scoped, `LlmExplainabilityService` scoped, `PropertyIntelligenceDbContext` scoped; configure OpenTelemetry with OTLP exporter pointing to `GRAFANA_OTLP_ENDPOINT`

**Checkpoint**: `POST /v1/property/analyze` returns HTTP 200 with all 6 dimensions, composite score, grade, insight, and a new `property_analyses` row. US1 independently verified.

---

## Phase 4: User Story 2 — Dimensional Score Transparency (Priority: P2)

**Goal**: All 6 dimension `trend` fields accurate (improving/stable/worsening) based on correct historical windows; `risk_flags` and `opportunity_flags` carry specific business-rule values; `warnings` array and proportional composite verified for partial analyses.

**Independent Test**: Submit address in an area with known rising crime → verify `score.dimensions.security.trend == "worsening"` and `risk_flags` contains `"crime_trend_12m"`. Submit request with SSP-SP provider disabled → verify `warnings` array has one entry, `score.max == 800`, grade computed from 5/6 dimensions. Both tests independent of US3.

### Contract Tests for US2 (write first, confirm failing)

- [ ] T040 [P] [US2] Write unit test for trend calculation logic in `tests/PropertyIntelligence.Tests.Unit/TrendCalculationTests.cs`; inject mock crime data with rising counts over 24 months → expect `worsening`; flat counts → `stable`; decreasing → `improving`; confirm test FAILS before implementation
- [ ] T041 [P] [US2] Write unit test for proportional composite in `tests/PropertyIntelligence.Tests.Unit/ScoringEngineTests.cs`; simulate 1 unavailable provider → verify `composite_max == 800`, grade uses `composite/800` percentage; confirm FAILS

### Implementation for US2

- [ ] T042 [US2] Add 24-month trend calculation to `CrimeDataProvider` in `src/PropertyIntelligence.Providers/Crime/CrimeDataProvider.cs`; query `crime_records` for last 24 months grouped by month; compute linear regression slope over `per_100k` values; slope > +5%/yr → `worsening`, slope < -5%/yr → `improving`, else `stable`; include `CrimeTrend` in returned `CrimeData`
- [ ] T043 [P] [US2] Add 36-month appreciation trend to `IptuApiProvider` in `src/PropertyIntelligence.Providers/Iptu/IptuApiProvider.cs`; compute CAGR from IPTU historical `valor_venal` list over available years (up to 36 months); positive CAGR → `improving`, negative → `worsening`, near-zero → `stable`; include `AppreciationTrend` in `IptuData`
- [ ] T044 [P] [US2] Add environment trend to `AnaFloodRiskProvider` in `src/PropertyIntelligence.Providers/Ana/AnaFloodRiskProvider.cs`; at MVP, environment trend is always `stable` (ANA data updates rarely; accurate trend requires ≥2 import snapshots); add `// TODO: derive trend from flood_risk_zones import history once 2nd snapshot available` comment; ensure `FloodRiskData.Trend = TrendDirection.Stable`
- [ ] T045 [US2] Populate `DimensionScore.Trend` for all 6 dimensions in `PropertyAnalysisEngine` in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; map provider trend enums to `"improving"/"stable"/"worsening"` strings; null when dimension `status == unavailable`
- [ ] T046 [P] [US2] Implement `risk_flags` generation in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; rules: flood risk_level ≥ moderate → `moderate_flood_risk` / `high_flood_risk`; security trend = worsening → `crime_trend_12m`; mobility score < 80 → `low_mobility`; no hospital within 2km → `no_hospital_2km`
- [ ] T047 [P] [US2] Implement `opportunity_flags` generation in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; rules: future metro station from Overpass (`construction=station`) within 1km → `metro_expansion_nearby`; IPTU zoning class allows high density → `zoning_upscale`; appreciation trend = improving → `appreciation_trend_up`; nearest school IDEB ≥ 7.0 within 1km → `school_excellence_1km`
- [ ] T048 [US2] Implement `warnings` array in `PropertyEnrichmentModule` in `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`; for each provider in `ProvidersUnavailable`, map to affected dimension name and generate PT-BR message per `AnalysisWarning` template in `contracts/analyze-endpoint.md`; pass warnings list to `PropertyAnalysis`
- [ ] T079 [US2] Add 24-month mobility trend to `OverpassPoiProvider` in `src/PropertyIntelligence.Providers/Overpass/OverpassPoiProvider.cs`; compare current transit-stop counts against counts from the prior cached fetch (stored alongside result in Redis with timestamp key); positive delta → `improving`, negative → `worsening`, near-zero → `stable`; include `MobilityTrend` in `PoiData`
- [ ] T080 [P] [US2] Add 36-month infrastructure trend to `CnesHealthProvider` and `InepSchoolProvider`; for each, compare current facility/school counts (within radius) against a snapshot stored in Redis at last-refresh timestamp; include `InfrastructureTrend` in `HealthData` / `SchoolData`
- [ ] T081 [P] [US2] Add 36-month urban context trend to `IbgeCensusProvider` in `src/PropertyIntelligence.Providers/Ibge/IbgeCensusProvider.cs`; compare current `median_income_group` of the census sector against the prior census year value stored in `census_sectors`; include `UrbanContextTrend` in `CensusData`

**Checkpoint**: `score.dimensions.security.trend` is `"worsening"` for a crime-up neighbourhood. Partial analysis (1 provider down) returns `score.max == 800`, correct grade, one entry in `warnings`. US2 independently verified.

---

## Phase 5: User Story 3 — Visual Demo Frontend (Priority: P3)

**Goal**: A browser user types a Brazilian address and sees a radar chart of 6 dimensions, color-coded composite score with grade, risk/opportunity flag badges, and PT-BR insight — all within 10 seconds.

**Independent Test**: Open `http://localhost:5173`, submit "Rua Augusta, 1500, São Paulo", verify: radar chart renders with 6 labeled axes, score badge shows `724 / B+`, at least 1 flag badge visible, insight paragraph visible in PT-BR. No API or backend knowledge required.

### Implementation for US3

- [ ] T049 [US3] Initialize Vue.js 3 + Vite + TypeScript project in `frontend/`; install Chart.js, Leaflet, @types packages; create `frontend/src/` directory structure per `plan.md`
- [ ] T050 [P] [US3] Implement `propertyIntelligenceApi.ts` in `frontend/src/services/propertyIntelligenceApi.ts`; typed `analyzeProperty(address: string)` function using `fetch`; maps API JSON to TypeScript interfaces mirroring `contracts/analyze-endpoint.md` response shape; throws typed `ApiError` on non-200 responses
- [ ] T051 [P] [US3] Implement `AddressInput.vue` in `frontend/src/components/AddressInput.vue`; text input with submit button; emits `analyze` event with address string; shows inline spinner while loading; disables input during request; clears on new submit
- [ ] T052 [US3] Implement `ScoreRadarChart.vue` in `frontend/src/components/ScoreRadarChart.vue`; Chart.js `radar` type with 6 axes (PT-BR labels: Segurança, Mobilidade, Infraestrutura, Ambiental, Valorização, Contexto); data normalized to 0–100%; tooltip on hover shows `score/max` and trend arrow (↑↓→); unavailable dimensions shown as dashed line at 0
- [ ] T053 [US3] Implement `ScoreCard.vue` in `frontend/src/components/ScoreCard.vue`; displays composite score (`724 / 1000`), grade badge (`B+`) with color coding (A+/A = green, B+/B = blue, C+/C = yellow, D = orange, F = red); shows normalized address below score
- [ ] T054 [P] [US3] Implement `FlagBadges.vue` in `frontend/src/components/FlagBadges.vue`; renders risk flags in red pill badges and opportunity flags in green pill badges; PT-BR label map for each known flag value; unknown flags displayed as-is
- [ ] T055 [P] [US3] Implement `InsightPanel.vue` in `frontend/src/components/InsightPanel.vue`; displays PT-BR insight text; if `insight_unavailable == true`, shows graceful message "Explicação indisponível no momento"; renders `warnings` as yellow alert boxes above insight; shows `providers_used` list as small grey chips
- [ ] T056 [US3] Implement `Home.vue` in `frontend/src/pages/Home.vue`; compose all components in layout: AddressInput → loading state → (ScoreCard + ScoreRadarChart + FlagBadges + InsightPanel); on error shows PT-BR error message without exposing internal details; reset button returns to input state
- [ ] T057 [US3] Configure `frontend/vite.config.ts` for Cloudflare Pages; set `VITE_API_BASE_URL` env variable for API endpoint; create `frontend/public/_redirects` with `/* /index.html 200` for SPA routing

**Checkpoint**: Open `http://localhost:5173`, submit address, radar chart + score card + flags + insight all render. Error on invalid address shows PT-BR message. US3 independently verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Integration tests, performance validation, documentation, and Aspire wiring.

**NOTE**: T058 (structured JSON logging) was moved to Phase 2 Foundational per constitution Principle V.

- [ ] T059 [P] Write unit tests for `PropertyAnalysisEngine` in `tests/PropertyIntelligence.Tests.Unit/ScoringEngineTests.cs`; cover: all-available → composite = sum of 6 dimensions; 1 unavailable → max reduces by 200; grade boundary values (90%, 80%, 70%, etc.); flags generated correctly
- [ ] T060 [P] Write unit tests for `AddressNormalizer` in `tests/PropertyIntelligence.Tests.Unit/AddressNormalizerTests.cs`; mock HTTP; test successful normalization, 404 → 422, ambiguous → 422 with candidates
- [ ] T061 Write integration test for full analysis pipeline in `tests/PropertyIntelligence.Tests.Integration/PropertyAnalysisIntegrationTests.cs`; use Testcontainers PostgreSQL (`postgis/postgis:16-3.4`) + Redis; mock Overpass and OpenRouter via WireMock.Net; seed `crime_records` + `flood_risk_zones`; assert: 200 response, `property_analyses` row created, `data_provider_raw_logs` rows created
- [ ] T062 [P] Write integration test for graceful degradation in `tests/PropertyIntelligence.Tests.Integration/PropertyAnalysisIntegrationTests.cs`; configure WireMock.Net to return 503 for `ssp_sp` provider; assert: HTTP 200 returned, `score.max == 800`, `warnings` has 1 entry, `providers_unavailable` contains `ssp_sp`
- [ ] T063 [P] Write `data/import/README.md` documenting all 4 import scripts (ANA, IBGE, INEP, CNES, SSP-SP); include expected row counts, re-run cadence, and manual cron setup instructions
- [ ] T064 Run `quickstart.md` validation checklist end-to-end; check all 7 items pass; update `quickstart.md` with any corrections found
- [ ] T065 [P] Wire .NET Aspire AppHost in `src/PropertyIntelligence.AppHost/Program.cs` (project created in T001); register `PropertyIntelligence.Api`; verify Aspire dashboard at `http://localhost:15000` shows traces and health; can be done as early as Phase 1 if preferred for better local dev experience
- [ ] T066 [P] Add Cloudflare Tunnel container to `infra/docker-compose.yml` (`cloudflare/cloudflared:latest`) with `CLOUDFLARE_TUNNEL_TOKEN` env var
- [ ] T082 Add performance validation to integration tests in `tests/PropertyIntelligence.Tests.Integration/PropertyAnalysisIntegrationTests.cs`; use `Stopwatch` to assert SC-001 (non-cached ≤ 8s) and SC-002 (cached ≤ 500ms); also create `data/benchmark/smoke.sh` using `oha` or `curl` for manual p95 latency check

---

## Phase 7: Infrastructure

**Purpose**: OpenTofu IaC for Hetzner provisioning, K3s production manifests, Grafana Cloud observability.

- [ ] T067 Write OpenTofu config in `infra/tofu/main.tf`: provision Hetzner CX31 server (Ubuntu 24.04, Falkenstein), Hetzner Firewall (allow 80/443/6443), Cloudflare DNS A record; remote-exec installs K3s + cloudflared; write `infra/tofu/variables.tf` (hetzner_token, cloudflare_api_token, cloudflare_zone_id) and `infra/tofu/outputs.tf` (server_ip, kubeconfig path)
- [ ] T068 [P] Write K3s namespace and Property Intelligence API manifest in `infra/k3s/namespace.yaml` and `infra/k3s/property-intelligence-api.yaml`; Deployment with 2 replicas, rolling update strategy, liveness/readiness probes on `GET /health`; ConfigMap for non-secret env vars; Secret ref for `OPENROUTER_API_KEY`, `DATABASE_URL`, `GRAFANA_OTLP_TOKEN`
- [ ] T069 [P] Write K3s PostgreSQL StatefulSet in `infra/k3s/postgres.yaml`; image `postgis/postgis:16-3.4`; 20GB PersistentVolumeClaim on Hetzner local storage; init container runs `infra/migrations/run.sh` on first boot
- [ ] T070 [P] Write K3s Redis Deployment in `infra/k3s/redis.yaml` and Cloudflared DaemonSet in `infra/k3s/cloudflared.yaml`; cloudflared reads tunnel token from K3s Secret
- [ ] T071 Instrument custom OpenTelemetry metrics in `src/PropertyIntelligence.Api/`: add `Meter("PropertyIntelligence")` with counters/histograms: `property_intelligence.analysis.duration_ms`, `property_intelligence.provider.fetch_duration_ms` (tagged by provider), `property_intelligence.provider.cache_hit_total`, `property_intelligence.score.composite`, `property_intelligence.insight.generation_ms`; verify metrics appear in Grafana Cloud
- [ ] T072 [P] Create Grafana Cloud dashboard JSON in `infra/grafana/property-intelligence-dashboard.json`; panels: request rate, p95 latency by endpoint, provider error rate by provider name, cache hit ratio, composite score histogram by grade, OpenRouter latency; import via Grafana API or manual upload
- [ ] T073 Add K3s deploy job to `.github/workflows/ci.yml` (extends T004); add `deploy` job that runs after integration tests pass on `main`: `kubectl set image deployment/property-intelligence-api api=ghcr.io/heliomarpm/property-intelligence-api:${GITHUB_SHA} -n property-intelligence`; authenticate via `KUBE_CONFIG` GitHub Secret populated from T067 `tofu output kubeconfig`; **depends on T067 (cluster must exist first)**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — no dependency on US2 or US3
- **US2 (Phase 4)**: Depends on Phase 3 completion — extends providers and engine; independent of US3
- **US3 (Phase 5)**: Depends on Phase 3 (needs working API) — independent of US2 timing
- **Polish (Phase 6)**: Depends on all desired stories being complete

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2 — self-contained full pipeline
- **US2 (P2)**: Starts after US1 — extends trend + flag logic in existing code
- **US3 (P3)**: Starts after US1 — pure frontend; does not require US2 to be complete

### Within Each Phase

- Test tasks (contract/unit) MUST be written and FAIL before implementation begins
- T074–T077 (import scripts) MUST be complete before T014 (run imports)
- T078 (dimension rule tests) MUST be written and FAIL before T028–T033
- Provider implementations (T018–T025) are fully parallel once T005–T009 complete
- NRules dimension rules (T028–T033) are fully parallel once T027 (facts) + T078 (tests) complete
- Vue components (T051–T056) are partially parallel once T050 (API service) complete
- T073 (K3s deploy CI job) depends on T067 (cluster provisioned)

---

## Parallel Opportunities

### Phase 2 (Foundational) — parallel block after T009 (migrations)

```
T011 CacheService          T012 ApiKeyAuthMiddleware     T013 GET /health
T006 Domain value types    T007 Entity types             T008 PropertyProfile
T009 DB migrations         T014 Static dataset imports
```

### Phase 3 (US1) — parallel block after T026 (EnrichmentModule)

```
T018 ViaCepProvider        T019 OverpassPoiProvider      T020 AnaFloodRiskProvider
T021 IbgeCensusProvider    T022 CrimeDataProvider        T023 CnesHealthProvider
T024 InepSchoolProvider    T025 IptuApiProvider
```

```
T028 SecurityRules         T029 MobilityRules            T030 InfrastructureRules
T031 EnvironmentRules      T032 AppreciationRules        T033 UrbanContextRules
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: US1
4. **STOP AND VALIDATE**: `POST /v1/property/analyze` returns 200 with all fields
5. Demo is live; US2 and US3 are enhancements

### Incremental Delivery

1. Setup + Foundational → infrastructure ready
2. US1 → core API working → MVP ✅
3. US2 → trends accurate, flags specific → richer analysis
4. US3 → frontend visible to recruiters → portfolio complete
5. Polish → tests green, CI passing → production-ready

---

## Notes

- `[P]` tasks = different files, no blocking dependencies on incomplete work
- Constitution Principle III (Test-First) requires: write test → confirm red → implement → confirm green
- Commit after each checkpoint (end of phase or user story completion)
- Do NOT merge US2 trend changes into US1 tasks — keep stories independently testable
- `data_provider_raw_logs` INSERT must happen even when the downstream analysis fails
