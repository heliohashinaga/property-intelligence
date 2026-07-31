<template>
  <div v-if="riskFlags.length || opportunityFlags.length" class="flag-badges">
    <div v-if="riskFlags.length" class="flag-group">
      <h3 class="flag-group-title">Riscos</h3>
      <div class="badges">
        <span v-for="flag in riskFlags" :key="flag" class="badge badge-risk">
          {{ riskLabel(flag) }}
        </span>
      </div>
    </div>
    <div v-if="opportunityFlags.length" class="flag-group">
      <h3 class="flag-group-title">Oportunidades</h3>
      <div class="badges">
        <span v-for="flag in opportunityFlags" :key="flag" class="badge badge-opportunity">
          {{ opportunityLabel(flag) }}
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

const RISK_LABELS: Record<string, string> = {
  moderate_flood_risk: 'Risco moderado de enchente',
  high_flood_risk: 'Risco alto de enchente',
  crime_trend_12m: 'Tendência de aumento da criminalidade',
  low_mobility: 'Baixa mobilidade urbana',
  no_hospital_2km: 'Sem hospital em 2km',
}

const OPPORTUNITY_LABELS: Record<string, string> = {
  metro_expansion_nearby: 'Expansão do metrô próxima',
  zoning_upscale: 'Zoneamento para alta densidade',
  appreciation_trend_up: 'Tendência de valorização',
  school_excellence_1km: 'Escola com alto IDEB em 1km',
  metro_line6_nearby_2026: 'Linha 6 do metrô próxima (2026)',
}

function riskLabel(flag: string): string {
  return RISK_LABELS[flag] ?? flag.replace(/_/g, ' ')
}

function opportunityLabel(flag: string): string {
  return OPPORTUNITY_LABELS[flag] ?? flag.replace(/_/g, ' ')
}
</script>

<style scoped>
.flag-badges {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.flag-group-title {
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #6b7280;
  margin: 0 0 0.4rem;
}

.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.badge {
  display: inline-block;
  padding: 0.25rem 0.75rem;
  border-radius: 9999px;
  font-size: 0.82rem;
  font-weight: 500;
}

.badge-risk {
  background: #fee2e2;
  color: #b91c1c;
}

.badge-opportunity {
  background: #dcfce7;
  color: #15803d;
}
</style>
