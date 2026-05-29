<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/001-property-intelligence-api/plan.md`.

Key artifacts:
- Plan: `specs/001-property-intelligence-api/plan.md`
- Spec: `specs/001-property-intelligence-api/spec.md`
- Research: `specs/001-property-intelligence-api/research.md`
- Data model: `specs/001-property-intelligence-api/data-model.md`
- Contracts: `specs/001-property-intelligence-api/contracts/`
- Quickstart: `specs/001-property-intelligence-api/quickstart.md`
<!-- SPECKIT END -->

# Property Intelligence — Agent Guidance

**This repository mandates multi-agent as the development default.**

- All significant implementation, refactor, test, and release tasks SHALL be executed using multi-agent delegation (parallel agents in isolated git worktrees, coordinated by the orchestrating session).
- No single-agent feature branches unless justified by unique coupling or strategic need; vertical slices and concurrency are the norm.
- Keep this instruction at the top — future contributors and agents MUST follow multi-agent as the workflow baseline.

## Multi-Agent Execution Standard

### Git Worktree is mandatory for parallel agent execution

Every agent working in parallel on this repository **MUST** operate in its own isolated git worktree.
This is the industry-established standard (adopted by Claude Code, OpenAI Codex, Cursor, and other agent harnesses).

**Why:**
- Eliminates file-level conflicts between concurrent agents sharing the same repo.
- Lightweight: shares the single `.git` object store — no disk bloat from full clones.
- Clean merge history: each worktree lives on its own branch; integration is straightforward.
- Universally supported: any agent that can run `git worktree add` can follow this standard.

### Worktree setup per agent/slice

```sh
# Create a worktree for a slice (run from repo root)
git worktree add ../property-intelligence-<slice-name> main

# Example: one worktree per provider
git worktree add ../property-intelligence-viacep main
git worktree add ../property-intelligence-overpass main
git worktree add ../property-intelligence-ana main

# List active worktrees
git worktree list

# Remove after merge
git worktree remove ../property-intelligence-<slice-name>
```

### Parallel agent dispatch — tool-agnostic pattern

The orchestrating session creates one worktree per slice, then delegates:

```sh
# Orchestrator: create worktrees and hand off tasks
git worktree add ../property-intelligence-viacep main    # → agent A
git worktree add ../property-intelligence-overpass main  # → agent B
git worktree add ../property-intelligence-ana main       # → agent C

# Each agent works in its own directory; no shared mutable state
# When done: gh pr create --fill --base main  (from within the worktree)
```

> **Note for pi users:** the `pi-subagents` extension supports this via `worktree: true`
> in the parallel task config. Any equivalent multi-agent tool works the same way.

### Known limitations & mitigations

| Issue | Mitigation |
|---|---|
| Port conflicts (dotnet watch, etc.) | Each agent uses a distinct port via env var |
| Shared Redis/Postgres in dev | Use Docker Compose with named containers; agents share infra but not workspace |
| `.env` collisions | Each worktree gets its own `.env` (gitignored); copy from `.env.example` on setup |

### Merge flow

1. Agent finishes work → commits to its own branch inside its worktree.
2. Orchestrator (main session or human) reviews diff.
3. PR created via `gh pr create --fill --base main` from within the worktree.
4. PR approved → merge → worktree removed.

---

This file is the runtime reference for AI agents working on this codebase.
Read it before making any changes. It supersedes ad-hoc guesses.

---

## Project Summary

**Property Intelligence** é uma plataforma aberta de análise e explicação multidimensional de imóveis brasileiros. O sistema atribui score de risco/oportunidade para endereços no Brasil em 6 dimensões (0–200 cada, totalizando 0–1000) e gera uma explicação natural automatizada em PT-BR via IA.

Diferencial: scoring multidimensional + explicação gerada por IA + fontes 100% públicas/gratuitas — tudo em um único endpoint REST.

---

## Constitution

The project constitution lives at `.specify/memory/constitution.md`.
**Read it before implementing any feature.** The five non-negotiable principles:

1. **Domain-First** — Define domain entities in specs before picking tech.
2. **Data Accuracy & Auditability** — Append-only records; every output is traceable.
3. **Test-First** — Tests written and failing before implementation begins.
4. **API-First** — Contracts committed to `specs/` before development starts.
5. **Observability** — Structured JSON logs + correlation IDs everywhere; no silent failures.

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | .NET 10 Minimal API (C#) |
| Rules Engine | NRules |
| Database | PostgreSQL + PostGIS |
| Cache | Redis |
| AI | OpenRouter (model via `LLM_MODEL` env var — swap without code changes) |
| Frontend | Vue.js + Chart.js + Leaflet |
| Dev infra | Docker Compose |
| CI/CD | GitHub Actions |
| Script runner | `sh` (POSIX shell) |

---

## Project Structure

```
property-intelligence/
├── src/
│   ├── PropertyIntelligence.Api/              # Minimal API — endpoints, middleware, auth
│   ├── PropertyIntelligence.Core/             # Domain entities, interfaces, engine contracts
│   ├── PropertyIntelligence.Providers/        # IDataProvider<T> implementations (one per source)
│   ├── PropertyIntelligence.Rules/            # NRules scoring rules per dimension
│   ├── PropertyIntelligence.Explainability/   # LlmExplainabilityService → OpenRouter
│   └── PropertyIntelligence.AppHost/          # .NET Aspire local dev orchestrator
├── tests/
│   ├── PropertyIntelligence.Tests.Contract/     # Provider contract tests (WireMock.Net)
│   ├── PropertyIntelligence.Tests.Integration/  # End-to-end API tests (Testcontainers)
│   └── PropertyIntelligence.Tests.Unit/         # Domain logic unit tests
├── frontend/                   # Vue.js 3 + Chart.js + Leaflet
├── infra/
│   ├── docker-compose.yml        # PostgreSQL+PostGIS, Redis, Cloudflared (dev)
│   ├── migrations/               # SQL schema migrations (001–005)
│   ├── tofu/                     # OpenTofu IaC (Hetzner VPS + Cloudflare DNS)
│   ├── k3s/                      # K3s manifests (prod: API, Postgres, Redis, Cloudflared)
│   └── grafana/                  # Grafana Cloud dashboard JSON
├── data/
│   └── import/                   # Scripts for SSP-SP CSV, ANA shapefiles, IBGE, INEP, CNES
├── specs/                      # Feature specifications (one dir per feature)
│   └── 001-property-intelligence-api/
│       └── spec.md
└── .specify/                   # Speckit workflow state
    ├── memory/constitution.md
    └── feature.json              # Active feature directory pointer
```

---

## Core Abstractions

### `IDataProvider<TResult>` (PropertyIntelligence.Core)

Every external data source is an isolated provider implementing this interface:

```csharp
public interface IDataProvider<TResult>
{
    string ProviderName { get; }
    TimeSpan CacheTtl { get; }
    Task<TResult> FetchAsync(PropertyAddress address, CancellationToken ct);
}
```

Providers run in parallel inside `PropertyEnrichmentModule`. Each result is
cached in Redis under a key of `{ProviderName}:{AddressHash}` with its own TTL.

### Provider TTL Reference

| Provider | Source | Cache TTL |
|---|---|---|
| `ViaCepProvider` | ViaCEP | 30 days |
| `OverpassPoiProvider` | OpenStreetMap / Overpass | 7 days |
| `AnaFloodRiskProvider` | ANA SNIRH (PostGIS local) | 30 days |
| `IbgeCensusProvider` | IBGE Censo 2022 / CNEFE | 30 days |
| `CrimeDataProvider` | SSP-SP CSV (local DB) | 24 hours |
| `CnesHealthProvider` | DataSUS / CNES | 7 days |
| `InepSchoolProvider` | INEP / IDEB | 30 days |
| `IptuApiProvider` | IPTU API (free tier) | 30 days |

### Scoring Dimensions

| Dimension | Scale | Key inputs |
|---|---|---|
| `security` | 0–200 | Crime rate per 100k, YoY trend |
| `mobility` | 0–200 | Transit stops within 500m/1km, walk score |
| `infrastructure` | 0–200 | Hospital, school, pharmacy counts within 2km |
| `environment` | 0–200 | Flood zone distance/severity (PostGIS) |
| `appreciation` | 0–200 | IPTU trend, zoning class, planned works |
| `urban_context` | 0–200 | Census income, density, age distribution |

Composite score = sum of all 6 dimensions (0–1000).
Grade mapping: 900–1000 → A+, 800–899 → A, 700–799 → B+, 600–699 → B,
500–599 → C+, 400–499 → C, 300–399 → D, 0–299 → F.

---

## API Contract

### `POST /v1/property/analyze`

**Auth**: `X-Api-Key: <key>` header required on every request.

**Request**:
```json
{ "address": "Rua Augusta, 1500, São Paulo" }
```

**Success (200)**:
```json
{
  "address": {
    "normalized": "Rua Augusta, 1500 - Consolação, São Paulo - SP",
    "coordinates": { "lat": -23.556, "lng": -46.654 }
  },
  "score": {
    "composite": 724,
    "grade": "B+",
    "dimensions": {
      "security":       { "score": 118, "max": 200, "trend": "stable" },
      "mobility":       { "score": 185, "max": 200, "trend": "improving" },
      "infrastructure": { "score": 162, "max": 200, "trend": "stable" },
      "environment":    { "score":  95, "max": 200, "trend": "worsening" },
      "appreciation":   { "score": 104, "max": 200, "trend": "improving" },
      "urban_context":  { "score":  60, "max": 200, "trend": "stable" }
    }
  },
  "risk_flags": ["moderate_flood_risk", "crime_trend_12m"],
  "opportunity_flags": ["metro_line6_nearby_2026", "zoning_upscale"],
  "insight": "...",
  "providers_used": ["viacep", "overpass", "ana_snirh", "ssp_sp", "cnes", "inep", "iptu_api"],
  "cached": false,
  "analyzed_at": "2026-05-28T10:23:00Z"
}
```

**Error responses**:

| Code | When |
|---|---|
| 401 | Missing or invalid API key |
| 422 | Address unrecognized or too ambiguous to normalize |
| 503 | All critical providers unavailable |

Partial provider failures return 200 with `providers_unavailable` array — never
fail the full request because one source is down.

---

## Development Commands

```sh
# Start .NET Aspire — orchestrates PostgreSQL+PostGIS, Redis, API + live dashboard
# PostgreSQL: port 5432 (fixed) | Redis: port 6379 (fixed) | Dashboard: http://localhost:15000
dotnet run --project src/PropertyIntelligence.AppHost

# Cloudflare tunnel only (staging/public access — not needed for local dev)
# CLOUDFLARE_TUNNEL_TOKEN=<token> docker compose -f infra/docker-compose.yml --profile tunnel up -d

# Run the API only (without Aspire)
dotnet watch run --project src/PropertyIntelligence.Api

# Run all tests
dotnet test

# Run by layer
dotnet test tests/PropertyIntelligence.Tests.Unit        # fast, no I/O
dotnet test tests/PropertyIntelligence.Tests.Contract    # WireMock.Net stubs
dotnet test tests/PropertyIntelligence.Tests.Integration # Testcontainers — needs Docker

# Apply database migrations
sh infra/migrations/run.sh

# Provision Hetzner infrastructure (first time or after changes)
cd infra/tofu && tofu init && tofu apply

# Deploy to K3s (after tofu apply provides kubeconfig)
kubectl apply -f infra/k3s/
kubectl rollout status deployment/property-intelligence-api -n property-intelligence

# Import static datasets (one-time + on source updates)
sh data/import/ana_shapefile_import.sh
sh data/import/ibge_cnefe_import.sh
sh data/import/inep_ideb_import.sh
sh data/import/cnes_import.sh

# Import SSP-SP crime CSV (run monthly via cron)
sh data/import/ssp_sp_import.sh <path-to-csv>
```

---

## Speckit Workflow

All features follow this flow — never skip steps:

```
/speckit.specify  → specs/NNN-<name>/spec.md
/speckit.plan     → specs/NNN-<name>/plan.md + research.md + data-model.md
/speckit.tasks    → specs/NNN-<name>/tasks.md
/speckit.implement → implementation (task by task)
```

The active feature directory is always stored in `.specify/feature.json`.
Specs, plans, contracts, and tasks are committed alongside source code.

---

## Key Constraints for Agents

- **No credentials in code or specs.** Use environment variables.
- **Never mutate historical records.** Valuations and scores are append-only.
- **Raw provider payloads must be stored before transformation.** If a provider
  returns bad data, the original payload must be recoverable.
- **Every HTTP handler must emit a structured log entry** with `correlation_id`,
  `operation`, `duration_ms`, and outcome.
- **Tests before implementation** — write the test, confirm it fails, then implement.
- **LLM calls via OpenRouter are best-effort** — if insight generation fails,
  return the score with `insight: null`; do NOT fail the entire request.
  Model configured via `LLM_MODEL` env var — swap models without code changes.
- **PostGIS queries for geospatial data** — do not implement geometric logic
  in application code when a PostGIS function covers it.
- **Infrastructure as code only** — never provision or modify Hetzner/Cloudflare
  resources manually; all changes go through `infra/tofu/`.

---

## Environment Variables

```sh
# Database (dev: Docker Compose | staging: Supabase | prod: K3s StatefulSet)
DATABASE_URL=postgres://property_intelligence:property_intelligence@localhost:5432/property_intelligence

# Cache
REDIS_URL=redis://localhost:6379

# LLM via OpenRouter (openrouter.ai)
OPENROUTER_API_KEY=<secret>          # openrouter.ai/keys
LLM_MODEL=meta-llama/llama-3.1-8b-instruct:free  # dev/CI (free)
# LLM_MODEL=anthropic/claude-3-haiku  # production

# Data providers
IPTU_API_KEY=<secret>                # iptuapi.com.br (free tier)

# Auth
API_KEY_SALT=<random-32-chars>       # for hashing consumer API keys at rest

# Cloudflare Tunnel
CLOUDFLARE_TUNNEL_TOKEN=<secret>     # dash.cloudflare.com → Zero Trust → Tunnels

# Observability — Grafana Cloud OTLP
GRAFANA_OTLP_ENDPOINT=https://otlp-gateway-prod-sa-east-1.grafana.net/otlp
GRAFANA_OTLP_TOKEN=<secret>          # Grafana Cloud → My Account → API Keys
```

Set these in `.env` locally (`.env` is gitignored). In CI/CD use GitHub Secrets.
In K3s production, inject via Kubernetes Secrets (`kubectl create secret generic`).
