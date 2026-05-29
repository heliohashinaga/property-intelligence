# Contract: GET /health

**Version**: 1.0
**Date**: 2026-05-28

---

## Request

**Method**: `GET`
**Path**: `/health`
**Auth**: None required

---

## Responses

### 200 OK — All dependencies healthy

```json
{
  "status": "healthy",
  "version": "1.0.0",
  "checks": {
    "database": "healthy",
    "redis": "healthy"
  },
  "timestamp": "2026-05-28T10:23:00Z"
}
```

### 200 OK — Degraded (some dependencies unavailable)

```json
{
  "status": "degraded",
  "version": "1.0.0",
  "checks": {
    "database": "healthy",
    "redis": "unhealthy"
  },
  "timestamp": "2026-05-28T10:23:00Z"
}
```

### 503 Service Unavailable — Database unreachable

```json
{
  "status": "unhealthy",
  "version": "1.0.0",
  "checks": {
    "database": "unhealthy",
    "redis": "healthy"
  },
  "timestamp": "2026-05-28T10:23:00Z"
}
```

Returns 503 only when the primary database (PostgreSQL) is unreachable, as
it is required for auth (API key lookup) and audit logging. Redis unhealthy
returns 200/degraded since the API can still function (albeit without caching).

---

## Notes

- Used by Cloudflare health checks and Docker Compose `healthcheck`
- Response time target: < 100ms
- Does NOT check external provider availability (Overpass, Claude, etc.)
