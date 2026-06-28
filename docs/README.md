# Documentation Index

Quick navigation for Property Intelligence project documentation.

## 🚀 Getting Started

- **[Orchestration & Resource Management](../README.md#orchestration--resource-management)** — Detect your tier before delegating work
- **[ORCHESTRATION-GUIDE.md](../ORCHESTRATION-GUIDE.md)** — Quickstart + integration examples for all execution patterns
- **[AGENTS.md](../AGENTS.md)** — Multi-agent execution standards + orchestration rules

---

## 📚 Project Artifacts

### Core Specifications

| Document | Location | Purpose |
|----------|----------|---------|
| **Feature Spec** | `specs/001-property-intelligence-api/spec.md` | Functional requirements + API contracts |
| **Implementation Plan** | `specs/001-property-intelligence-api/plan.md` | Phases, phases dependencies, timelines |
| **Data Model** | `specs/001-property-intelligence-api/data-model.md` | Entity definitions, schema, provider reference |
| **Task Breakdown** | `specs/001-property-intelligence-api/tasks.md` | Granular task list (Phase 1–7, status tracking) |
| **API Contracts** | `specs/001-property-intelligence-api/contracts/` | OpenAPI specs, request/response examples |
| **Quickstart** | `specs/001-property-intelligence-api/quickstart.md` | First-run setup, local testing, basic usage |

### Strategy & Design

| Document | Location | Purpose |
|----------|----------|---------|
| **Orchestration Strategy** | `orchestration-strategy.md` (this dir) | Design rationale, tier decision trees, benchmarks |
| **Orchestration Guide** | `ORCHESTRATION-GUIDE.md` (root) | Practical quickstart, per-tier patterns, troubleshooting |
| **Multi-Agent Playbook** | `AGENTS.md` (root) | Worktree setup, acceptance contracts, operational rules |

### Completion Reports

| Document | Location | Purpose |
|----------|----------|---------|
| **T037–T039 Completion** | `COMPLETION-T037-T039.md` (root) | Status, metrics, lessons learned (Phase 3 checkpoint A) |
| **T037–T039 Plan** | `PLAN-T037-T039.md` (root) | Detailed task breakdowns, risk mitigation, git strategy |

---

## 🛠️ Configuration

| File | Purpose |
|------|---------|
| `orchestration.config.json` | Per-tier settings: timeouts, build flags, health check thresholds |
| `.env.example` | Environment variable template |
| `infra/docker-compose.yml` | Local dev: PostgreSQL + PostGIS + Redis + Cloudflared |

---

## 📜 Scripts

### System Detection & Validation

| Script | Location | Purpose |
|--------|----------|---------|
| **Detect Tier** | `scripts/detect-orchestration-tier.sh` | Auto-detect TIER=single/semi/full based on RAM + cores |
| **Health Check** | `scripts/pre-delegation-check.sh` | Validate memory, swap, disk, git state before delegation |

### Data Import

| Script | Location | Purpose |
|--------|----------|---------|
| **ANA Flood Zones** | `data/import/ana_shapefile_import.sh` | Import SNIRH shapefile to PostGIS |
| **IBGE Census** | `data/import/ibge_cnefe_import.sh` | Import sectors + demographics |
| **INEP Schools** | `data/import/inep_ideb_import.sh` | Import school + IDEB scores |
| **CNES Health** | `data/import/cnes_import.sh` | Import health facilities |
| **SSP-SP Crime** | `data/import/ssp_sp_import.sh` | Import monthly crime records (manual/cron) |

---

## 🎓 Developer Workflow

### Phase 3: Mock MVP (Complete ✅)

```
Tasks: T037–T039 (Phase 3 checkpoint A)
├─ T037: Audit persistence (DbContext transactions)
├─ T038: Mock config (appsettings.Mock.json)
└─ T039: DI wiring (service registrations)

Orchestration: TIER=single (sequential, 16 min, 3.5 GB peak)
Reference: PLAN-T037-T039.md, COMPLETION-T037-T039.md
```

### Phase 3B: Real Public Providers (Next)

```
Tasks: T089–T102
├─ T089–T091: Provider contract tests (ViaCEP, Transit, ANA)
├─ T092–T101: Provider implementations (10 providers, 2–3 batches)
└─ T014, T102: Dataset imports + validation

Orchestration: TIER=single per batch (chain: planner → writer → reviewer)
Pattern: /skill:multi-agent-orchestration + /skill:adaptive-orchestration
```

### Phase 4+: Dashboard + Infrastructure

```
Phase 4: Dimensional trends + flags
Phase 5: Vue.js frontend
Phase 6: Integration tests + polish
Phase 7: OpenTofu IaC + K3s manifests
```

---

## 🔍 Skills & Resources

### Global Skills (pi)

- `/skill:adaptive-orchestration` — Tier detection, health checks, decision rules
- `/skill:multi-agent-orchestration` — Worktrees, chains, acceptance contracts
- `/skill:dev-toolbox` — CLI tools, PATH management, environment audit

---

## 📞 Quick References

### Tier Decision Flowchart

```
RAM available?
  ├─ < 2 GB    → FAIL (insufficient)
  ├─ 2–4 GB    → TIER=single (sequential)
  ├─ 4–8 GB    → TIER=semi (scout || worker)
  └─ ≥ 8 GB    → TIER=full (all parallel)

Cores?
  ├─ < 2       → Force TIER=single
  └─ ≥ 2       → Use RAM tier
```

### Before Every Delegation

```bash
./scripts/detect-orchestration-tier.sh   # Detect tier
./scripts/pre-delegation-check.sh        # Validate system
# Proceed with appropriate tier pattern
```

### Performance Benchmarks (This Project)

| Tier | Concurrency | Duration | Peak Memory | For |
|------|-------------|----------|-------------|-----|
| Single | 1 | 16 min | 3.5 GB | Notebook (3–4 GB) |
| Semi | 2 | 12 min | 4.0 GB | Medium (4–8 GB) |
| Full | 3+ | 7 min | 7.8 GB | CI (11+ GB) |

---

## 📋 Maintenance

### When to Update Docs

- After completing a phase checkpoint → update `tasks.md`, create completion report
- After discovering resource constraints → update `orchestration.config.json`, note in AGENTS.md
- Before Phase 3B starts → verify all task descriptions in `tasks.md` align with /skill:multi-agent-orchestration best practices
- After adding new providers → update `data-model.md` provider reference table

---

## 🗂️ Full Directory Map

```
property-intelligence/
├── README.md                              ← Start here
├── AGENTS.md                              ← Multi-agent rules + orchestration
├── ORCHESTRATION-GUIDE.md                 ← Quickstart + examples
├── PLAN-T037-T039.md                      ← Phase 3 detailed plan
├── COMPLETION-T037-T039.md                ← Phase 3 completion report
├── orchestration.config.json              ← Tier configuration
├── docs/
│   ├── README.md                          ← This file
│   └── orchestration-strategy.md          ← Design + rationale
├── specs/001-property-intelligence-api/
│   ├── spec.md                            ← Feature spec
│   ├── plan.md                            ← Implementation plan
│   ├── data-model.md                      ← Domain model
│   ├── tasks.md                           ← Task breakdown
│   ├── research.md                        ← Background research
│   ├── quickstart.md                      ← First-run guide
│   └── contracts/                         ← API contracts
├── scripts/
│   ├── detect-orchestration-tier.sh
│   └── pre-delegation-check.sh
├── data/import/                           ← Dataset import scripts
├── src/                                   ← Source code
├── tests/                                 ← Test projects
└── infra/                                 ← Infrastructure (docker, tofu, k3s)
```

---

## 💡 Pro Tips

1. **Always run health checks first**: `./scripts/pre-delegation-check.sh`
2. **Use TIER=single on notebooks**: Sequential + cleanup is faster than swap thrashing
3. **Commit before finishing**: Worker agents must `git add -A && git commit`
4. **Check tasks.md for progress**: Source of truth (mark [x] after build + tests pass)
5. **Load skills in pi**: `/skill:adaptive-orchestration`, `/skill:multi-agent-orchestration`

---

**Last updated**: 2026-06-28 (Phase 3 checkpoint A)
