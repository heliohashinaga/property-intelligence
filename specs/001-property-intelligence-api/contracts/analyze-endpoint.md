# Contract: POST /v1/property/analyze

**Version**: 1.0
**Date**: 2026-05-28

---

## Request

**Method**: `POST`
**Path**: `/v1/property/analyze`
**Content-Type**: `application/json`

### Headers

| Header | Required | Notes |
|---|---|---|
| `X-Api-Key` | ✅ Yes | Raw API key; validated against SHA-256 hash in DB |
| `Content-Type` | ✅ Yes | Must be `application/json` |
| `CF-Connecting-IP` | Auto | Set by Cloudflare; used as request_ip in audit log |

### Request Body

```json
{
  "address": "Rua Augusta, 1500, São Paulo"
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `address` | `string` | ✅ Yes | 5–300 characters; must contain at least a street name |

---

## Responses

**Provider-set note**: `providers_used` and `providers_unavailable` are dynamic
arrays derived from the currently enabled provider registry. The examples below
use the initial MVP registry, but clients MUST treat these arrays as variable
subsets rather than a fixed contractually frozen list.

### 200 OK — Full Analysis (all enabled providers available)

```json
{
  "address": {
    "normalized": "Rua Augusta, 1500 - Consolação, São Paulo - SP",
    "street": "Rua Augusta",
    "number": "1500",
    "neighborhood": "Consolação",
    "city": "São Paulo",
    "state": "SP",
    "postal_code": "01310100",
    "coordinates": {
      "latitude": -23.5563,
      "longitude": -46.6543
    }
  },
  "score": {
    "composite": 724,
    "max": 1000,
    "grade": "B+",
    "dimensions": {
      "security": {
        "score": 118,
        "max": 200,
        "trend": "stable",
        "status": "available"
      },
      "mobility": {
        "score": 185,
        "max": 200,
        "trend": "improving",
        "status": "available"
      },
      "infrastructure": {
        "score": 162,
        "max": 200,
        "trend": "stable",
        "status": "available"
      },
      "environment": {
        "score": 95,
        "max": 200,
        "trend": "worsening",
        "status": "available"
      },
      "appreciation": {
        "score": 104,
        "max": 200,
        "trend": "improving",
        "status": "available"
      },
      "urban_context": {
        "score": 60,
        "max": 200,
        "trend": "stable",
        "status": "available"
      }
    }
  },
  "risk_flags": ["moderate_flood_risk", "crime_trend_12m"],
  "opportunity_flags": ["metro_line6_nearby_2026", "zoning_upscale"],
  "insight": "A Rua Augusta tem mobilidade excepcional — metrô Consolação a 400m e 18 linhas de ônibus no raio de 500m. O score de segurança é médio: crimes patrimoniais cresceram 8% nos últimos 12 meses na região. O ponto de atenção é ambiental: o endereço está a 200m de uma área mapeada pela ANA como risco moderado de alagamento. A tendência de valorização é positiva — zoneamento permite até 24 andares e a Linha 6 do metrô deve passar a 600m em 2026.",
  "insight_unavailable": false,
  "warnings": [],
  "providers_used": ["viacep", "overpass", "ana_snirh", "ibge_census", "ssp_sp", "cnes", "inep", "iptu_api"],
  "providers_unavailable": [],
  "cached": false,
  "analysis_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "analyzed_at": "2026-05-28T10:23:00Z"
}
```

---

### 200 OK — Partial Analysis (some enabled providers unavailable)

When 3–5 of 6 dimensions have data. `score.max` reflects the reduced ceiling.
Grade derived from `composite / max` percentage. Disabled providers are omitted
from both arrays and do not count as unavailable.

```json
{
  "address": { "...": "same as above" },
  "score": {
    "composite": 540,
    "max": 800,
    "grade": "B+",
    "dimensions": {
      "security": {
        "score": null,
        "max": 200,
        "trend": null,
        "status": "unavailable"
      },
      "mobility": { "score": 185, "max": 200, "trend": "improving", "status": "available" },
      "infrastructure": { "score": 162, "max": 200, "trend": "stable", "status": "available" },
      "environment": { "score": 95, "max": 200, "trend": "worsening", "status": "available" },
      "appreciation": { "score": 104, "max": 200, "trend": "improving", "status": "available" }
    }
  },
  "risk_flags": ["moderate_flood_risk"],
  "opportunity_flags": ["metro_line6_nearby_2026"],
  "insight": "A análise de segurança não pôde ser realizada por indisponibilidade temporária dos dados. Nos demais aspectos avaliados, a Rua Augusta apresenta mobilidade excepcional e risco ambiental moderado.",
  "insight_unavailable": false,
  "warnings": [
    {
      "dimension": "security",
      "provider": "ssp_sp",
      "message": "Dados de criminalidade indisponíveis no momento. Score de segurança não incluído nesta análise."
    }
  ],
  "providers_used": ["viacep", "overpass", "ana_snirh", "ibge_census", "cnes", "inep", "iptu_api"],
  "providers_unavailable": ["ssp_sp"],
  "cached": false,
  "analysis_id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "analyzed_at": "2026-05-28T10:23:05Z"
}
```

---

### 400 Bad Request — Missing or empty address field

```json
{
  "error": "validation_error",
  "message": "O campo 'address' é obrigatório.",
  "field": "address"
}
```

---

### 401 Unauthorized — Missing or invalid API key

```json
{
  "error": "unauthorized",
  "message": "API key inválida ou ausente. Forneça um X-Api-Key válido."
}
```

No `WWW-Authenticate` header (not HTTP Basic/Bearer auth).

---

### 422 Unprocessable Entity — Address not recognizable

```json
{
  "error": "address_unresolvable",
  "message": "Não foi possível normalizar o endereço fornecido. Verifique se o CEP ou município está correto.",
  "candidates": []
}
```

When multiple candidate matches exist:
```json
{
  "error": "address_ambiguous",
  "message": "O endereço é ambíguo. Especifique o município.",
  "candidates": [
    "Rua Augusta, 1500 - Consolação, São Paulo - SP",
    "Rua Augusta, 1500 - Centro, Campinas - SP"
  ]
}
```

---

### 503 Service Unavailable — Fewer than 3 dimensions have data

```json
{
  "error": "insufficient_data",
  "message": "Análise indisponível: dados suficientes para menos de 3 das 6 dimensões foram obtidos. Tente novamente em instantes.",
  "providers_unavailable": ["ssp_sp", "cnes", "inep", "ana_snirh"]
}
```

---

## Validation Rules

| Rule | Behavior |
|---|---|
| `address` missing | 400 |
| `address` length < 5 or > 300 chars | 400 |
| `X-Api-Key` header missing | 401 |
| `X-Api-Key` does not match any active consumer | 401 |
| Address not resolvable via ViaCEP | 422 |
| Address matches multiple municipalities | 422 with `candidates` |
| < 3 dimensions have data after enrichment | 503 |

---

## Scoring Business Rules Summary

| Threshold | Grade |
|---|---|
| ≥ 90% of available max | A+ |
| 80–89% | A |
| 70–79% | B+ |
| 60–69% | B |
| 50–59% | C+ |
| 40–49% | C |
| 30–39% | D |
| < 30% | F |

**Grade is always computed as `composite / max` (not over 1000).**

---

## Known Risk Flag Values

| Flag | Meaning |
|---|---|
| `moderate_flood_risk` | Address within ANA moderate flood zone |
| `high_flood_risk` | Address within ANA high/critical flood zone |
| `crime_trend_12m` | Security dimension trend = worsening (24m window) |
| `low_mobility` | Mobility score < 80/200 |
| `no_hospital_2km` | No hospital within 2km radius |

## Known Opportunity Flag Values

| Flag | Meaning |
|---|---|
| `metro_expansion_nearby` | Future metro station within 1km (confirmed works) |
| `zoning_upscale` | Zoning class allows high-density development |
| `appreciation_trend_up` | Appreciation dimension trend = improving |
| `school_excellence_1km` | IDEB ≥ 7.0 school within 1km |
