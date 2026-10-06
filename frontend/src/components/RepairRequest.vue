<script setup>
import WorkItem from './WorkItem.vue'

defineProps({
  request: { type: Object, required: true },
})

const emit = defineEmits(['view-media'])
</script>

<template>
  <article class="repair-request">
    <header class="repair-request__header">
      <div>
        <time :datetime="request.date">{{ request.dateLabel || request.date || 'Дата не указана' }}</time>
        <h4>Заявка на ремонт</h4>
      </div>
      <p class="repair-request__description">
        {{ request.description || 'Описание неисправности не указано.' }}
      </p>
    </header>

    <div
      v-if="request.photos?.length || request.videos?.length"
      class="repair-request__media"
    >
      <button
        v-if="request.photos?.length"
        type="button"
        @click="emit('view-media', request.photos, 'image')"
      >
        Просмотр фото ({{ request.photos.length }})
      </button>
      <button
        v-if="request.videos?.length"
        type="button"
        @click="emit('view-media', request.videos, 'video')"
      >
        Просмотр видео ({{ request.videos.length }})
      </button>
    </div>

    <p v-if="!request.works?.length" class="repair-request__empty">
      Работы по заявке не зарегистрированы.
    </p>
    <details v-else class="repair-request__details">
      <summary>Работы по заявке ({{ request.works.length }})</summary>
      <div class="repair-request__works">
        <WorkItem
          v-for="work in request.works"
          :key="work.id"
          :work="work"
          @view-media="(...args) => emit('view-media', ...args)"
        />
      </div>
    </details>
  </article>
</template>

<style scoped>
.repair-request {
  display: grid;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #e5e9ef;
  border-radius: 0.85rem;
  background: #f8fafc;
}

.repair-request__header {
  display: grid;
  gap: 0.35rem;
}

.repair-request time {
  color: #687386;
  font-size: 0.9rem;
}

.repair-request h4 {
  margin: 0.2rem 0 0;
  color: #202a38;
}

.repair-request__description {
  margin: 0;
  color: #344054;
}

.repair-request__media {
  display: flex;
  flex-wrap: wrap;
  gap: 0.55rem;
}

.repair-request__details {
  min-width: 0;
}

.repair-request__details summary {
  width: fit-content;
  color: #1768b2;
  font-weight: 650;
  cursor: pointer;
}

.repair-request__details[open] summary {
  margin-bottom: 0.7rem;
}

.repair-request__works {
  display: grid;
  gap: 0.55rem;
  padding-left: 0.85rem;
  border-left: 2px solid #d8e8f8;
}

.repair-request__media button {
  padding: 0.55rem 0.75rem;
  border: 1px solid #c9dff5;
  border-radius: 0.6rem;
  background: #f1f7fd;
  color: #1768b2;
  font: inherit;
  cursor: pointer;
}

.repair-request__empty {
  margin: 0;
  color: #687386;
  font-size: 0.9rem;
}
</style>
