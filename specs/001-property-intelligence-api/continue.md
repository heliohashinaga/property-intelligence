# Handoff — Phase 3 (Property Intelligence)

**Data**: 2026-05-29
**Branch**: `feat/phase-3` (criada a partir de `feat/phase-2`)
**Estado**: Phase 2 ✅ completa; Phase 3 não iniciada.

---

## Como retomar

```sh
cd /home/helio/repos/property-intelligence
git checkout feat/phase-3
dotnet run --project src/PropertyIntelligence.AppHost
# Aspire dashboard: http://localhost:15000
# Postgres: localhost:5432  (fixo — WithHostPort)
# Redis:    localhost:6379  (fixo — WithHostPort)
# API health: http://localhost:<porta-aspire>/health
```

> As migrations persistem no volume Docker `property-intelligence-postgres-data`.
> Se o volume for perdido, reaplicar: `sh infra/migrations/run.sh`

---

## Phase 2 — resumo do que foi entregue

| Task | Detalhe |
|---|---|
| T005–T008 | Interfaces + domain entities + PropertyProfile |
| T009 | DbContext + EF Core ValueComparers (sem warnings de startup) |
| T010 | Migrations 001–005 aplicadas |
| T011 | CacheService + CacheKeyHelper |
| T012 | ApiKeyAuthMiddleware (X-Api-Key → SHA-256 → api_consumers) |
| T013 | `GET /health` → `{"status":"healthy","database":"healthy","redis":"healthy"}` ✓ |
| T058 | Structured JSON logging (correlation_id, operation, duration_ms) |
| T065 | Aspire AppHost — Postgres:5432 + Redis:6379 (portas fixas), pgAdmin, WaitFor |
| T066 | docker-compose reduzido a Cloudflare tunnel only (profile `tunnel`) |
| T074–T077 | Scripts de import ANA, IBGE, INEP, CNES |
| T014 | ⏸ Deferred — scripts prontos; executar quando arquivos-fonte externos disponíveis |
| Arch tests | `Tests.Architecture` — 30 testes, 29 passando (1 falho real: `IptuData` naming) |
| Docs | tasks.md, research.md, plan.md, AGENTS.md, quickstart.md — todos atualizados para Aspire-only |

---

## Phase 3 — próximas tasks

### Ordem de execução (TDD — testes primeiro)

```
1. T015 — Contract test POST /v1/property/analyze happy path (WireMock.Net)
2. T016 — Contract test ViaCepProvider
3. T017 — Contract test OverpassPoiProvider
   → Confirmar que todos falham antes da implementação

4. [Paralelo] Providers:
   T018 ViaCepProvider      T019 OverpassPoiProvider   T020 AnaFloodRiskProvider
   T021 IbgeCensusProvider  T022 CrimeDataProvider     T023 CnesHealthProvider
   T024 InepSchoolProvider  T025 IptuApiProvider

5. T026 PropertyEnrichmentModule  (orquestra providers em paralelo + cache)
6. T027 AddressNormalizerService  (ViaCEP + Nominatim fallback)

7. [Paralelo] NRules:
   T028 SecurityRules       T029 MobilityRules         T030 InfrastructureRules
   T031 EnvironmentRules    T032 AppreciationRules      T033 UrbanContextRules

8. T034 PropertyAnalysisEngine
9. T035 LlmExplainabilityService (OpenRouter)
10. T036 POST /v1/property/analyze endpoint
11. T037 Persist PropertyAnalysis + raw logs
12. T038 Integration tests (Testcontainers)
13. T039 Unit tests (engine + rules)
```

**Checkpoint Phase 3**: `POST /v1/property/analyze` → HTTP 200, 6 dimensões, composite score, grade, insight.

---

## Pendências conhecidas

| Item | Detalhe |
|---|---|
| `IptuData` naming | Classe em `Core.Domain` começa com `I`; arch test detecta como violação. Renomear para `IptuRecord` ou `IptuInfo` antes de Phase 3 |
| T014 imports | Requer `ogr2ogr` + arquivos ANA/IBGE/INEP/CNES — executar quando disponíveis |
| `run.sh` precisa de `psql` local | Usar `docker exec -i postgres psql ...` como alternativa (ver Makefile ou script wrapper) |

---

## Arquivos-chave

```
src/PropertyIntelligence.AppHost/AppHost.cs         ← orquestração Aspire
src/PropertyIntelligence.Core/Data/DbContext.cs      ← EF Core mappings + ValueComparers
src/PropertyIntelligence.Api/Program.cs              ← DI, middleware, endpoints
src/PropertyIntelligence.Core/Interfaces/            ← contratos de todos os serviços
src/PropertyIntelligence.Core/Domain/                ← entidades de domínio
infra/migrations/                                    ← 001–005 SQL + run.sh
tests/PropertyIntelligence.Tests.Architecture/       ← 30 arch tests (NetArchTest + Shouldly)
specs/001-property-intelligence-api/tasks.md         ← todas as tasks com status
progress.md                                          ← status por fase
```
