<script setup>
import RepairActionsMenu from './RepairActionsMenu.vue'
import WorkParticipants from './WorkParticipants.vue'
defineProps({
  isAdministrator: { type: Boolean, default: false },
  busy: { type: Boolean, default: false },
  work: { type: Object, required: true },
})

const emit = defineEmits(['view-media', 'manage-entry'])
</script>

<template>
  <article class="work-item">
    <header class="work-item__header">
      <div>
        <time :datetime="work.date">{{ work.dateLabel || work.date || 'Дата не указана' }}</time>
        <h5>{{ work.name || 'Ремонтная работа' }}</h5>
      </div>
      <RepairActionsMenu v-if="isAdministrator" label="Действия с работой" edit-label="Редактировать работу" delete-label="Удалить работу" :busy="busy"
        @edit="emit('manage-entry', 'edit', 'works', work)" @delete="emit('manage-entry', 'delete', 'works', work)" />
    </header>

    <WorkParticipants :work="work" />

    <div
      v-if="work.photos?.length || work.videos?.length"
      class="work-item__media"
    >
      <button
        v-if="work.photos?.length"
        type="button"
        @click="emit('view-media', work.photos, 'image')"
      >
        Просмотр фото ({{ work.photos.length }})
      </button>
      <button
        v-if="work.videos?.length"
        type="button"
        @click="emit('view-media', work.videos, 'video')"
      >
        Просмотр видео ({{ work.videos.length }})
      </button>
    </div>
    <p v-else class="work-item__no-media">Медиафайлы не прикреплены.</p>
  </article>
</template>

<style scoped>
.work-item {
  min-width: 0;
  display: grid;
  gap: 0.4rem;
  padding: 0.6rem;
  border: 1px solid var(--border);
  border-radius: 0.75rem;
  background: var(--bg-surface);
}

.work-item__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.work-item__header > div { min-width: 0; overflow-wrap: anywhere; display: flex; align-items: baseline; flex-wrap: wrap; gap: 0.25rem 0.65rem; }

.work-item time,
.work-item__performer,
.work-item__no-media {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.work-item h5 {
  margin: 0;
  color: var(--text-main);
  font-size: 1rem;
}

.work-item__performer,
.work-item__no-media {
  margin: 0;
}

.work-item__media {
  display: flex;
  flex-wrap: wrap;
  gap: 0.55rem;
}

.work-item__media button {
  padding: 0.35rem 0.65rem;
  min-height: 36px;
  border: 1px solid var(--border);
  border-radius: 0.6rem;
  background: var(--bg-surface);
  color: var(--accent);
  font: inherit;
  cursor: pointer;
}

@media (max-width: 480px) {
  .work-item__header {
    gap: 0.5rem;
  }
}
</style>
