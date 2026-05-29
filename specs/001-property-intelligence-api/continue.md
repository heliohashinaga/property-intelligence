# Handoff — Phase 2 (Property Intelligence)

**Data**: 2026-05-29  
**Branch**: `feat/phase-2`  
**Estado**: Phase 2 completa (menos T014); Phase 3 não iniciada.

---

## O que foi feito nesta sessão

| Task | Status | Detalhe |
|---|---|---|
| Docker instalado e infra up | ✅ | Postgres+PostGIS + Redis via `docker compose` |
| Migrations aplicadas | ✅ | 001–005 criadas e aplicadas |
| AppHost implementado | ✅ | `AddPostgres(postgis)` + `AddRedis` + `AddProject<Api>` + `WaitFor` |
| API roda via Aspire | ✅ | `dotnet run --project src/PropertyIntelligence.AppHost` |
| Bugs EF Core corrigidos | ✅ | `Lat/Lng` ignore duplo; `RequestIp inet` incompatível |
| `GET /health` | ✅ | `{"status":"healthy","database":"healthy","redis":"healthy"}` |

---

## Para retomar

```sh
cd /home/helio/repos/property-intelligence
dotnet run --project src/PropertyIntelligence.AppHost
# Dashboard: https://localhost:17118
# Health:    http://localhost:5276/health
```

> **Nota**: O Aspire cria containers novos com porta aleatória a cada reinício.  
> As migrations persistem via volume Docker `property-intelligence-postgres-data`.

---

## Próxima task pendente

### T014 — Importar datasets estáticos (última da Phase 2)

```sh
sh data/import/ana_shapefile_import.sh    # zonas de risco ANA → flood_risk_zones
sh data/import/ibge_cnefe_import.sh       # setores censitários IBGE → census_sectors
sh data/import/inep_ideb_import.sh        # escolas INEP/IDEB → school_records
sh data/import/cnes_import.sh             # unidades de saúde CNES → health_facilities
```

Verificar contagem de linhas em cada tabela após importação.

**Dependências de T014**: T074–T077 (scripts já criados ✅)

---

## Início da Phase 3 (após T014)

**Meta**: `POST /v1/property/analyze` retorna score 0–1000 + insight PT-BR.

Ordem sugerida (paralelas marcadas com [P]):
- T015, T016, T017 — contract tests (escrever primeiro, confirmar failing)
- T018–T021 — providers ViaCep, Overpass, ANA, IBGE [P]
- T022–T027 — demais providers + scoring engine
- T028–T035 — NRules dimensions + explainability
- T039 — wiring final no `Program.cs`

---

## Arquivos modificados nesta sessão

- `src/PropertyIntelligence.AppHost/AppHost.cs` — orquestração Aspire
- `src/PropertyIntelligence.AppHost/PropertyIntelligence.AppHost.csproj` — pacotes + ref Api
- `src/PropertyIntelligence.Api/Program.cs` — dual connection string (Aspire + env var)
- `src/PropertyIntelligence.Core/Data/PropertyIntelligenceDbContext.cs` — fixes EF Core mapping
- `.env` — variáveis locais de dev (gitignored)
