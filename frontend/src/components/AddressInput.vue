<template>
  <div class="address-input">
    <form @submit.prevent="handleSubmit">
      <div class="input-row">
        <input
          v-model="inputValue"
          type="text"
          class="address-field"
          placeholder="Ex: Rua Augusta, 1500, São Paulo"
          :disabled="loading"
          aria-label="Endereço do imóvel"
          minlength="5"
          maxlength="300"
          required
        />
        <button type="submit" class="analyze-btn" :disabled="loading || inputValue.trim().length < 5">
          <span v-if="loading" class="spinner" aria-hidden="true"></span>
          <span v-if="loading">Analisando…</span>
          <span v-else>Analisar</span>
        </button>
      </div>
    </form>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{
  loading: boolean
}>()

const emit = defineEmits<{
  analyze: [address: string]
}>()

const inputValue = ref('')

function handleSubmit() {
  const address = inputValue.value.trim()
  if (!address || address.length < 5) return
  inputValue.value = ''
  emit('analyze', address)
}
</script>

<style scoped>
.address-input {
  width: 100%;
}

.input-row {
  display: flex;
  gap: 0.5rem;
}

.address-field {
  flex: 1;
  padding: 0.75rem 1rem;
  font-size: 1rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  outline: none;
  transition: border-color 0.2s;
}

.address-field:focus {
  border-color: #3b82f6;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.2);
}

.address-field:disabled {
  background: #f3f4f6;
  color: #9ca3af;
  cursor: not-allowed;
}

.analyze-btn {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.75rem 1.5rem;
  font-size: 1rem;
  font-weight: 600;
  color: #fff;
  background: #3b82f6;
  border: none;
  border-radius: 0.5rem;
  cursor: pointer;
  transition: background 0.2s;
  white-space: nowrap;
}

.analyze-btn:hover:not(:disabled) {
  background: #2563eb;
}

.analyze-btn:disabled {
  background: #93c5fd;
  cursor: not-allowed;
}

.spinner {
  display: inline-block;
  width: 1rem;
  height: 1rem;
  border: 2px solid rgba(255, 255, 255, 0.4);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
