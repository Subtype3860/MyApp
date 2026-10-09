<script setup>
import RepairList from './RepairList.vue'

defineProps({
  vehicle: { type: Object, required: true },
  requests: { type: Array, required: true },
})

const emit = defineEmits(['view-media'])
</script>

<template>
  <article class="vehicle-card">
    <p class="vehicle-card__summary">
      {{ [vehicle.typeName, vehicle.modelName].filter(Boolean).join('-') || 'Транспорт' }}
      · {{ vehicle.stateNumber || vehicle.garageNumber || 'Номер не указан' }}
      · {{ vehicle.vin || 'VIN не указан' }}
    </p>

    <RepairList
      :requests="requests"
      @view-media="(...args) => emit('view-media', ...args)"
    />
  </article>
</template>

<style scoped>
.vehicle-card {
  display: grid;
  gap: 1rem;
  padding: clamp(1rem, 3vw, 1.5rem);
  border: 1px solid var(--border);
  border-radius: 1rem;
  background: var(--bg-surface);
  box-shadow: 0 8px 24px rgb(30 48 76 / 5%);
}

.vehicle-card__summary {
  margin: 0;
  color: var(--text-main);
  font-size: clamp(1rem, 2.5vw, 1.2rem);
  font-weight: 750;
  overflow-wrap: anywhere;
}
</style>
