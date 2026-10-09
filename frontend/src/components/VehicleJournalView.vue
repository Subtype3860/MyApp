<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import UserAvatar from './UserAvatar.vue'
import RepairHistory from './RepairHistory.vue'
import RepairMediaViewer from './RepairMediaViewer.vue'

/**
 * Компонент раздела «Транспорт»: журнал техники, заявки на ремонт,
 * карточки ремонта с медиавложениями, моточасы, закупки и отчёты.
 * Конкретный подраздел определяется пропом `section`.
 */
const props = defineProps({
  navigationCollapsed: { type: Boolean, required: true },
  section: { type: String, required: true },
  token: { type: String, required: true },
})

// --- Список техники и поиск ---
const vehicles = ref([])
const vehicleQuery = ref('')
const selectedVehicleId = ref('')
const searchMatches = ref([])
const isLoading = ref(false)
const isSaving = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
// Журнал выбранной техники (закупки, неисправности, моточасы, работы).
const journal = ref(null)
// Плоский список неисправностей, отображаемых во вкладке «Ремонт».
const repairDefects = ref([])
const repairDrafts = ref({})
const repairFileInputs = new Map()
const expandNewRepairForm = window.matchMedia('(min-width: 761px)').matches

// --- Полноэкранный просмотрщик медиафайлов ---
const mediaViewerOpen = ref(false)
const mediaViewerItems = ref([])
const mediaViewerType = ref('image')

// --- Форма заявки на ремонт ---
const problemDescription = ref('')
const requestDate = ref(new Date().toISOString().slice(0, 10))
const requestTime = ref(new Date().toTimeString().slice(0, 5))
const photos = ref([])
const photoPreviews = ref([])
const videos = ref([])
const videoPreviews = ref([])
const photoIndex = ref(0)
const videoIndex = ref(0)

// --- Zoom-просмотрщик предпросмотра формы заявки (десктопный wheel/drag zoom) ---
const zoomVisible = ref(false)
const zoomSrc = ref('')
const zoomScale = ref(1)
const zoomX = ref(0)
const zoomY = ref(0)
let dragStartX = 0
let dragStartY = 0

/** Выбранная в данный момент единица техники (по `selectedVehicleId`). */
const selectedVehicle = computed(() =>
  vehicles.value.find(
    (vehicle) => String(vehicle.id) === String(selectedVehicleId.value),
  ),
)
const partsRequests = ref([])
const partsRequestDrafts = ref({})
const partsRequestEditDrafts = ref({})
const editingPartsRequestId = ref('')

function partsRequestDraft(request) {
  if (!partsRequestDrafts.value[request.id]) {
    partsRequestDrafts.value[request.id] = {
      date: new Date().toISOString().slice(0, 10),
      description: '',
    }
  }
  return partsRequestDrafts.value[request.id]
}

function partsRequestHistory(request) {
  return request.partsRequests ?? []
}

/** Заголовки для активного раздела (вкладки) журнала техники. */
const sectionTitle = computed(() => ({
  repairRequest: 'Заявки на ремонт',
  works: 'Ремонт',
  repairHistory: 'История ремонта',
  partsRequest: 'Заявка на закупку ЗЧ',
  hours: 'Моточасы',
  report: 'Отчёт',
}[props.section] ?? 'Транспорт'))

onMounted(async () => {
  await loadRepairSection()
})
watch(() => props.section, loadRepairSection)
onBeforeUnmount(() => {
  clearPreviews()
  closeMediaViewer()
  stopDrag()
})

/**
 * Формирует заголовки авторизованного запроса.
 * @param {boolean} [json] Если true, добавляет заголовок `Content-Type: application/json`.
 * @returns {Record<string, string>} Заголовки запроса.
 */
function authHeaders(json = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  }
}

function workMediaForWorks(works, type) {
  return works.flatMap((work) =>
    (type === 'video' ? work.videos ?? [] : work.photos ?? [])
      .map((media) => ({
        media: { ...media, source: 'work' },
        type,
      })),
  )
}

/**
 * Загружает данные, необходимые разделу ремонта, в правильном порядке.
 * Сначала загружается техника, затем её журналы и неисправности.
 */
async function loadRepairSection() {
  if (props.section === 'repairHistory') {
    return
  }

  if (!vehicles.value.length) {
    await loadVehicles()
  }

  if (props.section === 'works' && vehicles.value.length) {
    await loadRepairDefects()
  }
  if (props.section === 'partsRequest' && vehicles.value.length) {
    await loadPartsRequests()
  }
}

async function loadPartsRequests() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const requests = []
    for (const vehicle of vehicles.value) {
      const response = await fetch(
        `/api/vehicles/${vehicle.id}/journal`,
        { headers: authHeaders() },
      )
      if (!response.ok) continue
      const result = await response.json()
      const works = [...(result.works ?? [])].sort(
        (left, right) => new Date(right.createdAt) - new Date(left.createdAt),
      )
      const currentStatusByDefect = new Map()
      const currentWorkByDefect = new Map()
      for (const work of works) {
        const defectKey = work.defectId || `vehicle:${vehicle.id}`
        if (!currentStatusByDefect.has(defectKey)) {
          currentStatusByDefect.set(
            defectKey,
            String(work.status ?? '').trim().toLowerCase(),
          )
          currentWorkByDefect.set(defectKey, work)
        }
      }
      for (const [defectKey, work] of currentWorkByDefect) {
        if (currentStatusByDefect.get(defectKey) === 'awaiting_parts') {
          requests.push({
            ...work,
            partsRequests: work.partsRequests ?? [],
            vehicleName: vehicle.modelName,
            vehicleTypeName: vehicle.typeName,
            vehicleStateNumber: vehicle.stateNumber,
            vehicleGarageNumber: vehicle.garageNumber,
          })
          partsRequestDraft(work)
        }
      }

    }
    partsRequests.value = requests
  } catch (error) {
    partsRequests.value = []
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

async function savePartsRequest(request) {
  const draft = partsRequestDraft(request)
  if (!draft.date || !draft.description.trim()) {
    errorMessage.value = 'Укажите дату и номер с описанием заявки.'
    return
  }

  isSaving.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(
      `/api/vehicles/defects/${request.defectId}/parts-requests`,
      {
      method: 'POST',
      headers: authHeaders(true),
      body: JSON.stringify({
        requestDate: draft.date,
        requestNumber: draft.description.trim().slice(0, 100),
        description: draft.description.trim(),
      }),
      },
    )
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      const validation = result?.errors
        ? Object.values(result.errors).flat()[0]
        : null
      throw new Error(
        validation ??
        result?.detail ??
        result?.message ??
        result?.title ??
        (response.status === 401
          ? 'Сессия истекла. Войдите в систему повторно.'
          : response.status === 403
            ? 'Недостаточно прав для добавления заявки.'
            : response.status === 404
              ? 'API заявок на закупку не найден. Перезапустите backend.'
              : null) ??
        'Не удалось сохранить заявку.',
      )
    }
    request.partsRequests = [
      ...(request.partsRequests ?? []),
      {
        id: result?.id ?? result,
        defectId: request.defectId,
        requestDate: draft.date,
        requestNumber: draft.description.trim().slice(0, 100),
        description: draft.description.trim(),
      },
    ]
    draft.description = ''
    successMessage.value = 'Заявка сохранена.'
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

function startEditPartsRequest(item) {
  editingPartsRequestId.value = item.id
  partsRequestEditDrafts.value[item.id] = {
    date: item.requestDate,
    description: item.description || item.requestNumber,
  }
}

function cancelEditPartsRequest() {
  editingPartsRequestId.value = ''
}

async function saveEditedPartsRequest(item) {
  const draft = partsRequestEditDrafts.value[item.id]
  if (!draft?.date || !draft.description.trim()) {
    errorMessage.value = 'Укажите дату и номер с описанием заявки.'
    return
  }

  isSaving.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/parts-requests/${item.id}`, {
      method: 'PUT',
      headers: authHeaders(true),
      body: JSON.stringify({
        requestDate: draft.date,
        requestNumber: draft.description.trim().slice(0, 100),
        description: draft.description.trim(),
        requiredParts: item.requiredParts || '',
      }),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      const validation = result?.errors
        ? Object.values(result.errors).flat()[0]
        : null
      throw new Error(
        validation ?? result?.detail ?? result?.message ?? result?.title ??
        (response.status === 403
          ? 'Недостаточно прав для редактирования заявки.'
          : 'Не удалось изменить заявку.'),
      )
    }
    item.requestDate = draft.date
    item.requestNumber = draft.description.trim().slice(0, 100)
    item.description = draft.description.trim()
    editingPartsRequestId.value = ''
    successMessage.value = 'Заявка изменена.'
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

async function deletePartsRequest(request, item) {
  if (!window.confirm('Удалить эту заявку на закупку?')) return
  isSaving.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/parts-requests/${item.id}`, {
      method: 'DELETE',
      headers: authHeaders(),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      throw new Error(
        result?.detail ?? result?.message ?? result?.title ??
        (response.status === 403
          ? 'Недостаточно прав для удаления заявки.'
          : 'Не удалось удалить заявку.'),
      )
    }
    request.partsRequests = request.partsRequests.filter(({ id }) => id !== item.id)
    if (editingPartsRequestId.value === item.id) editingPartsRequestId.value = ''
    successMessage.value = 'Заявка удалена.'
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

/** Загружает список всей техники с сервера. */
async function loadVehicles() {
  isLoading.value = true
  try {
    const response = await fetch('/api/vehicles', { headers: authHeaders() })
    if (!response.ok) throw new Error('Не удалось загрузить список транспорта.')
    vehicles.value = await response.json()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

async function searchVehicleHistory(plateNumber, signal) {
  const requestOptions = {
    headers: authHeaders(),
    ...(signal ? { signal } : {}),
  }
  const vehiclesResponse = await fetch('/api/vehicles', requestOptions)
  if (!vehiclesResponse.ok) {
    throw new Error('Vehicle lookup failed.')
  }

  const normalizeNumber = (value) =>
    String(value ?? '').replace(/[\s-]+/g, '').toLocaleUpperCase('ru-RU')
  const query = normalizeNumber(plateNumber)
  const vehicle = (await vehiclesResponse.json()).find((candidate) =>
    normalizeNumber(candidate.stateNumber) === query ||
    normalizeNumber(candidate.garageNumber) === query,
  )
  if (!vehicle) return null

  const journalResponse = await fetch(
    `/api/vehicles/${vehicle.id}/journal`,
    requestOptions,
  )
  if (!journalResponse.ok) {
    throw new Error('Vehicle history lookup failed.')
  }
  const journal = await journalResponse.json()
  const worksByDefect = new Map()
  for (const work of journal.works ?? []) {
    if (!work.defectId) continue
    const works = worksByDefect.get(work.defectId) ?? []
    works.push({
      id: work.id,
      date: work.completedAt || work.createdAt,
      dateLabel: formatDateTime(work.completedAt || work.createdAt),
      name: work.description || work.cause || 'Ремонтная работа',
      status:
        work.completedAt || isCompletedStatus(work.status)
          ? 'completed'
          : 'in_progress',
      performer: work.performerName,
      photos: (work.photos ?? []).map((photo) => ({
        ...photo,
        source: 'work',
      })),
      videos: (work.videos ?? []).map((video) => ({
        ...video,
        source: 'work',
      })),
    })
    worksByDefect.set(work.defectId, works)
  }

  const requests = (journal.defects ?? []).map((defect) => {
    const requestDate = defect.createdAt || defect.downtimeStartedAt
    const works = (worksByDefect.get(defect.id) ?? [])
      .sort((left, right) => new Date(left.date ?? 0) - new Date(right.date ?? 0))

    return {
      id: defect.id,
      date: requestDate,
      dateLabel: formatDateTime(requestDate),
      description: defect.symptoms || defect.failureReason,
      photos: (defect.photos ?? []).map((photo) => ({
        ...photo,
        source: 'defect',
      })),
      videos: (defect.videos ?? []).map((video) => ({
        ...video,
        source: 'defect',
      })),
      works,
    }
  })

  return { vehicle: journal.vehicle ?? vehicle, requests }
}

/**
 * Загружает журналы всей техники и собирает плоский список незавершённых
 * неисправностей для вкладки «Ремонт». Миниатюры медиавложений
 * подгружаются лениво по мере появления карточек во вьюпорте.
 */
async function loadRepairDefects() {
  if (!vehicles.value.length) {
    repairDefects.value = []
    return
  }
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/vehicles/repair-journal', {
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error(
        response.status === 401
          ? 'Сессия истекла. Войдите в систему повторно.'
          : 'Не удалось загрузить данные ремонта. Обновите страницу.',
      )
    }
    const journals = await response.json()
    repairDefects.value = journals.flatMap((journal) => {
      const worksByDefect = new Map()
      for (const work of journal.works ?? []) {
        if (!work.defectId) continue
        const works = worksByDefect.get(work.defectId) ?? []
        works.push(work)
        worksByDefect.set(work.defectId, works)
      }

      return (journal.defects ?? [])
        .filter((defect) => !isCompletedStatus(defect.status))
        .map((defect) => ({
          ...defect,
          repairWorks: worksByDefect.get(defect.id) ?? [],
          repairStatus: repairStatus(defect.status),
          vehicleName: journal.vehicle.modelName,
          vehicleGarageNumber: journal.vehicle.garageNumber,
          vehicleStateNumber: journal.vehicle.stateNumber,
          vehicleId: journal.vehicle.id,
        }))
    })
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

/**
 * Проверяет, завершена ли неисправность и должна ли она исчезнуть
 * из списка текущих ремонтных карточек.
 * @param {string} status Статус неисправности.
 * @returns {boolean}
 */
function isCompletedStatus(status) {
  return [
    'done',
    'completed',
    'complete',
    'fixed',
    'closed',
    'repaired',
    'исправна',
    'исправна (готово)',
    'завершено',
  ].includes(String(status ?? '').trim().toLocaleLowerCase('ru-RU'))
}

/**
 * Переводит статус работы/неисправности с бэкенда в один из
 * внутренних статусов карточки ремонта: `queue`, `repair`, `waiting`, `done`.
 * @param {string} status Статус, полученный от API.
 * @returns {'queue'|'repair'|'waiting'|'done'}
 */
function repairStatus(status) {
  const normalized = String(status ?? '').trim().toLowerCase()
  if (!normalized) return 'done'
  if ([
    'repaired',
    'done',
    'completed',
    'исправна',
    'исправна (готово)',
    'завершено',
  ].includes(normalized)) return 'done'
  if (['awaiting_parts', 'waiting', 'ожидание запчастей'].includes(normalized)) return 'waiting'
  if (['in_progress', 'in progress', 'repair', 'faulty', 'ремонт'].includes(normalized)) return 'repair'
  return 'queue'
}

/**
 * Возвращает отображаемую подпись для внутреннего статуса ремонта.
 * @param {string} status Статус (в любом формате, распознаваемом {@link repairStatus}).
 * @returns {string}
 */
function repairStatusLabel(status) {
  return {
    queue: 'В очереди',
    repair: 'На ремонте',
    waiting: 'Ожидает запчасти',
    done: 'Исправна (Готово)',
  }[repairStatus(status)]
}

function repairWorksInOrder(defect) {
  return [...(defect.repairWorks ?? [])].sort((first, second) =>
    new Date(first.createdAt ?? 0) - new Date(second.createdAt ?? 0))
}

async function deleteRepairWork(defect, workId) {
  const work = defect.repairWorks?.find(item => item.id === workId)
  if (!window.confirm(`Удалить этап от ${formatDateTime(work?.createdAt)} вместе с фото и видео?`)) return

  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(`/api/vehicles/works/${workId}`, {
      method: 'DELETE',
      headers: authHeaders(),
    })
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      throw new Error(
        result?.detail ??
        result?.message ??
        'Не удалось удалить этап ремонта.',
      )
    }
    successMessage.value = 'Этап ремонта и его медиа удалены.'
    await loadRepairDefects()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

function repairDraft(defect) {
  if (!repairDrafts.value[defect.id]) {
    repairDrafts.value[defect.id] = {
      description: '',
      status: 'repair',
      files: [],
      previews: [],
    }
  }
  return repairDrafts.value[defect.id]
}

/**
 * Проверяет, что файл является допустимым для зоны загрузки медиа ремонта (фото или видео).
 * @param {File} file
 * @returns {boolean}
 */
function isRepairMediaFile(file) {
  return ['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
    ['video/mp4', 'video/webm', 'video/quicktime'].includes(file.type)
}

/**
 * Обрабатывает выбор фото/видео для карточки ремонта. Файлы сохраняются
 * локально до нажатия кнопки «ВЫПОЛНЕНИЕ».
 * @param {object} defect Неисправность, к которой относится загрузка.
 * @param {DragEvent|Event} event Событие drop либо изменения `<input>`.
 */
async function uploadRepairFiles(defect, event) {
  const files = Array.from(event.dataTransfer?.files ?? event.target?.files ?? [])
  if (event.target) event.target.value = ''
  if (!files.length) {
    errorMessage.value = 'Выберите фото или видео для загрузки.'
    return
  }

  const invalidFile = files.find((file) => !isRepairMediaFile(file))
  if (invalidFile) {
    errorMessage.value = `Файл «${invalidFile.name}» имеет неподдерживаемый формат.`
    return
  }

  try {
    const processedFiles = await Promise.all(files.map((file) =>
      file.type.startsWith('image/') ? convertImageToWebp(file) : file))
    const oversizedImage = processedFiles.find((file) =>
      file.type === 'image/webp' && file.size > 8 * 1024 * 1024)
    if (oversizedImage) {
      throw new Error(`Фото «${oversizedImage.name}» после конвертации превышает 8 МБ.`)
    }

    const draft = repairDraft(defect)
    draft.files.push(...processedFiles)
    draft.previews.push(...processedFiles.map((file) => ({
      file,
      url: URL.createObjectURL(file),
      type: file.type.startsWith('video/') ? 'video' : 'image',
    })))
    errorMessage.value = ''
  } catch (error) {
    errorMessage.value = error.message || 'Не удалось подготовить файлы к загрузке.'
  }
}

async function completeRepair(defect) {
  const draft = repairDraft(defect)
  const description = draft.description.trim()
  if (!description) {
    errorMessage.value = 'Введите отчёт о выполненной работе.'
    return
  }

  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const status = draft.status === 'done'
      ? 'repaired'
      : draft.status === 'waiting'
        ? 'awaiting_parts'
        : draft.status === 'queue'
          ? 'faulty'
          : 'faulty'
    const response = await fetch(`/api/vehicles/${defect.vehicleId}/works`, {
      method: 'POST',
      headers: authHeaders(true),
      body: JSON.stringify({
        defectId: defect.id,
        cause: defect.failureReason || 'Неисправность техники',
        description,
        status,
        requiredParts: status === 'awaiting_parts' ? 'Требуется определить' : null,
      }),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      const validation = result?.errors
        ? Object.values(result.errors).flat()[0]
        : null
      throw new Error(
        validation ??
        result?.detail ??
        result?.message ??
        result?.title ??
        'Не удалось сохранить отчёт.',
      )
    }
    await uploadFiles(
      'works',
      result.id,
      'photos',
      draft.files.filter((file) => file.type.startsWith('image/')),
    )
    await uploadFiles(
      'works',
      result.id,
      'videos',
      draft.files.filter((file) => file.type.startsWith('video/')),
    )
    draft.previews.forEach(({ url }) => URL.revokeObjectURL(url))
    delete repairDrafts.value[defect.id]
    successMessage.value = 'Отчёт о ремонтных работах сохранён.'
    await loadRepairDefects()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

/**
 * Приводит номер (гос. или гаражный) к верхнему регистру и убирает
 * все символы, кроме букв и цифр, для сравнения при поиске.
 * @param {string} value Исходное значение.
 * @returns {string}
 */
function normalizeNumber(value) {
  return String(value ?? '')
    .toLocaleUpperCase('ru-RU')
    .replace(/[^A-ZА-ЯЁ0-9]/g, '')
}

/**
 * Ищет технику по введённому в поиск гос./гаражному номеру.
 * Если найдено ровно одно совпадение — выбирает его, иначе показывает
 * список совпадений или сообщение об ошибке.
 */
function findVehicle() {
  const query = normalizeNumber(vehicleQuery.value)
  if (!query) {
    errorMessage.value = 'Введите государственный или гаражный номер.'
    return
  }
  const matches = vehicles.value.filter((vehicle) =>
    normalizeNumber(vehicle.stateNumber).includes(query) ||
    normalizeNumber(vehicle.garageNumber).includes(query),
  )
  if (matches.length !== 1) {
    selectedVehicleId.value = ''
    searchMatches.value = matches
    errorMessage.value = matches.length
      ? 'Найдено несколько машин. Выберите нужную.'
      : 'Транспорт с таким номером не найден.'
    return
  }
  chooseVehicle(matches[0])
}

/**
 * Выбирает технику из списка совпадений и загружает её журнал.
 * @param {object} vehicle Выбранная техника.
 */
function chooseVehicle(vehicle) {
  selectedVehicleId.value = vehicle.id
  vehicleQuery.value = vehicle.stateNumber || String(vehicle.garageNumber ?? '')
  searchMatches.value = []
  errorMessage.value = ''
  loadJournal()
}

/** Загружает журнал (закупки, неисправности, моточасы, работы) выбранной техники. */
async function loadJournal() {
  if (!selectedVehicleId.value) {
    journal.value = null
    return
  }
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(
      `/api/vehicles/${selectedVehicleId.value}/journal`,
      { headers: authHeaders() },
    )
    if (!response.ok) {
      throw new Error('Не удалось загрузить информацию о неисправностях.')
    }
    journal.value = await response.json()
  } catch (error) {
    journal.value = null
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

/**
 * Обрабатывает выбор файлов для формы заявки на ремонт, валидирует их
 * и формирует предпросмотры.
 * @param {Event} event Событие изменения `<input type="file">`.
 * @param {'photo'|'video'} type Тип выбираемых файлов.
 */
async function selectFiles(event, type) {
  const files = Array.from(event.target.files ?? [])
  event.target.value = ''
  const validation = type === 'photo'
    ? validatePhotos(files)
    : validateVideos(files)
  if (validation) {
    errorMessage.value = validation
    return
  }
  if (type === 'photo') {
    try {
      const webpFiles = await Promise.all(files.map(convertImageToWebp))
      const oversizedImage = webpFiles.find((file) => file.size > 8 * 1024 * 1024)
      if (oversizedImage) {
        throw new Error(`Фото «${oversizedImage.name}» после конвертации превышает 8 МБ.`)
      }
      clearPhotoPreviews()
      photos.value = webpFiles
      photoPreviews.value = webpFiles.map((file) => URL.createObjectURL(file))
      photoIndex.value = 0
      errorMessage.value = ''
    } catch (error) {
      errorMessage.value = error.message || 'Не удалось преобразовать фото в WebP.'
    }
  } else {
    clearVideoPreviews()
    videos.value = files
    videoPreviews.value = files.map((file) => URL.createObjectURL(file))
    videoIndex.value = 0
  }
  errorMessage.value = ''
}

/**
 * Валидирует список выбранных фотографий (тип, размер, количество).
 * @param {File[]} files
 * @returns {string} Текст ошибки или пустая строка, если валидация пройдена.
 */
function validatePhotos(files) {
  if (files.length > 10) return 'Можно выбрать не более 10 фотографий.'
  if (files.some((file) =>
    !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
    file.size > 32 * 1024 * 1024
  )) {
    return 'Разрешены JPEG, PNG и WebP размером не более 32 МБ до конвертации.'
  }
  return ''
}

async function convertImageToWebp(file) {
  let bitmap
  try {
    if (file.size > 32 * 1024 * 1024) {
      throw new Error(`Фото «${file.name}» превышает 32 МБ до конвертации.`)
    }
    bitmap = await createImageBitmap(file)
    const canvas = document.createElement('canvas')
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Браузер не смог обработать изображение.')
    context.drawImage(bitmap, 0, 0)

    const blob = await new Promise((resolve, reject) => {
      canvas.toBlob(
        (result) => result
          ? resolve(result)
          : reject(new Error(`Не удалось преобразовать фото «${file.name}».`)),
        'image/webp',
        0.8,
      )
    })
    if (blob.type !== 'image/webp') {
      throw new Error('Браузер не поддерживает преобразование изображений в WebP.')
    }

    const webpName = `${file.name.replace(/\.[^.]+$/, '')}.webp`
    return new File([blob], webpName, {
      type: 'image/webp',
      lastModified: file.lastModified,
    })
  } finally {
    bitmap?.close()
  }
}

/**
 * Валидирует список выбранных видео (тип, размер).
 * @param {File[]} files
 * @returns {string} Текст ошибки или пустая строка, если валидация пройдена.
 */
function validateVideos(files) {
  if (files.some((file) =>
    !['video/mp4', 'video/webm', 'video/quicktime'].includes(file.type) ||
    file.size > 100 * 1024 * 1024
  )) {
    return 'Разрешены MP4, WebM и MOV размером не более 100 МБ.'
  }
  return ''
}

/**
 * Отправляет заявку на ремонт (создание неисправности) вместе с
 * прикреплёнными фото и видео.
 */
async function submitForm() {
  if (!selectedVehicle.value) {
    errorMessage.value = 'Сначала найдите и выберите транспорт.'
    return
  }
  isSaving.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(
      `/api/vehicles/${selectedVehicle.value.id}/defects`,
      {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify({
          errorCode: null,
          symptoms: problemDescription.value,
          downtimeStartedAt: new Date(
            `${requestDate.value}T${requestTime.value}`,
          ).toISOString(),
        }),
      },
    )
    const result = await response.json().catch(() => null)
    if (!response.ok) {
      const validation = result?.errors
        ? Object.values(result.errors).flat()[0]
        : null
      throw new Error(validation ?? result?.detail ?? 'Не удалось создать заявку.')
    }
    await uploadFiles('defects', result.id, 'photos', photos.value)
    await uploadFiles('defects', result.id, 'videos', videos.value)
    successMessage.value = 'Заявка на ремонт отправлена.'
    problemDescription.value = ''
    photos.value = []
    videos.value = []
    clearPreviews()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

/**
 * Загружает набор файлов на сервер для указанной записи (неисправность или работа).
 * @param {string} category Категория записи (например, `defects`).
 * @param {string|number} entryId Идентификатор записи.
 * @param {'photos'|'videos'} type Тип загружаемых файлов.
 * @param {File[]} files Список файлов для загрузки.
 */
async function uploadFiles(category, entryId, type, files) {
  for (const file of files) {
    const data = new FormData()
    data.append('file', file)
    const response = await fetch(
      `/api/vehicles/${category}/${entryId}/${type}`,
      { method: 'POST', headers: authHeaders(), body: data },
    )
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      throw new Error(result?.detail ?? `Не удалось загрузить файл «${file.name}».`)
    }

  }
}

function defectMedia(defect, type) {
  return (type === 'video' ? defect.videos ?? [] : defect.photos ?? [])
    .map((media) => ({ media, type }))
}

function openRepairMedia(items, type) {
  if (!items.length) return
  mediaViewerItems.value = items
  mediaViewerType.value = type
  mediaViewerOpen.value = true
}

function openRepairMediaCollection(defect, type) {
  return openRepairMedia(defectMedia(defect, type), type)
}

function closeMediaViewer() {
  mediaViewerOpen.value = false
  mediaViewerItems.value = []
}

/** Освобождает Blob-URL предпросмотров фото формы заявки на ремонт. */
function clearPhotoPreviews() {
  photoPreviews.value.forEach((url) => URL.revokeObjectURL(url))
  photoPreviews.value = []
}

/** Освобождает Blob-URL предпросмотров видео формы заявки на ремонт. */
function clearVideoPreviews() {
  videoPreviews.value.forEach((url) => URL.revokeObjectURL(url))
  videoPreviews.value = []
}

/** Освобождает все Blob-URL предпросмотров формы заявки на ремонт. */
function clearPreviews() {
  clearPhotoPreviews()
  clearVideoPreviews()
}

/**
 * Удаляет выбранную фотографию из формы заявки перед отправкой.
 * @param {number} index Индекс удаляемой фотографии.
 */
function removePhoto(index) {
  URL.revokeObjectURL(photoPreviews.value[index])
  photos.value.splice(index, 1)
  photoPreviews.value.splice(index, 1)
  photoIndex.value = Math.min(photoIndex.value, Math.max(photoPreviews.value.length - 1, 0))
}

/**
 * Удаляет выбранное видео из формы заявки перед отправкой.
 * @param {number} index Индекс удаляемого видео.
 */
function removeVideo(index) {
  URL.revokeObjectURL(videoPreviews.value[index])
  videos.value.splice(index, 1)
  videoPreviews.value.splice(index, 1)
  videoIndex.value = Math.min(videoIndex.value, Math.max(videoPreviews.value.length - 1, 0))
}

/**
 * Открывает модальный zoom-просмотр предпросмотра формы заявки (десктопный wheel/drag zoom).
 * @param {string} src Blob-URL изображения.
 */
function openZoom(src) {
  zoomSrc.value = src
  zoomVisible.value = true
  zoomScale.value = 1
  zoomX.value = 0
  zoomY.value = 0
}

/** Закрывает модальный zoom-просмотр предпросмотра формы заявки. */
function closeZoom() {
  zoomVisible.value = false
}

/**
 * Изменяет масштаб предпросмотра прокруткой колеса мыши.
 * @param {WheelEvent} event
 */
function wheelZoom(event) {
  event.preventDefault()
  zoomScale.value = Math.min(Math.max(zoomScale.value - event.deltaY * 0.001, 1), 4)
}

/**
 * Начинает перетаскивание увеличенного изображения мышью.
 * @param {MouseEvent} event
 */
function startDrag(event) {
  dragStartX = event.clientX - zoomX.value
  dragStartY = event.clientY - zoomY.value
  window.addEventListener('mousemove', drag)
  window.addEventListener('mouseup', stopDrag)
}

/**
 * Обновляет смещение увеличенного изображения при перетаскивании мышью.
 * @param {MouseEvent} event
 */
function drag(event) {
  zoomX.value = event.clientX - dragStartX
  zoomY.value = event.clientY - dragStartY
}

/** Останавливает перетаскивание увеличенного изображения, снимая обработчики. */
function stopDrag() {
  window.removeEventListener('mousemove', drag)
  window.removeEventListener('mouseup', stopDrag)
}

/**
 * Возвращает отображаемую подпись статуса неисправности/работы (журнал техники).
 * @param {string} status Статус в исходном формате бэкенда.
 * @returns {string}
 */
function statusLabel(status) {
  return {
    new: 'Новое',
    in_progress: 'В работе',
    'in progress': 'В работе',
    in_work: 'В работе',
    working: 'В работе',
    repaired: 'Исправен',
    faulty: 'Неисправна',
    repair: 'Ремонт',
    ремонт: 'Ремонт',
    awaiting_parts: 'Ожидание запчастей',
  }[status] ?? status ?? '—'
}

/**
 * Форматирует дату/время в короткий русский формат для отображения в UI.
 * @param {string|Date|null} value
 * @returns {string}
 */
function formatDateTime(value) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}
</script>

<template>
  <main class="home-page vehicle-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div>
        <p class="eyebrow">ТРАНСПОРТ</p>
        <h1>{{ sectionTitle }}</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <section class="vehicle-content">
      <RepairHistory
        v-if="props.section === 'repairHistory'"
        :token="token"
        :search-vehicle-history="searchVehicleHistory"
      />

      <form
        v-if="props.section === 'repairRequest'"
        class="vehicle-search"
        @submit.prevent="findVehicle"
      >
        <label class="vehicle-field">
          <span>Государственный или гаражный номер</span>
          <input v-model="vehicleQuery" placeholder="Например: 901 или 5113 CC 65" />
        </label>
        <button class="primary-button" type="submit" :disabled="isLoading">
          {{ isLoading ? 'Загрузка...' : 'Найти транспорт' }}
        </button>
        <div v-if="searchMatches.length" class="vehicle-search-results vehicle-field--full">
          <span>Выберите транспорт:</span>
          <button v-for="vehicle in searchMatches" :key="vehicle.id" type="button" @click="chooseVehicle(vehicle)">
            <strong>{{ vehicle.modelName }}</strong>
            <span>{{ vehicle.stateNumber || `Гар. № ${vehicle.garageNumber ?? '—'}` }}</span>
          </button>
        </div>
      </form>

      <p v-if="errorMessage" class="table-message table-message--error">{{ errorMessage }}</p>
      <p
        v-if="successMessage && props.section !== 'partsRequest'"
        class="form-message form-message--success"
      >
        {{ successMessage }}
      </p>

      <section v-if="props.section === 'repairRequest'" class="vehicle-controls macos-glass-panel repair-form">
        <div class="repair-form-heading">
          <div>
            <p class="eyebrow">НОВАЯ ЗАЯВКА</p>
            <h2>Заявка на ремонт транспорта</h2>
          </div>
        </div>

        <div class="repair-form-grid">
          <label class="vehicle-field">
            <span>Вид транспорта</span>
            <input :value="selectedVehicle?.typeName || 'Выберите транспорт'" readonly />
          </label>
          <label class="vehicle-field">
            <span>Государственный или гаражный номер</span>
            <input :value="selectedVehicle?.stateNumber || selectedVehicle?.garageNumber || vehicleQuery" readonly />
          </label>
          <label class="vehicle-field vehicle-field--full">
            <span>Описание неисправности</span>
            <textarea v-model="problemDescription" required rows="4" placeholder="Опишите неисправность"></textarea>
          </label>
          <label class="vehicle-field">
            <span>Дата</span>
            <input v-model="requestDate" required type="date" />
          </label>
          <label class="vehicle-field">
            <span>Время</span>
            <input v-model="requestTime" required type="time" />
          </label>
          <label class="vehicle-field vehicle-field--full">
            <span>Фото неисправности</span>
            <input accept="image/jpeg,image/png,image/webp" multiple type="file" @change="selectFiles($event, 'photo')" />
          </label>
          <div v-if="photoPreviews.length" class="repair-media vehicle-field--full">
            <div class="repair-media-main">
              <img :src="photoPreviews[photoIndex]" alt="Предпросмотр фотографии" @click="openZoom(photoPreviews[photoIndex])" />
              <button class="table-action-button table-action-button--danger" type="button" @click="removePhoto(photoIndex)">Удалить</button>
            </div>
            <div class="repair-media-controls">
              <button type="button" @click="photoIndex = (photoIndex - 1 + photoPreviews.length) % photoPreviews.length">◀</button>
              <span>{{ photoIndex + 1 }} / {{ photoPreviews.length }}</span>
              <button type="button" @click="photoIndex = (photoIndex + 1) % photoPreviews.length">▶</button>
            </div>
            <div class="repair-thumbs">
              <div v-for="(src, index) in photoPreviews" :key="src">
                <img :src="src" :class="{ active: index === photoIndex }" alt="" @click="photoIndex = index" />
                <button type="button" @click="removePhoto(index)">×</button>
              </div>
            </div>
          </div>
          <label class="vehicle-field vehicle-field--full">
            <span>Видео неисправности</span>
            <input accept="video/mp4,video/webm,video/quicktime" multiple type="file" @change="selectFiles($event, 'video')" />
          </label>
          <div v-if="videoPreviews.length" class="repair-media vehicle-field--full">
            <div class="repair-media-main">
              <video :src="videoPreviews[videoIndex]" controls></video>
              <button class="table-action-button table-action-button--danger" type="button" @click="removeVideo(videoIndex)">Удалить</button>
            </div>
            <div class="repair-media-controls">
              <button type="button" @click="videoIndex = (videoIndex - 1 + videoPreviews.length) % videoPreviews.length">◀</button>
              <span>{{ videoIndex + 1 }} / {{ videoPreviews.length }}</span>
              <button type="button" @click="videoIndex = (videoIndex + 1) % videoPreviews.length">▶</button>
            </div>
            <div class="repair-thumbs">
              <div v-for="(src, index) in videoPreviews" :key="src">
                <video :src="src" :class="{ active: index === videoIndex }" @click="videoIndex = index"></video>
                <button type="button" @click="removeVideo(index)">×</button>
              </div>
            </div>
          </div>
        </div>
        <button class="primary-button vehicle-save-button" type="button" :disabled="isSaving" @click="submitForm">
          {{ isSaving ? 'Отправка...' : 'Отправить заявку' }}
        </button>
      </section>

      <section
        v-if="props.section === 'partsRequest'"
        class="vehicle-controls macos-glass-panel parts-request-cards"
      >
        <div class="repair-form-heading">
          <div>
            <p class="eyebrow">ЗАПЧАСТИ</p>
            <h2>Заявки на закупку ЗЧ</h2>
          </div>
        </div>

        <p v-if="isLoading" class="table-message">Загрузка заявок...</p>
        <div v-else-if="partsRequests.length" class="parts-request-card-list">
          <article v-for="request in partsRequests" :key="request.id" class="parts-request-card">
            <header class="parts-request-card__header">
              <div class="parts-request-card__vehicle">
                <p class="eyebrow">ОЖИДАЕТ ЗАПЧАСТИ</p>
                <div class="parts-request-card__vehicle-details">
                  <span>{{ request.vehicleTypeName || 'Тип —' }}</span>
                  <span>{{ request.vehicleName || 'Модель —' }}</span>
                  <span>Гаражный № {{ request.vehicleGarageNumber ?? '—' }}</span>
                  <span>Гос. № {{ request.vehicleStateNumber || '—' }}</span>
                </div>
              </div>
            </header>
            <div class="parts-request-card__body">
              <p><strong>Причина простоя:</strong> {{ request.cause || request.failureCause || '—' }}</p>
              <form class="parts-request-form" @submit.prevent="savePartsRequest(request)">
                <label>
                  Дата создания заявки
                  <input v-model="partsRequestDraft(request).date" type="date" required />
                </label>
                <label>
                  Номер и описание заявки
                  <textarea
                    v-model="partsRequestDraft(request).description"
                    rows="3"
                    maxlength="5000"
                    required
                  ></textarea>
                </label>
                <button class="primary-button" type="submit" :disabled="isSaving">
                  {{ isSaving ? 'Сохранение...' : 'Добавить заявку' }}
                </button>
              </form>
              <div
                v-if="partsRequestHistory(request).length"
                class="parts-request-history"
              >
                <p class="eyebrow">ЗАЯВКИ</p>
                <div
                  v-for="item in partsRequestHistory(request)"
                  :key="item.id"
                  class="parts-request-history__item"
                >
                  <template v-if="editingPartsRequestId === item.id">
                    <form class="parts-request-edit-form" @submit.prevent="saveEditedPartsRequest(item)">
                      <input v-model="partsRequestEditDrafts[item.id].date" type="date" required />
                      <textarea
                        v-model="partsRequestEditDrafts[item.id].description"
                        rows="2"
                        maxlength="5000"
                        required
                      ></textarea>
                      <span class="parts-request-actions">
                        <button class="primary-button" type="submit" :disabled="isSaving">Сохранить</button>
                        <button class="secondary-button" type="button" @click="cancelEditPartsRequest">Отмена</button>
                      </span>
                    </form>
                  </template>
                  <template v-else>
                    <time>{{ item.requestDate }}</time>
                    <span>{{ item.description || item.requestNumber }}</span>
                    <span class="parts-request-actions">
                      <button class="secondary-button" type="button" @click="startEditPartsRequest(item)">Изменить</button>
                      <button class="secondary-button" type="button" @click="deletePartsRequest(request, item)">Удалить</button>
                    </span>
                  </template>
                </div>
              </div>
            </div>
          </article>
        </div>
        <p v-else class="vehicle-empty">Заявок на закупку ЗЧ нет.</p>
      </section>

      <section v-if="props.section === 'works'" class="vehicle-controls macos-glass-panel repair-defects">
        <div class="repair-form-heading">
          <div>
            <p class="eyebrow">НЕИСПРАВНАЯ ТЕХНИКА</p>
            <h2>Информация для ремонта</h2>
          </div>
        </div>

        <p v-if="isLoading" class="table-message">Загрузка неисправностей...</p>
        <div v-else class="repair-defect-cards">
          <article
            v-for="defect in repairDefects"
            :key="defect.id"
            class="repair-card repair-timeline-card"
          >
            <header class="repair-overview-header">
              <div>
                <h2>{{ defect.vehicleName || 'Техника' }} · Гар. №{{ defect.vehicleGarageNumber ?? '—' }}</h2>
                <p class="repair-overview-meta"><span>Гос. № {{ defect.vehicleStateNumber || '—' }}</span><span>Простой с {{ formatDateTime(defect.downtimeStartedAt) }}</span></p>
              </div>
              <span class="repair-status-badge" :class="`repair-status-badge--${defect.repairStatus}`">{{ repairStatusLabel(defect.repairStatus) }}</span>
            </header>

            <section class="repair-fault-panel" aria-label="Неисправность">
              <div><h3>Неисправность</h3><p>{{ defect.symptoms || defect.failureReason || 'Описание не указано.' }}</p></div>
              <div class="repair-timeline-media">
                <button v-if="defect.photos?.length" class="secondary-button" type="button" @click="openRepairMediaCollection(defect, 'image')">Фото · {{ defect.photos.length }}</button>
                <button v-if="defect.videos?.length" class="secondary-button" type="button" @click="openRepairMediaCollection(defect, 'video')">Видео · {{ defect.videos.length }}</button>
                <span v-if="!defect.photos?.length && !defect.videos?.length" class="repair-card-muted">Нет вложений</span>
              </div>
            </section>

            <section class="repair-timeline-panel" aria-label="История ремонта">
              <h3>История ремонта <span class="repair-stage-count">Этапов: {{ defect.repairWorks?.length || 0 }}</span></h3>
              <ol v-if="defect.repairWorks?.length" class="repair-timeline">
                <li v-for="(work, index) in repairWorksInOrder(defect).slice().reverse()" :key="work.id" class="repair-timeline-item">
                  <details class="repair-timeline-stage" :open="index === 0">
                    <summary>
                      <span class="repair-stage-date">{{ formatDateTime(work.createdAt) }}</span>
                      <span class="repair-stage-excerpt">{{ work.description || 'Описание не указано.' }}</span>
                      <span class="repair-stage-chevron" aria-hidden="true">⌄</span>
                    </summary>
                    <div class="repair-stage-content">
                      <p>{{ work.description || 'Описание не указано.' }}</p>
                      <div class="repair-timeline-media">
                        <button v-if="work.photos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([work], 'image'), 'image')">Фото · {{ work.photos.length }}</button>
                        <button v-if="work.videos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([work], 'video'), 'video')">Видео · {{ work.videos.length }}</button>
                        <span v-if="!work.photos?.length && !work.videos?.length" class="repair-card-muted">Нет вложений</span>
                      </div>
                    </div>
                  </details>
                  <button class="repair-stage-delete repair-timeline-delete" type="button" :disabled="isSaving" :aria-label="`Удалить этап ${formatDateTime(work.createdAt)} вместе с медиа`" title="Удалить этап вместе с медиа" @click="deleteRepairWork(defect, work.id)">
                    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 14h10l1-14M10 10v7m4-7v7" /></svg>
                  </button>
                </li>
              </ol>
              <p v-else class="repair-card-muted">Этапов пока нет. Добавьте первую запись о выполненной работе.</p>
            </section>

            <details class="repair-new-stage" :open="expandNewRepairForm">
              <summary><span class="repair-new-stage-closed">+ Добавить этап</span><span class="repair-new-stage-open">Новый этап</span><span aria-hidden="true" class="repair-stage-chevron">⌄</span></summary>
              <form class="repair-new-stage-form" @submit.prevent="completeRepair(defect)">
                <div class="repair-new-stage-description">
                  <label :for="`repair-description-${defect.id}`">Описание выполненных работ</label>
                  <textarea :id="`repair-description-${defect.id}`" v-model="repairDraft(defect).description" rows="4" placeholder="Что было сделано?" required :disabled="isSaving"></textarea>
                  <div class="repair-file-upload">
                    <button class="secondary-button repair-upload-button" type="button" :disabled="isSaving" @click="repairFileInputs.get(defect.id)?.click()"><span aria-hidden="true">+</span> Добавить фото или видео</button>
                    <input :ref="element => element ? repairFileInputs.set(defect.id, element) : repairFileInputs.delete(defect.id)" type="file" accept="image/*,video/*" multiple hidden :disabled="isSaving" @change="uploadRepairFiles(defect, $event)" />
                  </div>
                  <div v-if="repairDraft(defect).previews.length" class="repair-card-file-list" aria-live="polite"><span v-for="item in repairDraft(defect).previews" :key="item.url">{{ item.file.name }}</span></div>
                </div>
                <div class="repair-new-stage-actions">
                  <fieldset class="repair-status-options" :disabled="isSaving">
                    <legend>Статус после выполнения</legend>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="repair" /> На ремонте</label>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="waiting" /> Ожидает запчасти</label>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="done" /> Исправна (Готово)</label>
                  </fieldset>
                  <button class="primary-button repair-save-stage" type="submit" :disabled="isSaving">{{ isSaving ? 'Сохранение…' : 'Сохранить этап' }}</button>
                </div>
              </form>
            </details>
          </article>
          <p v-if="!repairDefects.length" class="vehicle-empty">
            Неисправностей нет.
          </p>
        </div>
      </section>
    </section>

    <div v-if="zoomVisible" class="repair-zoom" @click.self="closeZoom">
      <img
        :src="zoomSrc"
        alt="Увеличенный просмотр"
        :style="{ transform: `translate(${zoomX}px, ${zoomY}px) scale(${zoomScale})` }"
        @wheel="wheelZoom"
        @mousedown="startDrag"
      />
      <button type="button" @click="closeZoom">×</button>
    </div>
    <RepairMediaViewer
      :open="mediaViewerOpen"
      :items="mediaViewerItems"
      :media-type="mediaViewerType"
      :token="token"
      @close="closeMediaViewer"
    />
  </main>
</template>
