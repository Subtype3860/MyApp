<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import UserAvatar from './UserAvatar.vue'
import { Swiper, SwiperSlide } from 'swiper/vue'
import { Navigation, Pagination } from 'swiper/modules'
import 'swiper/css'
import 'swiper/css/navigation'
import 'swiper/css/pagination'

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
const selectedRepairWorkIds = ref({})

// --- Полноэкранный просмотрщик медиавложений неисправности (dialog + Swiper) ---
const mediaViewerUrl = ref('')
const mediaViewerName = ref('')
const mediaViewerType = ref('image')
const mediaViewerItems = ref([])
const mediaViewerIndex = ref(0)
// Кэш Blob-URL миниатюр вложений по ключу `${type}:${mediaId}`.
const mediaThumbnailUrls = ref({})
const zoomDialog = ref(null)
const pinchStartDistance = ref(0)
const pinchStartScale = ref(1)

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
  clearMediaThumbnails()
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

function workMedia(defect) {
  const work = selectedRepairWork(defect)
  return workMediaForWorks(work ? [work] : [])
}

function allWorkMedia(defect) {
  return workMediaForWorks(defect.repairWorks ?? [])
}

function workMediaForWorks(works) {
  return works.flatMap((work) => [
    ...(work.photos ?? []).map((media) => ({
      media: { ...media, source: 'work' },
      type: 'image',
      src: mediaThumbnail({ ...media, source: 'work' }, 'image'),
    })),
    ...(work.videos ?? []).map((media) => ({
      media: { ...media, source: 'work' },
      type: 'video',
      src: mediaThumbnail({ ...media, source: 'work' }, 'video'),
    })),
  ])
}

/**
 * Загружает данные, необходимые разделу ремонта, в правильном порядке.
 * Сначала загружается техника, затем её журналы и неисправности.
 */
async function loadRepairSection() {
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

/**
 * Загружает журналы всей техники и собирает плоский список незавершённых
 * неисправностей для вкладки «Ремонт», после чего подгружает миниатюры
 * медиавложений.
 */
async function loadRepairDefects() {
  if (!vehicles.value.length) {
    repairDefects.value = []
    return
  }
  isLoading.value = true
  errorMessage.value = ''
  try {
    const journalResults = []
    for (const vehicle of vehicles.value) {
      try {
        journalResults.push({
          status: 'fulfilled',
          value: await loadVehicleRepairDefects(vehicle),
        })
      } catch (reason) {
        journalResults.push({ status: 'rejected', reason })
      }
    }
    const journals = journalResults
      .filter((result) => result.status === 'fulfilled')
      .map((result) => result.value)
    const failedVehicles = journalResults
      .map((result, index) => result.status === 'rejected' ? vehicles.value[index] : null)
      .filter(Boolean)
    repairDefects.value = journals.flat()
    if (failedVehicles.length) {
      errorMessage.value =
        `Не удалось загрузить данные для ${failedVehicles.length} единиц техники. Обновите страницу.`
    }
    try {
      await loadMediaThumbnails(repairDefects.value)
    } catch {
      // Карточки ремонта остаются доступными, даже если отдельное медиа не загрузилось.
    }
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

async function loadVehicleRepairDefects(vehicle) {
  let lastError
  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await fetch(
        `/api/vehicles/${vehicle.id}/journal`,
        { headers: authHeaders() },
      )
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`)
      }
      const result = await response.json()
      return (result.defects ?? [])
        .filter((defect) => !isCompletedStatus(defect.status))
        .map((defect) => ({
          ...defect,
          repairWorks: (result.works ?? []).filter((work) => work.defectId === defect.id),
          repairStatus: repairStatus(defect.status),
          vehicleName: vehicle.modelName,
          vehicleGarageNumber: vehicle.garageNumber,
          vehicleStateNumber: vehicle.stateNumber,
          vehicleId: vehicle.id,
        }))
    } catch (error) {
      lastError = error
      if (attempt === 0) {
        await new Promise((resolve) => setTimeout(resolve, 250))
      }
    }
  }
  throw new Error(
    `Не удалось загрузить журнал техники «${vehicle.modelName || vehicle.id}».`,
    { cause: lastError },
  )
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
  if (['repaired', 'done', 'completed', 'исправна'].includes(normalized)) return 'done'
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

/**
 * Строит список записей истории этапов ремонта для карточки неисправности.
 * @param {object} defect Неисправность с массивом связанных работ `repairWorks`.
 * @returns {{date: string, text: string}[]}
 */
function repairHistory(defect) {
  return [...(defect.repairWorks ?? [])]
    .sort((first, second) =>
      new Date(first.createdAt ?? 0) - new Date(second.createdAt ?? 0))
    .map((work) => ({
      id: work.id,
      date: formatDateTime(work.createdAt),
    }))
}

function repairWorksInOrder(defect) {
  return [...(defect.repairWorks ?? [])].sort((first, second) =>
    new Date(first.createdAt ?? 0) - new Date(second.createdAt ?? 0))
}

function selectedRepairWork(defect) {
  const works = repairWorksInOrder(defect)
  if (!works.length) return null
  const selectedId = selectedRepairWorkIds.value[defect.id]
  return works.find((work) => work.id === selectedId) ?? works[0]
}

function selectRepairWork(defect, workId) {
  selectedRepairWorkIds.value[defect.id] = workId
}

async function deleteRepairWork(defect, workId) {
  if (!window.confirm('Удалить этап ремонта вместе с фото и видео?')) return

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
    delete selectedRepairWorkIds.value[defect.id]
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
  return file.type.startsWith('image/') || file.type.startsWith('video/')
}

/**
 * Обрабатывает загрузку фото/видео в зону «upload-zone» карточки ремонта
 * (drag-and-drop или выбор через `<input type="file">`). Файлы сохраняются
 * локально до нажатия кнопки «ВЫПОЛНЕНИЕ».
 * @param {object} defect Неисправность, к которой относится загрузка.
 * @param {DragEvent|Event} event Событие drop либо изменения `<input>`.
 */
function uploadRepairFiles(defect, event) {
  const files = Array.from(event.dataTransfer?.files ?? event.target?.files ?? [])
    .filter(isRepairMediaFile)
  if (event.target) event.target.value = ''
  if (!files.length) {
    errorMessage.value = 'Выберите фото или видео для загрузки.'
    return
  }

  const draft = repairDraft(defect)
  draft.files.push(...files)
  draft.previews.push(...files.map((file) => ({
    file,
    url: URL.createObjectURL(file),
    type: file.type.startsWith('video/') ? 'video' : 'image',
  })))
  errorMessage.value = ''
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
function selectFiles(event, type) {
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
    clearPhotoPreviews()
    photos.value = files
    photoPreviews.value = files.map((file) => URL.createObjectURL(file))
    photoIndex.value = 0
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
    file.size > 8 * 1024 * 1024
  )) {
    return 'Разрешены JPEG, PNG и WebP размером не более 8 МБ.'
  }
  return ''
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

/**
 * Формирует общий список медиавложений (фото и видео) неисправности
 * с миниатюрами для отображения в Swiper-карусели.
 * @param {object} defect Неисправность.
 * @returns {{media: object, type: 'image'|'video', src: string}[]}
 */
function defectMedia(defect) {
  return [
    ...(defect.photos ?? []).map((media) => ({
      media,
      type: 'image',
      src: mediaThumbnail(media, 'image'),
    })),
    ...(defect.videos ?? []).map((media) => ({
      media,
      type: 'video',
      src: mediaThumbnail(media, 'video'),
    })),
  ]
}

/**
 * Возвращает сегмент API-маршрута для получения медиафайла нужного типа.
 * @param {'image'|'video'} type
 * @returns {string}
 */
function mediaRoute(type, media) {
  const work = media?.source === 'work'
  if (work) return type === 'video' ? 'work-videos' : 'work-photos'
  return type === 'video' ? 'defect-videos' : 'defect-photos'
}

/**
 * Загружает бинарные данные медиавложения с сервера.
 * @param {object} media Медиавложение с идентификатором `id`.
 * @param {'image'|'video'} type Тип вложения.
 * @returns {Promise<Blob>}
 */
async function fetchMediaBlob(media, type) {
  const response = await fetch(`/api/vehicles/${mediaRoute(type, media)}/${media.id}`, {
    headers: authHeaders(),
  })
  if (!response.ok) throw new Error('Не удалось открыть вложение.')
  return response.blob()
}

/**
 * Подгружает и кэширует Blob-URL миниатюр всех медиавложений
 * переданного списка неисправностей.
 * @param {object[]} defects Список неисправностей.
 */
async function loadMediaThumbnails(defects) {
  clearMediaThumbnails()
  const entries = defects.flatMap((defect) => [
    ...defectMedia(defect),
    ...allWorkMedia(defect),
  ])
  await Promise.all(entries.map(async ({ media, type }) => {
    try {
      mediaThumbnailUrls.value[`${type}:${media.id}`] =
        URL.createObjectURL(await fetchMediaBlob(media, type))
    } catch {
      mediaThumbnailUrls.value[`${type}:${media.id}`] = ''
    }
  }))
}

/** Освобождает все закэшированные Blob-URL миниатюр вложений. */
function clearMediaThumbnails() {
  Object.values(mediaThumbnailUrls.value).forEach((url) => {
    if (url) URL.revokeObjectURL(url)
  })
  mediaThumbnailUrls.value = {}
}

/**
 * Возвращает закэшированный Blob-URL миниатюры вложения.
 * @param {object} media Медиавложение.
 * @param {'image'|'video'} type Тип вложения.
 * @returns {string}
 */
function mediaThumbnail(media, type) {
  return mediaThumbnailUrls.value[`${type}:${media.id}`] || ''
}

/**
 * Открывает полноэкранный просмотрщик медиавложений ремонта.
 * @param {object} media Вложение, которое нужно открыть первым.
 * @param {'image'|'video'} type Тип вложения.
 * @param {{media: object, type: string}[]} [items] Список вложений для навигации (карусель).
 * @param {number} [index] Индекс открываемого вложения в `items`.
 */
async function openRepairMedia(media, type, items = [{ media, type }], index = 0) {
  closeMediaViewer()
  mediaViewerItems.value = items
  mediaViewerIndex.value = index
  await loadRepairMedia(media, type)
  if (mediaViewerUrl.value) zoomDialog.value?.showModal()
}

/**
 * Загружает медиавложение в просмотрщик и сбрасывает состояние зума.
 * @param {object} media Вложение.
 * @param {'image'|'video'} type Тип вложения.
 */
async function loadRepairMedia(media, type) {
  try {
    mediaViewerUrl.value = URL.createObjectURL(await fetchMediaBlob(media, type))
    mediaViewerName.value = media.fileName
    mediaViewerType.value = type
    zoomScale.value = 1
    zoomX.value = 0
    zoomY.value = 0
  } catch (error) {
    errorMessage.value = error.message
  }
}

/**
 * Переключает текущее медиавложение в просмотрщике вперёд/назад по кругу.
 * @param {number} step Смещение (+1 — следующее, -1 — предыдущее).
 */
async function changeRepairMedia(step) {
  if (!mediaViewerItems.value.length) return
  mediaViewerIndex.value =
    (mediaViewerIndex.value + step + mediaViewerItems.value.length) %
    mediaViewerItems.value.length
  const item = mediaViewerItems.value[mediaViewerIndex.value]
  await loadRepairMedia(item.media, item.type)
}

/** Закрывает полноэкранный просмотрщик медиавложений и сбрасывает его состояние. */
function closeMediaViewer() {
  if (zoomDialog.value?.open) zoomDialog.value.close()
  if (mediaViewerUrl.value) URL.revokeObjectURL(mediaViewerUrl.value)
  mediaViewerUrl.value = ''
  mediaViewerName.value = ''
  mediaViewerType.value = 'image'
  mediaViewerItems.value = []
  mediaViewerIndex.value = 0
  pinchStartDistance.value = 0
  zoomScale.value = 1
  zoomX.value = 0
  zoomY.value = 0
}

/** Переключает масштаб медиавложения в просмотрщике по одиночному клику/тапу. */
function toggleMediaZoom() {
  zoomScale.value = zoomScale.value === 1 ? 2.5 : 1
  if (zoomScale.value === 1) {
    zoomX.value = 0
    zoomY.value = 0
  }
}

/**
 * Вычисляет расстояние между двумя точками касания (для pinch-zoom).
 * @param {TouchList} touches
 * @returns {number}
 */
function touchDistance(touches) {
  const [first, second] = touches
  return Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY)
}

/**
 * Начинает pinch-zoom жест: запоминает начальное расстояние между пальцами и масштаб.
 * @param {TouchEvent} event
 */
function startMediaTouch(event) {
  if (event.touches.length !== 2) return
  pinchStartDistance.value = touchDistance(event.touches)
  pinchStartScale.value = zoomScale.value
}

/**
 * Обновляет масштаб при движении пальцев во время pinch-zoom.
 * @param {TouchEvent} event
 */
function moveMediaTouch(event) {
  if (event.touches.length !== 2 || !pinchStartDistance.value) return
  const scale = pinchStartScale.value *
    (touchDistance(event.touches) / pinchStartDistance.value)
  zoomScale.value = Math.min(Math.max(scale, 1), 4)
  if (zoomScale.value === 1) {
    zoomX.value = 0
    zoomY.value = 0
  }
}

/**
 * Завершает pinch-zoom жест, когда остаётся меньше двух точек касания.
 * @param {TouchEvent} event
 */
function endMediaTouch(event) {
  if (event.touches.length < 2) pinchStartDistance.value = 0
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
          <article v-for="defect in repairDefects" :key="defect.id" class="repair-card">
            <header class="card-header">
              <h2>Ремонт: Гар. № {{ defect.vehicleGarageNumber ?? '—' }} ({{ defect.vehicleName || '—' }})</h2>
              <div class="header-actions">
                <span class="repair-status-badge" :class="`repair-status-badge--${defect.repairStatus}`">
                  Статус: {{ repairStatusLabel(defect.repairStatus) }}
                </span>
              </div>
            </header>
            <section class="card-body">
              <div class="column left">
                <h3>ТЕХНИКА</h3>
                <p><b>Модель:</b> {{ defect.vehicleName || '—' }}</p>
                <p><b>Гос. №:</b> {{ defect.vehicleStateNumber || '—' }}</p>
                <p><b>Дата простоя:</b> {{ formatDateTime(defect.downtimeStartedAt) }}</p>

                <h3>ОПИСАНИЕ НЕИСПРАВНОСТИ</h3>
                <p class="repair-description">{{ defect.symptoms || defect.failureReason || '—' }}</p>

                <h3>ЭТАПЫ РЕМОНТА (История)</h3>
                <ul v-if="repairHistory(defect).length">
                  <li v-for="item in repairHistory(defect)" :key="item.id">
                    <button
                      class="repair-stage-button"
                      :class="{ active: selectedRepairWork(defect)?.id === item.id }"
                      type="button"
                      @click="selectRepairWork(defect, item.id)"
                    >
                      • {{ item.date }}
                    </button>
                    <button
                      class="repair-stage-delete"
                      type="button"
                      :disabled="isSaving"
                      title="Удалить этап вместе с медиа"
                      @click.stop="deleteRepairWork(defect, item.id)"
                    >
                      ×
                    </button>
                  </li>
                </ul>
                <p v-else class="repair-card-muted">История пока отсутствует.</p>
                <div class="add-history">
                  <textarea
                    v-model="repairDraft(defect).description"
                    rows="3"
                    placeholder="Отчёт о выполненной работе..."
                  ></textarea>
                </div>

                <label
                  class="upload-zone"
                  @dragover.prevent
                  @drop.prevent="uploadRepairFiles(defect, $event)"
                >
                  <input
                    type="file"
                    accept="image/*,video/*"
                    multiple
                    @change="uploadRepairFiles(defect, $event)"
                  />
                  ⬆ Перетащите файлы сюда<br />или нажмите для выбора
                </label>
                <div v-if="repairDraft(defect).previews.length" class="repair-card-file-list">
                  <span v-for="item in repairDraft(defect).previews" :key="item.url">
                    {{ item.file.name }}
                  </span>
                </div>

                <h3>СТАТУС РЕМОНТА</h3>
                <div class="status-options">
                  <label>
                    <input v-model="repairDraft(defect).status" type="radio" value="repair" />
                    На ремонте
                  </label>
                  <label>
                    <input v-model="repairDraft(defect).status" type="radio" value="waiting" />
                    Ожидает запчасти
                  </label>
                  <label>
                    <input v-model="repairDraft(defect).status" type="radio" value="done" />
                    Исправна (Готово)
                  </label>
                </div>
                <button
                  class="repair-card-button repair-card-button--secondary"
                  type="button"
                  :disabled="isSaving"
                  @click="completeRepair(defect)"
                >
                  ВЫПОЛНЕНИЕ
                </button>
              </div>

              <div class="column right">
                <h3>ФОТО И ВИДЕО НЕИСПРАВНОСТИ</h3>
                <div class="repair-card-media">
                  <span v-if="!defect.photos.length && !defect.videos.length">Нет вложений</span>
                  <Swiper
                    v-else
                    :modules="[Navigation, Pagination]"
                    navigation
                    pagination
                    class="repair-media-swiper"
                  >
                    <SwiperSlide
                      v-for="(item, index) in defectMedia(defect)"
                      :key="item.media.id"
                    >
                      <div
                        class="repair-media-slide"
                        @click="openRepairMedia(item.media, item.type, defectMedia(defect), index)"
                      >
                        <img
                          v-if="item.type === 'image' && item.src"
                          :src="item.src"
                          :alt="item.media.fileName || 'Фото неисправности'"
                        />
                        <video
                          v-else-if="item.type === 'video' && item.src"
                          :src="item.src"
                          muted
                          preload="metadata"
                          aria-label="Видео неисправности"
                        ></video>
                        <span v-else class="repair-media-thumb-placeholder">
                          {{ item.type === 'video' ? 'Видео' : 'Фото' }}
                        </span>
                      </div>
                    </SwiperSlide>
                  </Swiper>
                </div>

                <h3>ФОТО И ВИДЕО РЕМОНТНЫХ РАБОТ</h3>
                <div class="repair-card-media">
                  <span v-if="!workMedia(defect).length">
                    Нет вложений
                  </span>
                  <Swiper
                    v-else
                    :modules="[Navigation, Pagination]"
                    navigation
                    pagination
                    class="repair-media-swiper"
                  >
                    <SwiperSlide
                      v-for="(item, index) in workMedia(defect)"
                      :key="`work-${item.media.id}`"
                    >
                      <div
                        class="repair-media-slide"
                        @click="openRepairMedia(item.media, item.type, workMedia(defect), index)"
                      >
                        <img
                          v-if="item.type === 'image' && item.src"
                          :src="item.src"
                          :alt="item.media.fileName || 'Фото ремонтных работ'"
                        />
                        <video
                          v-else-if="item.type === 'video' && item.src"
                          :src="item.src"
                          muted
                          preload="metadata"
                          aria-label="Видео ремонтных работ"
                        ></video>
                        <span v-else class="repair-media-thumb-placeholder">
                          {{ item.type === 'video' ? 'Видео' : 'Фото' }}
                        </span>
                      </div>
                    </SwiperSlide>
                  </Swiper>
                </div>
                <div v-if="selectedRepairWork(defect)" class="repair-work-report">
                  <h3>ОПИСАНИЕ ВЫПОЛНЕННЫХ РАБОТ</h3>
                  <p>
                    {{ selectedRepairWork(defect).description || 'Описание не указано.' }}
                  </p>
                </div>

              </div>
            </section>
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
    <dialog ref="zoomDialog" class="repair-zoom repair-media-dialog" @click.self="closeMediaViewer">
      <div v-if="mediaViewerUrl" class="repair-media-viewer">
          <div class="repair-media-viewer-header">
            <strong>{{ mediaViewerName }}</strong>
            <span v-if="mediaViewerItems.length">
              {{ mediaViewerIndex + 1 }} / {{ mediaViewerItems.length }}
            </span>
          </div>
          <div class="repair-media-viewer-content">
            <button
              v-if="mediaViewerItems.length > 1"
              class="repair-media-nav repair-media-nav--prev"
              type="button"
              aria-label="Предыдущее вложение"
              @click="changeRepairMedia(-1)"
            >
              ‹
            </button>
            <video
              v-if="mediaViewerType === 'video'"
              :src="mediaViewerUrl"
              controls
            ></video>
            <img
              v-else
              :src="mediaViewerUrl"
              :alt="mediaViewerName"
              :style="{ transform: `translate(${zoomX}px, ${zoomY}px) scale(${zoomScale})` }"
              @dblclick="toggleMediaZoom"
              @touchstart="startMediaTouch"
              @touchmove.prevent="moveMediaTouch"
              @touchend="endMediaTouch"
            />
            <button
              v-if="mediaViewerItems.length > 1"
              class="repair-media-nav repair-media-nav--next"
              type="button"
              aria-label="Следующее вложение"
              @click="changeRepairMedia(1)"
            >
              ›
            </button>
          </div>
          <div class="repair-media-viewer-thumbs">
            <button
              v-for="(item, index) in mediaViewerItems"
              :key="item.media.id"
              class="repair-media-viewer-thumb"
              type="button"
              :class="{ active: index === mediaViewerIndex }"
              @click="changeRepairMedia(index - mediaViewerIndex)"
            >
              <img
                v-if="mediaThumbnail(item.media, item.type) && item.type === 'image'"
                :src="mediaThumbnail(item.media, item.type)"
                :alt="item.media.fileName || `Фото ${index + 1}`"
              />
              <video
                v-else-if="mediaThumbnail(item.media, item.type)"
                :src="mediaThumbnail(item.media, item.type)"
                muted
                preload="metadata"
                aria-hidden="true"
              ></video>
              <span v-else>{{ item.type === 'video' ? 'Видео' : 'Фото' }} {{ index + 1 }}</span>
            </button>
          </div>
        <button class="repair-media-viewer-close" type="button" @click="closeMediaViewer">×</button>
      </div>
    </dialog>
  </main>
</template>
