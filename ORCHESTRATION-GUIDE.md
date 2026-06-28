# Orchestration Strategy Guide

## Quick Start

### 1. Detect Your System Tier

```bash
./scripts/detect-orchestration-tier.sh
```

**Output Example**:
```
=== System Profile ===
Total RAM: 3 GB
Available RAM: 1 GB
CPU Cores: 2
VS Code processes: 7

ORCHESTRATION_TIER=single
MAX_WORKERS=1
BUILD_FLAGS=--no-parallel
Tier: ⚠️  SINGLE (Sequential) — Limited resources
```

### 2. Run Pre-Delegation Health Check

```bash
./scripts/pre-delegation-check.sh
```

**Must pass before delegating large tasks**:
- ✅ Memory: At least 2GB available
- ✅ Swap: Not exceeding 80% usage
- ✅ Disk: At least 5GB free
- ✅ Git: Clean working tree
- ⚠️  VS Code: Not running excessive Roslyn processes

---

## Tier 1: SINGLE (Your Notebook Now)

**When to use**: `RAM < 4 GB` or `CORES < 4`

### Setup

```bash
# Before delegation
dotnet clean
./scripts/pre-delegation-check.sh

# Optional: Close VS Code to free ~1 GB
killall code-server  # or close IDE
```

### Delegation Pattern

```bash
# Sequential: T037 → T038 → T039
subagent({ agent: 'worker', task: 'T037...' })  # 6 min
# Wait completion ↓
subagent({ agent: 'worker', task: 'T038...' })  # 3 min
# Wait completion ↓
subagent({ agent: 'worker', task: 'T039...' })  # 7 min
# Total: ~16 minutes
```

### Performance Expectations

| Task | Time | Memory Peak | Status |
|------|------|-------------|--------|
| T037 | 6 min | 3.5 GB | ✅ Safe |
| T038 | 3 min | 2.8 GB | ✅ Safe |
| T039 | 7 min | 3.4 GB | ✅ Safe |
| **Total** | **~16 min** | **3.5 GB** | **✅ No OOM** |

---

## Tier 2: SEMI-PARALLEL (4–8 GB Machine)

**When to use**: `4 GB ≤ RAM < 8 GB` and `CORES ≥ 2`

### Delegation Pattern

```bash
# Scout context + Build in parallel
subagent({
  tasks: [
    { agent: 'scout', task: 'Analyze codebase for T037-T039...' },
    { agent: 'worker', task: 'T037 implementation...' }
  ],
  concurrency: 2
})

# Then sequential dependent tasks
subagent({ agent: 'worker', task: 'T038 + T039 (use context from scout)...' })
```

### Performance Expectations

| Phase | Tasks | Time | Memory Peak |
|-------|-------|------|-------------|
| Parallel | Scout + T037 | 6 min | 4.0 GB |
| Sequential | T038 → T039 | 10 min | 3.2 GB |
| **Total** | **3 tasks** | **~12 min** | **4.0 GB** |

---

## Tier 3: FULL-PARALLEL (CI/CD: 8+ GB RAM)

**When to use**: GitHub Actions, cloud runners, or local machines with 8+ GB RAM + 4 cores

### Delegation Pattern

```bash
# All tasks in parallel
subagent({
  tasks: [
    { agent: 'worker', task: 'T037...' },
    { agent: 'worker', task: 'T038...' },
    { agent: 'worker', task: 'T039...' }
  ],
  concurrency: 3,
  worktree: true
})
```

### Performance Expectations

| Phase | Tasks | Time | Memory Peak |
|-------|-------|------|-------------|
| Parallel | T037 \|\| T038 \|\| T039 | 7 min | 7.8 GB |
| Merge + Validation | Single | 2 min | 3.5 GB |
| **Total** | **3 tasks** | **~7 min** | **7.8 GB** |

---

## Auto-Detect in Code

```javascript
// Load tier from environment
const tier = process.env.ORCHESTRATION_TIER || 'single';
const maxWorkers = parseInt(process.env.MAX_WORKERS) || 1;

async function delegateWithAutoTier() {
  // Run pre-check
  const { execSync } = require('child_process');
  try {
    execSync('./scripts/pre-delegation-check.sh', { stdio: 'inherit' });
  } catch (e) {
    console.error('❌ System not ready for delegation');
    process.exit(1);
  }

  // Auto-switch strategy
  if (tier === 'single') {
    await delegateSequential();
  } else if (tier === 'semi') {
    await delegateSemiParallel();
  } else {
    await delegateFullParallel(maxWorkers);
  }
}
```

---

## Troubleshooting

### "Pre-delegation check failed: Only 1GB available"

```bash
# Clean up
dotnet clean
rm -rf ~/.nuget/http-cache
pkill -f "code-server"  # Kill VS Code

# Or if using Docker:
docker system prune --all --force
```

### "Subagent timed out at 95% memory"

**Fix for this notebook**:
1. Revert to `TIER=single`
2. Add `--no-parallel` to all builds
3. Increase timeout per task to 15 min

**In orchestration.config.json**:
```json
"single": {
  "timeoutPerTask": 900000  // 15 minutes instead of 10
}
```

### "Swap usage at 85%"

This notebook has only 1 GB swap. Solutions:

```bash
# Option 1: Increase swap (permanent)
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile

# Option 2: Reduce swap reliance (use TIER=single)
export ORCHESTRATION_TIER=single
```

---

## CI/CD Integration (GitHub Actions)

### `.github/workflows/ci.yml` (excerpt)

```yaml
jobs:
  delegate-tasks:
    runs-on: ubuntu-latest  # 11 GB RAM, 4 cores → TIER=full
    steps:
      - uses: actions/checkout@v3
      
      - name: Detect tier
        run: |
          source ./scripts/detect-orchestration-tier.sh >> $GITHUB_ENV
      
      - name: Run delegation
        run: |
          # GitHub has 11+ GB, so this auto-enables full parallel
          node scripts/delegate-tasks.js
```

---

## Decision Tree: Quick Reference

```
Your system?
│
├─ RAM < 4 GB
│  └─ → Use TIER=single
│      └─ Sequential + dotnet clean between tasks
│
├─ RAM 4–8 GB
│  ├─ Cores ≥ 2
│  │  └─ → Use TIER=semi
│  │      └─ Scout || Worker(build) then sequential dependent
│  └─ Cores < 2
│     └─ → Use TIER=single
│
└─ RAM ≥ 8 GB + Cores ≥ 4
   └─ → Use TIER=full
       └─ Full parallel, concurrency = CORES - 1
```

---

## References

- **Main Strategy**: `docs/orchestration-strategy.md`
- **Config**: `orchestration.config.json`
- **Detection Script**: `scripts/detect-orchestration-tier.sh`
- **Health Check**: `scripts/pre-delegation-check.sh`

