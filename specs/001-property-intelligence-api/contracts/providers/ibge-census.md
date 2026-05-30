# Provider: IBGE Census (Censo 2022)

## Fonte
- **Origem**: IBGE — Instituto Brasileiro de Geografia e Estatística
- **Dataset**: Censo 2022 + CNEFE (Cadastro Nacional de Endereços para Fins Estatísticos)
- **Tipo**: Banco de dados local (PostGIS) — importado via `data/import/ibge_cnefe_import.sh`
- **Implementação**: `src/PropertyIntelligence.Providers/Ibge/IbgeCensusProvider.cs`

---

## Tabela PostgreSQL

```sql
CREATE TABLE census_sectors (
    id                   SERIAL PRIMARY KEY,
    geometry             GEOMETRY(MultiPolygon, 4326) NOT NULL,
    cd_setor             VARCHAR(15),               -- código do setor censitário IBGE
    median_income_group  INT,                       -- 1–5 (quintis de renda)
    population_density   DECIMAL(10, 2),            -- habitantes/km²
    median_age           DECIMAL(5, 2),             -- idade mediana
    working_age_pct      DECIMAL(5, 2)              -- % da população entre 15–64 anos
);

CREATE INDEX idx_census_geometry ON census_sectors USING GIST(geometry);
```

Arquivo de migração: `infra/migrations/004_census_sectors.sql`

---

## Query PostGIS

```sql
SELECT median_income_group,
       population_density,
       median_age,
       working_age_pct
FROM   census_sectors
WHERE  ST_Intersects(
           geometry,
           ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)
       )
LIMIT 1;
```

Retorna o setor censitário que contém o ponto do endereço.

---

## Mapeamento → domínio (`CensusData`)

| Campo SQL             | Campo domínio                | Notas                              |
|-----------------------|------------------------------|------------------------------------|
| `median_income_group` | `MedianIncomeGroup` (int?)   | Quintil 1 (menor) a 5 (maior)      |
| `population_density`  | `PopulationDensity` (double?)| Hab/km²; ótimo: 5.000–20.000/km²   |
| `median_age`          | *(não exposto no MVP)*       | Reservado para US2                 |
| `working_age_pct`     | `WorkingAgePct` (double?)    | % 15–64 anos                       |

### Escala de renda (`MedianIncomeGroup`)
| Grupo | Faixa aproximada (SM = salário mínimo) |
|-------|----------------------------------------|
| 1     | Até ½ SM                               |
| 2     | ½ a 1 SM                               |
| 3     | 1 a 2 SM                               |
| 4     | 2 a 5 SM                               |
| 5     | Acima de 5 SM                          |

---

## Importação dos dados

```sh
sh data/import/ibge_cnefe_import.sh
```

Requer:
- `ogr2ogr` (GDAL) para importar shapefile de setores
- CSV do Censo 2022 baixado do `ftp.ibge.gov.br`
- Join por `cd_setor` entre shapefile e CSV de renda

---

## Quirks e limitações

- **Ponto fora de setor**: endereços em áreas não cobertas (zonas rurais, municípios sem Censo) retornam `CensusData()` vazio → score `urban_context` neutro (60 pts)
- **Quintis calculados pelo importador**: o script de importação divide os setores em 5 grupos por renda mediana municipal — não é a escala nacional do IBGE
- **Densidade vs. área edificada**: `population_density` inclui área de parques e preservação, podendo subestimar a densidade urbana real
- **Censo 2022**: dados de 2022; re-importar quando IBGE publicar revisão

## Cache
- **TTL**: 30 dias
- **Chave**: `ibge_census:{SHA256(normalizedAddress)[..16]}`
