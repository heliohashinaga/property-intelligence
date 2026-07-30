# Data Import Scripts

This directory contains shell scripts for importing static and periodically-updated
datasets into the Property Intelligence PostgreSQL database.

All scripts require the `DATABASE_URL` environment variable to be set.

---

## One-Time / Infrequent Imports

These scripts import datasets that change rarely or are one-time setup tasks.

### ANA Flood Risk Zones
```sh
sh data/import/ana_shapefile_import.sh
```
Imports ANA SNIRH flood risk shapefiles into the `flood_risk_zones` PostGIS table.
**Frequency**: On initial setup, or when ANA publishes updated flood maps.

### IBGE Census Sectors
```sh
sh data/import/ibge_cnefe_import.sh
```
Imports IBGE Censo 2022 / CNEFE sector-level socioeconomic data into `census_sectors`.
**Frequency**: On initial setup, or after a new census (every ~10 years).

### INEP School Records
```sh
sh data/import/inep_ideb_import.sh
```
Imports INEP school location and IDEB scores into `school_records`.
**Frequency**: Annually (IDEB is updated every 2 years, but location data may update annually).

### CNES Health Facilities
```sh
sh data/import/cnes_import.sh
```
Imports DataSUS/CNES health facility registry into `health_facilities`.
**Frequency**: Monthly or quarterly (CNES publishes monthly updates).

---

## Monthly / Periodic Imports

These scripts should be run regularly to keep data current.

### SSP-SP Crime Statistics
```sh
sh data/import/ssp_sp_import.sh
```
Downloads and imports the latest SSP-SP crime statistics CSV into `crime_records`.

**Frequency**: **Monthly** (SSP-SP publishes new data monthly).

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

The script is **idempotent**: re-running with the same or overlapping data will not
create duplicates (uses `ON CONFLICT DO NOTHING`).

---

## Requirements

All scripts require:
- `psql` (PostgreSQL client)
- `DATABASE_URL` environment variable set to a valid PostgreSQL connection string

Some scripts additionally require:
- `wget` and `unzip` (for auto-download mode)
- `ogr2ogr` (GDAL — for shapefile imports)

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
