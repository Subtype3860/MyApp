<script setup>
import RepairRequest from './RepairRequest.vue'

defineProps({
  isAdministrator: { type: Boolean, default: false },
  busy: { type: Boolean, default: false },
  requests: { type: Array, required: true },
})

const emit = defineEmits(['view-media', 'manage-entry'])
</script>

<template>
  <section class="repair-list" aria-label="История ремонтов">
    <p v-if="!requests.length" class="repair-list__empty">
      Ремонтов не производилось
    </p>
    <div v-else class="repair-list__items">
      <RepairRequest
        v-for="request in requests"
        :key="request.id"
        :request="request"
      :is-administrator="isAdministrator"
      :busy="busy"
      @manage-entry="(...args) => emit('manage-entry', ...args)"
        @view-media="(...args) => emit('view-media', ...args)"
      />
    </div>
  </section>
</template>

<style scoped>
.repair-list {
  display: grid;
  gap: 0.5rem;
}

.repair-list__items {
  display: grid;
  gap: 0.5rem;
}

.repair-list__empty {
  margin: 0;
  padding: 0.5rem;
  border-radius: 0.7rem;
  background: var(--bg-main);
  color: var(--text-secondary);
}
</style>
