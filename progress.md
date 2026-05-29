# Progress — Property Intelligence API

## Phase 1: Setup ✅

| Task | Status | Notes |
|---|---|---|
| T001 — Scaffold .NET 10 solution (9 projects) | ✅ Done | Committed + pushed |
| T002 — NuGet packages + project references | ✅ Done | Committed + pushed |
| T003 — docker-compose.yml + .env.example + migrations scaffold | ✅ Done | Committed + pushed |
| T004 — GitHub Actions CI pipeline | ✅ Done | Committed + pushed |

## Phase 2: Foundational ✅

| Task | Status | Notes |
|---|---|---|
| T005 — Interfaces (IDataProvider, IPropertyAnalysisEngine, IExplainabilityService, IAddressNormalizer, ICacheService) | ✅ Done | `Core/Interfaces/` |
| T006 — Domain value types (PropertyAddress, DimensionScore, AnalysisWarning) | ✅ Done | `Core/Domain/` |
| T007 — Entity types (PropertyAnalysis, ApiConsumer, DataProviderRawLog) | ✅ Done | `Core/Domain/` |
| T008 — PropertyProfile aggregate + provider sub-records | ✅ Done | `Core/Domain/PropertyProfile.cs` |
| T009 — PropertyIntelligenceDbContext | ✅ Done | `Core/Data/PropertyIntelligenceDbContext.cs` (+ EF Core ValueComparers) |
| T010 — SQL migrations (001–005) | ✅ Done | `infra/migrations/001–005.sql + run.sh` — applied |
| T011 — CacheService + CacheKeyHelper | ✅ Done | `Providers/Shared/` |
| T012 — ApiKeyAuthMiddleware | ✅ Done | `Api/Middleware/ApiKeyAuthMiddleware.cs` |
| T013 — GET /health endpoint | ✅ Done | `{"status":"healthy","database":"healthy","redis":"healthy"}` ✓ |
| T058 — Structured JSON logging middleware | ✅ Done | `Api/Program.cs` (JSON console + correlation ID) |
| T074 — ANA shapefile import script | ✅ Done | `data/import/ana_shapefile_import.sh` |
| T075 — IBGE CNEFE import script | ✅ Done | `data/import/ibge_cnefe_import.sh` |
| T076 — INEP IDEB import script | ✅ Done | `data/import/inep_ideb_import.sh` |
| T077 — CNES import script | ✅ Done | `data/import/cnes_import.sh` |
| T014 — Run static dataset imports | ⏸ Deferred | Scripts prontos; requer arquivos-fonte externos (ANA, IBGE, INEP, CNES) e `ogr2ogr` |

**Checkpoint verificado**: `GET /health → 200 {"status":"healthy","database":"healthy","redis":"healthy"}`

**Infra**: Aspire é o único orquestrador local (Postgres :5432, Redis :6379). docker-compose reduzido a Cloudflare tunnel (staging only).

---

## Phase 3: US1 — Property Risk Analysis (branch `feat/phase-3`) 🎯 MVP

### Contract Tests — TDD (escrever antes da implementação)

| Task | Status | Notes |
|---|---|---|
| T015 — Contrato `POST /v1/property/analyze` happy path | ⬜ Todo | `Tests.Contract/AnalyzeEndpointTests.cs` — WireMock.Net |
| T016 — Contrato ViaCepProvider | ⬜ Todo | `Tests.Contract/ViaCepProviderTests.cs` |
| T017 — Contrato OverpassPoiProvider | ⬜ Todo | `Tests.Contract/OverpassProviderTests.cs` |

### Implementação (paralela por slice)

| Task | Status | Notes |
|---|---|---|
| T018 — ViaCepProvider | ⬜ Todo | `Providers/ViaCepProvider.cs` |
| T019 — OverpassPoiProvider | ⬜ Todo | `Providers/OverpassPoiProvider.cs` |
| T020 — AnaFloodRiskProvider | ⬜ Todo | `Providers/AnaFloodRiskProvider.cs` (PostGIS) |
| T021 — IbgeCensusProvider | ⬜ Todo | `Providers/IbgeCensusProvider.cs` |
| T022 — CrimeDataProvider | ⬜ Todo | `Providers/CrimeDataProvider.cs` |
| T023 — CnesHealthProvider | ⬜ Todo | `Providers/CnesHealthProvider.cs` |
| T024 — InepSchoolProvider | ⬜ Todo | `Providers/InepSchoolProvider.cs` |
| T025 — IptuApiProvider | ⬜ Todo | `Providers/IptuApiProvider.cs` |
| T026 — PropertyEnrichmentModule | ⬜ Todo | `Core/` — orquestra providers em paralelo + cache |
| T027 — AddressNormalizerService | ⬜ Todo | `Api/` — ViaCEP + Nominatim fallback |
| T028–T033 — NRules (6 dimensões) | ⬜ Todo | `Rules/` — security, mobility, infrastructure, environment, appreciation, urban_context |
| T034 — PropertyAnalysisEngine | ⬜ Todo | `Core/` — EnrichmentModule + Rules + grade |
| T035 — LlmExplainabilityService | ⬜ Todo | `Explainability/` — OpenRouter |
| T036 — POST /v1/property/analyze endpoint | ⬜ Todo | `Api/Endpoints/AnalyzeEndpoint.cs` |
| T037 — Persist PropertyAnalysis + raw logs | ⬜ Todo | append-only, audit trail |
| T038 — Integration tests US1 | ⬜ Todo | `Tests.Integration/` — Testcontainers |
| T039 — Unit tests (engine + rules) | ⬜ Todo | `Tests.Unit/` |

**Checkpoint**: `POST /v1/property/analyze` → HTTP 200, 6 dimensões, composite score, grade, insight.

## Next

- Branch `feat/phase-3` a partir de `feat/phase-2`
- Multi-agent paralelo (worktrees por slice):
  - **Slice A** — Contract tests: T015, T016, T017
  - **Slice B** — Providers: T018–T025 + EnrichmentModule T026 + AddressNormalizer T027
  - **Slice C** — Rules + Engine: T028–T033 + T034
  - **Slice D** — Endpoint + Persistence + LLM: T035, T036, T037
  - **Slice E** — Tests: T038, T039
