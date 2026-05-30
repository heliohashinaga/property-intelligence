# Provider: ANA Flood Risk (SNIRH)

## Fonte
- **Origem**: ANA — Agência Nacional de Águas e Saneamento Básico
- **Dataset**: SNIRH (Sistema Nacional de Informações sobre Recursos Hídricos) — Zonas de risco de inundação
- **Tipo**: Banco de dados local (PostGIS) — importado via `data/import/ana_shapefile_import.sh`
- **Implementação**: `src/PropertyIntelligence.Providers/Ana/AnaFloodRiskProvider.cs`

---

## Tabela PostgreSQL

```sql
CREATE TABLE flood_risk_zones (
    id          SERIAL PRIMARY KEY,
    geometry    GEOMETRY(MultiPolygon, 4326) NOT NULL,
    risk_level  VARCHAR(20) NOT NULL,   -- 'low' | 'moderate' | 'high' | 'critical'
    source_date DATE
);

CREATE INDEX idx_flood_risk_geometry ON flood_risk_zones USING GIST(geometry);
```

Arquivo de migração: `infra/migrations/003_flood_risk_zones.sql`

---

## Query PostGIS

```sql
SELECT risk_level
FROM   flood_risk_zones
WHERE  ST_Intersects(
           geometry,
           ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)
       )
ORDER BY CASE risk_level
    WHEN 'critical' THEN 1
    WHEN 'high'     THEN 2
    WHEN 'moderate' THEN 3
    WHEN 'low'      THEN 4
    ELSE 5
END
LIMIT 1;
```

Retorna o **nível mais grave** de zona que intersecta o ponto do endereço.

---

## Mapeamento → domínio (`FloodRiskData`)

| Resultado SQL  | `FloodRiskData.RiskLevel` | Score `environment` |
|----------------|---------------------------|---------------------|
| `NULL` (fora)  | `null`                    | 200 pts             |
| `"low"`        | `"low"`                   | 160 pts             |
| `"moderate"`   | `"moderate"`              | 100 pts             |
| `"high"`       | `"high"`                  | 40 pts              |
| `"critical"`   | `"critical"`              | 0 pts               |

---

## Importação dos dados

```sh
sh data/import/ana_shapefile_import.sh
```

Requer:
- `ogr2ogr` (GDAL) instalado
- Shapefile ANA SNIRH baixado manualmente de `snirh.gov.br`
- PostgreSQL acessível com `DATABASE_URL`

**Não há atualização automática** — re-importar quando ANA publicar nova versão do shapefile.

---

## Quirks e limitações

- **Requer coordenadas**: se `Lat`/`Lng` for `null`, retorna `FloodRiskData { RiskLevel = null }` sem query
- **Dados não importados**: sem dados na tabela, todos os pontos retornam `null` → score 200 (neutro)
- **Sem `DistanceMetres`** no MVP: campo reservado para US2 (Phase 4) que calculará distância à borda da zona
- **SRID 4326**: shapefile importado com reprojeção para WGS84

## Cache
- **TTL**: 30 dias
- **Chave**: `ana_snirh:{SHA256(normalizedAddress)[..16]}`
