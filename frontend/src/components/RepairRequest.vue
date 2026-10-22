<script setup>
import { inject } from 'vue'
import WorkItem from './WorkItem.vue'
import RepairActionsMenu from './RepairActionsMenu.vue'

defineProps({
  isAdministrator: { type: Boolean, default: false },
  busy: { type: Boolean, default: false },
  request: { type: Object, required: true },
})

const canEditRequest = inject('canEditHistoryRequest', () => false)
const emit = defineEmits(['view-media', 'manage-entry'])
</script>

<template>
  <article class="repair-request">
    <header class="repair-request__header">
      <div class="repair-request__heading">
        <div>
        <time :datetime="request.date">{{ request.dateLabel || request.date || 'Дата не указана' }}</time>
        <h4>Заявка на ремонт</h4>
        <span class="repair-request__status" :class="`repair-request__status--${request.status || 'queue'}`">{{ request.statusLabel || 'Зарегистрирована' }}</span>
        </div>
        <button v-if="!isAdministrator && canEditRequest(request)" type="button" class="repair-request__edit" :disabled="busy" @click="emit('manage-entry', 'edit', 'defects', request)">Редактировать заявку</button>
        <RepairActionsMenu v-if="isAdministrator" label="Действия с заявкой" edit-label="Редактировать заявку" delete-label="Удалить заявку" :busy="busy"
          @edit="emit('manage-entry', 'edit', 'defects', request)" @delete="emit('manage-entry', 'delete', 'defects', request)" />
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
          :is-administrator="isAdministrator"
          :busy="busy"
          @manage-entry="(...args) => emit('manage-entry', ...args)"
          @view-media="(...args) => emit('view-media', ...args)"
        />
      </div>
    </details>
  </article>
</template>

<style scoped>
.repair-request__edit { padding: 8px 12px; border: 1px solid var(--border); border-radius: 8px; background: var(--bg-surface); color: var(--text-main); cursor: pointer; }
.repair-request {
  display: grid;
  gap: 0.4rem;
  padding: 0.6rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 0.85rem;
  background: var(--bg-main);
}

.repair-request__header {
  display: grid;
  gap: 0.35rem;
}

.repair-request__heading { display: flex; justify-content: space-between; align-items: flex-start; gap: 0.75rem; }
.repair-request__heading > div { min-width: 0; display: flex; align-items: center; flex-wrap: wrap; gap: 0.25rem 0.65rem; }
.repair-request__status { width: fit-content; padding: 0.15rem 0.5rem; border-radius: 999px; font-size: 0.8rem; font-weight: 700; background: var(--bg-surface); color: var(--text-secondary); }
.repair-request__status--done { background: #e9f7ef; color: #187647; }
.repair-request__status--repair, .repair-request__status--waiting { background: #fff4df; color: #9a5b00; }

.repair-request time {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.repair-request h4 {
  margin: 0;
  color: var(--text-main);
}

.repair-request__description {
  margin: 0;
  color: var(--text-main);
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
  color: var(--accent);
  font-weight: 650;
  cursor: pointer;
}

.repair-request__details[open] summary {
  margin-bottom: 0.4rem;
}

.repair-request__works {
  display: grid;
  gap: 0.55rem;
  padding-left: 0.5rem;
  border-left: 2px solid var(--border);
}

.repair-request__media button {
  padding: 0.35rem 0.65rem;
  min-height: 36px;
  border: 1px solid var(--border);
  border-radius: 0.6rem;
  background: var(--bg-surface);
  color: var(--accent);
  font: inherit;
  cursor: pointer;
}

.repair-request__empty {
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}
</style>
