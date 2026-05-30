<template>
  <div class="insight-panel">
    <!-- Warnings -->
    <div v-if="warnings.length" class="warnings">
      <div v-for="(w, i) in warnings" :key="i" class="warning-box">
        <span class="warning-icon">⚠</span>
        {{ w.message }}
      </div>
    </div>

    <!-- Insight text -->
    <div class="insight-text">
      <h3 class="insight-heading">Explicação</h3>
      <p v-if="insight && !insightUnavailable" class="insight-body">{{ insight }}</p>
      <p v-else class="insight-unavailable">Explicação indisponível no momento.</p>
    </div>

    <!-- Providers used -->
    <div v-if="providersUsed.length" class="providers">
      <span class="providers-label">Fontes:</span>
      <span v-for="p in providersUsed" :key="p" class="provider-chip">{{ p }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { AnalysisWarning } from '../services/propertyIntelligenceApi'

defineProps<{
  insight: string | null
  insightUnavailable: boolean
  warnings: AnalysisWarning[]
  providersUsed: string[]
}>()
</script>

<style scoped>
.insight-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.warnings {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.warning-box {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: #fefce8;
  border: 1px solid #fde047;
  border-radius: 0.5rem;
  font-size: 0.875rem;
  color: #713f12;
  line-height: 1.5;
}

.warning-icon {
  flex-shrink: 0;
  font-size: 1rem;
}

.insight-heading {
  margin: 0 0 0.5rem;
  font-size: 0.875rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #6b7280;
}

.insight-body {
  margin: 0;
  font-size: 0.9375rem;
  line-height: 1.7;
  color: #1f2937;
}

.insight-unavailable {
  margin: 0;
  font-size: 0.9375rem;
  color: #9ca3af;
  font-style: italic;
}

.providers {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.25rem;
}

.providers-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #9ca3af;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.provider-chip {
  display: inline-block;
  padding: 0.15rem 0.6rem;
  background: #f3f4f6;
  color: #6b7280;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 500;
  border: 1px solid #e5e7eb;
}
</style>
