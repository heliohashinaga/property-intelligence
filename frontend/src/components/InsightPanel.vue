<template>
  <div class="insight-panel">
    <div v-if="warnings && warnings.length" class="warnings">
      <div v-for="(w, idx) in warnings" :key="idx" class="warning-box">
        ⚠️ {{ w.message }}
      </div>
    </div>

    <div class="insight-text">
      <p v-if="insightUnavailable" class="unavailable-msg">
        Explicação indisponível no momento.
      </p>
      <p v-else-if="insight" class="insight-content">{{ insight }}</p>
    </div>

    <div v-if="providersUsed && providersUsed.length" class="providers">
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
  gap: 0.75rem;
}

.warnings {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.warning-box {
  padding: 0.6rem 0.9rem;
  background: #fef9c3;
  border-left: 4px solid #f59e0b;
  border-radius: 0.25rem;
  font-size: 0.88rem;
  color: #78350f;
}

.insight-content {
  font-size: 0.95rem;
  color: #374151;
  line-height: 1.6;
  margin: 0;
}

.unavailable-msg {
  font-size: 0.9rem;
  color: #9ca3af;
  font-style: italic;
  margin: 0;
}

.providers {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.3rem;
  margin-top: 0.25rem;
}

.providers-label {
  font-size: 0.75rem;
  color: #9ca3af;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-right: 0.15rem;
}

.provider-chip {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  background: #f3f4f6;
  color: #6b7280;
  border-radius: 9999px;
  font-size: 0.75rem;
}
</style>
