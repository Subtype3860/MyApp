<script setup>
import RepairList from './RepairList.vue'

defineProps({
  isAdministrator: { type: Boolean, default: false },
  busy: { type: Boolean, default: false },
  vehicle: { type: Object, required: true },
  requests: { type: Array, required: true },
})

const emit = defineEmits(['view-media', 'manage-entry'])
</script>

<template>
  <article class="vehicle-card">
    <header class="vehicle-card__header">
    <p class="vehicle-card__summary">
      {{ vehicle.modelName || 'Модель не указана' }}
      · Гар. № {{ vehicle.garageNumber ?? 'не указан' }}
    </p>
    <p class="vehicle-card__state">{{ { healthy: 'Исправна', operating_with_defect: 'Работает с неисправностью', stopped: 'Простаивает' }[vehicle.operationalState] || 'Состояние не указано' }}</p>
    </header>
    <p v-if="vehicle.isArchived" class="vehicle-card__state">Техника удалена из активного парка. История сохранена.</p>
    <RepairList
      :requests="requests"
      :is-administrator="isAdministrator"
      :busy="busy"
      @manage-entry="(...args) => emit('manage-entry', ...args)"
      @view-media="(...args) => emit('view-media', ...args)"
    />
  </article>
</template>

<style scoped>
.vehicle-card {
  display: grid;
  gap: 0.6rem;
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 1rem;
  background: var(--bg-surface);
  box-shadow: 0 8px 24px rgb(30 48 76 / 5%);
}

.vehicle-card__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.25rem 1rem;
}

.vehicle-card__state {
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.85rem;
}

.vehicle-card__summary {
  margin: 0;
  color: var(--text-main);
  font-size: clamp(1rem, 2.5vw, 1.2rem);
  font-weight: 750;
  overflow-wrap: anywhere;
}
</style>
