# Data Inventory — Property Intelligence

<!-- Constitution v1.1.0 §VI Technical Standards -->
<!-- Update this file whenever a new data collection point is introduced -->
<!-- Legal basis: LGPD Art. 5º, I (dado pessoal) + Art. 37 (registro de operações) -->

**Version**: 1.0 | **Last Updated**: 2026-05-30
**Controller**: Property Intelligence
**DPO**: privacidade@[domínio]

---

## Personal Data Collected

| Dado | Categoria LGPD | Finalidade | Retenção | Base Legal |
|---|---|---|---|---|
| Endereço submetido (texto livre) | Pessoal não sensível (Art. 5º, I) | Normalização geocodificada + cálculo de score imobiliário | TTL cache: 24 h; audit log: indefinido (append-only, não deletável) | Art. 7º, II — execução de contrato/procedimentos preliminares |
| Coordenadas geográficas (lat/lng) | Pessoal de localização (Art. 5º, I) | Consultas geoespaciais aos provedores de dados (ANA, Overpass, IBGE) | Mesmo que endereço (derivado) | Art. 7º, II |
| CF-Connecting-IP (IP do consumidor) | Pessoal (Art. 5º, I) | Rate limiting via Cloudflare WAF; identificação de abuso | Não persistido pela aplicação; retido por Cloudflare conforme política deles | Art. 7º, II |
| API Key (hash SHA-256) | Pessoal — identificador (Art. 5º, I) | Autenticação e rastreamento de uso por consumidor | Hash armazenado indefinidamente; chave raw nunca persistida | Art. 7º, II |

---

## Data NOT Collected

O sistema **não trata** as seguintes categorias:

- ❌ Dados sensíveis (Art. 5º, II): origem racial, saúde, biometria, opinião política, religião, orientação sexual
- ❌ Dados pessoais do proprietário ou morador do imóvel
- ❌ CPF, nome, e-mail ou qualquer identificador pessoal além dos listados acima
- ❌ Dados de menores de idade
- ❌ Dados financeiros pessoais (IPTU é dado do imóvel, não da pessoa)

Se qualquer provedor externo retornar dados das categorias acima, eles **devem ser descartados
antes de qualquer persistência** (ver AGENTS.md §Key Constraints — LGPD).

---

## Data Flow

```
Consumidor (API Key)
    │
    ▼ POST /v1/property/analyze { "address": "..." }
    │
    ├─► AddressNormalizerService → ViaCEP / Nominatim (address only, no PII returned)
    │       └─ logs: address_hash (SHA-256, 16 chars) — NUNCA endereço em texto claro
    │
    ├─► PropertyEnrichmentModule → 8 providers in parallel
    │       └─ each provider: receives coordinates only (not raw address text)
    │
    ├─► PropertyAnalysisEngine → NRules scoring
    │
    └─► PostgreSQL: property_analyses (audit log, append-only)
            Fields stored: address_hash, coordinates, scores, flags, providers_used
            NOT stored: raw address text after normalization
```

---

## Retention Policy

| Dado | Retenção | Justificativa |
|---|---|---|
| Analysis cache (Redis) | 24 horas | FR-007 — performance |
| Provider cache (Redis) | 7–30 dias por provider | FR-007 — TTL por fonte |
| property_analyses (PostgreSQL) | Indefinido (append-only) | Auditabilidade — Constitution §II |
| data_provider_raw_logs (PostgreSQL) | Indefinido | Rastreabilidade de erros de provider |
| IP (CF-Connecting-IP) | Não persistido pela API | Minimização — Art. 6º, III |

---

## Rights of Data Subjects (Art. 18)

Prazo de resposta: **até 15 dias corridos** da requisição.
Canal: **privacidade@[domínio]**
Processo MVP: manual via e-mail (sem endpoint self-service).

| Direito | Implementação MVP |
|---|---|
| Confirmação e acesso | Manual — DPO consulta banco por api_consumer_id |
| Correção | Manual — DPO corrige dados cadastrais |
| Eliminação | Manual — DPO remove análises e hash de API key |
| Portabilidade | Manual — DPO exporta JSON das análises do consumidor |
| Revogação de consentimento | Desativação da API Key (base legal é Art. 7º, II — não consentimento) |

---

*Revisar trimestralmente ou ao introduzir novo ponto de coleta de dados pessoais.*
