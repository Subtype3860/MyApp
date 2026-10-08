<script setup>
import RepairRequest from './RepairRequest.vue'

defineProps({
  requests: { type: Array, required: true },
})

const emit = defineEmits(['view-media'])
</script>

<template>
  <section class="repair-list">
    <h3>История ремонтов</h3>
    <p v-if="!requests.length" class="repair-list__empty">
      Ремонтов не производилось
    </p>
    <div v-else class="repair-list__items">
      <RepairRequest
        v-for="request in requests"
        :key="request.id"
        :request="request"
        @view-media="(...args) => emit('view-media', ...args)"
      />
    </div>
  </section>
</template>

<style scoped>
.repair-list {
  display: grid;
  gap: 0.85rem;
}

.repair-list h3 {
  margin: 0;
  color: var(--text-main);
}

.repair-list__items {
  display: grid;
  gap: 0.85rem;
}

.repair-list__empty {
  margin: 0;
  padding: 1rem;
  border-radius: 0.7rem;
  background: var(--bg-main);
  color: var(--text-secondary);
}
</style>
