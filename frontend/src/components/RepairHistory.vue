<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import VehicleCard from './VehicleCard.vue'

const props = defineProps({
  token: { type: String, required: true },
  searchVehicleHistory: { type: Function, required: true },
})

const plateNumber = ref('')
const result = ref(null)
const isLoading = ref(false)
const errorMessage = ref('')
const mediaViewerOpen = ref(false)
const mediaViewerItems = ref([])
const mediaViewerType = ref('image')

let debounceTimer
let currentRequestId = 0
let currentController

watch(plateNumber, (value) => {
  clearTimeout(debounceTimer)
  currentController?.abort()
  currentRequestId++
  result.value = null
  errorMessage.value = ''

  if (!value.trim()) {
    errorMessage.value = 'Введите госномер или внутренний номер.'
    isLoading.value = false
    return
  }

  if (normalizedNumber(value).length < 3) {
    errorMessage.value = 'Введите не менее 3 символов.'
    isLoading.value = false
    return
  }

  debounceTimer = setTimeout(() => search(), 300)
})

onBeforeUnmount(() => {
  clearTimeout(debounceTimer)
  currentController?.abort()
})

function normalizedNumber(value) {
  return String(value ?? '').replace(/[\s-]+/g, '').toLocaleUpperCase('ru-RU')
}

async function search() {
  const query = plateNumber.value.trim()
  if (!query) {
    errorMessage.value = 'Введите госномер или внутренний номер.'
    result.value = null
    return
  }
  if (normalizedNumber(query).length < 3) {
    errorMessage.value = 'Введите не менее 3 символов.'
    result.value = null
    return
  }

  clearTimeout(debounceTimer)
  currentController?.abort()
  const controller = new AbortController()
  currentController = controller
  const requestId = ++currentRequestId
  isLoading.value = true
  result.value = null
  errorMessage.value = ''

  try {
    const response = await props.searchVehicleHistory(query, controller.signal)
    if (requestId !== currentRequestId) return

    if (!response) {
      errorMessage.value = 'Автотранспорт не найден. Проверьте номер'
      return
    }

    result.value = {
      ...response,
      requests: [...(response.requests ?? [])]
        .map((request) => ({
          ...request,
          works: [...(request.works ?? [])].sort(
            (left, right) => new Date(left.date ?? 0) - new Date(right.date ?? 0),
          ),
        }))
        .sort(
          (left, right) => new Date(right.date ?? 0) - new Date(left.date ?? 0),
        ),
    }
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

function openMedia(items, type) {
  if (!items.length) return
  mediaViewerItems.value = items.map((media) => ({
    ...media,
    source: media.source ?? 'defect',
  }))
  mediaViewerType.value = type
  mediaViewerOpen.value = true
}

function closeMedia() {
  mediaViewerOpen.value = false
  mediaViewerItems.value = []
}
</script>

<template>
  <section class="repair-history">
    <form class="repair-history__search" role="search" @submit.prevent="search">
      <label for="repair-history-number">Госномер или внутренний номер</label>
      <div class="repair-history__search-row">
        <input
          id="repair-history-number"
          v-model="plateNumber"
          autocomplete="off"
          maxlength="32"
          placeholder="Например, А123ВС или 901"
          type="search"
        />
        <button type="submit" :disabled="!plateNumber.trim() || isLoading">
          Найти
        </button>
      </div>
      <p v-if="!plateNumber.trim()" class="repair-history__hint">
        Введите не менее 3 символов.
      </p>
      <p
        v-else-if="normalizedNumber(plateNumber).length < 3"
        class="repair-history__hint repair-history__hint--warning"
      >
        Введите не менее 3 символов.
      </p>
    </form>

    <div v-if="isLoading" class="repair-history__loading" role="status">
      <span class="repair-history__spinner" aria-hidden="true"></span>
      <span>Загрузка истории ремонта...</span>
    </div>

    <p v-else-if="errorMessage" class="repair-history__message" role="alert">
      {{ errorMessage }}
    </p>

    <VehicleCard
      v-else-if="result"
      :vehicle="result.vehicle"
      :requests="result.requests"
      @view-media="openMedia"
    />

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
  gap: 1rem;
}

.repair-history__search,
.repair-history__loading,
.repair-history__message {
  padding: 1.25rem;
  border: 1px solid #e1e5eb;
  border-radius: 1rem;
  background: #fff;
}

.repair-history__search label {
  display: block;
  margin-bottom: 0.55rem;
  color: #273244;
  font-weight: 650;
}

.repair-history__search-row {
  display: flex;
  gap: 0.65rem;
}

.repair-history__search-row input {
  min-width: 0;
  flex: 1;
  padding: 0.75rem 0.9rem;
  border: 1px solid #cbd3df;
  border-radius: 0.65rem;
  font: inherit;
}

.repair-history__search-row button {
  padding: 0.7rem 1.25rem;
  border: 0;
  border-radius: 0.65rem;
  background: #0878e5;
  color: #fff;
  font: inherit;
  font-weight: 650;
  cursor: pointer;
}

.repair-history__search-row button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.repair-history__hint {
  margin: 0.5rem 0 0;
  color: #687386;
  font-size: 0.9rem;
}

.repair-history__hint--warning,
.repair-history__message {
  color: #b42318;
}

.repair-history__loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  min-height: 7rem;
  color: #586477;
}

.repair-history__spinner {
  width: 1.35rem;
  height: 1.35rem;
  border: 3px solid #d5e7fb;
  border-top-color: #0878e5;
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

@media (max-width: 560px) {
  .repair-history__search-row {
    flex-direction: column;
  }
}
</style>
