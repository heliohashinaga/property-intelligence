<template>
  <div v-if="riskFlags.length || opportunityFlags.length" class="flags-wrapper">
    <div v-if="riskFlags.length" class="flag-group">
      <h3 class="group-label risk-label">⚠ Riscos</h3>
      <div class="badges">
        <span v-for="flag in riskFlags" :key="flag" class="badge risk-badge">
          {{ labelOf(flag) }}
        </span>
      </div>
    </div>

    <div v-if="opportunityFlags.length" class="flag-group">
      <h3 class="group-label opportunity-label">✦ Oportunidades</h3>
      <div class="badges">
        <span v-for="flag in opportunityFlags" :key="flag" class="badge opportunity-badge">
          {{ labelOf(flag) }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  riskFlags: string[]
  opportunityFlags: string[]
}>()

const FLAG_LABELS: Record<string, string> = {
  moderate_flood_risk: 'Risco de alagamento moderado',
  high_flood_risk: 'Alto risco de alagamento',
  crime_trend_12m: 'Tendência de aumento da criminalidade',
  low_mobility: 'Baixa mobilidade urbana',
  no_hospital_2km: 'Sem hospital em 2km',
  metro_expansion_nearby: 'Expansão do metrô próxima',
  zoning_upscale: 'Zoneamento para alta densidade',
  appreciation_trend_up: 'Tendência de valorização',
  school_excellence_1km: 'Escola de excelência em 1km',
}

function labelOf(flag: string): string {
  return FLAG_LABELS[flag] ?? flag
}
</script>

<style scoped>
.flags-wrapper {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.flag-group {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.group-label {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 0;
}

.risk-label { color: #b91c1c; }
.opportunity-label { color: #15803d; }

.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.badge {
  display: inline-block;
  padding: 0.25rem 0.75rem;
  border-radius: 9999px;
  font-size: 0.8rem;
  font-weight: 600;
  line-height: 1.4;
}

.risk-badge {
  background: #fee2e2;
  color: #991b1b;
  border: 1px solid #fca5a5;
}

.opportunity-badge {
  background: #dcfce7;
  color: #166534;
  border: 1px solid #86efac;
}
</style>
