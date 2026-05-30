# Provider: Crime Data (SSP-SP)

## Fonte
- **Origem**: SSP-SP — Secretaria da Segurança Pública do Estado de São Paulo
- **Dataset**: Boletins de Ocorrência por município/mês/tipo de crime
- **Tipo**: Banco de dados local — importado via `data/import/ssp_sp_import.sh`
- **Cobertura**: Apenas municípios do Estado de São Paulo
- **Implementação**: `src/PropertyIntelligence.Providers/Crime/CrimeDataProvider.cs`

---

## Tabela PostgreSQL

```sql
CREATE TABLE crime_records (
    id          SERIAL PRIMARY KEY,
    municipality VARCHAR(100) NOT NULL,   -- nome do município (ex: "São Paulo")
    year         INT NOT NULL,
    month        INT NOT NULL,            -- 1–12
    crime_type   VARCHAR(50) NOT NULL,    -- ex: "HOMICIDIO_DOLOSO", "ROUBO_OUTROS"
    count        INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_crime_municipality_year ON crime_records(municipality, year);
```

Arquivo de migração: `infra/migrations/002_crime_records.sql`

---

## Query SQL

```sql
SELECT crime_type, SUM(count) AS total
FROM   crime_records
WHERE  municipality = :city
  AND  year >= :cutoffYear
GROUP  BY crime_type;
```

`cutoffYear = ano_atual - 2` — janela deslizante de 2 anos.

---

## Cálculo `CrimeRatePer100k`

```
total_crimes = SUM(count) de todos os crime_types
per100k = total_crimes / (populacao / 100_000)
```

**Populacao default**: 12.325.000 (São Paulo, IBGE 2022).  
Se `IbgeCensusProvider` disponível, usar `PopulationDensity × área_do_setor` como estimativa local.  
No MVP: constante por município.

---

## Mapeamento → domínio (`CrimeData`)

| Calculado               | Campo domínio                    |
|-------------------------|----------------------------------|
| `per100k`               | `CrimeRatePer100k` (double?)     |
| *(YoY trend — US2)*     | `YoyChangePct` — Phase 4         |

### Classificação de risco por `CrimeRatePer100k`
| Faixa            | Score `security` |
|------------------|------------------|
| `null`           | 100 pts (neutro) |
| < 50/100k        | 180 pts          |
| 50–100/100k      | 140 pts          |
| 100–200/100k     | 100 pts          |
| 200–300/100k     | 60 pts           |
| > 300/100k       | 20 pts           |

---

## Importação dos dados

```sh
sh data/import/ssp_sp_import.sh <caminho-do-csv>
```

- **Frequência**: mensal (SSP-SP publica novos CSVs todo mês)
- **Fonte**: `ssp.sp.gov.br/estatistica` → "Download de Dados"
- **Importação idempotente**: `INSERT ... ON CONFLICT DO NOTHING` por (municipality, year, month, crime_type)

---

## Quirks e limitações

- **Apenas SP**: municípios de outros estados retornam `CrimeData()` vazio → score neutro (100 pts)
- **Subnotificação**: dados de BO; crimes não registrados não constam
- **Granularidade municipal**: não há dados por bairro ou CEP — toda a cidade compartilha o mesmo índice
- **Lag de publicação**: SSP-SP publica com ~2 meses de atraso; os 2 meses mais recentes podem não estar disponíveis

## Cache
- **TTL**: 24 horas (dados mensais, mas novos CSVs chegam mensalmente)
- **Chave**: `ssp_sp:{SHA256(normalizedAddress)[..16]}`
