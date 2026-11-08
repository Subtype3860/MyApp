<script setup>
import { computed, ref } from 'vue'
import { createPdfDocument, renderPdfDocument } from '../utils/pdfPreview'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  navigationCollapsed: {
    type: Boolean,
    required: true,
  },
  rows: {
    type: Array,
    required: true,
  },
  token: {
    type: String,
    required: true,
  },
  responsibleEmployee: {
    type: Object,
    default: null,
  },
})

const emit = defineEmits(['back', 'clear', 'update:rows', 'update:responsibleEmployee'])
const responsibleDialog = ref(null), choosingResponsible = ref(false), employees = ref([]), employeeIndex = ref(''), loadingEmployees = ref(false), employeeError = ref('')
const chosenEmployee = ref(null)
const responsible = computed(() => chosenEmployee.value || props.responsibleEmployee)
async function chooseResponsible() {
  choosingResponsible.value = true; loadingEmployees.value = true; employeeError.value = ''; employees.value = []; employeeIndex.value = ''
  try {
    const query = new URLSearchParams({ sourceTable: props.rows[0]?.__sourceTable || '' })
    const response = await fetch(`/api/employees/responsible?${query}`, { headers: { Authorization: `Bearer ${props.token}` } })
    if (!response.ok) throw new Error('Не удалось загрузить ответственных. Попробуйте ещё раз.')
    employees.value = await response.json()
  } catch (e) { employeeError.value = e.message } finally { loadingEmployees.value = false }
}
function confirmResponsible() {
  const employee = employees.value[Number(employeeIndex.value)]
  if (employeeIndex.value === '' || !employee) return
  chosenEmployee.value = { ...employee, sourceTable: props.rows[0]?.__sourceTable }
  emit('update:responsibleEmployee', chosenEmployee.value)
  responsibleDialog.value.close()
  generateDocument()
}
function continueWithoutResponsible() { responsibleDialog.value.close(); generateDocument(true) }
const vehicleNumber = ref('')
const documentDate = ref(formatLocalDate(new Date()))
const documentError = ref('')
const isGeneratingDocument = ref(false)

const databaseQuantityColumns = new Set([
  'amount',
  'count',
  'qty',
  'quantity',
  'количество',
  'кол-во',
  'колво',
])

const hiddenDatabaseColumns = new Set([
  ...databaseQuantityColumns,
  'cost',
  'price',
  'стоимость',
  'цена',
])

const columnLabels = {
  name: 'Наименование',
  unit: 'Ед. изм.',
}

/** Определяет служебные столбцы базы, которые не нужно показывать пользователю. */
function isHiddenDatabaseColumn(column) {
  return hiddenDatabaseColumns.has(column.trim().toLowerCase())
}

const columns = computed(() => {
  const names = new Set()
  props.rows.forEach((row) => {
    Object.keys(row)
      .filter((column) => !column.startsWith('__') && !isHiddenDatabaseColumn(column))
      .forEach((column) => names.add(column))
  })
  return [...names]
})

/** Приводит значение ячейки к отображаемому тексту. */
function formatCell(value) {
  if (value === null || value === undefined || value === '') {
    return '—'
  }

  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

/** Получает значение нужного столбца строки. */
function getRowValue(row, aliases) {
  const normalizedAliases = new Set(aliases.map((alias) => alias.toLowerCase()))
  const key = Object.keys(row).find((column) =>
    normalizedAliases.has(column.toLowerCase()),
  )
  return key ? row[key] : ''
}

/** Форматирует дату в местном календаре без сдвига через UTC. */
function formatLocalDate(date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/** Удаляет материал из списка выбранных позиций требования. */
function removeRow(index) {
  emit(
    'update:rows',
    props.rows.filter((_, rowIndex) => rowIndex !== index),
  )
}

/** Проверяет выбранные позиции и запрашивает формирование документа требования. */
async function generateDocument(withoutResponsible = false) {
  if (isGeneratingDocument.value) return
  documentError.value = ''
  if (!vehicleNumber.value.trim()) {
    documentError.value = 'Укажите номер техники.'
    return
  }

  const vehicleNumbers = vehicleNumber.value
    .split(';')
    .map((value) => value.trim())
    .filter(Boolean)
  if (vehicleNumbers.length > props.rows.length) {
    documentError.value =
      'Количество номеров техники не должно превышать количество наименований.'
    return
  }

  const sourceTable = props.rows[0]?.__sourceTable
  if (
    !sourceTable ||
    props.rows.some((row) => row.__sourceTable !== sourceTable) ||
    (responsible.value && responsible.value.sourceTable !== sourceTable)
  ) {
    documentError.value =
      'Ответственное лицо не соответствует источнику выбранных компонентов.'
    return
  }

  const maintenanceNames = [
    ...new Set(
      props.rows
        .map((row) => String(row.__maintenanceName ?? '').trim())
        .filter(Boolean),
    ),
  ]
  const jobName = maintenanceNames.length === 1
    ? maintenanceNames[0]
    : 'Аварийная'

  if (props.rows.length > 100) {
    documentError.value = 'Можно сформировать не более 100 компонентов за один раз.'
    return
  }

  if (!responsible.value && withoutResponsible !== true) {
    choosingResponsible.value = false; employeeError.value = ''; responsibleDialog.value.showModal(); return
  }

  const previewWindow = window.open('', '_blank')
  if (!previewWindow) {
    documentError.value = 'Разрешите открытие всплывающих окон для просмотра PDF.'
    return
  }

  previewWindow.document.body.textContent = 'Формирование PDF...'
  isGeneratingDocument.value = true
  try {
    const documentRequest = {
      date: documentDate.value,
      vehicleNumber: vehicleNumber.value.trim(),
      sourceTable,
      jobName,
      withoutResponsibleEmployee: !responsible.value && withoutResponsible === true,
      responsibleEmployee: responsible.value ? {
        firstName: responsible.value.firstName,
        patronymic: responsible.value.patronymic,
        lastName: responsible.value.lastName,
      } : null,
      items: props.rows.map((row) => ({
        name: String(getRowValue(row, ['name', 'Наименование']) ?? ''),
        unit: String(getRowValue(row, ['unit', 'Ед.изм.', 'Ед. изм.']) ?? ''),
        quantity: Number(row.__quantity ?? 1),
        availableQuantity: Number(
          row.__availableQuantity ??
            getRowValue(row, ['amount', 'Количество']) ??
            0,
        ),
      })),
    }
    const response = await fetch('/api/documents/components/preview', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${props.token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(documentRequest),
    })

    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      const validationMessage = problem?.errors
        ? Object.values(problem.errors).flat()[0]
        : null
      throw new Error(
        validationMessage ??
          problem?.message ??
          problem?.detail ??
          problem?.title ??
          'Не удалось сформировать PDF.',
      )
    }

    const result = await response.json()
    const pdfBlob = await createPdfDocument(result.document, props.token)
    const commitResponse = await fetch('/api/documents/components', {
      method: 'POST',
      headers: {
        Authorization: 'Bearer ' + props.token,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(documentRequest),
    })
    if (!commitResponse.ok) {
      const problem = await commitResponse.json().catch(() => null)
      throw new Error(
        problem?.message ??
          problem?.detail ??
          problem?.title ??
          'PDF создан, но не удалось списать компоненты.',
      )
    }
    renderPdfDocument(previewWindow, pdfBlob)
  } catch (error) {
    previewWindow.close()
    documentError.value =
      error instanceof Error ? error.message : 'Не удалось сформировать PDF.'
  } finally {
    isGeneratingDocument.value = false
  }
}

</script>

<template>
  <main class="home-page tables-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div>
        <p class="eyebrow">V_FULL_OST / V_MEH_OST</p>
        <h1>Выбранные компоненты</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <section class="data-table-panel macos-glass-panel">
      <div class="data-table-header selected-components-header">
        <div>
          <p class="eyebrow">РЕЗУЛЬТАТ ВЫБОРА</p>
          <h2>Компоненты</h2>
        </div>
        <span class="record-count">{{ rows.length }} выбрано</span>
      </div>

      <div class="selected-components-actions">
        <button class="secondary-button" type="button" @click="$emit('back')">
          ← Вернуться к выбору
        </button>
        <button
          class="table-action-button table-action-button--danger"
          type="button"
          :disabled="rows.length === 0"
          @click="$emit('clear')"
        >
          Очистить список
        </button>
      </div>

      <form class="document-form" @submit.prevent="generateDocument">
        <label>
          <span>Дата</span>
          <input v-model="documentDate" type="date" required />
        </label>
        <label>
          <span>Номер техники</span>
          <input
            v-model="vehicleNumber"
            type="text"
            maxlength="500"
            placeholder="Один номер или несколько через точку с запятой"
            required
          />
        </label>
        <button
          class="primary-button"
          type="submit"
          :disabled="rows.length === 0 || isGeneratingDocument"
        >
          {{ isGeneratingDocument ? 'Формирование...' : 'Просмотреть PDF' }}
        </button>
      </form>
      <p v-if="documentError" class="form-error" role="alert">{{ documentError }}</p>

      <div class="data-table-scroll">
        <table v-if="rows.length">
          <thead>
            <tr>
              <th v-for="column in columns" :key="column">
                {{ columnLabels[column] ?? column }}
              </th>
              <th class="quantity-column">Количество</th>
              <th class="selection-remove-column">Действие</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(row, rowIndex) in rows" :key="rowIndex">
              <td v-for="column in columns" :key="column">
                {{ formatCell(row[column]) }}
              </td>
              <td class="quantity-column">{{ formatCell(row.__quantity ?? 1) }}</td>
              <td class="selection-remove-column">
                <button
                  class="table-action-button table-action-button--danger"
                  type="button"
                  @click="removeRow(rowIndex)"
                >
                  Удалить
                </button>
              </td>
            </tr>
          </tbody>
        </table>
        <p v-else class="table-message">
          Компоненты ещё не выбраны. Вернитесь в таблицу и отметьте нужные строки.
        </p>
      </div>
    </section>
    <dialog ref="responsibleDialog" class="responsible-dialog" aria-labelledby="responsible-dialog-title">
      <h2 id="responsible-dialog-title">Ответственное лицо не выбрано</h2>
      <p>Добавьте ответственного за выдачу или создайте PDF без него.</p>
      <template v-if="choosingResponsible">
        <p v-if="loadingEmployees" role="status">Загрузка ответственных…</p>
        <p v-if="employeeError" role="alert">{{ employeeError }}</p>
        <label v-if="!loadingEmployees && !employeeError">Ответственное лицо
          <select v-model="employeeIndex" aria-label="Ответственное лицо"><option value="">Выберите сотрудника</option><option v-for="(employee, index) in employees" :key="index" :value="String(index)">{{ [employee.lastName, employee.firstName, employee.patronymic].filter(Boolean).join(' ') }}</option></select>
        </label>
        <p v-if="!loadingEmployees && !employeeError && !employees.length">Для этого источника ответственные не найдены.</p>
        <button v-if="employeeError" type="button" @click="chooseResponsible">Повторить загрузку</button>
        <button class="primary-button" type="button" :disabled="loadingEmployees || employeeIndex === ''" @click="confirmResponsible">Создать PDF с ответственным</button>
      </template>
      <div class="responsible-dialog__actions">
        <button v-if="!choosingResponsible" class="primary-button" type="button" autofocus @click="chooseResponsible">Добавить ответственного</button>
        <button class="secondary-button" type="button" @click="continueWithoutResponsible">Создать PDF без ответственного</button>
        <button class="secondary-button" type="button" @click="responsibleDialog.close()">Отмена</button>
      </div>
    </dialog>
  </main>
</template>

<style scoped>
.responsible-dialog{box-sizing:border-box;width:min(540px,calc(100vw - 24px));max-height:90dvh;overflow:auto;padding:20px;border:1px solid var(--border);border-radius:12px;background:var(--bg-surface);color:var(--text-main)}
.responsible-dialog::backdrop{background:#0008}.responsible-dialog h2{margin:0 0 12px;font-size:20px}.responsible-dialog__actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px}.responsible-dialog label{display:grid;gap:8px;margin:12px 0}.responsible-dialog select{max-width:100%;padding:10px;font:inherit}.responsible-dialog button{min-height:40px}
</style>
