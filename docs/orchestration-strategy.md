# Orchestration Strategy for Property Intelligence

## Problem Statement

**Current Constraints** (this notebook):
- 3.7 GiB RAM total
- 2 CPU cores
- 3 worktrees simultaneous = 3 Roslyn servers (~300 MB each)
- MSBuild node reuse enabled = up to 9 workers (~150 MB each)
- VS Code background indexing always active
- Swap space: Only 1 GiB (thrashing at 85%+)

**Result**: Parallel builds hit OOM → subagent timeouts.

---

## Proposed Solution: Adaptive Orchestration

### Tier 1: Single-Device (This Notebook) — **SEQUENTIAL OPTIMIZED**

**Trigger**: `RAM < 4 GB` or `CORES < 4`

```
Task 1 (T037)
├─ Cherry-pick setup
├─ dotnet clean (free artifacts)
├─ dotnet build --no-parallel
├─ Tests run
├─ Commit
└─ CLEANUP: rm -rf bin obj .dotnet/obj/cache
     ↓
Task 2 (T038)
├─ Setup
├─ dotnet build --no-parallel
└─ Commit
     ↓
Task 3 (T039)
├─ Cherry-pick both
├─ dotnet build --no-parallel
├─ Tests run
└─ Commit
```

**Key Changes**:
1. `--no-parallel` flag for MSBuild (disable node reuse)
2. `dotnet clean` between tasks (free 100-200 MB)
3. Explicit sleep/pause between workers (let Roslyn cleanup)
4. Close VS Code Roslyn before critical builds

**Expected Performance**:
- T037: 6 min (includes recovery time)
- T038: 3 min (config only)
- T039: 7 min (cherry-pick + tests)
- **Total: ~16 min sequential** vs **timeout at 10+ min parallel**

---

### Tier 2: Medium Machine (4–8 GB RAM) — **SEMI-PARALLEL**

**Trigger**: `4 GB ≤ RAM < 8 GB` and `CORES ≥ 2`

```
Task 1 (Read-only)      Task 2 (Build)
├─ Scout codebase       ├─ dotnet build
└─ Generate context     └─ Tests

     ↓ Both complete
        ↓
Task 3 (Implementation with context from Task 1)
├─ Review context
├─ Implement changes
├─ dotnet test
└─ Commit
```

**Concurrency**: 2 parallel workers max
- 1 Scout (read-only, ~200 MB)
- 1 Worker (build, up to ~1.5 GB)
- Total: ~1.7 GB safe overhead

---

### Tier 3: CI/CD Runner (8 GB+ RAM) — **FULL PARALLEL**

**Trigger**: `RAM ≥ 8 GB` and `CORES ≥ 4` (GitHub Actions default)

```
Task 1          Task 2          Task 3
├─ Build        ├─ Build        ├─ Build
├─ Test         ├─ Test         ├─ Test
└─ Push         └─ Push         └─ Push

All run simultaneously
Total time: max(T1, T2, T3) ≈ 7 min
```

**Concurrency**: Up to `CORES - 1` parallel workers

---

## Implementation: Detection + Auto-Switch

### Step 1: Add Detection Script

**File**: `scripts/detect-orchestration-tier.sh`

```bash
#!/bin/bash
# Detect optimal orchestration tier for this machine

TOTAL_RAM_GB=$(free -g | awk '/^Mem:/ {print $2}')
AVAILABLE_RAM_GB=$(free -g | awk '/^Mem:/ {print $7}')
CORES=$(nproc)
VS_CODE_PID=$(pgrep -f "code-server|Roslyn" | wc -l)

# Reserve 500 MB for system
USABLE_RAM=$((AVAILABLE_RAM_GB - 1))

echo "=== System Profile ==="
echo "Total RAM: ${TOTAL_RAM_GB} GB"
echo "Available RAM: ${AVAILABLE_RAM_GB} GB"
echo "CPU Cores: ${CORES}"
echo "VS Code processes: ${VS_CODE_PID}"

if [ "$TOTAL_RAM_GB" -lt 4 ] || [ "$USABLE_RAM" -lt 3 ]; then
    echo "Tier: SINGLE (Sequential)"
    echo "ORCHESTRATION_TIER=single"
    echo "MAX_WORKERS=1"
    echo "BUILD_FLAGS=--no-parallel"
elif [ "$TOTAL_RAM_GB" -ge 4 ] && [ "$TOTAL_RAM_GB" -lt 8 ]; then
    echo "Tier: SEMI-PARALLEL"
    echo "ORCHESTRATION_TIER=semi"
    echo "MAX_WORKERS=2"
    echo "BUILD_FLAGS=--no-parallel"
else
    echo "Tier: FULL PARALLEL"
    echo "ORCHESTRATION_TIER=full"
    echo "MAX_WORKERS=$((CORES - 1))"
    echo "BUILD_FLAGS="
fi
```

### Step 2: Export to Env

```bash
eval "$(scripts/detect-orchestration-tier.sh)"
echo "export ORCHESTRATION_TIER=$ORCHESTRATION_TIER" >> .env.local
echo "export MAX_WORKERS=$MAX_WORKERS" >> .env.local
echo "export DOTNET_BUILD_FLAGS=$BUILD_FLAGS" >> .env.local
```

### Step 3: Use in Subagent Delegation

```javascript
// Get tier from env
const tier = process.env.ORCHESTRATION_TIER || 'single';
const maxWorkers = parseInt(process.env.MAX_WORKERS) || 1;

if (tier === 'single') {
  // Sequential execution
  subagent({ agent: 'worker', task: 'T037...' });
  // Wait completion
  subagent({ agent: 'worker', task: 'T038...' });
  // Wait completion
  subagent({ agent: 'worker', task: 'T039...' });
} else if (tier === 'semi') {
  // Scout + Worker parallel, then dependent task
  subagent({
    tasks: [
      { agent: 'scout', task: '...' },
      { agent: 'worker', task: 'T037...' }
    ],
    concurrency: 2
  });
} else {
  // Full parallel
  subagent({
    tasks: [
      { agent: 'worker', task: 'T037...' },
      { agent: 'worker', task: 'T038...' },
      { agent: 'worker', task: 'T039...' }
    ],
    concurrency: maxWorkers,
    worktree: true
  });
}
```

---

## Pre-Delegation Health Checks

### New Script: `scripts/pre-delegation-check.sh`

```bash
#!/bin/bash
# Validate system state before delegating tasks

echo "=== Pre-Delegation Health Check ==="

# 1. Memory state
AVAILABLE=$(free -g | awk '/^Mem:/ {print $7}')
if [ "$AVAILABLE" -lt 2 ]; then
    echo "❌ FAIL: Only ${AVAILABLE}GB available (need 2GB)"
    echo "   ACTION: Run 'dotnet clean' or close VS Code"
    exit 1
fi
echo "✅ Memory: ${AVAILABLE}GB available"

# 2. Swap state
SWAP_USED=$(free -g | awk '/^Swap:/ {print $3}')
SWAP_TOTAL=$(free -g | awk '/^Swap:/ {print $2}')
if [ "$SWAP_USED" -gt $((SWAP_TOTAL / 2)) ]; then
    echo "⚠️  WARNING: Swap ${SWAP_USED}GB/${SWAP_TOTAL}GB (${SWAP_PERC}%)"
    echo "   ACTION: Kill unused processes"
fi
echo "✅ Swap: ${SWAP_USED}GB/${SWAP_TOTAL}GB"

# 3. Disk space
DISK_FREE=$(df /home/helio | awk 'NR==2 {print $4}' | numfmt --to=iec 2>/dev/null || echo "OK")
echo "✅ Disk: ${DISK_FREE} free"

# 4. VS Code state
VS_PIDS=$(pgrep -f "Roslyn|csdevkit" | wc -l)
if [ "$VS_PIDS" -gt 3 ]; then
    echo "⚠️  WARNING: ${VS_PIDS} VS Code .NET processes active"
    echo "   RECOMMENDATION: Close VS Code for builds"
fi

# 5. Clean state
if [ -d "bin" ] || [ -d "obj" ]; then
    echo "⚠️  WARNING: bin/ or obj/ directories present"
    echo "   RECOMMENDATION: Run 'dotnet clean' to free ~200MB"
fi

# 6. Git state
if ! git diff-index --quiet HEAD --; then
    echo "❌ FAIL: Working tree has uncommitted changes"
    exit 1
fi
echo "✅ Git: Clean working tree"

echo ""
echo "✅ Ready for delegation!"
```

---

## Task Dependency Graph (for decision logic)

### Sequential Mode (Single Device)

```
T037 → T038 → T039 → Tests → Merge
└─────────────────────────────┘
         ~16 minutes
```

### Semi-Parallel Mode (4-8 GB)

```
Scout (context)     Worker (T037)
       ↓                    ↓
    Context + T037 ─→ T038 + T039 + Tests
                            ↓
                          Merge
         ~12 minutes
```

### Full Parallel (8+ GB)

```
T037 ──┐
T038 ──┼─→ All complete ─→ Tests ─→ Merge
T039 ──┘
         ~7 minutes
```

---

## Configuration: `orchestration.config.json`

```json
{
  "strategies": {
    "single": {
      "maxWorkers": 1,
      "buildFlags": "--no-parallel",
      "dotnetClean": true,
      "roslynCleanup": true,
      "sleepBetweenTasks": 5000,
      "timeoutPerTask": 900000
    },
    "semi": {
      "maxWorkers": 2,
      "buildFlags": "--no-parallel",
      "dotnetClean": false,
      "roslynCleanup": false,
      "sleepBetweenTasks": 2000,
      "timeoutPerTask": 600000
    },
    "full": {
      "maxWorkers": 4,
      "buildFlags": "",
      "dotnetClean": false,
      "roslynCleanup": false,
      "sleepBetweenTasks": 0,
      "timeoutPerTask": 300000
    }
  },
  "detection": {
    "ramThresholds": [4, 8],
    "coreMinimum": 2,
    "autoDowngrade": true
  }
}
```

---

## Decision Tree for Orchestration

```
START
  ↓
[Available RAM?]
  ├─ < 2 GB → FAIL (need memory)
  ├─ 2-4 GB → SINGLE (sequential)
  │          └─ Pre-clean + --no-parallel + 5s pause
  ├─ 4-8 GB → SEMI-PARALLEL
  │          └─ Scout || Worker(build) → then sequential dependent tasks
  └─ > 8 GB → FULL-PARALLEL
             └─ Concurrency = min(CORES-1, TASKS)

[Cores available?]
  ├─ < 2 → Force SINGLE
  └─ ≥ 2 → Use RAM tier

[VS Code active?]
  ├─ YES + RAM < 4GB → Recommend close
  └─ NO → Proceed

[Git dirty?]
  ├─ YES → FAIL (commit first)
  └─ NO → Proceed

DELEGATE → Monitor RAM during execution
  ├─ If hits 90% → Graceful degrade to SINGLE
  └─ If hits 95%+ → Kill subagent + retry sequentially
```

---

## Metrics & Observability

### Per-Task Metrics

```json
{
  "task": "T037",
  "strategy": "single",
  "startTime": "2026-06-28T19:00:00Z",
  "memoryBefore": 3.1,
  "memoryPeak": 3.5,
  "memoryAfter": 2.8,
  "duration": 360000,
  "status": "success"
}
```

### Dashboard Summary

```
Orchestration Performance:
├─ Single mode: 16 min avg (reliable, no OOM)
├─ Semi-parallel: 12 min avg (balanced)
└─ Full-parallel: 7 min avg (requires 8+ GB)

RAM utilization:
├─ Tier 1: Peak 3.5 GB (100% safe)
├─ Tier 2: Peak 4.2 GB (safe on 4GB+)
└─ Tier 3: Peak 7.8 GB (safe on 8GB+)
```

