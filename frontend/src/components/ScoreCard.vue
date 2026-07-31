<template>
  <div class="score-card">
    <div class="grade-badge" :class="gradeClass">{{ score.grade }}</div>
    <div class="composite-score">
      <span class="score-value">{{ score.composite }}</span>
      <span class="score-separator">/</span>
      <span class="score-max">{{ score.max }}</span>
    </div>
    <p class="normalized-address">{{ normalizedAddress }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { Score } from '../services/propertyIntelligenceApi'

const props = defineProps<{
  score: Score
  normalizedAddress: string
}>()

const gradeClass = computed(() => {
  const grade = props.score.grade
  if (grade === 'A+' || grade === 'A') return 'grade-green'
  if (grade === 'B+' || grade === 'B') return 'grade-blue'
  if (grade === 'C+' || grade === 'C') return 'grade-yellow'
  if (grade === 'D') return 'grade-orange'
  return 'grade-red'
})
</script>

<style scoped>
.score-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 1.5rem;
  background: #fff;
  border-radius: 1rem;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
}

.grade-badge {
  font-size: 2rem;
  font-weight: 800;
  padding: 0.25rem 1rem;
  border-radius: 0.5rem;
  color: #fff;
  letter-spacing: 0.05em;
}

.grade-green {
  background: #22c55e;
}

.grade-blue {
  background: #3b82f6;
}

.grade-yellow {
  background: #f59e0b;
  color: #1f2937;
}

.grade-orange {
  background: #f97316;
}

.grade-red {
  background: #ef4444;
}

.composite-score {
  display: flex;
  align-items: baseline;
  gap: 0.15rem;
  margin-top: 0.25rem;
}

.score-value {
  font-size: 3rem;
  font-weight: 800;
  color: #1f2937;
  line-height: 1;
}

.score-separator {
  font-size: 1.75rem;
  color: #9ca3af;
}

.score-max {
  font-size: 1.5rem;
  color: #6b7280;
}

.normalized-address {
  font-size: 0.85rem;
  color: #6b7280;
  text-align: center;
  margin: 0;
  max-width: 320px;
}
</style>
