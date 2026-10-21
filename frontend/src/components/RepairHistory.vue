<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import VehicleCard from './VehicleCard.vue'

const props = defineProps({
  token: { type: String, required: true },
  isAdministrator: { type: Boolean, default: false },
  loadHistory: { type: Function, required: true },
})

const plateNumber = ref('')
const downtimeReason = ref('')
const histories = ref([])
const page = ref(1)
const pageSize = 8
const isLoading = ref(false)
const errorMessage = ref('')
const mediaViewerOpen = ref(false)
const mediaViewerItems = ref([])
const mediaViewerType = ref('image')

let currentRequestId = 0
let currentController
/** Нормализует номер техники для поиска в истории ремонта. */
function normalizedNumber(value) {
  return String(value ?? '').replace(/[\s-]+/g, '').toLocaleUpperCase('ru-RU')
}
const filteredHistories = computed(() => {
  const number = normalizedNumber(plateNumber.value)
  const reason = downtimeReason.value.trim().toLocaleLowerCase('ru-RU')
  return histories.value.flatMap(history => {
    if (number && ![history.vehicle.stateNumber, history.vehicle.garageNumber]
      .some(value => normalizedNumber(value).includes(number))) return []
    const requests = history.requests.filter(request => !reason ||
      String(request.description ?? '').toLocaleLowerCase('ru-RU').includes(reason))
    if (!requests.length && (reason || !number)) return []
    return [{ ...history, requests }]
  })
})
const pageCount = computed(() => Math.max(1, Math.ceil(filteredHistories.value.length / pageSize)))
const visibleHistories = computed(() => filteredHistories.value.slice((page.value - 1) * pageSize, page.value * pageSize))
const requestCount = computed(() => filteredHistories.value.reduce((sum, history) => sum + history.requests.length, 0))
watch([plateNumber, downtimeReason], () => { page.value = 1 })
onMounted(search)
onBeforeUnmount(() => currentController?.abort())
/** Сбрасывает ограничения отображения истории ремонта. */
function showAll() {
  plateNumber.value = ''
  downtimeReason.value = ''
  page.value = 1
}
/** Применяет введённые параметры поиска к истории ремонта. */
async function search() {
  currentController?.abort()
  const controller = new AbortController()
  currentController = controller
  const requestId = ++currentRequestId
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await props.loadHistory(controller.signal)
    if (requestId !== currentRequestId) return
    histories.value = response.map(history => ({
      ...history,
      requests: [...(history.requests ?? [])].sort((left, right) => new Date(right.date ?? 0) - new Date(left.date ?? 0)),
    })).sort((left, right) => new Date(right.requests[0]?.date ?? 0) - new Date(left.requests[0]?.date ?? 0))
    page.value = Math.min(page.value, pageCount.value)
  } catch (error) {
    if (requestId !== currentRequestId || error.name === 'AbortError') return
    errorMessage.value = 'Ошибка при загрузке данных. Попробуйте позже'
  } finally {
    if (requestId === currentRequestId) {
      isLoading.value = false
      currentController = null
    }
  }
}

const mutationBusy = ref(false)
const mutationError = ref('')
/** Передаёт выбранную запись истории для выполнения действия. */
async function manageEntry(action, category, entry) {
  if (!props.isAdministrator || mutationBusy.value) return
  let description
  if (action === 'edit') {
    description = window.prompt('Описание', entry.description ?? entry.name ?? '')
    if (description === null || !description.trim()) return
  } else if (!window.confirm(category === 'defects'
    ? 'Удалить заявку на ремонт со всеми работами, фото и видео?'
    : 'Удалить работу с фото и видео?')) return
  mutationBusy.value = true
  mutationError.value = ''
  try {
    const response = await fetch(action === 'edit'
      ? `/api/vehicles/history/${category}/${entry.id}`
      : `/api/vehicles/${category}/${entry.id}`, {
      method: action === 'edit' ? 'PUT' : 'DELETE',
      headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' },
      ...(action === 'edit' ? { body: JSON.stringify({ description }) } : {}),
    })
    if (!response.ok) throw new Error('Не удалось сохранить изменения.')
    await search()
  } catch (error) { mutationError.value = error.message }
  finally { mutationBusy.value = false }
}

/** Открывает вложения выбранной записи истории ремонта. */
function openMedia(items, type) {
  if (!items.length) return
  mediaViewerItems.value = items.map((media) => ({
    ...media,
    source: media.source ?? 'defect',
  }))
  mediaViewerType.value = type
  mediaViewerOpen.value = true
}

/** Закрывает просмотр вложений и очищает выбранные медиа. */
function closeMedia() {
  mediaViewerOpen.value = false
  mediaViewerItems.value = []
}
</script>

<template>
  <section class="repair-history">
    <form class="repair-history__search" role="search" @submit.prevent="search">
      <div class="repair-history__filters">
        <div>
          <label for="repair-history-number">Госномер или внутренний номер</label>
          <input id="repair-history-number" v-model="plateNumber" autocomplete="off" maxlength="32" placeholder="Например, А123ВС или 901" type="search" />
        </div>
        <div>
          <label for="repair-history-reason">Причина простоя</label>
          <input id="repair-history-reason" v-model="downtimeReason" placeholder="Например, утечка антифриза" type="search" />
        </div>
      </div>
      <div class="repair-history__search-row">
        <button type="button" @click="showAll">Вся история</button>
        <button type="submit" :disabled="isLoading || mutationBusy">Обновить</button>
      </div>
      <p class="repair-history__hint">Фильтры можно использовать вместе. Поиск по номеру работает и по части номера.</p>
    </form>

    <div v-if="isLoading" class="repair-history__loading" role="status">
      <span class="repair-history__spinner" aria-hidden="true"></span>
      <span>Загрузка истории ремонта...</span>
    </div>

    <p v-else-if="errorMessage" class="repair-history__message" role="alert">
      {{ errorMessage }}
    </p>

    <p v-if="mutationError" role="alert">{{ mutationError }}</p>
    <template v-if="!isLoading && !errorMessage">
      <p class="repair-history__hint" role="status">Техника: {{ filteredHistories.length }} · Заявки: {{ requestCount }}</p>
      <p v-if="!filteredHistories.length" class="repair-history__message" role="status">{{ plateNumber.trim() || downtimeReason.trim() ? 'По выбранным фильтрам ничего не найдено.' : 'История ремонта пока пуста.' }}</p>
      <VehicleCard v-for="history in visibleHistories" :key="history.vehicle.id"
        :vehicle="history.vehicle" :requests="history.requests"
        :is-administrator="isAdministrator" :busy="mutationBusy"
        @manage-entry="manageEntry" @view-media="openMedia" />
      <div v-if="pageCount > 1" class="repair-history__search-row repair-history__pagination">
        <button type="button" :disabled="page <= 1" @click="page--">Назад</button>
        <span aria-live="polite">{{ page }} / {{ pageCount }}</span>
        <button type="button" :disabled="page >= pageCount" @click="page++">Далее</button>
      </div>
    </template>

    <RepairMediaViewer
      :open="mediaViewerOpen"
      :items="mediaViewerItems"
      :media-type="mediaViewerType"
      :token="token"
      @close="closeMedia"
    />
  </section>
</template>

<style scoped>
.repair-history {
  display: grid;
  gap: 0.65rem;
}

.repair-history__search,
.repair-history__loading,
.repair-history__message {
  padding: 0.75rem;
  border: 1px solid var(--border);
  border-radius: 1rem;
  background: var(--bg-surface);
}

.repair-history__search label {
  display: block;
  margin-bottom: 0.3rem;
  color: var(--text-main);
  font-weight: 650;
}

.repair-history__search { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: end; gap: 0.5rem 0.75rem; }
.repair-history__filters { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem; }
.repair-history__pagination { align-items: center; justify-content: center; }
.repair-history__search-row {
  display: flex;
  gap: 0.65rem;
}

.repair-history__filters input {
  min-width: 0;
  width: 100%;
  box-sizing: border-box;
  padding: 0.5rem 0.7rem;
  min-height: 40px;
  border: 1px solid var(--border);
  border-radius: 0.65rem;
  font: inherit;
}

.repair-history__search-row button {
  padding: 0.5rem 0.85rem;
  min-height: 40px;
  border: 0;
  border-radius: 0.65rem;
  background: var(--accent);
  color: var(--bg-surface);
  font: inherit;
  font-weight: 650;
  cursor: pointer;
}

.repair-history__search-row button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.repair-history__hint {
  grid-column: 1 / -1;
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.repair-history__message[role="alert"] {
  color: var(--system-red);
}

.repair-history__loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  min-height: 7rem;
  color: var(--text-secondary);
}

.repair-history__spinner {
  width: 1.35rem;
  height: 1.35rem;
  border: 3px solid var(--border);
  border-top-color: var(--accent);
  border-radius: 50%;
  animation: repair-history-spin 0.75s linear infinite;
}

.repair-history__message {
  margin: 0;
}

@keyframes repair-history-spin {
  to {
    transform: rotate(360deg);
  }
}

@media (max-width: 900px) {
  .repair-history__search { grid-template-columns: minmax(0, 1fr); }
}

@media (max-width: 560px) {
  .repair-history__filters { grid-template-columns: minmax(0, 1fr); }
  .repair-history__search-row {
    flex-wrap: wrap;
  }
}
</style>
