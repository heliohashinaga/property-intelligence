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
- [x] T003 [P] Create `infra/docker-compose.yml` with PostgreSQL 16 + PostGIS 3.4 (`postgis/postgis:16-3.4`) and Redis 7 services; add Docker Compose `healthcheck` for both; create `.env.example` with `DATABASE_URL`, `REDIS_URL`, `OPENROUTER_API_KEY`, `LLM_MODEL`, `API_KEY_SALT`, `CLOUDFLARE_TUNNEL_TOKEN`, `GRAFANA_OTLP_ENDPOINT`, `GRAFANA_OTLP_TOKEN`
- [x] T004 [P] Create `.github/workflows/ci.yml` with GitHub Actions pipeline: restore → build → unit tests → contract tests; integration tests on push to `main`; **NOTE: K3s deploy job is added separately in T073 (Phase 7) after the cluster is provisioned by T067 — do NOT add kubectl steps here yet**

**Checkpoint**: `dotnet build` succeeds; optional local infrastructure (`docker compose up -d`) starts PostgreSQL and Redis healthy.

---

## Phase 2: Foundational

**Purpose**: Core interfaces, domain types, database schema, auth, and health endpoint that ALL user stories depend on.

⚠️ **CRITICAL**: No user story implementation can begin until this phase is complete.

- [x] T005 Define interfaces `IDataProvider<TResult>`, `IPropertyAnalysisEngine`, `IExplainabilityService`, `IAddressNormalizer`, `ICacheService` in `src/PropertyIntelligence.Core/Interfaces/` — write interface contracts first; confirm no implementation exists yet
- [x] T006 [P] Create domain value types `PropertyAddress`, `DimensionScore`, `AnalysisWarning` as C# records in `src/PropertyIntelligence.Core/Domain/`; include all fields from `data-model.md`; `DimensionScore` MUST have `Status` enum (Available/Unavailable), nullable `Score` and `Trend`
- [x] T007 [P] Create entity types `PropertyAnalysis`, `ApiConsumer`, `DataProviderRawLog` as C# records in `src/PropertyIntelligence.Core/Domain/`; `PropertyAnalysis` MUST include `LlmModel string` and `RulesVersion string` fields (constitution Principle II — algorithm/model version traceability); `PropertyAnalysis.DimensionScores` typed as `IReadOnlyList<DimensionScore>` (stored as JSONB)
- [x] T008 [P] Create `PropertyProfile` aggregate record in `src/PropertyIntelligence.Core/Domain/PropertyProfile.cs` with typed sub-records for each provider's data (AddressData, MobilityData, FloodRiskData, CensusData, CrimeData, HealthData, SchoolData, AppreciationData) plus `IReadOnlyList<string> ProvidersUnavailable`
- [x] T009 Create `PropertyIntelligenceDbContext` with EF Core + `UseNpgsql(..., o => o.UseNetTopologySuite())` in `src/PropertyIntelligence.Core/`; configure entity mappings for `property_addresses`, `property_analyses`, `api_consumers`, `data_provider_raw_logs`; register as scoped in DI
- [x] T010 Create database migrations in `infra/migrations/`: `001_initial_schema.sql` (property_addresses, property_analyses — include `llm_model VARCHAR(100) NOT NULL` and `rules_version VARCHAR(20) NOT NULL` columns, api_consumers, data_provider_raw_logs), `002_crime_records.sql`, `003_flood_risk_zones.sql` (PostGIS), `004_census_sectors.sql` (PostGIS), `005_health_and_schools.sql` (PostGIS); create `infra/migrations/run.sh` to apply all in order
- [x] T011 [P] Implement `CacheService` in `src/PropertyIntelligence.Providers/Shared/CacheService.cs` implementing `ICacheService`; Redis key format `{provider_id}:{SHA256(normalizedAddress)[..16]}`; generic get/set with per-call `TimeSpan ttl`; return null on miss
- [x] T012 [P] Implement `ApiKeyAuthMiddleware` in `src/PropertyIntelligence.Api/Middleware/ApiKeyAuthMiddleware.cs`; read `X-Api-Key` header → SHA-256 hash → lookup `api_consumers.api_key_hash`; return 401 JSON on missing/invalid; attach `ApiConsumer` to `HttpContext.Items`; extract `CF-Connecting-IP` header and store in `HttpContext.Items["ClientIp"]` for downstream audit use; fall back to `RemoteIpAddress` when header absent
- [x] T058 [P] Add structured JSON logging middleware to `src/PropertyIntelligence.Api/Program.cs`; every request logs `correlation_id` (UUID from `X-Request-Id` header or generated), `operation`, `duration_ms`, `status_code`, `api_consumer_id`, `request_ip`; use `Microsoft.Extensions.Logging` with JSON console formatter; **this MUST be in Foundational — constitution Principle V requires observability before any business logic runs**
- [x] T074 [P] Create `data/import/ana_shapefile_import.sh`; use `ogr2ogr` to import ANA SNIRH flood-risk shapefile into `flood_risk_zones` table with SRID 4326; print row count on success
- [x] T075 [P] Create `data/import/ibge_cnefe_import.sh`; download IBGE Censo 2022 CSV + sector boundary shapefile; join on `cd_setor`; import into `census_sectors` table with PostGIS geometry; print sector count
- [x] T076 [P] Create `data/import/inep_ideb_import.sh`; convert INEP IDEB XLS/CSV to importable format; prefer official lat/lng fields and fall back to local/open geocoding workflow (`geocodebr`/CNEFE or municipal address bases) instead of bulk public Nominatim usage; import into `school_records` with `ST_MakePoint` geometry
- [x] T077 [P] Create `data/import/cnes_import.sh`; download CNES/DataSUS CSV; filter to São Paulo state; import into `health_facilities` table with lat/lng geometry; print facility count
- [x] T013 [P] Implement `GET /health` endpoint in `src/PropertyIntelligence.Api/Endpoints/HealthEndpoint.cs` matching `contracts/health-endpoint.md`; check PostgreSQL connectivity (simple `SELECT 1`) and Redis PING; return 200 healthy/degraded or 503 when DB unreachable; response time < 100ms
- [x] T083 [P] Define provider registry contracts in `src/PropertyIntelligence.Core/Interfaces/` and `src/PropertyIntelligence.Providers/Registry/`: create `ProviderDescriptor` / `ProviderCatalogEntry` types plus `IProviderRegistry`; include `provider_id`, `enabled`, supported capabilities/dimensions, `cache_ttl`, `timeout`, `source_type`, and `version`
- [x] T084 Create provider registry persistence/config shape in `infra/migrations/001_initial_schema.sql` and app configuration: add `provider_catalog` table (or equivalent seeded config source) and ensure startup can load enabled providers without hardcoding the provider list
- [x] T085 [P] Add provider registry test fixtures in `tests/PropertyIntelligence.Tests.Contract/Fixtures/`; a single fixture set must define which providers are enabled for a scenario so contract tests can run against subsets without code changes
- [x] T086 Implement registry-aware cache and orchestration wiring: cache keys use `{provider_id}:{hash}` and `PropertyEnrichmentModule` must resolve timeout/TTL from the registry rather than hardcoded provider switches

**Checkpoint**: `GET /health` returns `{"status":"healthy"}`; all migration tables exist; provider registry metadata can enable/disable sources without touching endpoint code.

---

## Phase 3: User Story 1 — Mock Data MVP Slice (Priority: P1) 🚀

**Goal**: entregar `POST /v1/property/analyze` end-to-end usando apenas providers mockados, fixtures determinísticos e o provider registry. Nenhuma importação de dataset, API pública ou geocoding externo deve bloquear o primeiro green path.

**Independent Test**: iniciar a API com um perfil/configuração `mock` e enviar `POST /v1/property/analyze` com endereço de fixture. A resposta retorna HTTP 200 com `score.composite`, 6 dimensões, flags, `insight` (ou `insight_unavailable`), persistência em `property_analyses`, linhas em `data_provider_raw_logs` e comportamento de cache previsível — sem depender de ANA/IBGE/CNES/INEP/SSP nem de chamadas externas.

### Contract Tests for US1 Mock Slice (write first, confirm failing)

- [x] T015 [P] [US1] Write contract test for `POST /v1/property/analyze` mock happy path in `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs`; verify response schema matches `contracts/analyze-endpoint.md`; load enabled providers from `Fixtures/Registry/mock-mvp.providers.json`; confirm test FAILS before implementation
- [x] T016 [P] [US1] Write contract test for graceful degradation using mock providers in `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs`; disable or fail one mock dimension provider via registry fixture; verify `score.max == 800`, `warnings` populated, and `providers_unavailable` contains only the failed enabled provider; confirm FAILS
- [x] T017 [P] [US1] Write unit test for `PropertyEnrichmentModule` registry selection and unavailable-provider handling in `tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs`; assert disabled providers are skipped before fan-out and failed enabled providers are recorded; confirm FAILS

### Implementation for US1 Mock Slice

- [x] T018 [P] [US1] Create deterministic mock fixtures in `tests/PropertyIntelligence.Tests.Contract/Fixtures/`: add `Analyze/mock-analysis-happy-path.json`, `Analyze/mock-analysis-partial.json`, `Providers/*.json`, and `Registry/mock-mvp.providers.json`
- [x] T019 [P] [US1] Implement `MockProviderDataLoader` in `src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs`; load deterministic JSON payloads keyed by `provider_id` and scenario/address
- [x] T020 [P] [US1] Implement `MockAddressProvider` in `src/PropertyIntelligence.Providers/Mock/MockAddressProvider.cs`; return normalized address and coordinates from fixtures; register as `mock_address`
- [x] T021 [P] [US1] Implement `MockMobilityProvider` in `src/PropertyIntelligence.Providers/Mock/MockMobilityProvider.cs`; return transit/POI counts and trend fixture data; register as `mock_mobility`
- [x] T022 [P] [US1] Implement `MockEnvironmentProvider` in `src/PropertyIntelligence.Providers/Mock/MockEnvironmentProvider.cs`; return flood/environment fixture data; register as `mock_environment`
- [x] T023 [P] [US1] Implement `MockSecurityProvider` in `src/PropertyIntelligence.Providers/Mock/MockSecurityProvider.cs`; return crime/security fixture data; register as `mock_security`
- [x] T024 [P] [US1] Implement mock infrastructure providers — split into `MockHealthProvider` (`mock_health`→HealthData) + `MockSchoolProvider` (`mock_school`→SchoolData), both capability `infrastructure`, mirroring real CNES/INEP split (IDataProvider<T> is single-typed)
- [x] T025 [P] [US1] Implement `MockAppreciationProvider` and `MockUrbanContextProvider` in `src/PropertyIntelligence.Providers/Mock/`; return zoneamento/valorização proxies and census/context fixture data; register as `mock_appreciation` and `mock_urban_context`
- [x] T026 [US1] Implement `PropertyEnrichmentModule` in `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`; iterate only enabled providers from `IProviderRegistry`, run them in parallel with per-provider `CancellationToken` timeout from registry metadata, catch per-provider exceptions → record in `ProvidersUnavailable`; aggregate results into `PropertyProfile`; store raw payloads in `data_provider_raw_logs`
- [x] T027 [US1] Implement NRules facts `PropertyFact` and `ScoringFact` in `src/PropertyIntelligence.Rules/Facts/`; `PropertyFact` wraps `PropertyProfile`; `ScoringFact` holds `(Dimension, Points, Reason)` string fields; no logic in fact classes
- [x] T078 [P] [US1] Write unit tests for all 6 NRules dimension rule classes in `tests/PropertyIntelligence.Tests.Unit/DimensionRulesTests.cs` **before** T028–T033 implementation (constitution Principle III — confirm all FAIL first); SecurityRules (Per100k < 50 → score ≥ 160, Per100k > 300 → score ≤ 40), MobilityRules (metro within 500m → score ≥ 150, no transit → score ≤ 40), InfrastructureRules (hospital within 2km → score ≥ 100), EnvironmentRules (null risk → 200, critical → 0), AppreciationRules (zoneamento permissivo/projeto confirmado → score ≥ 120), UrbanContextRules (income group 8+ → score ≥ 130)
- [x] T028 [P] [US1] Implement `SecurityRules` in `src/PropertyIntelligence.Rules/Dimensions/SecurityRules.cs`; rules score 0–200 based on `CrimeData.Per100k` thresholds (e.g., <50/100k → 180pts, 50-100 → 140pts, etc.); each rule produces one `ScoringFact("security", pts, reason)`
- [x] T029 [P] [US1] Implement `MobilityRules` in `src/PropertyIntelligence.Rules/Dimensions/MobilityRules.cs`; score based on metro/bus/transit-stop access and walkability proxy counts; max 200pts additive
- [x] T030 [P] [US1] Implement `InfrastructureRules` in `src/PropertyIntelligence.Rules/Dimensions/InfrastructureRules.cs`; score based on hospital within 2km, UBS/UPA counts within 1km, school IDEB ≥ 7.0 within 1km, pharmacy within 500m; max 200pts
- [x] T031 [P] [US1] Implement `EnvironmentRules` in `src/PropertyIntelligence.Rules/Dimensions/EnvironmentRules.cs`; score based on `FloodRiskData.RiskLevel`: null → 200pts, low → 160pts, moderate → 100pts, high → 40pts, critical → 0pts; no additive — single rule fires per risk level
- [x] T032 [P] [US1] Implement `AppreciationRules` in `src/PropertyIntelligence.Rules/Dimensions/AppreciationRules.cs`; score from public-data-friendly proxies such as zoning permissiveness, confirmed future transit proximity, and official cadastral signals; avoid dependence on commercial IPTU wrappers
- [x] T033 [P] [US1] Implement `UrbanContextRules` in `src/PropertyIntelligence.Rules/Dimensions/UrbanContextRules.cs`; score based on `CensusData.MedianIncomeGroup` (1–10 scale) and `PopulationDensity` (optimal density range); max 200pts additive
- [x] T034 [US1] Implement `PropertyAnalysisEngine` in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs` implementing `IPropertyAnalysisEngine`; create `ISessionFactory` singleton at startup; create `ISession` per request; assert `PropertyFact`; call `session.Fire()`; aggregate `ScoringFact` by dimension with `Math.Min(200, sum)`; compute proportional composite (`sum of available`) and `composite_max` (200 × N available); compute grade from `composite/max` percentage per grade mapping in `contracts/analyze-endpoint.md`; expose `RulesVersion` constant (e.g., `"1.0.0"`) for audit logging
- [x] T035 [US1] Implement `LlmExplainabilityService` in `src/PropertyIntelligence.Explainability/LlmExplainabilityService.cs` implementing `IExplainabilityService`; POST to `openrouter.ai/api/v1/chat/completions` (OpenAI-compatible format); headers: `Authorization: Bearer {OPENROUTER_API_KEY}`, `HTTP-Referer: https://property-intelligence.hashinaga.dev`, `X-Title: Property Intelligence`; body: `{model, messages, models: [primary, fallback], route: "fallback"}`; PT-BR prompt includes address, composite/max, all dimension scores, flags, unavailable providers; 10-second `HttpClient` timeout; return null on timeout/error (do NOT throw); log failure with correlation ID
- [ ] T036 [US1] Implement `POST /v1/property/analyze` endpoint in `src/PropertyIntelligence.Api/Endpoints/AnalyzeEndpoint.cs`; orchestrate: validate request → normalize address → enrich → analyze → explain → persist audit → return 200/422/503 per `contracts/analyze-endpoint.md`; support local/dev execution with a mock provider profile; attach correlation ID to all log entries; return 401 if auth middleware rejected
- [ ] T037 [US1] Implement audit persistence in `src/PropertyIntelligence.Api/Endpoints/AnalyzeEndpoint.cs`; upsert `property_addresses` (ON CONFLICT normalized address); INSERT `property_analyses` (append-only) including `llm_model` (value of `LLM_MODEL` env var) and `rules_version` (from `PropertyAnalysisEngine.RulesVersion`); after INSERT, backfill `data_provider_raw_logs SET analysis_id = :new_analysis_id WHERE address_id = :addr_id AND analysis_id IS NULL AND fetched_at >= :request_start`; all within a single DB transaction; log `analysis_id` at INFO level
- [ ] T038 [US1] Create mock runtime profile in `src/PropertyIntelligence.Api/appsettings.Mock.json` (or equivalent config source) and registry seed file(s); enable mock providers locally without changing endpoint or DI code
- [ ] T039 [US1] Register all services in `src/PropertyIntelligence.Api/Program.cs`: `ISessionFactory` singleton (NRules), `ISession` transient, all provider adapters plus `IProviderRegistry`, `CacheService` singleton, `PropertyEnrichmentModule` scoped, `PropertyAnalysisEngine` scoped, `LlmExplainabilityService` scoped, `PropertyIntelligenceDbContext` scoped; keep mock providers available for local/dev/test and wire enabled/disabled state from registry/config; configure OpenTelemetry with OTLP exporter pointing to `GRAFANA_OTLP_ENDPOINT`

**Checkpoint A**: `POST /v1/property/analyze` returns HTTP 200 end-to-end with mock providers only; persistence, cache behavior, warnings, and provider registry selection are validated. Frontend work may begin after this checkpoint.

---

## Phase 3B: User Story 1 — Real Public Sources Hardening

**Goal**: substituir os mocks gradualmente por fontes públicas/oficiais de São Paulo e do Brasil, mantendo os mocks disponíveis para desenvolvimento, testes e demos offline.

**Independent Test**: habilitar um subconjunto de providers públicos no registry e verificar que a análise continua funcional; providers reais podem coexistir com mocks e ser ativados por configuração.

### Contract Tests for US1 Real Sources (write first, confirm failing)

- [ ] T089 [P] [US1] Write provider contract test for `ViaCepAddressProvider` in `tests/PropertyIntelligence.Tests.Contract/ViaCepProviderTests.cs`; stub ViaCEP response and locality validation flow; confirm FAILS before implementation
- [ ] T090 [P] [US1] Write provider contract tests for mobility providers in `tests/PropertyIntelligence.Tests.Contract/TransitProvidersTests.cs`; cover official São Paulo transport sources first (SPTrans/GeoSampa/Metrô/CPTM) and Overpass only as fallback/supplement; confirm FAILS
- [ ] T091 [P] [US1] Write provider contract tests for public-data providers in `tests/PropertyIntelligence.Tests.Contract/PublicDataProvidersTests.cs`; cover ANA, IBGE, SSP-SP, CNES, INEP, and GeoSampa appreciation providers with deterministic fixtures; confirm FAILS

### Implementation for US1 Real Public Sources

- [ ] T092 [P] [US1] Implement `ViaCepAddressProvider` in `src/PropertyIntelligence.Providers/ViaCep/ViaCepAddressProvider.cs`; call `viacep.com.br/ws/{cep}/json/`, validate municipality/state via IBGE/local rules, and resolve coordinates from local/open São Paulo sources first; use public Nominatim only as low-volume fallback during MVP experimentation
- [ ] T093 [P] [US1] Implement `SpTransGeoSampaTransitProvider` in `src/PropertyIntelligence.Providers/Transit/SpTransGeoSampaTransitProvider.cs`; consume official/open São Paulo transport datasets (SPTrans GTFS, GeoSampa transport layers, Metrô/CPTM station layers where available); cache 7 days
- [ ] T094 [P] [US1] Implement `OverpassPoiFallbackProvider` in `src/PropertyIntelligence.Providers/Overpass/OverpassPoiFallbackProvider.cs`; use Overpass only as fallback/supplementary POI source for mobility/infrastructure gaps; cache 7 days
- [ ] T095 [P] [US1] Implement `AnaFloodRiskProvider` in `src/PropertyIntelligence.Providers/Ana/AnaFloodRiskProvider.cs`; execute PostGIS `ST_Intersects(geometry, ST_SetSRID(ST_MakePoint(:lng,:lat),4326))` against `flood_risk_zones`; return highest `risk_level` found (null if no zone); cache 30 days
- [ ] T096 [P] [US1] Implement `IbgeCensusProvider` in `src/PropertyIntelligence.Providers/Ibge/IbgeCensusProvider.cs`; execute PostGIS `ST_Intersects` against `census_sectors`; return `median_income_group`, `population_density`, `median_age`; cache 30 days
- [ ] T097 [P] [US1] Implement `CrimeDataProvider` in `src/PropertyIntelligence.Providers/Crime/CrimeDataProvider.cs`; query `crime_records WHERE municipality = :city AND year >= :cutoffYear`; sum counts by crime_type; return total crimes per 100k; cache 24 hours
- [ ] T098 [P] [US1] Implement `CnesHealthProvider` in `src/PropertyIntelligence.Providers/Cnes/CnesHealthProvider.cs`; execute PostGIS `ST_DWithin(location, ST_SetSRID(ST_MakePoint(:lng,:lat),4326)::geography, :radius)` against `health_facilities`; count by `facility_type` within 1km and 2km; cache 7 days
- [ ] T099 [P] [US1] Implement `InepSchoolProvider` in `src/PropertyIntelligence.Providers/Inep/InepSchoolProvider.cs`; PostGIS `ST_DWithin` against `school_records` within 1km and 2km; return nearest school IDEB score and count of IDEB ≥ 7.0 schools; cache 30 days
- [ ] T100 [P] [US1] Implement `GeoSampaZoneamentoProvider` in `src/PropertyIntelligence.Providers/GeoSampa/GeoSampaZoneamentoProvider.cs`; derive zoning permissiveness and land-use opportunity signals from official GeoSampa/zoneamento layers; cache 30 days
- [ ] T101 [P] [US1] Implement `TransitProjectsProvider` and/or `GeoSampaCadastroProvider` in `src/PropertyIntelligence.Providers/GeoSampa/`; expose confirmed future transit proximity and public cadastral/IPTU proxy fields for `appreciation`; cache 30 days
- [ ] T014 [US1] Run static dataset import scripts (**depends on T074–T077**): `sh data/import/ana_shapefile_import.sh` (ANA flood zones → PostGIS), `sh data/import/ibge_cnefe_import.sh` (census sectors), `sh data/import/inep_ideb_import.sh` (school records), `sh data/import/cnes_import.sh` (health facilities); verify row counts in each table
- [ ] T102 [US1] Implement SSP-SP monthly import in `data/import/ssp_sp_import.sh`; download latest official dataset from SSP-SP / Dados Abertos SP, parse municipality/year/month/crime_type/count columns, `INSERT INTO crime_records ... ON CONFLICT DO NOTHING` (idempotent), print imported row count, and document monthly execution in `data/import/README.md`

**Checkpoint B**: selected public/official providers replace mocks incrementally behind the same registry and test harness; mocks remain available as fallback for local/dev/test.

---

## Phase 4: User Story 2 — Dimensional Score Transparency (Priority: P2)

**Goal**: All 6 dimension `trend` fields accurate (improving/stable/worsening) based on correct historical windows; `risk_flags` and `opportunity_flags` carry specific business-rule values; `warnings` array and proportional composite verified for partial analyses.

**Independent Test**: Submit address in an area with known rising crime → verify `score.dimensions.security.trend == "worsening"` and `risk_flags` contains `"crime_trend_12m"`. Submit request with SSP-SP provider disabled → verify `warnings` array has one entry, `score.max == 800`, grade computed from 5/6 dimensions. Both tests independent of US3.

### Contract Tests for US2 (write first, confirm failing)

- [ ] T040 [P] [US2] Write unit test for trend calculation logic in `tests/PropertyIntelligence.Tests.Unit/TrendCalculationTests.cs`; start with mock and fixture-driven provider data (crime rising over 24 months → `worsening`; flat → `stable`; decreasing → `improving`) and confirm test FAILS before implementation
- [ ] T041 [P] [US2] Write unit test for proportional composite in `tests/PropertyIntelligence.Tests.Unit/ScoringEngineTests.cs`; simulate 1 unavailable enabled provider → verify `composite_max == 800`, grade uses `composite/800` percentage; confirm FAILS

### Implementation for US2

- [ ] T042 [US2] Add 24-month trend calculation to `CrimeDataProvider` in `src/PropertyIntelligence.Providers/Crime/CrimeDataProvider.cs`; query `crime_records` for last 24 months grouped by month; compute linear regression slope over `per_100k` values; slope > +5%/yr → `worsening`, slope < -5%/yr → `improving`, else `stable`; include `CrimeTrend` in returned `CrimeData`
- [ ] T043 [P] [US2] Add appreciation trend using public-source proxies in `GeoSampaZoneamentoProvider` and/or `TransitProjectsProvider`; if historical official data is insufficient, set MVP appreciation trend to `stable` and leave a follow-up TODO for multi-snapshot history
- [ ] T044 [P] [US2] Add environment trend to `AnaFloodRiskProvider` in `src/PropertyIntelligence.Providers/Ana/AnaFloodRiskProvider.cs`; at MVP, environment trend is always `stable` (ANA data updates rarely; accurate trend requires ≥2 import snapshots); add `// TODO: derive trend from flood_risk_zones import history once 2nd snapshot available` comment; ensure `FloodRiskData.Trend = TrendDirection.Stable`
- [ ] T045 [US2] Populate `DimensionScore.Trend` for all 6 dimensions in `PropertyAnalysisEngine` in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; map provider trend enums to `"improving"/"stable"/"worsening"` strings; null when dimension `status == unavailable`
- [ ] T046 [P] [US2] Implement `risk_flags` generation in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; rules: flood risk_level ≥ moderate → `moderate_flood_risk` / `high_flood_risk`; security trend = worsening → `crime_trend_12m`; mobility score < 80 → `low_mobility`; no hospital within 2km → `no_hospital_2km`
- [ ] T047 [P] [US2] Implement `opportunity_flags` generation in `src/PropertyIntelligence.Rules/PropertyAnalysisEngine.cs`; rules: confirmed future transit project within 1km → `metro_expansion_nearby`; GeoSampa zoning signal allows higher density → `zoning_upscale`; appreciation trend = improving → `appreciation_trend_up`; nearest school IDEB ≥ 7.0 within 1km → `school_excellence_1km`
- [ ] T048 [US2] Implement `warnings` array in `PropertyEnrichmentModule` in `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`; for each provider in `ProvidersUnavailable`, map to affected dimension name and generate PT-BR message per `AnalysisWarning` template in `contracts/analyze-endpoint.md`; pass warnings list to `PropertyAnalysis`
- [ ] T079 [US2] Add 24-month mobility trend to the preferred transit provider(s) in `src/PropertyIntelligence.Providers/Transit/`; compare current official transit-stop/station counts against prior cached or persisted snapshot data; use Overpass fallback metrics only when official sources are unavailable
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
- [ ] T061 Write integration test for full analysis pipeline in `tests/PropertyIntelligence.Tests.Integration/PropertyAnalysisIntegrationTests.cs`; use Testcontainers PostgreSQL (`postgis/postgis:16-3.4`) + Redis; run first against the mock provider profile, then add focused coverage for public-provider integration points (e.g. transit fallback/OpenRouter) via WireMock.Net; seed `crime_records` + `flood_risk_zones` where relevant; assert: 200 response, `property_analyses` row created, `data_provider_raw_logs` rows created
- [ ] T062 [P] Write integration test for graceful degradation in `tests/PropertyIntelligence.Tests.Integration/PropertyAnalysisIntegrationTests.cs`; configure the registry with `ssp_sp` enabled and WireMock.Net returning 503 for that provider; assert: HTTP 200 returned, `score.max == 800`, `warnings` has 1 entry, `providers_unavailable` contains `ssp_sp`
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
- **Mock MVP (Phase 3)**: Depends on Phase 2 — delivers the first end-to-end API path
- **Real Public Sources (Phase 3B)**: Depends on Phase 3 checkpoint A — incrementally replaces mocks behind the same registry
- **US2 (Phase 4)**: Depends on Phase 3 for core scoring/flags and on Phase 3B for real-provider trend hardening where applicable
- **US3 (Phase 5)**: Depends on Phase 3 checkpoint A (needs working API, mocks acceptable) — independent of Phase 4 timing
- **Polish (Phase 6)**: Depends on all desired stories being complete

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2 — first with mock providers, then with real public providers
- **US2 (P2)**: Starts after mock US1 is green — extends trend + flag logic in existing code
- **US3 (P3)**: Starts after mock US1 checkpoint A — pure frontend; does not require full real-data integration to begin

### Within Each Phase

- Test tasks (contract/unit) MUST be written and FAIL before implementation begins
- T078 (dimension rule tests) MUST be written and FAIL before T028–T033
- Mock provider implementations (T020–T025) are the first provider parallel block once T083–T086 are complete
- T074–T077 (import scripts) MUST be complete before T014 (run imports) in Phase 3B
- Real provider onboarding (T092–T101) is the second provider parallel block once Checkpoint A is green
- NRules dimension rules (T028–T033) are fully parallel once T027 (facts) + T078 (tests) complete
- Vue components (T051–T056) are partially parallel once T050 (API service) complete
- T073 (K3s deploy CI job) depends on T067 (cluster provisioned)

---

## Parallel Opportunities

### Phase 2 (Foundational) — parallel block after T009 (migrations)

```
T011 CacheService          T012 ApiKeyAuthMiddleware     T013 GET /health
T006 Domain value types    T007 Entity types             T008 PropertyProfile
T009 DB migrations         T083 Registry contracts       T084 Registry persistence/config
```

### Phase 3 (Mock MVP) — first provider parallel block

```
T020 MockAddressProvider        T021 MockMobilityProvider       T022 MockEnvironmentProvider
T023 MockSecurityProvider       T024 MockInfrastructureProvider T025 MockAppreciation/UrbanContextProvider
```

```
T028 SecurityRules         T029 MobilityRules            T030 InfrastructureRules
T031 EnvironmentRules      T032 AppreciationRules        T033 UrbanContextRules
```

### Phase 3B (Real Public Sources) — second provider parallel block

```
T092 ViaCepAddressProvider       T093 SpTransGeoSampaTransitProvider   T094 OverpassPoiFallbackProvider
T095 AnaFloodRiskProvider        T096 IbgeCensusProvider               T097 CrimeDataProvider
T098 CnesHealthProvider          T099 InepSchoolProvider               T100 GeoSampaZoneamentoProvider
T101 TransitProjects/CadastroProvider
```

---

## Implementation Strategy

### MVP First (Mock Slice)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: Mock Data MVP Slice
4. **STOP AND VALIDATE**: `POST /v1/property/analyze` returns 200 end-to-end with mocked data
5. Frontend demo can begin; real-source onboarding becomes incremental hardening

### Incremental Delivery

1. Setup + Foundational → infrastructure and provider registry ready
2. Phase 3 mock slice → core API working fast → MVP ✅
3. Phase 3B real public sources → replace mocks incrementally without endpoint churn
4. US2 → trends accurate, flags specific → richer analysis
5. US3 → frontend visible to recruiters → portfolio complete
6. Polish → tests green, CI passing → production-ready

---

## Notes

- `[P]` tasks = different files, no blocking dependencies on incomplete work
- Constitution Principle III (Test-First) requires: write test → confirm red → implement → confirm green
- Commit after each checkpoint (end of phase or user story completion)
- Do NOT merge US2 trend changes into US1 tasks — keep stories independently testable
- `data_provider_raw_logs` INSERT must happen even when the downstream analysis fails
- Provider add/remove operations should map to registry/config changes plus adapter-specific tests, not endpoint/schema rewrites
- Mock providers should remain available for local development, CI, demos, and outage fallback even after real public providers are onboarded
