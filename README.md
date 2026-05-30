# Property Intelligence

> Plataforma aberta para análise e explicação multidimensional de imóveis brasileiros

**Property Intelligence** é uma plataforma que recebe um endereço do Brasil e retorna:
- Um score composto (0–1000) calculado em 6 dimensões: segurança, mobilidade, infraestrutura, risco ambiental, valorização e contexto urbano
- Um resumo explicativo em linguagem natural (PT-BR), gerado por IA
- Visualização dos resultados (dashboard) e integração via API REST

---

## Como funciona

- **Entrada**: endereço livre (ex: "Rua Augusta, 1500, São Paulo")
- **Saída**: score, breakdown por dimensão, flags de risco/oportunidade e explicação em português
- **Acesso**: via requisições REST (API) ou dashboard visual

---

## Exemplo de resposta da API

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
      "security": { "score": 118, "max": 200, "trend": "stable" },
      "mobility": { "score": 185, "max": 200, "trend": "improving" },
      "infrastructure": { "score": 162, "max": 200, "trend": "stable" },
      "environment": { "score": 95, "max": 200, "trend": "worsening" },
      "appreciation": { "score": 104, "max": 200, "trend": "improving" },
      "urban_context": { "score": 60, "max": 200, "trend": "stable" }
    }
  },
  "risk_flags": ["moderate_flood_risk", "crime_trend_12m"],
  "opportunity_flags": ["metro_line6_nearby_2026", "zoning_upscale"],
  "insight": "A Rua Augusta tem mobilidade excepcional (...)",
  "cached": false,
  "analyzed_at": "2026-05-28T10:23:00Z"
}
```

---

## Dimensões analisadas

| Dimensão         | O que mede                                    | Fontes principais               |
|------------------|-----------------------------------------------|---------------------------------|
| Segurança        | Crimes, tendência histórica                   | SSP-SP, ISP-RJ                  |
| Mobilidade       | Transporte público, walkscore                 | SPTrans GTFS, Overpass/OSM      |
| Infraestrutura   | Saúde, ensino, comércio                       | CNES, INEP, OSM                 |
| Ambiente         | Risco de enchentes/desastres                  | ANA SNIRH (PostGIS)             |
| Valorização      | Tendência de IPTU/zonações/obras              | IPTU API, dados municipais      |
| Contexto urbano  | Renda, densidade, faixas etárias, demografia  | IBGE Censo/CNEFE                |

---

## Stack

| Camada | Tecnologia |
|---|---|
| API | .NET 10 Minimal API (C# 14) |
| Dispatch CQRS | Dispatcher customizado (MIT, sem MediatR) |
| Regras | NRules |
| Banco | PostgreSQL + PostGIS |
| Cache | Redis |
| IA | OpenRouter — modelo via `LLM_MODEL` env var |
| Frontend | Vue.js + Chart.js + Leaflet |
| Dev local | .NET Aspire (orquestra API, Postgres, Redis) |
| Infra | OpenTofu (Hetzner + Cloudflare) + K3s |
| CI/CD | GitHub Actions |

---

## Execução Local

```sh
# Inicia API + PostgreSQL + Redis via .NET Aspire (dashboard em http://localhost:15000)
dotnet run --project src/PropertyIntelligence.AppHost

# Apenas a API (sem Aspire)
dotnet watch run --project src/PropertyIntelligence.Api

# Testes
dotnet test

# Tunnel Cloudflare (acesso externo — não necessário para dev local)
# CLOUDFLARE_TUNNEL_TOKEN=<token> docker compose -f infra/docker-compose.yml --profile tunnel up -d
```

---

## Documentação detalhada

- [specs/001-property-intelligence-api/spec.md](specs/001-property-intelligence-api/spec.md) — Especificação
- [AGENTS.md](AGENTS.md) — Guia para automação e colaboração via agentes de IA
- [specs/001-property-intelligence-api/plan.md](specs/001-property-intelligence-api/plan.md) — Plano de execução
- [specs/001-property-intelligence-api/data-model.md](specs/001-property-intelligence-api/data-model.md) — Modelo de dados

---

## Privacidade & LGPD

Esta plataforma trata **endereços** como dados pessoais (Lei nº 13.709/2018 — LGPD).
Os dados são usados exclusivamente para gerar scores imobiliários e não são
compartilhados com terceiros além dos provedores listados. Dados não são vendidos.
Para exercer seus direitos (acesso, correção, exclusão): `privacidade@[domínio]`.

Ver [.specify/memory/constitution.md](.specify/memory/constitution.md) — Princípio VI para as regras técnicas completas.

---

## Licença

MIT
