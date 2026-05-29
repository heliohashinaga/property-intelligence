# Implementation Plan: Property Intelligence API

**Branch**: `001-property-intelligence-api` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-property-intelligence-api/spec.md`

## Summary

Loccali is a REST API that accepts a free-text Brazilian address and returns a
multidimensional property risk/opportunity score (0–1000) across 6 dimensions,
with AI-generated natural language explanation in PT-BR. The system orchestrates
8 heterogeneous data providers (public APIs + locally-imported datasets) in
parallel via a typed `IDataProvider<T>` abstraction, computes dimensional scores
with NRules, and generates insights via OpenRouter (LLM gateway → Claude Haiku
or Gemini Flash, model-agnostic). A Vue.js frontend demo renders the radar chart
and score visually.

Technical approach: .NET 10 Minimal API + .NET Aspire (local dev orchestration)
→ PostgreSQL + PostGIS (geospatial data + audit logs) → Redis (provider-level
TTL caching) → Cloudflare WAF + Tunnel (ingress + rate limiting) → K3s on
Hetzner CX31 (production) → OpenTofu (IaC) → Grafana Cloud via OpenTelemetry
(observability) → Vue.js on Cloudflare Pages (frontend demo).

---

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**:
- `NRules` 0.9+ — rules engine for dimensional scoring
- `Npgsql` 8 + `Npgsql.NetTopologySuite` — PostgreSQL + PostGIS driver
- `StackExchange.Redis` 2.8 — Redis cache client
- `System.Net.Http.Json` — typed HTTP client for OpenRouter (OpenAI-compatible)
- `NetTopologySuite` — geometry types for flood-zone point-in-polygon queries
- `NetTopologySuite.IO.ShapeFile` — shapefile import (correct NuGet package)
- `Aspire.Hosting` — .NET Aspire AppHost for local dev service orchestration
- `OpenTelemetry.Exporter.Otlp` + `OpenTelemetry.Instrumentation.AspNetCore` — metrics/traces → Grafana Cloud
- `xUnit` + `Testcontainers` — integration tests with real DB/Redis containers
- `WireMock.Net` — provider contract tests (mock external HTTP + OpenRouter)

**Storage**:
- PostgreSQL 16 + PostGIS 3.4 (geospatial queries, audit log, imported datasets)
- Redis 7 (provider result cache, TTL per provider)

**Testing**: xUnit 2.x, Testcontainers.PostgreSql, Testcontainers.Redis,
WireMock.Net, FluentAssertions

**Target Platform**: Linux; local dev via .NET Aspire + Docker Compose; production on K3s (K3s v1.30) single-node cluster on Hetzner CX31 (4 vCPU, 8GB RAM, Ubuntu 24.04); exposed via Cloudflare Tunnel (cloudflared)

**Project Type**: Web service (REST API) + Vue.js frontend demo

**Performance Goals**:
- Non-cached analysis ≤ 8 seconds end-to-end
- Cached analysis ≤ 500 ms
- Providers called in parallel (not sequentially)

**Constraints**:
- All MVP data sources are free/public; no paid API dependency at launch
- Records are append-only (PropertyAnalysis table); no UPDATE/DELETE on audit rows
- Raw provider payloads stored before transformation (DataProviderRawLog table)
- Rate limiting delegated to Cloudflare WAF; API trusts `CF-Connecting-IP`
- LLM calls via OpenRouter (OpenAI-compatible); model configured via `LLM_MODEL`
  env var — no code change to swap models; dev uses free-tier Llama, prod uses Claude Haiku
- Infrastructure provisioned exclusively via OpenTofu (no manual cloud console changes)
- Staging database: Supabase free tier (PostgreSQL + PostGIS managed); production:
  K3s StatefulSet on Hetzner; dev: Docker Compose. All environments use the same
  `DATABASE_URL` env var — switching environments requires only a connection string change.

**Scale/Scope**: Portfolio project; initial target São Paulo (SP); ~hundreds of
requests/day expected at demo scale

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| I. Domain-First | Domain entities defined in spec before stack chosen? | ✅ Pass — PropertyAddress, PropertyAnalysis, DimensionScore, AnalysisWarning, ApiConsumer, DataProviderResult all defined in spec |
| II. Data Accuracy & Auditability | Every output traceable to input + provider version? Raw payloads stored before transform? | ✅ Pass — FR-008 mandates full audit log; DataProviderRawLog stores raw payloads; append-only records |
| III. Test-First | Are tests written before implementation? | ✅ Pass — Tasks phase will enforce red-green-refactor; contract tests defined in contracts/ before implementation |
| IV. API-First | Contract committed before development? | ✅ Pass — contracts/ generated in this plan phase; endpoint shape locked before coding starts |
| V. Observability | Structured JSON logs + correlation IDs + health endpoint? | ✅ Pass — FR-008 audit log; AGENTS.md mandates structured logs; health endpoint included in contracts |

**No violations. Proceeding to Phase 0.**

---

## Project Structure

### Documentation (this feature)

```text
specs/001-property-intelligence-api/
├── plan.md          ← this file
├── research.md      ← Phase 0 output
├── data-model.md    ← Phase 1 output
├── quickstart.md    ← Phase 1 output
├── contracts/
│   ├── analyze-endpoint.md   ← POST /v1/property/analyze
│   └── health-endpoint.md    ← GET /health
└── tasks.md         ← Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Loccali.Api/
│   ├── Endpoints/
│   │   ├── AnalyzeEndpoint.cs
│   │   └── HealthEndpoint.cs
│   ├── Middleware/
│   │   └── ApiKeyAuthMiddleware.cs
│   ├── Program.cs
│   └── appsettings.json
│
├── Loccali.Core/
│   ├── Domain/
│   │   ├── PropertyAddress.cs
│   │   ├── PropertyProfile.cs
│   │   ├── PropertyAnalysis.cs
│   │   ├── DimensionScore.cs
│   │   ├── AnalysisWarning.cs
│   │   └── ApiConsumer.cs
│   ├── Interfaces/
│   │   ├── IDataProvider.cs
│   │   ├── IPropertyAnalysisEngine.cs
│   │   ├── IExplainabilityService.cs
│   │   ├── IAddressNormalizer.cs
│   │   └── ICacheService.cs
│   └── Services/
│       └── PropertyEnrichmentModule.cs
│
├── Loccali.Providers/
│   ├── ViaCep/ViaCepProvider.cs
│   ├── Overpass/OverpassPoiProvider.cs
│   ├── Ana/AnaFloodRiskProvider.cs
│   ├── Ibge/IbgeCensusProvider.cs
│   ├── Crime/CrimeDataProvider.cs
│   ├── Cnes/CnesHealthProvider.cs
│   ├── Inep/InepSchoolProvider.cs
│   ├── Iptu/IptuApiProvider.cs
│   └── Shared/CacheService.cs
│
├── Loccali.Rules/
│   ├── Facts/
│   │   ├── PropertyFact.cs
│   │   └── ScoringFact.cs
│   └── Dimensions/
│       ├── SecurityRules.cs
│       ├── MobilityRules.cs
│       ├── InfrastructureRules.cs
│       ├── EnvironmentRules.cs
│       ├── AppreciationRules.cs
│       └── UrbanContextRules.cs
│
└── Loccali.Explainability/
    └── LlmExplainabilityService.cs      ← OpenRouter (OpenAI-compatible)

src/Loccali.AppHost/                      ← .NET Aspire local dev orchestrator
    └── Program.cs                        ← wires Api + Providers + Rules projects

tests/
├── Loccali.Tests.Contract/
│   ├── AnalyzeEndpointTests.cs    ← contract tests against real endpoint shape
│   ├── ViaCepProviderTests.cs     ← WireMock.Net stubs per provider
│   └── OverpassProviderTests.cs
│
├── Loccali.Tests.Integration/
│   ├── PropertyAnalysisIntegrationTests.cs
│   └── CrimeDataProviderIntegrationTests.cs
│
└── Loccali.Tests.Unit/
    ├── ScoringEngineTests.cs
    ├── AddressNormalizerTests.cs
    └── DimensionRulesTests.cs

frontend/
├── src/
│   ├── components/
│   │   ├── AddressInput.vue
│   │   ├── ScoreRadarChart.vue
│   │   ├── ScoreCard.vue
│   │   ├── FlagBadges.vue
│   │   └── InsightPanel.vue
│   ├── pages/
│   │   └── Home.vue
│   └── services/
│       └── loccaliApi.ts
├── index.html
└── vite.config.ts

infra/
├── docker-compose.yml             ← PostgreSQL+PostGIS, Redis, Cloudflared (dev)
├── docker-compose.override.yml    ← local dev port overrides
├── migrations/
│   ├── 001_initial_schema.sql
│   ├── 002_crime_records.sql
│   ├── 003_flood_risk_zones.sql
│   ├── 004_census_data.sql
│   └── 005_health_facilities.sql
├── tofu/                          ← OpenTofu IaC (provisions Hetzner + Cloudflare DNS)
│   ├── main.tf
│   ├── variables.tf
│   └── outputs.tf
├── k3s/                           ← K3s Kubernetes manifests (production)
│   ├── namespace.yaml
│   ├── loccali-api.yaml           ← Deployment + Service + HPA
│   ├── postgres.yaml              ← StatefulSet + PVC
│   ├── redis.yaml
│   └── cloudflared.yaml           ← DaemonSet → Cloudflare Tunnel
└── grafana/
    └── loccali-dashboard.json     ← Grafana Cloud dashboard (import JSON)

data/
└── import/
    ├── ssp_sp_import.sh           ← download + import SSP-SP CSV
    ├── ana_shapefile_import.sh    ← GDAL ogr2ogr → PostGIS
    ├── ibge_cnefe_import.sh
    └── inep_ideb_import.sh
```

**Structure Decision**: Web application pattern (backend API + Vue frontend).
Backend split into 6 projects: Api (thin endpoint layer), Core (domain +
interfaces), Providers (8 provider implementations), Rules (NRules scoring),
Explainability (OpenRouter LLM integration), AppHost (.NET Aspire local
orchestrator). Separation enforces the Domain-First principle and makes each
provider independently testable. Production deployment managed by K3s manifests
under `infra/k3s/`, provisioned via OpenTofu in `infra/tofu/`.

---

## Complexity Tracking

> No constitution violations requiring justification.
