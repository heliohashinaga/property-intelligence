<template>
  <div class="home-page">
    <header class="page-header">
      <h1 class="page-title">Property Intelligence</h1>
      <p class="page-subtitle">Análise multidimensional de imóveis brasileiros</p>
    </header>

    <main class="page-content">
      <section class="input-section">
        <AddressInput :loading="loading" @analyze="onAnalyze" />
      </section>

      <section v-if="error" class="error-section">
        <div class="error-box">
          <p>{{ errorMessage }}</p>
          <button class="reset-btn" @click="resetState">Nova consulta</button>
        </div>
      </section>

      <section v-if="loading && !result" class="loading-section">
        <div class="loading-indicator">
          <div class="big-spinner"></div>
          <p>Analisando o imóvel…</p>
        </div>
      </section>

      <section v-if="result" class="result-section">
        <div class="result-grid">
          <div class="result-left">
            <ScoreCard :score="result.score" :normalized-address="result.address.normalized" />
            <FlagBadges
              :risk-flags="result.risk_flags"
              :opportunity-flags="result.opportunity_flags"
            />
          </div>
          <div class="result-center">
            <ScoreRadarChart :dimensions="result.score.dimensions" />
          </div>
          <div class="result-right">
            <InsightPanel
              :insight="result.insight"
              :insight-unavailable="result.insight_unavailable"
              :warnings="result.warnings"
              :providers-used="result.providers_used"
            />
          </div>
        </div>
        <div class="result-footer">
          <button class="reset-btn" @click="resetState">Nova consulta</button>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import AddressInput from '../components/AddressInput.vue'
import ScoreCard from '../components/ScoreCard.vue'
import ScoreRadarChart from '../components/ScoreRadarChart.vue'
import FlagBadges from '../components/FlagBadges.vue'
import InsightPanel from '../components/InsightPanel.vue'
import {
  analyzeProperty,
  PropertyApiError,
  type PropertyAnalysisResponse,
} from '../services/propertyIntelligenceApi'

const loading = ref(false)
const result = ref<PropertyAnalysisResponse | null>(null)
const error = ref<PropertyApiError | Error | null>(null)

const errorMessage = computed(() => {
  if (!error.value) return ''
  if (error.value instanceof PropertyApiError) {
    const status = error.value.status
    if (status === 401) return 'API key inválida ou ausente. Verifique suas credenciais.'
    if (status === 422) return error.value.body.message
    if (status === 503) return 'Dados insuficientes para análise no momento. Tente novamente em instantes.'
    return error.value.body.message
  }
  return 'Ocorreu um erro ao processar a solicitação. Tente novamente.'
})

async function onAnalyze(address: string) {
  loading.value = true
  result.value = null
  error.value = null
  try {
    result.value = await analyzeProperty(address)
  } catch (e) {
    error.value = e instanceof Error ? e : new Error(String(e))
  } finally {
    loading.value = false
  }
}

function resetState() {
  result.value = null
  error.value = null
  loading.value = false
}
</script>

<style scoped>
.home-page {
  min-height: 100vh;
  background: #f8fafc;
  display: flex;
  flex-direction: column;
}

.page-header {
  text-align: center;
  padding: 2.5rem 1rem 1rem;
}

.page-title {
  font-size: 2rem;
  font-weight: 800;
  color: #1e3a5f;
  margin: 0;
}

.page-subtitle {
  font-size: 1rem;
  color: #6b7280;
  margin: 0.4rem 0 0;
}

.page-content {
  flex: 1;
  width: 100%;
  max-width: 1100px;
  margin: 0 auto;
  padding: 1.5rem 1rem 3rem;
}

.input-section {
  max-width: 600px;
  margin: 0 auto 2rem;
}

.error-section {
  max-width: 600px;
  margin: 0 auto;
}

.error-box {
  padding: 1.25rem;
  background: #fef2f2;
  border: 1px solid #fca5a5;
  border-radius: 0.75rem;
  color: #b91c1c;
  font-size: 0.95rem;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.75rem;
}

.error-box p {
  margin: 0;
}

.loading-section {
  display: flex;
  justify-content: center;
  padding: 3rem 0;
}

.loading-indicator {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  color: #6b7280;
}

.big-spinner {
  width: 3rem;
  height: 3rem;
  border: 4px solid #e5e7eb;
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.result-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 1.5rem;
  align-items: start;
}

@media (max-width: 900px) {
  .result-grid {
    grid-template-columns: 1fr;
  }
}

.result-left,
.result-center,
.result-right {
  background: #fff;
  border-radius: 1rem;
  padding: 1.25rem;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
}

.result-footer {
  display: flex;
  justify-content: center;
  margin-top: 2rem;
}

.reset-btn {
  padding: 0.6rem 1.4rem;
  background: transparent;
  border: 1.5px solid #3b82f6;
  color: #3b82f6;
  border-radius: 0.5rem;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.2s, color 0.2s;
}

.reset-btn:hover {
  background: #3b82f6;
  color: #fff;
}
</style>
