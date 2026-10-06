<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  navigationCollapsed: { type: Boolean, required: true },
  role: { type: String, required: true },
  section: { type: String, required: true },
  token: { type: String, required: true },
})

const emit = defineEmits(['navigate-section'])

const vehicles = ref([])
const selectedVehicleId = ref('')
const journal = ref(null)
const vehicleQuery = ref('')
const vehicleSearchMatches = ref([])
const fromDate = ref('')
const toDate = ref('')
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const profile = ref(null)
const pendingWorkPhotos = ref([])
const pendingWorkVideos = ref([])
const pendingDefectPhotos = ref([])
const pendingDefectVideos = ref([])
const photoViewerUrl = ref('')
const photoViewerName = ref('')
const mediaViewerType = ref('image')
const hoursImportResult = ref(null)
const partsRequestFiles = reactive({})
const partsRequestForms = reactive({})
const selectedWorkId = ref('')

let mediaRequestId = 0

const today = toLocalDateInput(new Date())

const forms = reactive({
  defects: {
    errorCode: '',
    symptoms: '',
    downtimeStartedAt: toLocalDateTimeInput(new Date()),
  },
  works: {
    defectId: '',
    cause: '',
    description: '',
    status: 'repaired',
    requiredParts: '',
  },
})

const vehicleOptions = computed(() =>
  vehicles.value.flatMap((vehicle) => [
    ...(vehicle.garageNumber === null
      ? []
      : [{ value: String(vehicle.garageNumber), vehicle }]),
    ...(vehicle.stateNumber
      ? [{ value: vehicle.stateNumber, vehicle }]
      : []),
  ]),
)
const activeTab = computed(() => {
  if (props.section === 'works') return 'works'
  if (props.section === 'defects') return 'defects'
  return null
})
const currentEntries = computed(() => {
  if (props.section === 'requests') {
    return journal.value?.works?.filter(
      (work) => work.status === 'awaiting_parts',
    ) ?? []
  }
  if (!activeTab.value) return []
  return journal.value?.[activeTab.value] ?? []
})
const sectionTitle = computed(() => ({
  defects: 'Неисправность',
  works: 'Ремонт',
  requests: 'Заявка ОЗЧ',
  hours: 'Моточасы',
}[props.section] ?? 'Техника'))
const selectedVehicle = computed(() =>
  vehicles.value.find((vehicle) => vehicle.id === selectedVehicleId.value),
)
const selectedWork = computed(() =>
  journal.value?.works?.find((work) => work.id === selectedWorkId.value) ?? null,
)
const latestHours = computed(() => {
  const values = journal.value?.hours ?? []
  return values.length ? values[0].engineHours : null
})
const defectCount = computed(() => journal.value?.defects?.length ?? 0)
const normalizedProfession = computed(() =>
  profile.value?.position?.trim().toLocaleLowerCase('ru-RU') ?? '',
)
const isAdministrator = computed(() => props.role === 'administrator')
const canCreateDefect = computed(() =>
  isAdministrator.value || normalizedProfession.value === 'механик',
)
const canExecute = computed(() =>
  isAdministrator.value ||
  ['слесарь', 'электрослесарь', 'сервисный инженер'].includes(
    normalizedProfession.value,
  ),
)
const canManageParts = computed(() =>
  isAdministrator.value || normalizedProfession.value === 'старший механик',
)
const availableDefects = computed(() =>
  journal.value?.defects?.filter((defect) =>
    defect.status === 'in_progress' && (
      isAdministrator.value ||
      defect.assignedToId === profile.value?.id
    ),
  ) ?? [],
)

onMounted(async () => {
  await Promise.all([loadVehicles(), loadProfile()])
  await loadWorksOnEntry()
})
watch(() => props.section, loadWorksOnEntry)
onBeforeUnmount(closePhoto)

async function loadWorksOnEntry() {
  if (props.section !== 'works' || !vehicles.value.length) return
  if (!selectedVehicleId.value) {
    await chooseVehicle(vehicles.value[0])
    return
  }
  await loadJournal()
}

function authHeaders(json = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  }
}

async function loadProfile() {
  const response = await fetch('/api/profile', { headers: authHeaders() })
  if (response.ok) profile.value = await response.json()
}

async function loadVehicles() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/vehicles', {
      headers: authHeaders(),
    })
    if (!response.ok) throw new Error('Не удалось загрузить список техники.')
    vehicles.value = await response.json()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

function selectVehicle() {
  const query = normalizeVehicleNumber(vehicleQuery.value)
  if (!query) {
    errorMessage.value = 'Введите гаражный или государственный номер.'
    return
  }
  const exactMatches = vehicles.value.filter((vehicle) =>
    normalizeVehicleNumber(vehicle.garageNumber) === query ||
    normalizeVehicleNumber(vehicle.stateNumber) === query,
  )
  const matches = exactMatches.length
    ? exactMatches
    : vehicles.value.filter((vehicle) =>
        normalizeVehicleNumber(vehicle.garageNumber).includes(query) ||
        normalizeVehicleNumber(vehicle.stateNumber).includes(query),
      )
  if (matches.length !== 1) {
    selectedVehicleId.value = ''
    journal.value = null
    vehicleSearchMatches.value = matches
    errorMessage.value = matches.length
      ? ''
      : 'Техника с таким номером не найдена.'
    return
  }
  chooseVehicle(matches[0])
}

async function chooseVehicle(vehicle) {
  vehicleSearchMatches.value = []
  errorMessage.value = ''
  vehicleQuery.value = vehicle.stateNumber ||
    String(vehicle.garageNumber ?? '')
  selectedVehicleId.value = vehicle.id
  await loadJournal()
}

function normalizeVehicleNumber(value) {
  const lookalikes = {
    А: 'A', В: 'B', Е: 'E', К: 'K', М: 'M', Н: 'H',
    О: 'O', Р: 'P', С: 'C', Т: 'T', У: 'Y', Х: 'X',
  }
  return String(value ?? '')
    .toLocaleUpperCase('ru-RU')
    .replace(/[АВЕКМНОРСТУХ]/g, (letter) => lookalikes[letter])
    .replace(/[^A-Z0-9]/g, '')
}

async function loadJournal() {
  if (!selectedVehicleId.value) {
    journal.value = null
    return
  }
  isLoading.value = true
  errorMessage.value = ''
  try {
    const query = new URLSearchParams()
    if (fromDate.value) query.set('from', fromDate.value)
    if (toDate.value) query.set('to', toDate.value)
    const suffix = query.size ? `?${query}` : ''
    const response = await fetch(
      `/api/vehicles/${selectedVehicleId.value}/journal${suffix}`,
      { headers: authHeaders() },
    )
    if (!response.ok) throw new Error('Не удалось загрузить журнал техники.')
    journal.value = await response.json()
    if (props.section === 'works') {
      const works = journal.value.works ?? []
      if (!works.some((work) => work.id === selectedWorkId.value)) {
        selectedWorkId.value = works[0]?.id ?? ''
      }
    }
    for (const work of journal.value.works ?? []) {
      partsRequestForms[work.id] ??= {
        requestNumber: work.partsRequestNumber ?? '',
        requestDate: work.partsRequestDate ?? today,
      }
    }
  } catch (error) {
    journal.value = null
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

async function addEntry() {
  if (!selectedVehicleId.value || !activeTab.value) return
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const payload = { ...forms[activeTab.value] }
    if (activeTab.value === 'defects') {
      payload.downtimeStartedAt = new Date(payload.downtimeStartedAt).toISOString()
    }
    const response = await fetch(
      `/api/vehicles/${selectedVehicleId.value}/${activeTab.value}`,
      {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify(payload),
      },
    )
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      const validation = problem?.errors
        ? Object.values(problem.errors).flat()[0]
        : null
      throw new Error(validation ?? problem?.detail ?? 'Не удалось сохранить запись.')
    }
    const created = await response.json()
    if (activeTab.value === 'defects') {
      try {
        await uploadEntryMedia(
          'defects',
          created.id,
          pendingDefectPhotos.value,
          pendingDefectVideos.value,
        )
      } catch (error) {
        await loadJournal()
        throw new Error(`Дефект сохранён. ${error.message}`)
      }
    }

    if (activeTab.value === 'works') {
      try {
        await uploadEntryMedia(
          'works',
          created.id,
          pendingWorkPhotos.value,
          pendingWorkVideos.value,
        )
      } catch (error) {
        await loadJournal()
        throw new Error(`Работа сохранена. ${error.message}`)
      }
    }
    resetForm(activeTab.value)
    successMessage.value = activeTab.value === 'defects'
      ? 'Неисправность зарегистрирована.'
      : 'Результат работы сохранён.'
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function claimDefect(defect) {
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/defects/${defect.id}/claim`, {
      method: 'POST',
      headers: authHeaders(),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new Error(problem?.detail ?? problem?.title ?? 'Не удалось принять задание.')
    }
    forms.works.defectId = defect.id
    successMessage.value = 'Задание принято. Время начала работ зафиксировано.'
    await loadJournal()
    emit('navigate-section', 'works')
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function importHours(event) {
  const file = event.target.files[0]
  event.target.value = ''
  if (!file) return
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  hoursImportResult.value = null
  try {
    const data = new FormData()
    data.append('file', file)
    const response = await fetch('/api/vehicles/hours/import', {
      method: 'POST',
      headers: authHeaders(),
      body: data,
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      const validation = result?.errors
        ? Object.values(result.errors).flat()[0]
        : null
      throw new Error(
        validation ?? result?.detail ?? 'Не удалось импортировать моточасы.',
      )
    }
    hoursImportResult.value = result
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

function selectWorkPhotos(event) {
  const description = forms.works.description
  const files = [...event.target.files]
  event.target.value = ''
  const validation = validatePhotoFiles(files, 0)
  if (validation) {
    pendingWorkPhotos.value = []
    errorMessage.value = validation
    return
  }
  errorMessage.value = ''
  pendingWorkPhotos.value = files
  forms.works.description = description
}

function selectWorkVideos(event) {
  const description = forms.works.description
  const files = [...event.target.files]
  event.target.value = ''
  const validation = validateVideoFiles(files)
  if (validation) {
    pendingWorkVideos.value = []
    errorMessage.value = validation
    return
  }
  errorMessage.value = ''
  pendingWorkVideos.value = files
  forms.works.description = description
}

function selectWorkMedia(event) {
  const description = forms.works.description
  const files = [...event.target.files]
  event.target.value = ''
  const photos = files.filter((file) => file.type.startsWith('image/'))
  const videos = files.filter((file) => file.type.startsWith('video/'))
  const photoValidation = validatePhotoFiles(photos, 0)
  const videoValidation = validateVideoFiles(videos)
  if (photoValidation || videoValidation) {
    pendingWorkPhotos.value = []
    pendingWorkVideos.value = []
    errorMessage.value = photoValidation || videoValidation
    return
  }
  errorMessage.value = ''
  pendingWorkPhotos.value = photos
  pendingWorkVideos.value = videos
  forms.works.description = description
}

function selectDefectPhotos(event) {
  const files = [...event.target.files]
  event.target.value = ''
  const validation = validatePhotoFiles(files, 0)
  if (validation) {
    pendingDefectPhotos.value = []
    errorMessage.value = validation
    return
  }
  errorMessage.value = ''
  pendingDefectPhotos.value = files
}

function selectDefectVideos(event) {
  const files = [...event.target.files]
  event.target.value = ''
  const validation = validateVideoFiles(files)
  if (validation) {
    pendingDefectVideos.value = []
    errorMessage.value = validation
    return
  }
  errorMessage.value = ''
  pendingDefectVideos.value = files
}

async function uploadFiles(category, entryId, mediaType, files) {
  for (const file of files) {
    const data = new FormData()
    data.append('file', file)
    const response = await fetch(`/api/vehicles/${category}/${entryId}/${mediaType}`, {
      method: 'POST',
      headers: authHeaders(),
      body: data,
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      const validation = problem?.errors
        ? Object.values(problem.errors).flat()[0]
        : null
      throw new Error(
        validation ?? problem?.detail ?? `Не удалось загрузить «${file.name}».`,
      )
    }
  }
}

async function uploadEntryMedia(category, entryId, photos, videos) {
  await uploadFiles(category, entryId, 'photos', photos)
  await uploadFiles(category, entryId, 'videos', videos)
}

async function addPhotosToWork(workId, event) {
  const files = [...event.target.files]
  event.target.value = ''
  if (!files.length) return
  const work = journal.value?.works.find((item) => item.id === workId)
  const validation = validatePhotoFiles(files, work?.photos.length ?? 0)
  if (validation) {
    errorMessage.value = validation
    return
  }
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    await uploadFiles('works', workId, 'photos', files)
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function addPhotosToDefect(defectId, event) {
  const files = [...event.target.files]
  event.target.value = ''
  if (!files.length) return
  const defect = journal.value?.defects.find((item) => item.id === defectId)
  const validation = validatePhotoFiles(files, defect?.photos.length ?? 0)
  if (validation) {
    errorMessage.value = validation
    return
  }
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    await uploadFiles('defects', defectId, 'photos', files)
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function addVideos(category, entryId, event) {
  const files = [...event.target.files]
  event.target.value = ''
  if (!files.length) return
  const validation = validateVideoFiles(files)
  if (validation) {
    errorMessage.value = validation
    return
  }
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    await uploadFiles(category, entryId, 'videos', files)
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

function validatePhotoFiles(files, existingCount) {
  if (existingCount + files.length > 10) {
    return 'Для одной записи можно сохранить не более 10 фотографий.'
  }
  const allowedTypes = ['image/jpeg', 'image/png', 'image/webp']
  if (files.some((file) => !allowedTypes.includes(file.type) || file.size > 8 * 1024 * 1024)) {
    return 'Разрешены JPEG, PNG и WebP размером не более 8 МБ.'
  }
  return ''
}

function validateVideoFiles(files) {
  const allowedTypes = ['video/mp4', 'video/webm', 'video/quicktime']
  if (files.some((file) => !allowedTypes.includes(file.type) || file.size > 100 * 1024 * 1024)) {
    return 'Разрешены MP4, WebM и MOV размером не более 100 МБ.'
  }
  return ''
}

async function openMedia(media, route, type = 'image') {
  closePhoto()
  const requestId = mediaRequestId
  const response = await fetch(`/api/vehicles/${route}/${media.id}`, {
    headers: authHeaders(),
  })
  if (requestId !== mediaRequestId) return
  if (!response.ok) {
    errorMessage.value = 'Не удалось загрузить вложение.'
    return
  }
  const blob = await response.blob()
  if (requestId !== mediaRequestId) return
  photoViewerUrl.value = URL.createObjectURL(blob)
  photoViewerName.value = media.fileName
  mediaViewerType.value = type
}

function openPhoto(photo, category = 'work') {
  return openMedia(photo, `${category}-photos`)
}

function closePhoto() {
  mediaRequestId++
  if (photoViewerUrl.value) URL.revokeObjectURL(photoViewerUrl.value)
  photoViewerUrl.value = ''
  photoViewerName.value = ''
}

async function deletePhoto(photoId, category = 'work') {
  if (!window.confirm('Удалить фотографию?')) return
  const response = await fetch(`/api/vehicles/${category}-photos/${photoId}`, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  if (!response.ok) {
    errorMessage.value = 'Не удалось удалить фотографию.'
    return
  }
  closePhoto()
  await loadJournal()
}

async function deleteMedia(mediaId, route) {
  if (!window.confirm('Удалить вложение?')) return
  const response = await fetch(`/api/vehicles/${route}/${mediaId}`, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  if (!response.ok) {
    errorMessage.value = 'Не удалось удалить вложение.'
    return
  }
  closePhoto()
  await loadJournal()
}

async function savePartsRequest(work) {
  const form = partsRequestForms[work.id] ?? {}
  const data = new FormData()
  data.append('requestNumber', form.requestNumber ?? '')
  data.append('requestDate', form.requestDate ?? '')
  if (partsRequestFiles[work.id]) data.append('file', partsRequestFiles[work.id])
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/works/${work.id}/parts-request`, {
      method: 'PUT',
      headers: authHeaders(),
      body: data,
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new Error(problem?.detail ?? problem?.title ?? 'Не удалось сохранить заявку.')
    }

    successMessage.value = 'Заявка на запчасти сохранена.'
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function deletePartsRequest(work) {
  if (!window.confirm('Удалить заявку на закупку?')) return
  isSaving.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/works/${work.id}/parts-request`, {
      method: 'DELETE',
      headers: authHeaders(),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new Error(problem?.detail ?? problem?.title ?? 'Не удалось удалить заявку.')
    }
    successMessage.value = 'Заявка на запчасти удалена.'
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function downloadPartsRequest(work) {
  const response = await fetch(`/api/vehicles/works/${work.id}/parts-request/file`, {
    headers: authHeaders(),
  })
  if (!response.ok) {
    errorMessage.value = 'Не удалось скачать файл заявки.'
    return
  }
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = work.partsRequestFileName
  link.click()
  URL.revokeObjectURL(url)
}

async function deleteDefect(defect) {
  const confirmed = window.confirm(
    'Удалить заявку на ремонт? Связанные работы, фотографии и видео также будут удалены.',
  )
  if (!confirmed) return
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const route = isAdministrator.value
      ? `/api/vehicles/defects/${defect.id}/history`
      : `/api/vehicles/defects/${defect.id}`
    const response = await fetch(route, {
      method: 'DELETE',
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error(
        response.status === 404
          ? 'Заявка на ремонт не найдена или у вас нет прав на её удаление.'
          : 'Не удалось удалить заявку на ремонт.',
      )
    }
    closePhoto()
    successMessage.value = 'Заявка, связанные работы и медиафайлы удалены.'
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function deleteWork(work) {
  if (!window.confirm('Удалить эту работу и все её фотографии и видео?')) return
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(
      `/api/vehicles/works/${work.id}/history`,
      {
        method: 'DELETE',
        headers: authHeaders(),
      },
    )
    if (!response.ok) {
      throw new Error(
        response.status === 404
          ? 'Работа не найдена или у вас нет прав на её удаление.'
          : 'Не удалось удалить работу.',
      )
    }
    closePhoto()
    successMessage.value = 'Работа и связанные медиафайлы удалены.'
    await loadJournal()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

function canDeleteDefect(defect) {
  return isAdministrator.value || defect.createdById === profile.value?.id
}

function resetForm(category) {
  if (category === 'defects') {
    Object.assign(forms.defects, {
      errorCode: '',
      symptoms: '',
      downtimeStartedAt: toLocalDateTimeInput(new Date()),
    })
    pendingDefectPhotos.value = []
    pendingDefectVideos.value = []
  } else {
    Object.assign(forms.works, {
      defectId: '',
      cause: '',
      description: '',
      status: 'repaired',
      requiredParts: '',
    })
    pendingWorkPhotos.value = []
    pendingWorkVideos.value = []
  }
}

function statusLabel(status) {
  return {
    new: 'Новое',
    in_progress: 'В работе',
    repaired: 'Исправен',
    faulty: 'Неисправен',
    awaiting_parts: 'ОЗЧ',
  }[status] ?? status
}

function formatDate(value) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('ru-RU').format(
    new Date(`${value}T00:00:00`),
  )
}

function formatDateTime(value) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}

function toLocalDateInput(value) {
  const offset = value.getTimezoneOffset() * 60_000
  return new Date(value.getTime() - offset).toISOString().slice(0, 10)
}

function toLocalDateTimeInput(value) {
  const offset = value.getTimezoneOffset() * 60_000
  return new Date(value.getTime() - offset).toISOString().slice(0, 16)
}

function formatNumber(value) {
  if (value === null || value === undefined || value === '') return '—'
  return Number(value).toLocaleString('ru-RU', { maximumFractionDigits: 2 })
}

function printReport() {
  window.print()
}
</script>

<template>
  <main
    class="home-page vehicle-page"
    :class="{ 'home-page--expanded': navigationCollapsed }"
  >
    <header class="home-header vehicle-screen-only">
      <div>
        <p class="eyebrow">УЧЁТ И ИСТОРИЯ</p>
        <h1>{{ sectionTitle }}</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <section class="vehicle-layout">
      <div class="vehicle-content">
        <section
          v-if="section === 'hours'"
          class="vehicle-controls macos-glass-panel"
        >
          <div class="vehicle-hours-import vehicle-field--full">
            <div>
              <strong>Импорт из Excel или CSV</strong>
              <span>
                Excel: обозначение техники и моточасы в первых двух столбцах.
                Формат CSV:
                <code>garage_number;model;engine_hours</code>.
                Дата — сегодня, существующее показание заменяется.
                Если моточасы не указаны, используется последнее показание
                машины, а при его отсутствии — 0.
              </span>
            </div>
            <label class="secondary-button">
              {{ isSaving ? 'Загрузка...' : 'Загрузить файл' }}
              <input
                accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                :disabled="isSaving"
                type="file"
                @change="importHours"
              />
            </label>
          </div>
          <div
            v-if="hoursImportResult"
            class="vehicle-import-result vehicle-field--full"
            :class="{ 'vehicle-import-result--warning': hoursImportResult.errors.length }"
          >
            <strong>Импортировано: {{ hoursImportResult.imported }}</strong>
            <ul v-if="hoursImportResult.errors.length">
              <li v-for="error in hoursImportResult.errors" :key="`${error.line}-${error.message}`">
                Строка {{ error.line }}: {{ error.message }}
              </li>
            </ul>
          </div>
        </section>

        <form
          v-else
          class="vehicle-search macos-glass-panel vehicle-screen-only"
          @submit.prevent="selectVehicle"
        >
          <label class="vehicle-field">
            <span>Гаражный или государственный номер</span>
            <input
              v-model="vehicleQuery"
              list="vehicle-number-options"
              placeholder="Например: 901 или 5113 CC 65"
              type="search"
            />
            <datalist id="vehicle-number-options">
              <option
                v-for="option in vehicleOptions"
                :key="`${option.vehicle.id}-${option.value}`"
                :value="option.value"
              >
                {{ option.vehicle.modelName }}
              </option>
            </datalist>
          </label>
          <button class="primary-button" type="submit">Найти технику</button>
          <div
            v-if="vehicleSearchMatches.length"
            class="vehicle-search-results"
          >
            <span>Найдено несколько машин. Выберите нужную:</span>
            <button
              v-for="vehicle in vehicleSearchMatches"
              :key="vehicle.id"
              type="button"
              @click="chooseVehicle(vehicle)"
            >
              <strong>{{ vehicle.modelName }}</strong>
              <span>
                Гар. № {{ vehicle.garageNumber ?? '—' }} ·
                {{ vehicle.stateNumber || 'без гос. номера' }}
              </span>
            </button>
          </div>
        </form>

        <p v-if="errorMessage" class="table-message table-message--error">
          {{ errorMessage }}
        </p>
        <p v-else-if="isLoading && !journal" class="table-message">
          Загрузка...
        </p>

        <template v-if="section !== 'hours' && selectedVehicle && journal">
          <section class="vehicle-card macos-glass-panel">
            <div>
              <p class="eyebrow">{{ selectedVehicle.groupName }}</p>
              <h2>{{ selectedVehicle.modelName }}</h2>
              <p>
                {{ selectedVehicle.typeName }} · Гар. №
                {{ selectedVehicle.garageNumber ?? '—' }}
              </p>
            </div>
            <div class="vehicle-identifiers">
              <span>Гос. номер <strong>{{ selectedVehicle.stateNumber || '—' }}</strong></span>
              <span>VIN <strong>{{ selectedVehicle.vin || '—' }}</strong></span>
            </div>
          </section>

          <section class="vehicle-summary">
            <article class="macos-glass-panel">
              <span>Последние моточасы</span>
              <strong>{{ formatNumber(latestHours) }}</strong>
            </article>
            <article class="macos-glass-panel">
              <span>Неисправности за период</span>
              <strong>{{ defectCount }}</strong>
            </article>
            <article class="macos-glass-panel">
              <span>Заявки за период</span>
              <strong>{{ journal.purchases.length }}</strong>
            </article>
            <article class="macos-glass-panel">
              <span>Ремонты за период</span>
              <strong>{{ journal.works.length }}</strong>
            </article>
          </section>

          <section class="vehicle-controls macos-glass-panel vehicle-screen-only">
            <div class="vehicle-period">
              <label class="vehicle-field">
                <span>Период с</span>
                <input v-model="fromDate" type="date" />
              </label>
              <label class="vehicle-field">
                <span>по</span>
                <input v-model="toDate" type="date" />
              </label>
              <button class="secondary-button" type="button" @click="loadJournal">
                Применить
              </button>
              <button class="secondary-button" type="button" @click="printReport">
                Создать отчёт
              </button>
            </div>

            <form
              v-if="
                (section === 'defects' && canCreateDefect) ||
                (section === 'works' && canExecute)
              "
              class="vehicle-entry-form"
              @submit.prevent="addEntry"
            >
              <template v-if="section === 'defects'">
                <label class="vehicle-field">
                  <span>Код ошибки (при наличии)</span>
                  <input v-model="forms.defects.errorCode" maxlength="100" />
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Симптомы неисправности</span>
                  <textarea v-model="forms.defects.symptoms" required rows="3"></textarea>
                </label>
                <label class="vehicle-field">
                  <span>Начало простоя</span>
                  <input
                    v-model="forms.defects.downtimeStartedAt"
                    required
                    type="datetime-local"
                  />
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Фото (до 10 шт., JPEG, PNG, WebP до 8 МБ)</span>
                  <input
                    accept="image/jpeg,image/png,image/webp"
                    multiple
                    type="file"
                    @change="selectDefectPhotos"
                  />
                  <small v-if="pendingDefectPhotos.length">
                    Выбрано: {{ pendingDefectPhotos.length }}
                  </small>
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Видео (MP4, WebM, MOV до 100 МБ)</span>
                  <input
                    accept="video/mp4,video/webm,video/quicktime"
                    multiple
                    type="file"
                    @change="selectDefectVideos"
                  />
                  <small v-if="pendingDefectVideos.length">
                    Выбрано: {{ pendingDefectVideos.length }}
                  </small>
                </label>
              </template>

              <template v-else>
                <label class="vehicle-field vehicle-field--full">
                  <span>Принятое задание</span>
                  <select v-model="forms.works.defectId" required>
                    <option value="" disabled>Выберите принятое задание</option>
                    <option
                      v-for="defect in availableDefects"
                      :key="defect.id"
                      :value="defect.id"
                    >
                      {{ defect.errorCode || 'Без кода' }} — {{ defect.symptoms }}
                    </option>
                  </select>
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Причина неисправности</span>
                  <textarea v-model="forms.works.cause" required rows="2"></textarea>
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Выполненные работы</span>
                  <textarea
                    v-model="forms.works.description"
                    placeholder="Отчёт о выполненной работе..."
                    required
                    rows="3"
                  ></textarea>
                </label>
                <label class="vehicle-field">
                  <span>Статус</span>
                  <select v-model="forms.works.status" required>
                    <option value="repaired">Исправен</option>
                    <option value="faulty">Неисправен</option>
                    <option value="awaiting_parts">ОЗЧ</option>
                  </select>
                </label>
                <label
                  v-if="forms.works.status === 'awaiting_parts'"
                  class="vehicle-field vehicle-field--wide"
                >
                  <span>Необходимые запчасти</span>
                  <textarea v-model="forms.works.requiredParts" required rows="2"></textarea>
                </label>
                <label class="vehicle-field vehicle-field--full">
                  <span>Фото и видео выполненной работы</span>
                  <input
                    accept="image/*,video/*"
                    multiple
                    type="file"
                    @change="selectWorkMedia"
                  />
                  <small v-if="pendingWorkPhotos.length || pendingWorkVideos.length">
                    Выбрано файлов:
                    {{ pendingWorkPhotos.length + pendingWorkVideos.length }}
                  </small>
                </label>
              </template>

              <button class="primary-button vehicle-save-button" type="submit" :disabled="isSaving">
                {{ isSaving ? 'Сохранение...' : (section === 'works' ? 'ВЫПОЛНЕНИЕ' : 'Добавить запись') }}
              </button>
            </form>
            <p
              v-else-if="section === 'defects'"
              class="vehicle-empty"
            >
              Новые неисправности регистрирует пользователь с профессией «Механик».
            </p>
            <p
              v-else-if="section === 'works'"
              class="vehicle-empty"
            >
              Задания выполняют слесари, электрослесари и сервисные инженеры.
            </p>
          </section>

          <p v-if="successMessage" class="form-message form-message--success">
            {{ successMessage }}
          </p>

          <section class="vehicle-journal macos-glass-panel vehicle-screen-journal">
            <div class="vehicle-journal-header">
              <div>
                <p class="eyebrow">ЖУРНАЛ</p>
                <h2>{{ sectionTitle }}</h2>
              </div>
              <span>{{ currentEntries.length }} записей</span>
            </div>

            <div class="data-table-scroll vehicle-table">
              <table v-if="section === 'defects'" class="vehicle-journal-table vehicle-defects-table">
                <thead><tr><th>Код</th><th>Симптомы</th><th>Начало простоя</th><th>Статус /<br />исполнитель</th><th>Медиа</th><th class="vehicle-screen-only"></th></tr></thead>
                <tbody>
                  <tr v-for="item in currentEntries" :key="item.id">
                    <td data-label="Код">{{ item.errorCode || '—' }}</td>
                    <td data-label="Симптомы">{{ item.symptoms }}</td>
                    <td data-label="Начало простоя">{{ formatDateTime(item.downtimeStartedAt) }}</td>
                    <td data-label="Статус">
                      <strong>{{ statusLabel(item.status) }}</strong>
                      <span v-if="item.assignedToName"> · {{ item.assignedToName }}</span>
                    </td>
                    <td data-label="Медиа">
                      <div class="vehicle-photo-actions">
                        <span v-if="!item.photos.length && !item.videos.length">Нет</span>
                        <span v-for="photo in item.photos" :key="photo.id" class="vehicle-photo-chip">
                          <button type="button" @click="openPhoto(photo, 'defect')">
                            {{ photo.fileName }}
                          </button>
                          <button
                            v-if="canDeleteDefect(item)"
                            class="vehicle-photo-delete"
                            type="button"
                            aria-label="Удалить фотографию"
                            @click="deletePhoto(photo.id, 'defect')"
                          >
                            ×
                          </button>
                        </span>
                        <span v-for="video in item.videos" :key="video.id" class="vehicle-photo-chip">
                          <button type="button" @click="openMedia(video, 'defect-videos', 'video')">
                            {{ video.fileName }}
                          </button>
                          <button
                            v-if="canDeleteDefect(item)"
                            class="vehicle-photo-delete"
                            type="button"
                            @click="deleteMedia(video.id, 'defect-videos')"
                          >
                            ×
                          </button>
                        </span>
                        <label v-if="canCreateDefect" class="vehicle-photo-upload">
                          + Фото
                          <input
                            accept="image/jpeg,image/png,image/webp"
                            multiple
                            type="file"
                            @change="addPhotosToDefect(item.id, $event)"
                          />
                        </label>
                        <label v-if="canCreateDefect" class="vehicle-photo-upload">
                          + Видео
                          <input
                            accept="video/mp4,video/webm,video/quicktime"
                            multiple
                            type="file"
                            @change="addVideos('defects', item.id, $event)"
                          />
                        </label>
                      </div>
                    </td>
                    <td class="vehicle-screen-only vehicle-row-action">
                      <div class="vehicle-row-actions">
                        <button
                          v-if="canExecute && item.status === 'new'"
                          class="primary-button"
                          type="button"
                          :disabled="isSaving"
                          @click="claimDefect(item)"
                        >
                          Принять задание
                        </button>
                        <button
                          v-if="canDeleteDefect(item)"
                          class="table-action-button table-action-button--danger"
                          type="button"
                          :disabled="isSaving"
                          @click="deleteDefect(item)"
                        >
                          Удалить
                        </button>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
              <table v-else-if="section === 'requests'" class="vehicle-journal-table">
                <thead><tr><th>Неисправность</th><th>Необходимые запчасти</th><th>Исполнитель</th><th>Заявка ОЗЧ</th></tr></thead>
                <tbody>
                  <tr v-for="item in currentEntries" :key="item.id">
                    <td data-label="Неисправность">
                      {{ item.defectNodeName || item.cause || '—' }}
                    </td>
                    <td data-label="Необходимые запчасти">
                      {{ item.requiredParts || '—' }}
                    </td>
                    <td data-label="Исполнитель">{{ item.performerName || '—' }}</td>
                    <td data-label="Заявка ОЗЧ">
                      <div v-if="canManageParts" class="vehicle-parts-request">
                        <input v-model="partsRequestForms[item.id].requestNumber" placeholder="Номер заявки" />
                        <input v-model="partsRequestForms[item.id].requestDate" type="date" />
                        <input type="file" @change="partsRequestFiles[item.id] = $event.target.files[0]" />
                        <button class="secondary-button" type="button" @click="savePartsRequest(item)">
                          {{ item.partsRequestNumber ? 'Изменить' : 'Сохранить' }}
                        </button>
                        <button
                          v-if="item.partsRequestNumber"
                          class="secondary-button"
                          type="button"
                          :disabled="isSaving"
                          @click="deletePartsRequest(item)"
                        >
                          Удалить
                        </button>
                      </div>
                      <span v-else>{{ item.partsRequestNumber || '—' }}</span>
                      <button
                        v-if="item.partsRequestFileName"
                        class="vehicle-media-link"
                        type="button"
                        @click="downloadPartsRequest(item)"
                      >
                        {{ item.partsRequestFileName }}
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
              <table v-else class="vehicle-journal-table vehicle-repairs-table">
                <thead><tr><th>Этап ремонта</th><th>Задание</th><th>Причина и работы</th><th>Статус / запчасти</th><th>Медиа</th><th v-if="isAdministrator" class="vehicle-screen-only"></th></tr></thead>
                <tbody>
                  <tr
                    v-for="item in currentEntries"
                    :key="item.id"
                    :class="{ 'vehicle-repair-row--selected': item.id === selectedWorkId }"
                  >
                    <td data-label="Этап ремонта">
                      <button
                        class="vehicle-work-stage-button"
                        type="button"
                        @click="selectedWorkId = item.id"
                      >
                        {{ formatDateTime(item.createdAt || item.workDate) }}
                      </button>
                    </td>
                    <td data-label="Задание">{{ item.defectNodeName || 'Старая запись без привязки' }}</td>
                    <td data-label="Работы">
                      <strong>{{ item.cause || 'Причина не указана' }}</strong>
                      <div>{{ item.description }}</div>
                      <small>{{ item.performerName }}</small>
                    </td>
                    <td data-label="Статус">
                      <strong>{{ statusLabel(item.status) }}</strong>
                      <div v-if="item.requiredParts">{{ item.requiredParts }}</div>
                    </td>
                    <td data-label="Медиа">
                      <div class="vehicle-photo-actions">
                        <span v-if="!item.photos.length && !item.videos.length">Нет</span>
                        <span v-for="photo in item.photos" :key="photo.id" class="vehicle-photo-chip">
                          <button type="button" @click="openPhoto(photo)">
                            {{ photo.fileName }}
                          </button>
                          <button
                            v-if="isAdministrator || canExecute"
                            class="vehicle-photo-delete"
                            type="button"
                            aria-label="Удалить фотографию"
                            @click="deletePhoto(photo.id)"
                          >
                            ×
                          </button>
                        </span>
                        <span v-for="video in item.videos" :key="video.id" class="vehicle-photo-chip">
                          <button type="button" @click="openMedia(video, 'work-videos', 'video')">
                            {{ video.fileName }}
                          </button>
                          <button
                            v-if="isAdministrator || canExecute"
                            class="vehicle-photo-delete"
                            type="button"
                            @click="deleteMedia(video.id, 'work-videos')"
                          >
                            ×
                          </button>
                        </span>
                      </div>
                    </td>
                    <td v-if="isAdministrator" class="vehicle-screen-only vehicle-row-action">
                      <button
                        class="table-action-button table-action-button--danger"
                        type="button"
                        :disabled="isSaving"
                        @click="deleteWork(item)"
                      >
                        Удалить
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
              <section
                v-if="section === 'works' && selectedWork"
                class="vehicle-work-details"
              >
                <h3>ФОТО И ВИДЕО РЕМОНТНЫХ РАБОТ</h3>
                <div class="vehicle-photo-actions">
                  <span v-if="!selectedWork.photos.length && !selectedWork.videos.length">
                    Нет вложений
                  </span>
                  <span v-for="photo in selectedWork.photos" :key="photo.id" class="vehicle-photo-chip">
                    <button type="button" @click="openPhoto(photo)">
                      {{ photo.fileName }}
                    </button>
                  </span>
                  <span v-for="video in selectedWork.videos" :key="video.id" class="vehicle-photo-chip">
                    <button type="button" @click="openMedia(video, 'work-videos', 'video')">
                      {{ video.fileName }}
                    </button>
                  </span>
                </div>
                <h3>ОПИСАНИЕ ВЫПОЛНЕННЫХ РАБОТ</h3>
                <p class="vehicle-work-description">
                  {{ selectedWork.description || 'Описание не указано.' }}
                </p>
              </section>
              <p v-if="!currentEntries.length" class="vehicle-empty">
                За выбранный период записей нет
              </p>
            </div>
          </section>

          <section class="vehicle-print-report">
            <h1>Отчёт по технике: {{ selectedVehicle.modelName }}</h1>
            <p>
              Гаражный № {{ selectedVehicle.garageNumber ?? '—' }};
              гос. номер {{ selectedVehicle.stateNumber || '—' }};
              период {{ fromDate ? formatDate(fromDate) : 'за всё время' }}
              {{ toDate ? `— ${formatDate(toDate)}` : '' }}
            </p>

            <h2>Заявки на приобретение</h2>
            <table>
              <thead><tr><th>Дата</th><th>№</th><th>Наименование</th><th>Кол-во</th><th>Статус</th><th>Примечание</th></tr></thead>
              <tbody><tr v-for="item in journal.purchases" :key="item.id"><td>{{ formatDate(item.requestDate) }}</td><td>{{ item.requestNumber || '—' }}</td><td>{{ item.itemName }}</td><td>{{ formatNumber(item.quantity) }}</td><td>{{ item.status }}</td><td>{{ item.note || '—' }}</td></tr></tbody>
            </table>

            <h2>Неисправности</h2>
            <table>
              <thead><tr><th>Узел</th><th>Причина неисправности</th><th>Фото</th></tr></thead>
              <tbody><tr v-for="item in journal.defects" :key="item.id"><td>{{ item.nodeName }}</td><td>{{ item.failureReason }}</td><td>{{ item.photos.length }}</td></tr></tbody>
            </table>

            <h2>Наработанные моточасы</h2>
            <table>
              <thead><tr><th>Дата</th><th>Моточасы</th><th>Примечание</th></tr></thead>
              <tbody><tr v-for="item in journal.hours" :key="item.id"><td>{{ formatDate(item.readingDate) }}</td><td>{{ formatNumber(item.engineHours) }}</td><td>{{ item.note || '—' }}</td></tr></tbody>
            </table>

            <h2>Ремонты</h2>
            <table>
              <thead><tr><th>Неисправность / узел</th><th>Работы</th><th>№ заявки</th><th>Фото</th></tr></thead>
              <tbody><tr v-for="item in journal.works" :key="item.id"><td>{{ item.defectNodeName || 'Старая запись без привязки' }}</td><td>{{ item.description }}</td><td>{{ item.purchaseRequestNumber || '—' }}</td><td>{{ item.photos.length }}</td></tr></tbody>
            </table>
          </section>
        </template>
      </div>
    </section>

    <div
      v-if="photoViewerUrl"
      class="vehicle-photo-viewer"
      role="dialog"
      aria-modal="true"
      :aria-label="photoViewerName"
      @click.self="closePhoto"
    >
      <div class="vehicle-photo-viewer-card">
        <div>
          <strong>{{ photoViewerName }}</strong>
          <button type="button" aria-label="Закрыть" @click="closePhoto">×</button>
        </div>
        <video
          v-if="mediaViewerType === 'video'"
          :src="photoViewerUrl"
          controls
          preload="metadata"
        ></video>
        <img v-else :src="photoViewerUrl" :alt="photoViewerName" />
      </div>
    </div>
  </main>
</template>