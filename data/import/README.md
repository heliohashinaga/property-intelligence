# Static Dataset Import Scripts

This directory contains shell scripts for importing public data into the Property Intelligence
PostgreSQL database. All imports are idempotent — safe to re-run without duplicating data.

## Scripts

| Script | Table | Expected Rows | Cadence | Command |
|---|---|---|---|---|
| `ana_shapefile_import.sh` | `flood_risk_zones` | ~50 000 polygons (SP basin) | One-time + on shapefile update | `sh data/import/ana_shapefile_import.sh` |
| `ibge_cnefe_import.sh` | `census_sectors` | ~300 000 sectors (SP state) | One-time + every census (~5 yr) | `sh data/import/ibge_cnefe_import.sh` |
| `inep_ideb_import.sh` | `school_records` | ~15 000 schools (SP state) | Annual (INEP publishes Oct) | `sh data/import/inep_ideb_import.sh` |
| `cnes_import.sh` | `health_facilities` | ~5 000 facilities (SP state) | Quarterly | `sh data/import/cnes_import.sh` |
| `ssp_sp_import.sh` | `crime_records` | ~100 000 rows/month | Monthly cron | `sh data/import/ssp_sp_import.sh` |

## Prerequisites

- `DATABASE_URL` environment variable set (or `.env` file at repo root)
- `psql` CLI (PostgreSQL client)
- `gdal-bin` / `ogr2ogr` — required for ANA shapefile import
- `curl` — required for auto-download mode of `ssp_sp_import.sh`

```sh
# Debian/Ubuntu
sudo apt-get install -y gdal-bin postgresql-client curl

# Check connection
psql "$DATABASE_URL" -c "SELECT PostGIS_Version();"
```

## Run order (first-time setup)

```sh
# 1. Apply SQL migrations first
sh infra/migrations/run.sh

# 2. Import static datasets (order does not matter — all tables are independent)
sh data/import/ana_shapefile_import.sh
sh data/import/ibge_cnefe_import.sh
sh data/import/inep_ideb_import.sh
sh data/import/cnes_import.sh

# 3. Import crime data (requires SSP-SP CSV)
sh data/import/ssp_sp_import.sh        # auto-downloads latest from SSP-SP portal
# or with explicit CSV:
sh data/import/ssp_sp_import.sh /path/to/ssp_sp_YYYY_MM.csv
```

## Monthly SSP-SP cron job

SSP-SP publishes updated crime statistics monthly. Set up a cron job to keep the data current:

```sh
# Edit crontab
crontab -e

# Add this line (runs at 08:00 on the 1st of each month)
0 8 1 * * cd /home/deploy/property-intelligence && sh data/import/ssp_sp_import.sh >> /var/log/pi-import.log 2>&1
```

## Verifying import counts

```sql
SELECT 'flood_risk_zones'  AS table_name, COUNT(*) FROM flood_risk_zones  UNION ALL
SELECT 'census_sectors',                  COUNT(*) FROM census_sectors     UNION ALL
SELECT 'school_records',                  COUNT(*) FROM school_records     UNION ALL
SELECT 'health_facilities',               COUNT(*) FROM health_facilities  UNION ALL
SELECT 'crime_records (ssp_sp)',          COUNT(*) FROM crime_records WHERE source = 'ssp_sp';
```

## Data sources

| Source | URL | License |
|---|---|---|
| ANA SNIRH (flood risk) | https://snirh.gov.br | Dados Abertos (Lei 12.527/2011) |
| IBGE CNEFE (census) | https://ibge.gov.br/cnefe | CC BY 4.0 |
| INEP IDEB (schools) | https://inep.gov.br/ideb | Dados Abertos |
| CNES DataSUS (health) | https://datasus.saude.gov.br | Dados Abertos |
| SSP-SP (crime) | https://ssp.sp.gov.br/estatistica | Dados Abertos (SP) |
