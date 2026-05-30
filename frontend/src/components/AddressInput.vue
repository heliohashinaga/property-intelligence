<template>
  <form class="address-input" @submit.prevent="handleSubmit">
    <div class="input-row">
      <input
        v-model="address"
        type="text"
        placeholder="Ex: Rua Augusta, 1500, São Paulo"
        :disabled="loading"
        class="address-field"
        autocomplete="street-address"
        aria-label="Endereço do imóvel"
      />
      <button type="submit" :disabled="loading || !address.trim()" class="submit-btn">
        <span v-if="loading" class="spinner" aria-hidden="true"></span>
        <span>{{ loading ? 'Analisando…' : 'Analisar' }}</span>
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{ loading: boolean }>()
const emit = defineEmits<{ analyze: [address: string] }>()

const address = ref('')

function handleSubmit() {
  const trimmed = address.value.trim()
  if (!trimmed || props.loading) return
  emit('analyze', trimmed)
  address.value = ''
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
  border: 1.5px solid #d1d5db;
  border-radius: 0.5rem;
  outline: none;
  transition: border-color 0.2s;
}

.address-field:focus {
  border-color: #3b82f6;
}

.address-field:disabled {
  background: #f9fafb;
  color: #9ca3af;
  cursor: not-allowed;
}

.submit-btn {
  display: flex;
  align-items: center;
  gap: 0.5rem;
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

.submit-btn:hover:not(:disabled) {
  background: #2563eb;
}

.submit-btn:disabled {
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
  to { transform: rotate(360deg); }
}
</style>
