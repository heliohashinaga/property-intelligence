# Feature Specification: Property Intelligence

**Feature Branch**: `001-property-intelligence-api`

**Created**: 2026-05-28

**Status**: Draft

**Escopo**: Plataforma aberta para análise e explicação multidimensional de imóveis brasileiros. Recebe um endereço (input livre), retorna score composto (0–1000) em 6 dimensões (segurança, mobilidade, infraestrutura, risco ambiental, valorização, contexto urbano) e uma explicação gerada por IA, tudo acessível via API REST ou dashboard visual.

_Este projeto implementa uma Property Intelligence Platform: atribui score multidimensional e explicação automática para qualquer endereço de imóvel no Brasil, com integração via API e dashboard de visualização._

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Property Risk Analysis for a Single Address (Priority: P1)

A real estate professional, developer, or individual submits a Brazilian property
address through the API and receives a complete risk and opportunity analysis:
a composite score (0–1000) broken down across six dimensions, identified risk
and opportunity flags, and a natural language explanation of why the property
received that score — all in Brazilian Portuguese.

**Why this priority**: This is the core value proposition. Without it, the product
does not exist. Every other story extends this fundamental flow.

**Independent Test**: Send `POST /v1/property/analyze` with a valid São Paulo
address. Verify the response contains a composite score, six dimensional scores,
at least one insight paragraph in PT-BR, and an audit timestamp. No other feature
is needed to validate this story.

**Acceptance Scenarios**:

1. **Given** a valid Brazilian address string (e.g., "Rua Augusta, 1500, São Paulo"),
   **When** the consumer POSTs to `/v1/property/analyze` with a valid API key,
   **Then** the system returns HTTP 200 with a normalized address, coordinates,
   a composite score (0–1000), six dimensional scores (0–200 each), a letter
   grade (A+ to F), risk flags, opportunity flags, and a PT-BR insight paragraph.

2. **Given** the same address analyzed within the data cache window,
   **When** the consumer makes a second identical request,
   **Then** the system returns the cached result with `"cached": true` and
   a response time significantly faster than the first request.

3. **Given** an ambiguous or incomplete address (e.g., missing city),
   **When** the consumer POSTs the address,
   **Then** the system returns HTTP 422 with a clear, human-readable error
   explaining what is missing and how to correct the request.

4. **Given** a request without an API key or with an invalid key,
   **When** the consumer POSTs to `/v1/property/analyze`,
   **Then** the system returns HTTP 401 and does not perform any analysis.

---

### User Story 2 — Dimensional Score Transparency (Priority: P2)

A consumer wants to understand not just the total score but the per-dimension
breakdown with trend direction (improving / stable / worsening) so they can
identify which specific aspect of a property is its strength or weakness.

**Why this priority**: Without dimensional transparency, the composite score is
a black box. Dimension scores plus trend directions are what differentiate
Property Intelligence from raw data providers.

**Independent Test**: Inspect the `score.dimensions` object in the response.
Verify all 6 dimensions are present, each with `score`, `max`, and `trend`
fields. Verify `trend` is one of `improving`, `stable`, or `worsening`.

**Acceptance Scenarios**:

1. **Given** a successful analysis response,
   **When** the consumer reads `score.dimensions`,
   **Then** all six dimensions (`security`, `mobility`, `infrastructure`,
   `environment`, `appreciation`, `urban_context`) are present, each with
   a `score` (0–200), `max: 200`, and `trend` (improving/stable/worsening).

2. **Given** a property in an area with rising crime statistics,
   **When** the analysis completes,
   **Then** the `security.trend` field is `worsening` and the insight
   paragraph explicitly references the crime trend as a risk factor.

3. **Given** a property near a future metro station confirmed for 2026,
   **When** the analysis completes,
   **Then** `opportunity_flags` includes a flag related to transit expansion
   and the insight paragraph mentions the upcoming infrastructure benefit.

---

### User Story 3 — Visual Demo Frontend (Priority: P3)

A viewer of the portfolio (recruiter, technical lead) opens a web page, types
a Brazilian address, and immediately sees a visual representation of the analysis:
a radar chart of the 6 dimensions, a color-coded composite score with grade,
risk/opportunity flag badges, and the AI-generated explanation.

**Why this priority**: The API alone is invisible to non-technical evaluators.
A polished frontend demo is what makes the portfolio piece tangible and memorable.

**Independent Test**: Open the demo URL in a browser, submit "Rua Augusta, 1500,
São Paulo", and verify a radar chart renders with 6 axes, the composite score
displays with a letter grade, and the PT-BR insight is readable below the chart.
No backend or API knowledge is required to validate this story.

**Acceptance Scenarios**:

1. **Given** a user opens the demo web page,
   **When** they type a Brazilian address and submit,
   **Then** within 10 seconds a radar chart of 6 dimensions appears alongside
   the composite score, grade, risk/opportunity badges, and the PT-BR insight.

2. **Given** a valid analysis has rendered,
   **When** the user hovers over a dimension segment in the radar chart,
   **Then** the dimension name, score (e.g., "185/200"), and trend are displayed.

3. **Given** the API returns an error (e.g., unrecognized address),
   **When** the frontend receives the error,
   **Then** a clear, friendly error message is shown in PT-BR without exposing
   internal system details.

---

### Edge Cases

- What happens when a data provider is temporarily unavailable? The system must
  degrade gracefully — return partial scores using a **proportional composite**:
  the composite score is the sum of available dimensions only, and the `score.max`
  field reflects the adjusted ceiling (e.g., 5 available dimensions → max 800).
  The grade is derived from the available-dimension percentage. Unavailable
  dimensions appear in `score.dimensions` with `status: "unavailable"` and no
  numeric score. The `providers_unavailable` array lists each failed provider.
  A `warnings` array provides one human-readable PT-BR message per unavailable
  dimension/provider pair. The PT-BR insight generated by the AI MUST
  acknowledge missing dimensions when present.
  If fewer than 3 dimensions (50%) have data, the system returns HTTP 503
  instead of a partial score.
- What happens when an address matches multiple cities? The system must return
  the ambiguous-address error (HTTP 422) with candidate matches listed.
- What happens when the AI explanation service is unavailable? The score and
  flags must still be returned; `insight` may be `null` with an explanatory note.
- What happens when all data for a dimension comes from cache? The response
  must still be returned with `cached: true` and correct TTL metadata.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST accept a free-text Brazilian address and return a
  structured property risk analysis with composite and dimensional scores.
- **FR-002**: System MUST produce scores across exactly 6 dimensions
  (Security, Mobility, Infrastructure, Environmental Risk, Appreciation,
  Urban Context), each on a 0–200 scale, summing to a 0–1000 composite.
- **FR-003**: System MUST assign a letter grade (A+, A, B+, B, C+, C, D, F)
  to the composite score.
- **FR-004**: System MUST identify and return `risk_flags` and
  `opportunity_flags` derived from the enriched property data.
- **FR-005**: System MUST generate a natural language explanation in Brazilian
  Portuguese that references the key drivers behind the score.
- **FR-006**: System MUST authenticate every request via an API key passed
  in the request header (`X-Api-Key`). Rate limiting is enforced at the
  Cloudflare WAF layer before requests reach the API; the API itself MUST
  honour `CF-Connecting-IP` as the authoritative client IP and MUST NOT
  implement redundant in-process rate limiting.
- **FR-007**: System MUST cache enrichment data per source with TTLs
  appropriate to each data source's update frequency (shorter for volatile
  data like crime, longer for stable data like census figures).
- **FR-008**: System MUST persist an audit log for every analysis: input
  address, normalized address, all dimensional scores, flags, providers used,
  cache hit/miss status, timestamp, the **LLM model identifier** used to
  generate the insight (`llm_model`), and the **scoring rules version**
  (`rules_version`) active at time of analysis.
- **FR-009**: System MUST return a normalized address (structured street,
  number, neighborhood, city, state) and geographic coordinates (lat/lng)
  alongside every successful analysis.
- **FR-010**: System MUST degrade gracefully when individual data providers
  are unavailable: the composite score MUST be the sum of available dimensions
  only; `score.max` MUST reflect the reduced ceiling proportionally (200 × N
  available dimensions); the letter grade MUST be derived from the
  available-dimension percentage; unavailable dimensions MUST appear in
  `score.dimensions` with `status: "unavailable"`; `providers_unavailable`
  MUST list every provider that could not be reached; and a `warnings` array
  MUST contain one PT-BR human-readable message per unavailable dimension/
  provider pair. If fewer than 3 of 6 dimensions have data, the system MUST
  return HTTP 503 instead of a partial score.
- **FR-011**: When one or more dimensions are unavailable, the AI-generated
  insight MUST explicitly acknowledge the data gaps (e.g., "A análise de
  segurança não pôde ser realizada por indisponibilidade temporária dos dados.").
- **FR-012**: Trend direction (improving/stable/worsening) MUST be computed
  over a per-dimension historical window: `security` and `mobility` MUST use
  24 months of data; `infrastructure`, `environment`, `appreciation`, and
  `urban_context` MUST use 36 months. The system MUST store sufficient
  historical data per dimension to support these windows from the first
  production import.

### Key Entities

- **PropertyAddress**: Raw input string → normalized structured address +
  geographic coordinates.
- **PropertyProfile**: Aggregated enrichment data from all providers for a
  given address (POI counts, crime rates, census figures, flood zone status,
  IPTU history, etc.).
- **DimensionScore**: Score (0–200) + trend (improving/stable/worsening) +
  status (available/unavailable) for one of the 6 analysis dimensions.
  When status is `unavailable`, score and trend are absent. Trend is computed
  over a per-dimension historical window: `security` and `mobility` use
  24 months; `infrastructure`, `environment`, `appreciation`, and
  `urban_context` use 36 months.
- **PropertyAnalysis**: Composite score, grade, all 6 dimension scores, risk
  flags, opportunity flags, PT-BR insight, provider metadata, timestamp.
  This is the persisted audit record.
- **ApiConsumer**: A registered user identified by an API key; scope for
  usage tracking. Rate limiting and bot protection are enforced upstream
  by Cloudflare WAF rules, not by the API.
- **DataProviderResult**: Raw enrichment payload from a single external source,
  tagged with provider name, fetch timestamp, and TTL.
- **AnalysisWarning**: Human-readable PT-BR message surfaced when a provider
  is unavailable; contains `dimension`, `provider`, and `message` fields.
  Included in the `warnings` array of the API response.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A non-cached analysis for any address in **São Paulo (SP)**
  completes within 8 seconds from request receipt to response delivery.
- **SC-002**: A cached analysis for a previously analyzed address returns within
  500 milliseconds.
- **SC-003**: 100% of completed analyses are persisted in the audit log with all
  required fields; zero silent failures.
- **SC-004**: The PT-BR insight explicitly references at least 2 of the 6
  dimension scores by name in every successful response.
- **SC-005**: When one or more data providers are unavailable, the system still
  returns HTTP 200 with a proportional composite score at least 90% of the time;
  `score.max` accurately reflects the reduced ceiling and the grade is computed
  from the available-dimension percentage.
- **SC-006**: An independent evaluator (recruiter or technical lead) can open
  the demo frontend, submit an address, and read the full analysis result
  without consulting any documentation.

---

## Clarifications

### Session 2026-05-28

- Q: How is the composite score computed when dimension providers are unavailable? → A: Proportional composite — `score` sums only available dimensions; `score.max` reflects the reduced ceiling (200 × N available); grade derived from available-dimension percentage; unavailable dimensions appear with `status: "unavailable"` in `score.dimensions`.
- Q: What handles rate limiting for API consumers? → A: Cloudflare WAF (edge layer) — rate limiting rules defined in Cloudflare dashboard; API trusts `CF-Connecting-IP`; no in-process quota logic.
- Q: When is the SSP-SP crime CSV re-imported? → A: Automated monthly scheduled job — downloads latest CSV from SSP-SP and re-imports into PostgreSQL, matching SSP-SP's publication cadence.
- Q: What is the minimum dimension threshold for HTTP 200 vs 503 on provider failures, and should warnings be shown? → A: Minimum 3 of 6 dimensions required for HTTP 200; below that returns HTTP 503. Response includes a `warnings` array with one PT-BR message per unavailable dimension/provider pair; the AI insight also acknowledges missing dimensions explicitly.
- Q: What historical window is used to compute dimension trends? → A: Per-dimension — `security` and `mobility` use 24 months (captures recent shifts, filters seasonal noise); `infrastructure`, `environment`, `appreciation`, and `urban_context` use 36 months (aligned with long-term investment horizon).

---

## Assumptions

- The initial release targets addresses in São Paulo (SP) only; other Brazilian
  capitals will be added incrementally as data sources are validated per region.
- Authentication is a simple API key passed in an HTTP header (`X-Api-Key`);
  OAuth2 or JWT is out of scope for the MVP.
- The AI-generated insight is produced on demand per request; pre-generated or
  template-based explanations are explicitly out of scope.
- The system does not attempt to price or appraise the property (no Automated
  Valuation Model); it scores risk and opportunity dimensions only.
- Data from public sources (crime CSVs, census shapefiles, health registries,
  educational indexes) is imported periodically via background pipelines; the
  API does not call these sources in real-time — it queries local copies.
  Crime data (SSP-SP CSV) is refreshed by an automated monthly job that
  downloads the latest published CSV and re-imports it into PostgreSQL;
  the import cadence matches the SSP-SP publication schedule (monthly).
- Overpass API (OpenStreetMap POIs) and ViaCEP are called in real-time per
  request due to the dynamic nature of their data, protected by per-provider
  caching.
- The demo frontend is a read-only visualization layer; it does not require
  user accounts or persistence beyond the API response.
- Multi-tenancy (multiple API keys, per-consumer usage tracking) is supported
  at MVP scope; billing, self-service key management, and per-key quota
  configuration in Cloudflare are post-MVP.
- Rate limiting and bot/DDoS protection are delegated entirely to Cloudflare
  WAF; the API is not exposed directly to the public internet — traffic reaches
  it exclusively via Cloudflare Tunnel (`cloudflared`).
- Legal gray-area sources (ZAP/VivaReal listings, scraped portals) are
  explicitly excluded; all data sources in MVP are public or have free-tier
  agreements.
