<script setup>
import MaintenancePlans from "./MaintenancePlans.vue"
import { computed, nextTick, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import UserAvatar from './UserAvatar.vue'
import RepairHistory from './RepairHistory.vue'
import ShiftReport from './ShiftReport.vue'
import GtoForecast from './GtoForecast.vue'
import VehicleHours from './VehicleHours.vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import RepairActionsMenu from './RepairActionsMenu.vue'
import WorkParticipants from './WorkParticipants.vue'
import { convertImageToWebp } from '../utils/mediaUpload'

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
const session = computed(() => {
  try {
    const payload = props.token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    return JSON.parse(atob(payload))
  } catch { return {} }
})
const isAdministrator = computed(() => String(session.value.role ?? session.value['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ?? '').trim().toLowerCase() === 'administrator')
/** Определяет доступность удаления работы для текущего пользователя. */
function canDeleteWork(work) {
  return isAdministrator.value || Boolean(work.createdById && String(work.createdById).toLowerCase() === String(session.value.sub ?? '').toLowerCase())
}
const participationProfile = ref(null)
provide('workParticipation', {
  token: computed(() => props.token),
  userId: computed(() => participationProfile.value?.id || session.value.sub || ''),
  canReview: computed(() => String(participationProfile.value?.position || '').trim().toLocaleLowerCase('ru-RU') === 'механик'),
})
/** Загружает профиль для проверки действий с участниками работ. */
async function loadParticipationProfile() {
  try {
    const response = await fetch('/api/profile', { headers: authHeaders() })
    if (response.ok) participationProfile.value = await response.json()
  } catch { participationProfile.value = null }
}
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
const repairDialog = ref(null)
const selectedRepairId = ref('')
const selectedRepairs = computed(() => repairDefects.value.filter(defect => defect.id === selectedRepairId.value))
async function openRepair(defect) {
  selectedRepairId.value = defect.id
  await nextTick()
  repairDialog.value?.showModal()
}
function closeRepair() {
  repairDialog.value?.close()
  selectedRepairId.value = ''
}
watch(selectedRepairs, repairs => {
  if (selectedRepairId.value && !repairs.length) closeRepair()
})
const repairDrafts = ref({})
const repairFileInputs = new Map()
const expandNewRepairForm = window.matchMedia('(min-width: 761px)').matches

// --- Полноэкранный просмотрщик медиафайлов ---
const mediaViewerOpen = ref(false)
const mediaViewerItems = ref([])
const mediaViewerType = ref('image')

// --- Форма заявки на ремонт ---
const problemDescription = ref('')
const requestOperationalState = ref('operating')
const requestDate = ref(new Date().toISOString().slice(0, 10))
const requestTime = ref(new Date().toTimeString().slice(0, 5))
const requestFileInput = ref(null)
const preparingRequestMedia = ref(false)
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

const partsActionDialog = ref(null)
const partsAction = ref('add')
const partsActionRowId = ref('')
const partsActionItemId = ref('')
const partsActionDraft = ref({ date: '', description: '', completed: false, completedDate: '' })
const selectedPartsActionRow = computed(() => partsRequests.value.find(row => row.id === partsActionRowId.value))
const selectedPartsActionItem = computed(() => selectedPartsActionRow.value?.partsRequests.find(item => item.id === partsActionItemId.value))
watch(partsActionRowId, () => { partsActionItemId.value = selectedPartsActionRow.value?.partsRequests[0]?.id ?? '' })
watch(selectedPartsActionItem, item => {
  if (partsAction.value !== 'add') partsActionDraft.value = { date: item?.requestDate ?? '', description: item?.description || item?.requestNumber || '', completed: partsAction.value === 'complete' || Boolean(item?.completedDate), completedDate: item?.completedDate || new Date().toISOString().slice(0, 10) }
})
/** Открывает форму выбранного действия с заявкой на запчасти. */
async function openPartsAction(action, row) {
  if (isSaving.value || isLoading.value || !row || !partsRequests.value.includes(row)) return
  if (action !== 'add' && !row.partsRequests.length) return
  partsAction.value = action
  errorMessage.value = ''
  successMessage.value = ''
  partsActionRowId.value = row.id
  partsActionItemId.value = selectedPartsActionRow.value?.partsRequests[0]?.id ?? ''
  const item = selectedPartsActionItem.value
  partsActionDraft.value = action === 'add'
    ? { date: new Date().toISOString().slice(0, 10), description: '', completed: false, completedDate: new Date().toISOString().slice(0, 10) }
    : { date: item?.requestDate ?? '', description: item?.description || item?.requestNumber || '', completed: partsAction.value === 'complete' || Boolean(item?.completedDate), completedDate: item?.completedDate || new Date().toISOString().slice(0, 10) }
  await nextTick()
  partsActionDialog.value?.showModal()
}
/** Закрывает форму действия с заявкой на запчасти. */
function closePartsAction() {
  partsActionDialog.value?.close()
}
/** Выполняет выбранное действие с заявкой на запчасти по данным формы. */
async function submitPartsAction() {
  if (isSaving.value || !selectedPartsActionRow.value) return
  const row = selectedPartsActionRow.value
  const item = selectedPartsActionItem.value
  if (partsActionDraft.value.completed && (!partsActionDraft.value.completedDate || partsActionDraft.value.completedDate < partsActionDraft.value.date)) {
    errorMessage.value = 'Дата выполнения не может быть раньше даты заявки.'
    return
  }
  let saved
  if (partsAction.value === 'add') {
    partsRequestDrafts.value[row.id] = { ...partsActionDraft.value }
    saved = await savePartsRequest(row)
  } else if (item && ['edit', 'complete'].includes(partsAction.value)) {
    partsRequestEditDrafts.value[item.id] = { ...partsActionDraft.value }
    saved = await saveEditedPartsRequest(item)
  } else if (item) saved = await deletePartsRequest(row, item)
  if (saved) closePartsAction()
}

/** Возвращает или создаёт черновик редактирования заявки на запчасти. */
function partsRequestDraft(request) {
  if (!partsRequestDrafts.value[request.id]) {
    partsRequestDrafts.value[request.id] = {
      date: new Date().toISOString().slice(0, 10),
      description: '',
    }
  }
  return partsRequestDrafts.value[request.id]
}

/** Форматирует дату заявки на запчасти. */
function formatPartsRequestDate(value) {
  const date = new Date(`${value}T00:00:00`)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString('ru-RU')
}

/** Подготавливает историю заявок на запчасти для отображения. */
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
  report: 'Отчёт за смену',
  gtoForecast: 'Выход ГТО',
  maintenanceExecution: 'Проведение ТО',
}[props.section] ?? 'Транспорт'))

onMounted(async () => {
  await Promise.all([loadRepairSection(), loadParticipationProfile()])
})
watch(() => props.section, loadRepairSection)
onBeforeUnmount(() => {
  closeRepair()
  partsActionDialog.value?.close()
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

/** Собирает вложения из списка работ для просмотра. */
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
  closeRepair()
  partsActionDialog.value?.close()
  if (['repairHistory', 'report', 'gtoForecast', 'maintenanceExecution', 'hours'].includes(props.section)) {
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

/** Загружает заявки всего парка одним пакетным запросом. */
async function loadPartsRequests() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const requests = []
    const response = await fetch('/api/vehicles/repair-journal', { headers: authHeaders() })
    if (!response.ok) throw new Error('Не удалось загрузить заявки на закупку ЗЧ. Попробуйте открыть раздел повторно.')
    for (const result of await response.json()) {
      const vehicle = result.vehicle
      const works = [...(result.works ?? [])].sort(
        (left, right) => new Date(right.completedAt || right.createdAt) - new Date(left.completedAt || left.createdAt)
          || new Date(right.createdAt ?? 0) - new Date(left.createdAt ?? 0),
      )
      const worksByDefect = new Map()
      for (const work of works) {
        if (!work.defectId) continue
        const stages = worksByDefect.get(work.defectId) ?? []
        stages.push(work)
        worksByDefect.set(work.defectId, stages)
      }
      for (const item of result.partsRequests ?? []) {
        if (!worksByDefect.has(item.defectId)) worksByDefect.set(item.defectId, [])
      }
      for (const [defectId, stages] of worksByDefect) {
        const defect = (result.defects ?? []).find(item => item.id === defectId)
        const work = stages[0] ?? { id: defectId, defectId, description: defect?.symptoms ?? '', status: defect?.status ?? 'awaiting_parts' }
        const history = [...new Map([...stages.flatMap(stage => stage.partsRequests ?? []), ...(result.partsRequests ?? []).filter(item => item.defectId === defectId)]
          .map(item => [item.id, item])).values()]
          .sort((left, right) => String(right.requestDate).localeCompare(String(left.requestDate)))
        if (history.length || stages.some(stage => stage.status === 'awaiting_parts')) {
          requests.push({
            ...work,
            partsRequests: history,
            vehicleOperationalState: result.vehicle?.operationalState ?? vehicle.operationalState ?? (isCompletedStatus(work.status) ? "healthy" : "stopped"),
            vehicleName: vehicle.modelName,
            vehicleTypeName: vehicle.typeName,
            vehicleStateNumber: vehicle.stateNumber,
            vehicleGarageNumber: vehicle.garageNumber,
          })
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

/** Проверяет и создаёт заявку на запчасти. */
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
        completedDate: draft.completed ? draft.completedDate : null,
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
        completedDate: draft.completed ? draft.completedDate : null,
      },
    ]
    draft.description = ''
    successMessage.value = 'Заявка сохранена.'
    return true
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

/** Проверяет и сохраняет изменённые реквизиты заявки на запчасти. */
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
        completedDate: draft.completed ? draft.completedDate : null,
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
    item.completedDate = draft.completed ? draft.completedDate : null
    successMessage.value = 'Заявка изменена.'
    return true
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSaving.value = false
  }
}

/** Удаляет заявку на запчасти после подтверждения и обновляет список. */
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
    successMessage.value = 'Заявка удалена.'
    return true
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

/** Загружает историю ремонта, включая архивную технику. */
async function loadRepairHistory(signal) {
  const response = await fetch('/api/vehicles/repair-journal?includeArchived=true', {
    headers: authHeaders(),
    ...(signal ? { signal } : {}),
  })
  if (!response.ok) throw new Error('Не удалось загрузить историю ремонта.')
  return (await response.json()).map(mapRepairHistory)
}

/** Группирует работы, вложения и заявки вокруг неисправностей для истории ремонта. */
function mapRepairHistory(journal) {
  const worksByDefect = new Map()
  for (const work of journal.works ?? []) {
    if (!work.defectId) continue
    const works = worksByDefect.get(work.defectId) ?? []
    works.push({
      id: work.id,
      date: work.completedAt || work.createdAt,
      dateLabel: formatDateTime(work.completedAt || work.createdAt),
      name: work.description || work.cause || 'Ремонтная работа',
      performer: work.performerName,
      performerPosition: work.performerPosition,
      participants: work.participants,
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

    const latestCompletedWork = (journal.works ?? [])
      .filter(work => work.defectId === defect.id && work.completedAt)
      .sort((left, right) => new Date(right.completedAt) - new Date(left.completedAt)
        || new Date(right.createdAt ?? 0) - new Date(left.createdAt ?? 0))[0]
    const status = latestCompletedWork ? repairStatus(latestCompletedWork.status) : 'queue'
    return {
      status,
      statusLabel: repairStatusLabel(status),
      id: defect.id,
      createdById: defect.createdById,
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

  return { vehicle: journal.vehicle, requests }
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
          vehicleOperationalState: journal.vehicle.operationalState ?? "stopped",
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
    queue: 'Зарегистрирована',
    repair: 'В работе',
    waiting: 'Ожидает запчасти',
    done: 'Выполнена',
  }[repairStatus(status)]
}

/** Возвращает понятное название рабочего состояния техники. */
function operationalStateLabel(state) {
  return { healthy: 'Исправна', operating: 'Работает с неисправностью', operating_with_defect: 'Работает с неисправностью', stopped: 'Простаивает' }[state] || 'Простаивает'
}

/** Возвращает этапы ремонта в хронологическом порядке. */
function repairWorksInOrder(defect) {
  return [...(defect.repairWorks ?? [])].sort((first, second) =>
    new Date(first.createdAt ?? 0) - new Date(second.createdAt ?? 0))
}

/** После подтверждения удаляет этап ремонта вместе с его вложениями. */
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

/** Возвращает или создаёт черновик отчёта о ремонте выбранной неисправности. */
function repairDraft(defect) {
  if (!repairDrafts.value[defect.id]) {
    repairDrafts.value[defect.id] = {
      description: '',
      status: 'repair',
      location: '',
      operationalState: defect.operationalState === 'operating' ? 'operating' : 'stopped',
      downtimeStartedAt: defect.downtimeStartedAt ? new Date(new Date(defect.downtimeStartedAt).getTime() - new Date(defect.downtimeStartedAt).getTimezoneOffset() * 60000).toISOString().slice(0, 16) : '',
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

/** Проверяет отчёт о работе, сохраняет этап ремонта и загружает его вложения. */
async function completeRepair(defect) {
  const draft = repairDraft(defect)
  const description = draft.description.trim()
  if (!draft.location) {
    errorMessage.value = 'Укажите место работ: КАРЬЕР или РММ.'
    return
  }
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
        location: draft.location,
        status,
        operationalState: draft.status === 'done' ? 'operating' : draft.operationalState,
        downtimeStartedAt: draft.status !== 'done' && draft.operationalState === 'stopped' ? new Date(draft.downtimeStartedAt).toISOString() : null,
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

/** Добавляет фото и видео из одного выбора, сохраняя предыдущие вложения. */
async function selectFiles(event) {
  const files = Array.from(event.target.files ?? [])
  event.target.value = ''
  if (!files.length || preparingRequestMedia.value || isSaving.value) return
  const images = files.filter(file => file.type.startsWith('image/'))
  const clips = files.filter(file => file.type.startsWith('video/'))
  const validation = files.length !== images.length + clips.length
    ? 'Выберите фото или видео.'
    : photos.value.length + images.length > 10
      ? 'Можно выбрать не более 10 фотографий.'
      : validatePhotos(images) || validateVideos(clips)
  if (validation) { errorMessage.value = validation; return }
  preparingRequestMedia.value = true
  errorMessage.value = ''
  try {
    const webpFiles = await Promise.all(images.map(convertImageToWebp))
    const oversizedImage = webpFiles.find(file => file.size > 8 * 1024 * 1024)
    if (oversizedImage) throw new Error(`Фото «${oversizedImage.name}» после конвертации превышает 8 МБ.`)
    photos.value.push(...webpFiles)
    photoPreviews.value.push(...webpFiles.map(file => URL.createObjectURL(file)))
    videos.value.push(...clips)
    videoPreviews.value.push(...clips.map(file => URL.createObjectURL(file)))
  } catch (error) {
    errorMessage.value = error.message || 'Не удалось преобразовать фото в WebP.'
  } finally { preparingRequestMedia.value = false }
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
  if (isSaving.value || preparingRequestMedia.value) return
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
          operationalState: requestOperationalState.value,
          downtimeStartedAt: requestOperationalState.value === 'stopped' ? new Date(
            `${requestDate.value}T${requestTime.value}`,
          ).toISOString() : null,
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

/** Подготавливает список фотографий или видео неисправности для просмотрщика. */
function defectMedia(defect, type) {
  return (type === 'video' ? defect.videos ?? [] : defect.photos ?? [])
    .map((media) => ({ media, type }))
}

/** Открывает просмотр непустого списка вложений ремонта. */
function openRepairMedia(items, type) {
  if (!items.length) return
  mediaViewerItems.value = items
  mediaViewerType.value = type
  mediaViewerOpen.value = true
}

/** Открывает фотографии или видео выбранной неисправности. */
function openRepairMediaCollection(defect, type) {
  return openRepairMedia(defectMedia(defect, type), type)
}

/** Закрывает просмотрщик и очищает список вложений. */
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
        <p class="eyebrow">{{ ['report', 'gtoForecast'].includes(props.section) ? 'ОТЧЁТЫ' : 'ТРАНСПОРТ' }}</p>
        <h1>{{ sectionTitle }}</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <section class="vehicle-content">
      <VehicleHours v-if="props.section === 'hours'" :token="token" />
      <ShiftReport v-if="props.section === 'report'" :token="token" />
      <MaintenancePlans v-if="props.section === 'maintenanceExecution'" :token="token" />
      <GtoForecast v-if="props.section === 'gtoForecast'" :token="token" />
      <RepairHistory
        v-if="props.section === 'repairHistory'"
        :token="token"
        :load-history="loadRepairHistory"
        :current-user-id="participationProfile?.id || session.sub || ''"
        :is-mechanic="['механик', 'старший механик'].includes(String(participationProfile?.position || '').trim().toLocaleLowerCase('ru-RU'))"
        :is-administrator="isAdministrator"
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
          <label class="vehicle-field vehicle-field--full">
            <span>Эксплуатация машины</span>
            <select v-model="requestOperationalState"><option value="operating">Эксплуатация продолжается</option><option value="stopped">Машина остановлена</option></select>
          </label>
          <label v-if="requestOperationalState === 'stopped'" class="vehicle-field">
            <span>Дата начала простоя</span>
            <input v-model="requestDate" required type="date" />
          </label>
          <label v-if="requestOperationalState === 'stopped'" class="vehicle-field">
            <span>Время начала простоя</span>
            <input v-model="requestTime" required type="time" />
          </label>
          <div class="repair-file-upload vehicle-field--full">
            <button class="secondary-button repair-upload-button" type="button" :disabled="isSaving || preparingRequestMedia" @click="requestFileInput?.click()"><span aria-hidden="true">+</span> Добавить фото или видео</button>
            <input ref="requestFileInput" type="file" accept="image/jpeg,image/png,image/webp,video/mp4,video/webm,video/quicktime" multiple hidden :disabled="isSaving || preparingRequestMedia" @change="selectFiles" />
            <span v-if="preparingRequestMedia" role="status">Подготовка вложений…</span>
          </div>
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
        <button class="primary-button vehicle-save-button" type="button" :disabled="isSaving || preparingRequestMedia" @click="submitForm">
          {{ isSaving ? 'Отправка...' : 'Отправить заявку' }}
        </button>
      </section>

      <section v-if="props.section === 'partsRequest'" class="parts-request-cards">
        <div class="parts-request-toolbar">
          <p>Заявки на закупку запчастей <span class="parts-request-count">{{ partsRequests.length }}</span></p>

        </div>
        <p v-if="isLoading" class="table-message">Загрузка заявок...</p>
        <div v-else-if="partsRequests.length" class="parts-request-table-wrap">
          <table class="parts-request-table">
            <colgroup><col class="parts-request-col-vehicle" /><col class="parts-request-col-cause" /><col class="parts-request-col-orders" /></colgroup>
            <thead><tr><th scope="col">Техника</th><th scope="col">Требуется</th><th scope="col">Заявки</th></tr></thead>
            <tbody>
              <tr v-for="request in partsRequests" :key="request.id" class="parts-request-card">
                <td class="parts-request-cell-vehicle" data-label="Техника">
                  <h3>{{ request.vehicleName || 'Модель не указана' }}</h3>
                  <p class="parts-request-vehicle-type">{{ request.vehicleTypeName || 'Тип не указан' }}</p>
                  <dl class="parts-request-card__vehicle-details">
                    <div><dt>Гаражный №</dt><dd>{{ request.vehicleGarageNumber ?? '—' }}</dd></div>
                    <div><dt>Госномер</dt><dd>{{ request.vehicleStateNumber || '—' }}</dd></div>
                  </dl>
                  <span class="parts-request-card__status" :class="`parts-request-card__status--${repairStatus(request.status)}`">{{ operationalStateLabel(request.vehicleOperationalState) }}</span>
                </td>
                <td class="parts-request-cell-cause" data-label="Требуется">
                  <p class="parts-request-cause-text"><strong class="visually-hidden">Требуется: </strong>{{ request.description || 'Не указано, что требуется.' }}</p>
                  <div v-if="request.photos?.length || request.videos?.length" class="parts-request-media">
                    <button v-if="request.photos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([request], 'image'), 'image')">Фото · {{ request.photos.length }}</button>
                    <button v-if="request.videos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([request], 'video'), 'video')">Видео · {{ request.videos.length }}</button>
                  </div>
                </td>
                <td class="parts-request-cell-orders" data-label="Заявки">
          <RepairActionsMenu label="Действия с заявками на закупку" add-label="Добавить заявку" edit-label="Редактировать заявку" delete-label="Удалить заявку" complete-label="Выполнено" :busy="isSaving || isLoading"
            :edit-disabled="!request.partsRequests.length" :delete-disabled="!request.partsRequests.length"
            @add="openPartsAction('add', request)" @edit="openPartsAction('edit', request)" @delete="openPartsAction('delete', request)" @complete="openPartsAction('complete', request)" />
              <div
                v-if="partsRequestHistory(request).length"
                class="parts-request-history"
              >

                <div
                  v-for="item in partsRequestHistory(request)"
                  :key="item.id"
                  class="parts-request-history__item"
                >
                    <div class="parts-request-history__content">
                      <time :datetime="item.requestDate">{{ formatPartsRequestDate(item.requestDate) }}</time>
                      <p>{{ item.description || item.requestNumber }}</p>
                      <p v-if="item.completedDate" class="parts-request-completed">Выполнено · {{ formatPartsRequestDate(item.completedDate) }}</p>
                    </div>
                </div>
              </div>
              <div v-else class="parts-request-history parts-request-history--empty">

                <p>Заявок пока нет.</p>
              </div>


                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p v-else-if="!errorMessage" class="vehicle-empty">Заявок на закупку ЗЧ нет.</p>
        <p v-if="successMessage" class="vehicle-success" role="status">{{ successMessage }}</p>
        <dialog ref="partsActionDialog" class="parts-action-dialog" aria-labelledby="parts-action-title" @cancel="event => { if (isSaving) event.preventDefault(); else closePartsAction() }">
          <form class="parts-request-form" @submit.prevent="submitPartsAction">
            <h2 id="parts-action-title">{{ { add: 'Добавить заявку', edit: 'Редактировать заявку', delete: 'Удалить заявку', complete: 'Выполнено' }[partsAction] }}</h2>
            <div class="parts-action-context">
              <strong>{{ selectedPartsActionRow?.vehicleName || 'Техника' }}</strong>
              <p>Гаражный № {{ selectedPartsActionRow?.vehicleGarageNumber ?? '—' }} · {{ selectedPartsActionRow?.vehicleStateNumber || 'Госномер не указан' }}</p>
              <p>{{ selectedPartsActionRow?.description }}</p>
            </div>
            <label v-if="partsAction !== 'add'">Заявка
              <select v-model="partsActionItemId" aria-label="Заявка" :disabled="isSaving" required>
                <option v-for="item in selectedPartsActionRow?.partsRequests ?? []" :key="item.id" :value="item.id">{{ formatPartsRequestDate(item.requestDate) }} · {{ item.description || item.requestNumber }}</option>
              </select>
            </label>
            <template v-if="partsAction !== 'delete'">
              <label v-if="partsAction !== 'complete'">Дата заявки<input v-model="partsActionDraft.date" type="date" :disabled="isSaving" required /></label>
              <label v-if="partsAction !== 'complete'">Номер и описание заявки<textarea v-model="partsActionDraft.description" rows="5" maxlength="5000" :disabled="isSaving" required></textarea></label>
              <label v-if="partsAction !== 'complete'" class="parts-completed-toggle"><input v-model="partsActionDraft.completed" type="checkbox" :disabled="isSaving" />Выполнено</label>
              <label v-if="partsActionDraft.completed">Дата выполнения<input v-model="partsActionDraft.completedDate" type="date" :min="partsActionDraft.date" :disabled="isSaving" required /></label>
            </template>
            <p v-else class="parts-request-cause-text">{{ selectedPartsActionItem?.description || selectedPartsActionItem?.requestNumber }}</p>
            <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>
            <div class="parts-action-buttons">
              <button class="secondary-button" type="button" :disabled="isSaving" @click="closePartsAction">Отмена</button>
              <button class="primary-button" type="submit" :disabled="isSaving || !selectedPartsActionRow || (partsAction !== 'add' && !selectedPartsActionItem)">{{ isSaving ? 'Сохранение...' : partsAction === 'delete' ? 'Удалить' : 'Сохранить' }}</button>
            </div>
          </form>
        </dialog>
      </section>

      <section v-if="props.section === 'works'" class="vehicle-controls macos-glass-panel repair-defects">
        <div class="repair-form-heading">
          <div>
            <p class="eyebrow">НЕИСПРАВНАЯ ТЕХНИКА</p>
            <h2>Информация для ремонта</h2>
          </div>
        </div>

        <p v-if="isLoading" class="table-message">Загрузка неисправностей...</p>
        <ul v-else-if="repairDefects.length" class="repair-request-list" aria-label="Заявки на ремонт">
          <li v-for="defect in repairDefects" :key="defect.id">
            <button type="button" class="repair-request-row" aria-haspopup="dialog" @click="openRepair(defect)">
              <strong>{{ defect.vehicleName || 'Техника' }} · Гар. №{{ defect.vehicleGarageNumber ?? '—' }}</strong>
              <span>— {{ defect.symptoms || defect.failureReason || 'Описание не указано.' }}</span>
            </button>
          </li>
        </ul>
        <p v-else class="vehicle-empty">Неисправностей нет.</p>
        <dialog ref="repairDialog" class="repair-details-dialog" aria-label="Детали ремонта" @cancel="event => { if (isSaving) event.preventDefault(); else closeRepair() }">
          <div class="repair-dialog-toolbar">
            <strong>Заявка на ремонт</strong>
            <button type="button" class="secondary-button" :disabled="isSaving" @click="closeRepair()">Закрыть</button>
          </div>
          <p v-if="errorMessage" role="alert">{{ errorMessage }}</p>
          <p v-if="successMessage" role="status">{{ successMessage }}</p>
          <article
            v-for="defect in selectedRepairs"
            :key="defect.id"
            class="repair-card repair-timeline-card"
          >
            <header class="repair-overview-header">
              <div>
                <h2>{{ defect.vehicleName || 'Техника' }} · Гар. №{{ defect.vehicleGarageNumber ?? '—' }}</h2>
                <p class="repair-overview-meta"><span>Гос. № {{ defect.vehicleStateNumber || '—' }}</span><span v-if="defect.downtimeStartedAt">Простой с {{ formatDateTime(defect.downtimeStartedAt) }}</span></p>
              </div>
              <div class="repair-state-badges">
                <span class="repair-status-badge">{{ operationalStateLabel(defect.vehicleOperationalState) }}</span>
                <span class="repair-status-badge" :class="`repair-status-badge--${defect.repairStatus}`">Заявка: {{ repairStatusLabel(defect.repairStatus) }}</span>
              </div>
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
                      <WorkParticipants :work="work" />
                      <div class="repair-timeline-media">
                        <button v-if="work.photos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([work], 'image'), 'image')">Фото · {{ work.photos.length }}</button>
                        <button v-if="work.videos?.length" class="secondary-button" type="button" @click="openRepairMedia(workMediaForWorks([work], 'video'), 'video')">Видео · {{ work.videos.length }}</button>
                        <span v-if="!work.photos?.length && !work.videos?.length" class="repair-card-muted">Нет вложений</span>
                      </div>
                    </div>
                  </details>
                  <button v-if="canDeleteWork(work)" class="repair-stage-delete repair-timeline-delete" type="button" :disabled="isSaving" :aria-label="`Удалить этап ${formatDateTime(work.createdAt)} вместе с медиа`" title="Удалить этап вместе с медиа" @click="deleteRepairWork(defect, work.id)">
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
                  <label class="vehicle-field" :for="`repair-location-${defect.id}`"><span>Место работ</span><select :id="`repair-location-${defect.id}`" aria-label="Место работ" v-model="repairDraft(defect).location" required :disabled="isSaving"><option disabled value="">Выберите место</option><option>КАРЬЕР</option><option>РММ</option></select></label>
                  <fieldset class="repair-status-options" :disabled="isSaving">
                    <legend>Статус заявки после выполнения</legend>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="repair" /> В работе</label>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="waiting" /> Ожидает запчасти</label>
                    <label><input v-model="repairDraft(defect).status" :name="`repair-status-${defect.id}`" type="radio" value="done" /> Выполнена</label>
                  </fieldset>
                  <label v-if="repairDraft(defect).status !== 'done'" class="vehicle-field">
                    <span>Эксплуатация машины</span>
                    <select v-model="repairDraft(defect).operationalState" :disabled="isSaving"><option value="operating">Эксплуатация продолжается</option><option value="stopped">Машина остановлена</option></select>
                  </label>
                  <label v-if="repairDraft(defect).status !== 'done' && repairDraft(defect).operationalState === 'stopped'" class="vehicle-field">
                    <span>Начало простоя</span><input v-model="repairDraft(defect).downtimeStartedAt" type="datetime-local" required :disabled="isSaving" />
                  </label>
                  <button class="primary-button repair-save-stage" type="submit" :disabled="isSaving">{{ isSaving ? 'Сохранение…' : 'Сохранить этап' }}</button>
                </div>
              </form>
            </details>
          </article>
        </dialog>
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

<style scoped>
.repair-request-list { list-style: none; margin: 0; padding: 0; }
.repair-request-list li + li { border-top: 1px solid var(--border); }
.repair-request-row { display: flex; align-items: baseline; gap: 8px; width: 100%; min-height: 56px; padding: 16px; border: 0; background: transparent; color: inherit; text-align: left; font: inherit; cursor: pointer; overflow-wrap: anywhere; }
.repair-request-row strong { flex-shrink: 0; }
.repair-request-row:hover { background: var(--bg-surface); }
.repair-request-row:focus-visible { outline: 2px solid currentColor; outline-offset: -2px; }
.repair-details-dialog { box-sizing: border-box; width: min(1000px, calc(100vw - 24px)); max-height: calc(100dvh - 32px); padding: 20px; border: 1px solid var(--border); border-radius: 16px; background: var(--bg-surface, white); color: inherit; overflow: auto; }
.repair-details-dialog::backdrop { background: rgb(0 0 0 / 50%); }
.repair-dialog-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 16px; }
@media (max-width: 760px) {
  .repair-request-row { flex-direction: column; gap: 4px; }
  .repair-request-row strong { flex-shrink: 1; }
  .repair-details-dialog { padding: 12px; }
}

.gto-forecast-intro { padding: 24px; border: 1px solid var(--border); border-radius: 12px; background: var(--bg-surface); }
.gto-forecast-intro h2 { margin-top: 0; font-size: 20px; }
.gto-forecast-intro p { line-height: 1.6; }
.gto-forecast-intro__pending { color: var(--text-secondary); }
</style>
