<script setup>
defineProps({
  work: { type: Object, required: true },
})

const emit = defineEmits(['view-media'])
</script>

<template>
  <article class="work-item">
    <header class="work-item__header">
      <div>
        <time :datetime="work.date">{{ work.dateLabel || work.date || 'Дата не указана' }}</time>
        <h5>{{ work.name || 'Ремонтная работа' }}</h5>
      </div>
      <span
        class="work-item__status"
        :class="`work-item__status--${work.status === 'completed' ? 'completed' : 'in-progress'}`"
      >
        {{ work.status === 'completed' ? 'Завершена' : 'В работе' }}
      </span>
    </header>

    <p class="work-item__performer">
      <strong>Исполнитель:</strong> {{ work.performer || '—' }}
    </p>

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
  gap: 0.6rem;
  padding: 0.9rem;
  border: 1px solid #e5e9ef;
  border-radius: 0.75rem;
  background: #fff;
}

.work-item__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.work-item time,
.work-item__performer,
.work-item__no-media {
  color: #687386;
  font-size: 0.9rem;
}

.work-item h5 {
  margin: 0.25rem 0 0;
  color: #202a38;
  font-size: 1rem;
}

.work-item__status {
  flex: 0 0 auto;
  padding: 0.3rem 0.6rem;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 700;
}

.work-item__status--completed {
  background: #e9f7ef;
  color: #187647;
}

.work-item__status--in-progress {
  background: #fff4df;
  color: #9a5b00;
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
  padding: 0.55rem 0.75rem;
  border: 1px solid #c9dff5;
  border-radius: 0.6rem;
  background: #f1f7fd;
  color: #1768b2;
  font: inherit;
  cursor: pointer;
}

@media (max-width: 480px) {
  .work-item__header {
    flex-direction: column;
  }
}
</style>
