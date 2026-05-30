# Provider: CNES Health (DataSUS)

## Fonte
- **Origem**: CNES — Cadastro Nacional de Estabelecimentos de Saúde (DataSUS/Ministério da Saúde)
- **Tipo**: Banco de dados local (PostGIS) — importado via `data/import/cnes_import.sh`
- **Cobertura**: Todo o Brasil
- **Implementação**: `src/PropertyIntelligence.Providers/Cnes/CnesHealthProvider.cs`

---

## Tabela PostgreSQL

```sql
CREATE TABLE health_facilities (
    id            SERIAL PRIMARY KEY,
    name          VARCHAR(255),
    facility_type VARCHAR(50) NOT NULL,   -- ver tipos abaixo
    location      GEOMETRY(Point, 4326) NOT NULL,
    lat           DECIMAL(10, 7),
    lng           DECIMAL(10, 7)
);

CREATE INDEX idx_health_location ON health_facilities USING GIST(location);
```

Arquivo de migração: `infra/migrations/005_health_and_schools.sql`

### Tipos de estabelecimento (`facility_type`)
| Valor CNES      | Classificação no provider | Descrição                        |
|-----------------|---------------------------|----------------------------------|
| `HOSPITAL`      | `hospitalsWithin2km`      | Hospital geral ou especializado  |
| `UBS`           | `clinicsWithin2km`        | Unidade Básica de Saúde          |
| `APS`           | `clinicsWithin2km`        | Atenção Primária à Saúde         |
| `CLINICA`       | `clinicsWithin2km`        | Clínica médica                   |
| `UPA`           | `emergencyUnits2km`       | Unidade de Pronto Atendimento    |
| `EMERGENCIA`    | `emergencyUnits2km`       | Pronto-socorro                   |
| `SAMU`          | `emergencyUnits2km`       | Base do SAMU                     |

> ⚠️ **`UPA` pertence exclusivamente a `emergencyUnits2km`** — não deve ser contada
> em `clinicsWithin2km`. Bugfix aplicado em `fix(providers): corrigir 4 bugs`.

---

## Query PostGIS

```sql
SELECT facility_type,
       COUNT(*)::int AS cnt,
       MIN(ST_Distance(
           location::geography,
           ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography
       )) AS nearest_m
FROM   health_facilities
WHERE  ST_DWithin(
           location::geography,
           ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography,
           2000          -- raio 2km em metros (geography usa metros)
       )
GROUP  BY facility_type;
```

> `::geography` converte para tipo geográfico (esférico) — `ST_DWithin` em metros diretos.
> Sem o cast, `ST_DWithin` operaria em graus decimais (incorreto para raios em metros).

---

## Mapeamento → domínio (`HealthData`)

| Agrupamento SQL   | Campo domínio                  |
|-------------------|--------------------------------|
| `HOSPITAL`        | `HospitalsWithin2km`           |
| `UBS`/`APS`/`CLINICA` | `ClinicsWith2km`          |
| `UPA`/`EMERGENCIA`/`SAMU` | `EmergencyUnits2km`   |

### Impacto nas regras (`infrastructure`)
| Condição                        | Pontos |
|---------------------------------|--------|
| `HospitalsWithin2km >= 1`       | +80 pts |
| `ClinicsWith2km >= 1` (UBS/UPA) | +30 pts |
| `EmergencyUnits2km >= 1`        | +30 pts |

---

## Importação dos dados

```sh
sh data/import/cnes_import.sh
```

- **Fonte**: `datasus.gov.br` → CNES → Download → estabelecimentos por estado
- **Filtro**: apenas São Paulo (Estado) no MVP
- **Frequência**: trimestral (CNES atualiza cadastro trimestralmente)

---

## Quirks e limitações

- **Estabelecimentos desativados**: CNES inclui estabelecimentos suspensos; o importador deve filtrar por `tp_atividade = 'A'` (ativo)
- **Coordenadas incompletas**: ~15% dos estabelecimentos CNES não têm lat/lng — o importador deve descartar registros sem coordenadas
- **Tipo de estabelecimento**: a classificação CNES original usa códigos numéricos (`tp_unidade`); o importador normaliza para os strings usados acima

## Cache
- **TTL**: 7 dias
- **Chave**: `cnes:{SHA256(normalizedAddress)[..16]}`
