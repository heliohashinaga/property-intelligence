<template>
  <div class="radar-wrapper">
    <Radar :data="chartData" :options="chartOptions" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Radar } from 'vue-chartjs'
import {
  Chart as ChartJS,
  RadialLinearScale,
  PointElement,
  LineElement,
  Filler,
  Tooltip,
  Legend,
} from 'chart.js'
import type { DimensionKey, DimensionScore } from '../services/propertyIntelligenceApi'

ChartJS.register(RadialLinearScale, PointElement, LineElement, Filler, Tooltip, Legend)

const props = defineProps<{
  dimensions: Record<DimensionKey, DimensionScore>
}>()

const LABELS: Record<DimensionKey, string> = {
  security: 'Segurança',
  mobility: 'Mobilidade',
  infrastructure: 'Infraestrutura',
  environment: 'Ambiental',
  appreciation: 'Valorização',
  urban_context: 'Contexto',
}

const TREND_ARROW: Record<string, string> = {
  improving: '↑',
  worsening: '↓',
  stable: '→',
  insufficient_data: '?',
}

const ORDER: DimensionKey[] = [
  'security',
  'mobility',
  'infrastructure',
  'environment',
  'appreciation',
  'urban_context',
]

const chartData = computed(() => {
  const available: number[] = []
  const unavailable: number[] = []

  ORDER.forEach((key) => {
    const dim = props.dimensions[key]
    const pct = dim && dim.status === 'available' ? Math.round((dim.score / dim.max) * 100) : 0
    available.push(dim?.status === 'available' ? pct : 0)
    unavailable.push(dim?.status === 'unavailable' ? 0 : NaN)
  })

  return {
    labels: ORDER.map((k) => LABELS[k]),
    datasets: [
      {
        label: 'Score',
        data: available,
        backgroundColor: 'rgba(59, 130, 246, 0.2)',
        borderColor: '#3b82f6',
        borderWidth: 2,
        pointBackgroundColor: '#3b82f6',
        pointRadius: 4,
      },
      {
        label: 'Indisponível',
        data: ORDER.map((k) => (props.dimensions[k]?.status === 'unavailable' ? 0 : NaN)),
        backgroundColor: 'rgba(209, 213, 219, 0.15)',
        borderColor: '#d1d5db',
        borderWidth: 1,
        borderDash: [5, 5],
        pointRadius: 3,
        pointBackgroundColor: '#d1d5db',
      },
    ],
  }
})

const chartOptions = computed(() => ({
  responsive: true,
  maintainAspectRatio: true,
  scales: {
    r: {
      min: 0,
      max: 100,
      ticks: { stepSize: 25, font: { size: 11 } },
      pointLabels: { font: { size: 13, weight: 'bold' as const } },
    },
  },
  plugins: {
    legend: { display: false },
    tooltip: {
      callbacks: {
        label(ctx: { datasetIndex: number; dataIndex: number; parsed: { r: number } }) {
          if (ctx.datasetIndex !== 0) return ''
          const key = ORDER[ctx.dataIndex]
          const dim = props.dimensions[key]
          if (!dim || dim.status === 'unavailable') return 'Indisponível'
          const arrow = TREND_ARROW[dim.trend ?? ''] ?? ''
          return `${dim.score}/${dim.max} ${arrow}`
        },
      },
    },
  },
}))
</script>

<style scoped>
.radar-wrapper {
  max-width: 360px;
  width: 100%;
  margin: 0 auto;
}
</style>
