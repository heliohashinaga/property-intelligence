# Data Import Scripts

This directory contains shell scripts for importing static and periodically-updated
datasets into the Property Intelligence PostgreSQL database.

All scripts require the `DATABASE_URL` environment variable to be set.

---

## One-Time / Infrequent Imports

These scripts import datasets that change rarely or are one-time setup tasks.

### ANA Flood Risk Zones — `ana_shapefile_import.sh`
```sh
sh data/import/ana_shapefile_import.sh
# or: ANA_SHAPEFILE=/path/to/SNIRH_Inundacao.shp sh data/import/ana_shapefile_import.sh
```
Imports ANA SNIRH flood risk shapefiles into the `flood_risk_zones` PostGIS table.

| | |
|---|---|
| **Source** | [SNIRH / ANA Metadados](https://metadados.snirh.gov.br) — shapefile download |
| **Table** | `flood_risk_zones` |
| **Expected rows** | ~5 000–30 000 (varies by release; nationwide polygon coverage) |
| **Re-run cadence** | On initial setup; re-import whenever ANA publishes updated flood maps |
| **Idempotent** | Yes — `ON CONFLICT DO NOTHING` |

### IBGE Census Sectors — `ibge_cnefe_import.sh`
```sh
sh data/import/ibge_cnefe_import.sh
```
Imports IBGE Censo 2022 / CNEFE sector-level socioeconomic data into `census_sectors`.

| | |
|---|---|
| **Source** | IBGE FTP / API — Censo 2022 sector boundaries + CNEFE attribute CSV |
| **Table** | `census_sectors` |
| **Expected rows** | ~500 000 (Censo 2022 has ~617 000 sectors nationally; São Paulo state ~70 000) |
| **Re-run cadence** | On initial setup; re-import after a new census (~every 10 years) |
| **Idempotent** | Yes — `ON CONFLICT DO NOTHING` on sector code |

### INEP School Records — `inep_ideb_import.sh`
```sh
sh data/import/inep_ideb_import.sh
```
Imports INEP school location and IDEB scores into `school_records`.

| | |
|---|---|
| **Source** | [INEP Microdados](https://www.gov.br/inep) — Censo Escolar + IDEB XLS/CSV |
| **Table** | `school_records` |
| **Expected rows** | ~220 000 (all active schools in Brazil) |
| **Re-run cadence** | Annually — INEP releases updated school census each year; IDEB scores update every 2 years |
| **Idempotent** | Yes — `ON CONFLICT DO NOTHING` on INEP school code |

### CNES Health Facilities — `cnes_import.sh`
```sh
sh data/import/cnes_import.sh
```
Imports DataSUS/CNES health facility registry into `health_facilities`.

| | |
|---|---|
| **Source** | [DATASUS — CNES](https://cnes.datasus.gov.br) — monthly CSV download |
| **Table** | `health_facilities` |
| **Expected rows** | ~15 000–20 000 (São Paulo state; ~360 000 nationally) |
| **Re-run cadence** | Monthly or quarterly — CNES publishes monthly updates |
| **Idempotent** | Yes — `ON CONFLICT DO NOTHING` on CNES facility code |

---

## Monthly / Periodic Imports

These scripts should be run regularly to keep data current.

### SSP-SP Crime Statistics — `ssp_sp_import.sh`
```sh
sh data/import/ssp_sp_import.sh
```
Downloads and imports the latest SSP-SP crime statistics CSV into `crime_records`.

| | |
|---|---|
| **Source** | [SSP-SP / Dados Abertos SP](https://www.ssp.sp.gov.br/dados-abertos) — CSV |
| **Table** | `crime_records` |
| **Expected rows** | ~500 000–1 000 000 (grows ~50 000 rows/month for São Paulo state) |
| **Re-run cadence** | **Monthly** — SSP-SP publishes new data on the first business day of each month |
| **Idempotent** | Yes — `ON CONFLICT DO NOTHING` on `(municipio, year, month, crime_type)` |

**Recommended cron schedule** (runs on the 1st of each month at 2 AM):
```cron
0 2 1 * * cd /path/to/property-intelligence && sh data/import/ssp_sp_import.sh >> /var/log/ssp_import.log 2>&1
```

**Manual usage**:
```sh
# Auto-download latest data
sh data/import/ssp_sp_import.sh

# Use local CSV file
SSP_CSV=/path/to/local/ssp_crimes.csv sh data/import/ssp_sp_import.sh
```

---

## Requirements

All scripts require:
- `psql` (PostgreSQL client)
- `DATABASE_URL` environment variable set to a valid PostgreSQL connection string

Some scripts additionally require:
- `wget` and `unzip` (for auto-download mode)
- `ogr2ogr` (GDAL — for shapefile imports: `ana_shapefile_import.sh`, `ibge_cnefe_import.sh`)

---

## Testing Locally

1. Start the local database:
   ```sh
   docker compose up -d postgres
   ```

2. Set the database URL:
   ```sh
   export DATABASE_URL="******localhost:5432/property_intelligence"
   ```

3. Run migrations:
   ```sh
   sh infra/migrations/run.sh
   ```

4. Run import scripts:
   ```sh
   sh data/import/ssp_sp_import.sh
   # etc.
   ```

---

## Production Deployment

In production (K3s on Hetzner), these scripts can be run:
- **One-time** via a Kubernetes Job during initial deployment
- **Periodic** via a CronJob for monthly/quarterly updates

Example CronJob manifest for SSP-SP monthly import:
```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: ssp-sp-crime-import
  namespace: property-intelligence
spec:
  schedule: "0 2 1 * *"  # 1st of month at 2 AM UTC
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: import
            image: postgres:16-alpine
            command: ["/bin/sh", "/scripts/ssp_sp_import.sh"]
            env:
            - name: DATABASE_URL
              valueFrom:
                secretKeyRef:
                  name: property-intelligence-secrets
                  key: database-url
            volumeMounts:
            - name: scripts
              mountPath: /scripts
          restartPolicy: OnFailure
          volumes:
          - name: scripts
            configMap:
              name: import-scripts
```

