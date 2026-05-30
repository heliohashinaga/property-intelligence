<template>
  <div class="home">
    <header class="hero">
      <h1>Property Intelligence</h1>
      <p>Análise multidimensional de imóveis brasileiros</p>
    </header>

    <main class="main">
      <!-- Address input -->
      <section class="search-section">
        <AddressInput :loading="loading" @analyze="onAnalyze" />
      </section>

      <!-- Loading -->
      <section v-if="loading" class="loading-section" aria-live="polite">
        <div class="loading-spinner" aria-hidden="true"></div>
        <p>Analisando o imóvel…</p>
      </section>

      <!-- Error -->
      <section v-else-if="error" class="error-section" role="alert">
        <div class="error-box">
          <span class="error-icon">✕</span>
          <div>
            <p class="error-message">{{ error }}</p>
            <button class="reset-btn" @click="reset">Nova busca</button>
          </div>
        </div>
      </section>

      <!-- Results -->
      <section v-else-if="result" class="results-section">
        <!-- Score row -->
        <div class="score-row">
          <ScoreCard
            :composite="result.score.composite"
            :max="result.score.max"
            :grade="result.score.grade"
            :normalized-address="result.address.normalized"
          />
          <ScoreRadarChart :dimensions="result.score.dimensions" />
        </div>

        <!-- Flags -->
        <FlagBadges
          :risk-flags="result.risk_flags"
          :opportunity-flags="result.opportunity_flags"
        />

        <!-- Insight -->
        <InsightPanel
          :insight="result.insight"
          :insight-unavailable="result.insight_unavailable ?? false"
          :warnings="result.warnings ?? []"
          :providers-used="result.providers_used"
        />

        <div class="reset-row">
          <button class="reset-btn" @click="reset">Nova busca</button>
          <span v-if="result.cached" class="cached-badge">📦 Resultado em cache</span>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import AddressInput from '../components/AddressInput.vue'
import ScoreCard from '../components/ScoreCard.vue'
import ScoreRadarChart from '../components/ScoreRadarChart.vue'
import FlagBadges from '../components/FlagBadges.vue'
import InsightPanel from '../components/InsightPanel.vue'
import { analyzeProperty, ApiError } from '../services/propertyIntelligenceApi'
import type { AnalysisResult } from '../services/propertyIntelligenceApi'

const loading = ref(false)
const error = ref<string | null>(null)
const result = ref<AnalysisResult | null>(null)

async function onAnalyze(address: string) {
  loading.value = true
  error.value = null
  result.value = null

  try {
    result.value = await analyzeProperty(address)
  } catch (err) {
    if (err instanceof ApiError) {
      if (err.status === 422) {
        error.value = 'Endereço não reconhecido. Verifique se o CEP ou município está correto.'
      } else if (err.status === 401) {
        error.value = 'Chave de API inválida ou ausente.'
      } else if (err.status === 503) {
        error.value = 'Dados insuficientes para análise. Tente novamente em instantes.'
      } else {
        error.value = 'Erro ao analisar o imóvel. Tente novamente.'
      }
    } else {
      error.value = 'Erro de conexão. Verifique sua internet e tente novamente.'
    }
  } finally {
    loading.value = false
  }
}

function reset() {
  result.value = null
  error.value = null
}
</script>

<style scoped>
.home {
  min-height: 100vh;
  background: #f9fafb;
}

.hero {
  background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);
  color: #fff;
  text-align: center;
  padding: 2.5rem 1.5rem 2rem;
}

.hero h1 {
  margin: 0 0 0.5rem;
  font-size: 2rem;
  font-weight: 800;
  letter-spacing: -0.02em;
}

.hero p {
  margin: 0;
  font-size: 1rem;
  opacity: 0.85;
}

.main {
  max-width: 900px;
  margin: 0 auto;
  padding: 2rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.search-section {
  background: #fff;
  border-radius: 0.75rem;
  padding: 1.5rem;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}

.loading-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 3rem;
  color: #6b7280;
}

.loading-spinner {
  width: 2.5rem;
  height: 2.5rem;
  border: 3px solid #dbeafe;
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin { to { transform: rotate(360deg); } }

.error-section {
  background: #fff;
  border-radius: 0.75rem;
  padding: 1.5rem;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}

.error-box {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.error-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  background: #fee2e2;
  color: #dc2626;
  border-radius: 50%;
  font-weight: 700;
  flex-shrink: 0;
}

.error-message {
  margin: 0 0 1rem;
  color: #1f2937;
  line-height: 1.5;
}

.results-section {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.score-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
  background: #fff;
  border-radius: 0.75rem;
  padding: 1.5rem;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.06);
}

@media (max-width: 600px) {
  .score-row {
    grid-template-columns: 1fr;
  }
}

.reset-btn {
  padding: 0.5rem 1.25rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: #3b82f6;
  background: transparent;
  border: 1.5px solid #3b82f6;
  border-radius: 0.5rem;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.reset-btn:hover {
  background: #3b82f6;
  color: #fff;
}

.reset-row {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.5rem 0;
}

.cached-badge {
  font-size: 0.75rem;
  color: #9ca3af;
}
</style>
