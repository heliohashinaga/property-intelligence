<template>
  <div class="radar-chart-wrapper">
    <canvas ref="canvasRef"></canvas>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import {
  Chart,
  RadarController,
  RadialLinearScale,
  PointElement,
  LineElement,
  Filler,
  Tooltip,
  Legend,
} from 'chart.js'
import type { ScoreDimensions, DimensionScore } from '../services/propertyIntelligenceApi'

Chart.register(RadarController, RadialLinearScale, PointElement, LineElement, Filler, Tooltip, Legend)

const DIMENSION_LABELS: Record<keyof ScoreDimensions, string> = {
  security: 'Segurança',
  mobility: 'Mobilidade',
  infrastructure: 'Infraestrutura',
  environment: 'Ambiental',
  appreciation: 'Valorização',
  urban_context: 'Contexto',
}

const TREND_ARROW: Record<string, string> = {
  improving: '↑',
  stable: '→',
  worsening: '↓',
}

const props = defineProps<{
  dimensions: ScoreDimensions
}>()

const canvasRef = ref<HTMLCanvasElement | null>(null)
let chart: Chart | null = null

function buildChartData(dims: ScoreDimensions) {
  const keys = Object.keys(DIMENSION_LABELS) as Array<keyof ScoreDimensions>
  const labels = keys.map((k) => DIMENSION_LABELS[k])
  const values = keys.map((k) => {
    const d: DimensionScore = dims[k]
    if (d.status === 'unavailable' || d.score === null) return 0
    return Math.round((d.score / d.max) * 100)
  })
  const borderDash = keys.map((k) => (dims[k].status === 'unavailable' ? [6, 4] : []))

  return { labels, values, borderDash }
}

function createChart() {
  if (!canvasRef.value) return
  const { labels, values } = buildChartData(props.dimensions)
  chart = new Chart(canvasRef.value, {
    type: 'radar',
    data: {
      labels,
      datasets: [
        {
          label: 'Score (%)',
          data: values,
          backgroundColor: 'rgba(59, 130, 246, 0.2)',
          borderColor: 'rgba(59, 130, 246, 0.9)',
          borderWidth: 2,
          pointBackgroundColor: 'rgba(59, 130, 246, 0.9)',
          pointRadius: 4,
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: true,
      scales: {
        r: {
          min: 0,
          max: 100,
          ticks: {
            stepSize: 25,
            callback: (v) => `${v}%`,
          },
          pointLabels: {
            font: { size: 13 },
          },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label(ctx) {
              const keys = Object.keys(DIMENSION_LABELS) as Array<keyof ScoreDimensions>
              const key = keys[ctx.dataIndex]
              const d = props.dimensions[key]
              if (d.status === 'unavailable' || d.score === null) {
                return 'Indisponível'
              }
              const arrow = d.trend ? (TREND_ARROW[d.trend] ?? '') : ''
              return `${d.score}/${d.max} ${arrow}`.trim()
            },
          },
        },
      },
    },
  })
}

onMounted(() => {
  createChart()
})

watch(
  () => props.dimensions,
  (dims) => {
    if (!chart) return
    const { values } = buildChartData(dims)
    chart.data.datasets[0].data = values
    chart.update()
  },
  { deep: true },
)

onBeforeUnmount(() => {
  chart?.destroy()
  chart = null
})
</script>

<style scoped>
.radar-chart-wrapper {
  position: relative;
  width: 100%;
  max-width: 400px;
  margin: 0 auto;
}
</style>
