<template>
  <div class="score-card">
    <div class="score-row">
      <span class="composite">{{ composite }}</span>
      <span class="separator">/</span>
      <span class="max">{{ max }}</span>
      <span class="grade-badge" :style="{ background: gradeColor }">{{ grade }}</span>
    </div>
    <p class="address">{{ normalizedAddress }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { Grade } from '../services/propertyIntelligenceApi'

const props = defineProps<{
  composite: number
  max: number
  grade: Grade
  normalizedAddress: string
}>()

const GRADE_COLORS: Record<Grade, string> = {
  'A+': '#16a34a',
  'A': '#22c55e',
  'B+': '#2563eb',
  'B': '#3b82f6',
  'C+': '#ca8a04',
  'C': '#eab308',
  'D': '#f97316',
  'F': '#ef4444',
}

const gradeColor = computed(() => GRADE_COLORS[props.grade] ?? '#6b7280')
</script>

<style scoped>
.score-card {
  text-align: center;
  padding: 1.5rem;
}

.score-row {
  display: flex;
  align-items: baseline;
  justify-content: center;
  gap: 0.25rem;
}

.composite {
  font-size: 3.5rem;
  font-weight: 800;
  color: #111827;
  line-height: 1;
}

.separator {
  font-size: 2rem;
  color: #9ca3af;
  font-weight: 300;
}

.max {
  font-size: 1.5rem;
  color: #6b7280;
  font-weight: 500;
}

.grade-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 3rem;
  height: 3rem;
  border-radius: 50%;
  color: #fff;
  font-size: 1.1rem;
  font-weight: 800;
  margin-left: 0.75rem;
  flex-shrink: 0;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.18);
}

.address {
  margin-top: 0.75rem;
  font-size: 0.875rem;
  color: #6b7280;
  line-height: 1.4;
}
</style>
