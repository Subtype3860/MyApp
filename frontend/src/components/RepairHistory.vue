<script setup>
import FleetActionMenu from './FleetActionMenu.vue'
import { computed, nextTick, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import VehicleCard from './VehicleCard.vue'
import { forecastGroups } from '../utils/gtoForecast'

const props = defineProps({
  token: { type: String, required: true },
  isAdministrator: { type: Boolean, default: false },
  loadHistory: { type: Function, required: true },
  currentUserId: { type: String, default: '' },
  isMechanic: { type: Boolean, default: false },
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
  return forecastGroups(histories.value).flatMap(group => group.rows).flatMap(history => {
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
const historyDialog = ref(null)
const selectedVehicleId = ref('')
const selectedHistory = computed(() => filteredHistories.value.find(item => item.vehicle.id === selectedVehicleId.value) ?? histories.value.find(item => item.vehicle.id === selectedVehicleId.value))
async function openHistory(history) {
  selectedVehicleId.value = history.vehicle.id
  await nextTick()
  historyDialog.value?.showModal()
}
function closeHistory() {
  if (mutationBusy.value) return
  historyDialog.value?.close()
  selectedVehicleId.value = ''
}
const requestCount = computed(() => filteredHistories.value.reduce((sum, history) => sum + history.requests.length, 0))
watch([plateNumber, downtimeReason], () => { page.value = 1 })
onMounted(search)
onBeforeUnmount(() => { currentController?.abort(); editDialog.value?.close(); historyDialog.value?.close() })
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

function canEditRequest(entry) {
  return props.isAdministrator || props.isMechanic || Boolean(props.currentUserId && entry.createdById && String(entry.createdById).toLowerCase() === props.currentUserId.toLowerCase())
}
provide('canEditHistoryRequest', canEditRequest)
const editDialog = ref(null)
const editEntry = ref(null)
const editCategory = ref('defects')
const editDescription = ref('')
function closeEdit() {
  if (mutationBusy.value) return
  editDialog.value?.close()
  editEntry.value = null
  mutationError.value = ''
}
async function saveEdit() {
  if (mutationBusy.value || !editEntry.value || !editDescription.value.trim()) return
  if (!(props.isAdministrator || editCategory.value === 'defects' && canEditRequest(editEntry.value))) return
  mutationBusy.value = true
  mutationError.value = ''
  try {
    const response = await fetch(`/api/vehicles/history/${editCategory.value}/${editEntry.value.id}`, {
      method: 'PUT', headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ description: editDescription.value.trim() }),
    })
    if (!response.ok) throw new Error('Не удалось сохранить изменения. Проверьте доступ и повторите попытку.')
    editDialog.value?.close()
    editEntry.value = null
    await search()
  } catch (error) { mutationError.value = error.message }
  finally { mutationBusy.value = false }
}
const mutationBusy = ref(false)
const mutationError = ref('')
/** Передаёт выбранную запись истории для выполнения действия. */
async function manageEntry(action, category, entry) {
  if (mutationBusy.value) return
  if (action === 'edit') {
    if (!(props.isAdministrator || category === 'defects' && canEditRequest(entry))) return
    editEntry.value = entry
    editCategory.value = category
    editDescription.value = entry.description ?? entry.name ?? ''
    mutationError.value = ''
    await nextTick()
    editDialog.value?.showModal()
    return
  }
  if (!props.isAdministrator || !window.confirm(category === 'defects'
    ? 'Удалить заявку на ремонт со всеми работами, фото и видео?'
    : 'Удалить работу с фото и видео?')) return
  mutationBusy.value = true
  mutationError.value = ''
  try {
    const response = await fetch(`/api/vehicles/${category}/${entry.id}`, {
      method: 'DELETE', headers: { Authorization: `Bearer ${props.token}` },
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
        <FleetActionMenu label="Меню истории ремонта">
<button type="button" @click="showAll">Вся история</button>
        <button type="button" :disabled="isLoading || mutationBusy" @click="search">Обновить</button>
</FleetActionMenu>
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
      <ul v-if="visibleHistories.length" class="repair-history__list" aria-label="Техника в истории ремонта">
        <li v-for="history in visibleHistories" :key="history.vehicle.id">
          <button type="button" class="repair-history__row" aria-haspopup="dialog" @click="openHistory(history)">
            {{ history.vehicle.modelName || 'Модель не указана' }} — Гар. № {{ history.vehicle.garageNumber ?? 'не указан' }}
          </button>
        </li>
      </ul>
      <div v-if="pageCount > 1" class="repair-history__search-row repair-history__pagination">
        <button type="button" :disabled="page <= 1" @click="page--">Назад</button>
        <span aria-live="polite">{{ page }} / {{ pageCount }}</span>
        <button type="button" :disabled="page >= pageCount" @click="page++">Далее</button>
      </div>
    </template>

    <dialog ref="historyDialog" class="repair-history__vehicle-dialog" aria-label="Заявки техники" @cancel="event => { if (mutationBusy) event.preventDefault(); else closeHistory() }">
      <div class="repair-history__dialog-toolbar"><strong>История ремонта</strong><button type="button" :disabled="mutationBusy" @click="closeHistory">Закрыть</button></div>
      <p v-if="isLoading" role="status">Обновление истории…</p>
      <p v-if="errorMessage || mutationError" role="alert">{{ errorMessage || mutationError }}</p>
      <VehicleCard v-if="selectedHistory" :key="selectedHistory.vehicle.id"
        :vehicle="selectedHistory.vehicle" :requests="selectedHistory.requests"
        :is-administrator="isAdministrator" :busy="mutationBusy"
        @manage-entry="manageEntry" @view-media="openMedia" />
    </dialog>
    <dialog ref="editDialog" class="repair-history__edit-dialog" aria-labelledby="history-edit-title" @cancel="event => { if (mutationBusy) event.preventDefault(); else closeEdit() }">
      <form @submit.prevent="saveEdit">
        <h3 id="history-edit-title">{{ editCategory === 'defects' ? 'Редактировать заявку' : 'Редактировать работу' }}</h3>
        <label for="history-edit-description">Описание</label>
        <textarea id="history-edit-description" v-model="editDescription" rows="6" required :disabled="mutationBusy" autofocus></textarea>
        <p v-if="mutationError" role="alert">{{ mutationError }}</p>
        <div class="repair-history__search-row">
          <button type="submit" :disabled="mutationBusy || !editDescription.trim()">{{ mutationBusy ? 'Сохранение…' : 'Сохранить' }}</button>
          <button type="button" :disabled="mutationBusy" @click="closeEdit">Отмена</button>
        </div>
      </form>
    </dialog>
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
.repair-history__list { list-style: none; padding: 0; margin: 0; border: 1px solid var(--border); border-radius: 12px; overflow: hidden; }
.repair-history__list li + li { border-top: 1px solid var(--border); }
.repair-history__row { width: 100%; min-height: 52px; padding: 14px 16px; border: 0; background: var(--bg-surface); color: var(--text-main); text-align: left; font: inherit; font-weight: 650; cursor: pointer; overflow-wrap: anywhere; }
.repair-history__row:hover { background: var(--bg-main); }
.repair-history__row:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.repair-history__vehicle-dialog { width: min(1000px, calc(100vw - 24px)); max-height: calc(100dvh - 32px); box-sizing: border-box; overflow: auto; padding: 16px; border: 1px solid var(--border); border-radius: 14px; background: var(--bg-surface); color: var(--text-main); }
.repair-history__vehicle-dialog::backdrop { background: rgb(0 0 0 / 50%); }
.repair-history__dialog-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 12px; }

.repair-history__edit-dialog { width: min(600px, calc(100vw - 32px)); max-height: calc(100dvh - 32px); box-sizing: border-box; overflow: auto; padding: 20px; border: 1px solid var(--border); border-radius: 14px; background: var(--bg-surface); color: var(--text-main); }
.repair-history__edit-dialog::backdrop { background: rgb(0 0 0 / 50%); }
.repair-history__edit-dialog form { display: grid; gap: 12px; }
.repair-history__edit-dialog textarea { width: 100%; box-sizing: border-box; resize: vertical; font: inherit; }

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
