# Research: Property Intelligence API

**Date**: 2026-05-28
**Phase**: 0 — Technology & Integration Research

---

## 1. NRules — Multidimensional Scoring Engine

**Decision**: Use NRules forward-chaining engine with additive `ScoringFact`
pattern; one rules class per dimension; aggregate facts at engine session end.

**Rationale**: NRules is the most mature .NET rules engine with active
maintenance. The additive scoring pattern (multiple rules each contribute
partial points) is idiomatic NRules and avoids the ordering problems of
priority-based overwriting.

**Pattern**:

```csharp
// Fact injected into the session
public record PropertyFact(PropertyProfile Profile);

// Each rule produces one or more ScoringFacts
public record ScoringFact(string Dimension, int Points, string Reason);

// Example rule
[Name("mobility-metro-500m")]
public class MobilityMetro500mRule : Rule
{
    public override void Define()
    {
        PropertyFact fact = null;
        When().Match(() => fact,
            f => f.Profile.Pois.MetroStationsWithin500m >= 1);
        Then().Do(ctx =>
            ctx.Insert(new ScoringFact("mobility", 50, "Metro within 500m")));
    }
}

// Aggregation after session.Fire()
var scores = session.Query<ScoringFact>()
    .GroupBy(f => f.Dimension)
    .ToDictionary(g => g.Key, g => Math.Min(200, g.Sum(f => f.Points)));
```

**NRules session lifecycle per request**: Create `ISessionFactory` once at
startup (expensive); create lightweight `ISession` per analysis request
(cheap). Register as singleton + transient respectively in DI.

**Alternatives considered**:
- Drools (Java) — wrong runtime
- Hand-coded if/else scoring — not maintainable as rules grow
- Decision tables (Excel) — harder to test and version-control

---

## 2. PostGIS + Npgsql in .NET 10

**Decision**: `Npgsql.EntityFrameworkCore.PostgreSQL` + `NetTopologySuite`
plugin for spatial types. Raw SQL for complex geospatial queries.

**Rationale**: EF Core + NetTopologySuite gives typed geometry properties
(Polygon, Point) and generates correct PostGIS SQL. Complex spatial queries
(ST_DWithin, ST_Intersects) written as raw SQL for readability and control.

**Key packages**:
```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite" Version="9.*" />
<PackageReference Include="NetTopologySuite" Version="2.*" />
```

**EF Core config**:
```csharp
options.UseNpgsql(connectionString, o => o.UseNetTopologySuite());
```

**Flood risk point-in-polygon query** (raw SQL for clarity):
```sql
SELECT risk_level FROM flood_risk_zones
WHERE ST_Intersects(geometry, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326))
ORDER BY risk_level DESC LIMIT 1;
```

**POI radius search** (via Overpass, not PostGIS — POI data comes from OSM
in real-time; only flood/census are local PostGIS data).

---

## 3. IDataProvider\<T\> — Parallel Orchestration Pattern

**Decision**: `Task.WhenAll` with per-provider `CancellationToken` timeout
(5s per provider by default, overridable via provider registry metadata);
capture exceptions individually; mark failed providers as unavailable rather
than bubbling exceptions.

```csharp
public async Task<PropertyProfile> EnrichAsync(PropertyAddress address, CancellationToken ct)
{
    var results = await Task.WhenAll(
        _providers.Select(async p =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            try { return (p.ProviderName, Result: await p.FetchAsync(address, cts.Token), Error: (Exception?)null); }
            catch (Exception ex) { return (p.ProviderName, Result: (object?)null, Error: ex); }
        })
    );
    // build PropertyProfile from results, recording unavailable providers
}
```

**Cache key**: `{provider_id}:{SHA256(normalizedAddress)[..16]}` stored in Redis.
TTL and timeout should come from provider registry metadata, not from a
hardcoded provider switch.

### 3.1 Address Normalization & Geocoding — Prefer Public/Local Sources

**Decision**: Use a layered strategy:
1. `ViaCEP` for CEP normalization and canonical address fields.
2. `IBGE Localidades API` for municipality/state validation.
3. For coordinate resolution, prefer **local/open datasets** for São Paulo
   (CNEFE/geocodebr, GeoSampa lote/logradouro layers when sufficient) and use
   public Nominatim only as a low-volume fallback during MVP experimentation.

**Rationale**: ViaCEP is free and simple for address normalization, but the
public Nominatim service is explicitly rate-limited and not a good long-term
production dependency for bulk or frequent geocoding. For a São Paulo-focused
MVP, local/open address bases are more sustainable and easier to swap.

**Free/public sources preferred**:
- `https://viacep.com.br/` — free CEP normalization
- `https://servicodados.ibge.gov.br/api/docs/localidades` — official locality validation
- `https://ipea.github.io/geocodebr/` — open geocoding over CNEFE/open address bases
- `https://www.gov.br/conecta/catalogo/apis/cep-codigo-de-enderecamento-postal` — government CEP/address API catalog entry (evaluate access friction before adopting)

**Operational note**: Do **not** rely on the public `nominatim.openstreetmap.org`
instance for sustained production traffic; OSM policy limits heavy/bulk use and
recommends self-hosting or alternative sources for power users.

---

## 4. Mobility Data — Official SP Sources First, Overpass as Fallback

**Decision**: For São Paulo, prefer official/open transport datasets first
(SPTrans + GeoSampa + Metrô/CPTM layers) and keep Overpass/OSM as a secondary
fallback/supplementary provider. Cache per provider TTL = 7 days.

**Query pattern** (metrô within 500m):
```
[out:json][timeout:10];
(
  node["railway"="station"](around:500,-23.556,-46.654);
  node["subway"="yes"](around:500,-23.556,-46.654);
);
out count;
```

**Official/free sources preferred for São Paulo**:
- SPTrans developer/data portal and GTFS/open stop datasets
- GeoSampa transport layers (`pontos de ônibus`, `linhas de ônibus`, Metrô/CPTM station layers, project station layers)
- Metrô/CPTM layers exposed through GeoSampa metadata/WFS/WMS when available

**Overpass endpoint**: `https://overpass-api.de/api/interpreter` (public instance)
for MVP fallback/prototyping; for heavier usage prefer self-hosting or move more
queries to official local datasets.

**Radii used**: 500m (walking), 1km (short bike), 2km (transit catchment).
When using Overpass, query three radii in one request using union.

**Alternatives considered**: Google Places API (paid, unnecessary), HERE Maps
(paid), Foursquare (rate limits). Compared with pure Overpass, São Paulo's own
open transport datasets are easier to govern, easier to test, and less fragile.

---

## 5. ANA SNIRH — Flood Risk Shapefile Import

**Decision**: Download ANA SNIRH "Áreas Suscetíveis a Enchentes e Inundações"
shapefile → import with `ogr2ogr` (GDAL) into PostGIS `flood_risk_zones` table.
Geometry stored as `SRID 4326` (WGS84). Re-import manually when ANA publishes
updates (infrequent — typically annual).

**Import command**:
```bash
ogr2ogr -f "PostgreSQL" \
  PG:"host=localhost dbname=property_intelligence user=property_intelligence" \
  areas_risco_enchente.shp \
  -nln flood_risk_zones \
  -t_srs EPSG:4326 \
  -lco GEOMETRY_NAME=geometry
```

**Source**: `https://geoserver.snirh.gov.br/` (WFS/WMS) or bulk download from
ANA open data portal.

**Query at runtime**: ST_Intersects(point, zone.geometry) returns risk_level.

---

## 6. IBGE Censo 2022 / CNEFE

**Decision**: Download IBGE Censo 2022 aggregated results by setor censitário
(CSV) + setor boundaries (shapefile from IBGE malha). Join on `cd_setor` to
produce a `census_sectors` PostGIS table with geometry + income/density data.

**Key IBGE Censo 2022 tables used**:
- `Domicílios01` — number of households per sector
- `Responsáveis01` — income distribution per sector (nominal income groups)
- `Pessoas01` — population age distribution
- Malha de setores censitários — shapefile with sector polygons

**Import workflow**: Download CSVs + shapefile → Python/bash join script →
PostgreSQL `census_sectors` table with PostGIS geometry.

**Runtime query**: `ST_Intersects(ST_MakePoint(lng, lat), sector.geometry)`
→ returns median income group, density, age profile for the sector.

---

## 7. SSP-SP Crime Data — Monthly Official Import

**Decision**: Automated monthly job (cron) downloads the latest official crime
dataset from SSP-SP / Dados Abertos SP (`estatistica`, `transparenciassp`, or
`Números sem Mistério`, whichever remains the most stable official distribution)
→ inserts into `crime_records` table. Existing records never updated — new month appended.

**CSV structure** (SSP-SP standard columns):
```
Município | Delegacia | Natureza | Jan | Fev | Mar | ... | Total | Ano
```

**Import script logic**:
1. Download CSV for current year from SSP-SP portal
2. Parse CSV → normalize natureza (crime type) to canonical enum
3. INSERT INTO crime_records with ON CONFLICT DO NOTHING (idempotent)
4. Compute per-100k rates using IBGE population estimates

**Trend computation**: Query `crime_records` for last 24 months grouped by
month → compute linear regression slope → positive slope = worsening,
negative = improving, near-zero = stable (threshold: ±5% per year).

---

## 8. INEP IDEB — School Quality Data

**Decision**: Download INEP IDEB results XLS/CSV (public download) → convert to CSV
→ import into `school_records` table. Prefer official latitude/longitude fields
when present. For missing coordinates, use a local/open geocoding workflow
(`geocodebr`/CNEFE or municipal address bases) instead of bulk use of the public
Nominatim service.

**IDEB fields used**: `NO_ESCOLA`, `NU_LATITUDE`, `NU_LONGITUDE`, `VL_OBSERVADO`
(IDEB score), `NU_ANO_SAEB` (reference year), `TP_DEPENDENCIA` (public/private).

**Refresh cadence**: INEP publishes IDEB every 2 years; import manually when
new results published.

---

## 9. CNES / DataSUS — Health Facilities

**Decision**: Download CNES (Cadastro Nacional de Estabelecimentos de Saúde)
CSV from DataSUS or Dados Abertos → import into `health_facilities` table with
lat/lng. Prefer HTTPS/open-data mirrors when operationally simpler than legacy FTP.

**Source**: `ftp.datasus.gov.br/dissemin/publicos/CNES/` (FTP) or
`https://dados.gov.br/dados/conjuntos-dados/` (HTTPS). Monthly update cadence.

**Fields**: CNES code, establishment name, type (hospital, UBS, UPA, etc.),
municipality, lat/lng.

---

## 10. Cloudflare Tunnel — Local Dev & Production

**Decision**: `cloudflared` Docker container in `docker-compose.yml` for local
tunnel during development. In production, `cloudflared` deployed as systemd
service on the host or as a separate container.

**docker-compose.yml snippet**:
```yaml
cloudflared:
  image: cloudflare/cloudflared:latest
  command: tunnel --no-autoupdate run --token ${CLOUDFLARE_TUNNEL_TOKEN}
  restart: unless-stopped
  depends_on:
    - api
```

**Cloudflare WAF rate limiting rule** (configured in Cloudflare dashboard):
- Match: URI path starts with `/v1/`
- Rate: 60 requests / minute per IP
- Action: Block (429)
- `CF-Connecting-IP` header trusted as real client IP in the API.

---

## 11. Redis TTL Caching Strategy

**Decision**: `StackExchange.Redis` with `IDatabase.SetAsync(key, value, ttl)`.
Cache key format: `{provider_id}:{addressHash}` where addressHash is first 16 chars
of SHA-256 of the normalized address string (lowercase, trimmed).

**TTL policy**: resolve TTL from provider registry metadata instead of a
hardcoded table in code. The MVP seed values can start as:

| Provider | TTL |
|---|---|
| viacep | 30 days |
| overpass | 7 days |
| ana_snirh | 30 days |
| ibge_census | 30 days |
| ssp_sp | 24 hours |
| cnes | 7 days |
| inep | 30 days |
| geosampa_zoneamento | 30 days |
| geosampa_iptu | 30 days |

**Cache miss → fetch → store → return** is the standard read-through pattern.
Cache hit → deserialize → return with `cached: true`.

---

## 12. OpenRouter — LLM Gateway for PT-BR Insight Generation

**Decision**: Call OpenRouter API (`openrouter.ai/api/v1/chat/completions`) using
standard OpenAI-compatible format via `HttpClient`. Wrapped in
`LlmExplainabilityService` implementing `IExplainabilityService`. Model
configured via `LLM_MODEL` env var — no code changes to switch providers.

**Rationale**: OpenRouter provides a single endpoint for multiple LLM providers
(Claude, Gemini, Llama) with OpenAI-compatible API, built-in fallback routing,
and unified billing. Eliminates Anthropic SDK dependency and enables free-tier
models for development.

**Model strategy**:

| Environment | Model | Cost | Use |
|---|---|---|---|
| Dev/test | `meta-llama/llama-3.1-8b-instruct:free` | Free | Local dev, CI |
| Staging | `google/gemini-flash-1.5` | $0.075/M input | Staging validation |
| Production | `anthropic/claude-3-haiku` | $0.25/M input | Best PT-BR quality |

**Implementation**:

```csharp
// appsettings.json
"Llm": {
  "BaseUrl": "https://openrouter.ai/api/v1",
  "Model": "anthropic/claude-3-haiku",
  "ApiKey": "" // from OPENROUTER_API_KEY env var
}

// LlmExplainabilityService.cs
var payload = new {
  model = _options.Model,
  messages = new[] {
    new { role = "user", content = BuildPrompt(analysis) }
  },
  // Fallback model if primary unavailable:
  models = new[] { _options.Model, "google/gemini-flash-1.5" },
  route = "fallback"
};
// POST openrouter.ai/api/v1/chat/completions
// Authorization: Bearer {OPENROUTER_API_KEY}
// HTTP-Referer: https://property-intelligence.hashinaga.dev  ← required by OpenRouter
// X-Title: Property Intelligence
```

**PT-BR prompt** (same as before; model-agnostic):
```
Você é um analista imobiliário brasileiro. Com base nos dados abaixo,
escreva um parágrafo explicando o score do imóvel em linguagem acessível.
Mencione pelo menos 2 dimensões pelo nome. Se alguma dimensão estiver
indisponível, mencione isso explicitamente.
...
```

**Timeout**: 10 seconds. On timeout/error → return `null` insight;
do NOT fail the entire request.

**Alternatives considered**:
- Direct Anthropic API — rejected: SDK-specific, single provider, harder to swap in dev
- Direct Gemini API — rejected: separate auth, worse PT-BR than Claude
- Local Ollama — rejected: resource-heavy on Hetzner CX31, latency unpredictable

---

## 15. .NET Aspire — Local Dev Orchestration

**Decision**: Add `PropertyIntelligence.AppHost` project (Aspire AppHost) that orchestrates
all .NET projects locally with service discovery, health dashboard, and
OpenTelemetry built-in. Docker Compose is kept only as an optional path for
infrastructure services (PostgreSQL, Redis, Cloudflared).

**Rationale**: Aspire eliminates manual URL configuration between services,
provides a live dashboard (traces, metrics, logs) during development, and emits
OpenTelemetry natively — same signals go to Grafana Cloud in production.

```csharp
// src/PropertyIntelligence.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);
var api = builder.AddProject<Projects.PropertyIntelligence_Api>("api");
builder.Build().Run();
```

**Developer workflow**: `dotnet run --project src/PropertyIntelligence.AppHost` →
Aspire dashboard at `http://localhost:15000` (traces, logs, health per service).

---

## 16. K3s on Hetzner + OpenTofu

**Decision**: K3s v1.30 single-node on Hetzner CX31 (4 vCPU, 8GB RAM, ~€12/mo).
Infrastructure provisioned with OpenTofu (`infra/tofu/`). K3s manifests in
`infra/k3s/` deployed via GitHub Actions on push to `main`.

**OpenTofu provisions**:
- Hetzner Cloud Server (CX31, Ubuntu 24.04, Falkenstein datacenter)
- Hetzner Firewall (allow 80/443/6443 inbound)
- Cloudflare DNS A record pointing to server IP
- Remote-exec provisioner: installs K3s + `cloudflared`

**K3s workload layout**:
```
property-intelligence namespace:
  Deployment: property-intelligence-api (2 replicas, rolling update)
  StatefulSet: postgres (1 replica, 20GB PVC)
  Deployment: redis (1 replica)
  DaemonSet: cloudflared (1 pod → Cloudflare Tunnel)
```

**CI/CD deploy step** (GitHub Actions):
```bash
kubectl set image deployment/property-intelligence-api \
  api=ghcr.io/heliomarpm/property-intelligence-api:${GITHUB_SHA}
```

**Hetzner vs alternatives**:
- AWS ECS/EKS: 5× cost for equivalent resources
- Fly.io: simpler but less control, no PostGIS support on free tier
- Railway: no persistent volumes for PostGIS

---

## 17. Grafana Cloud + OpenTelemetry

**Decision**: Export metrics and traces from the .NET API to Grafana Cloud via
OTLP exporter. Free tier: 10k active series, 50GB logs/month, 14-day retention.

**Package setup**:
```xml
<PackageReference Include="OpenTelemetry.Exporter.Otlp" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
```

**Registration in Program.cs**:
```csharp
builder.Services.AddOpenTelemetry()
  .WithMetrics(m => m
    .AddAspNetCoreInstrumentation()
    .AddOtlpExporter(o => o.Endpoint = new Uri(grafanaOtlpEndpoint)))
  .WithTracing(t => t
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());
```

**Key Property Intelligence metrics to instrument**:
- `property_intelligence.analysis.duration_ms` (histogram, by cached/uncached)
- `property_intelligence.provider.fetch_duration_ms` (histogram, by provider name)
- `property_intelligence.provider.cache_hit_total` (counter, by provider name)
- `property_intelligence.score.composite` (histogram, distribution of scores)
- `property_intelligence.insight.generation_ms` (histogram, OpenRouter latency)

**Grafana dashboard** (`infra/grafana/property-intelligence-dashboard.json`): panels for
request rate, p95 latency per endpoint, provider error rate, cache hit ratio,
score distribution by grade.

**Decision**: Three-layer testing:

1. **Unit tests** (`PropertyIntelligence.Tests.Unit`): Pure domain logic, NRules scoring
   rules, address normalizer. No I/O. Fast (<1s total).

2. **Contract tests** (`PropertyIntelligence.Tests.Contract`): Each provider tested against
   WireMock.Net stubs. Verifies provider correctly parses known API responses.
   Written BEFORE implementation (TDD).

3. **Integration tests** (`PropertyIntelligence.Tests.Integration`): Real PostgreSQL +
   Redis via Testcontainers. Tests full analysis pipeline end-to-end with
   Overpass and Claude mocked via WireMock.Net.

**Testcontainers setup**:
```csharp
var postgres = new PostgreSqlBuilder()
    .WithImage("postgis/postgis:16-3.4")
    .Build();
var redis = new RedisBuilder().Build();
await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
```

**Alternatives considered**: SQLite for tests — rejected because PostGIS
spatial queries don't work on SQLite; mocking the DB — rejected because
integration tests are the primary correctness signal for spatial queries.

---

## 14. Appreciation / Zoning Data — Prefer Official Public Sources

**Decision**: Prefer official/open municipal sources for `appreciation` inputs,
especially GeoSampa / Prefeitura de São Paulo / URBIS datasets, instead of a
commercial IPTU wrapper API.

**Preferred free/public sources**:
- GeoSampa / PMSP IPTU cadastral datasets and metadata
- GeoSampa zoning layers (`Mapa 01 - Perímetros das Zonas`, SISZON, lote/setor/quadra layers)
- URBIS / PMSP land-use and development datasets
- PDE / legislação urbana open datasets
- Future transit project layers from GeoSampa/Metrô for appreciation proxies

**Rationale**: Although `iptuapi.com.br` has a small free tier, it is still a
third-party commercial dependency. If the goal is easy add/remove and maximum
free/public provenance, municipal open data is a better fit.

**Scoring strategy adjustment**: For MVP, `appreciation` can be derived from
public proxies such as zoning permissiveness, proximity to confirmed future
transit, urban projects, and fiscal/cadastral data available from the city. If
historical valor venal is incomplete, prefer a simpler official-data score over
introducing a paid/closed dependency.
