<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  navigationCollapsed: {
    type: Boolean,
    required: true,
  },
  selectedTableId: {
    type: String,
    required: true,
  },
  token: {
    type: String,
    required: true,
  },
  selectedComponentRows: {
    type: Array,
    required: true,
  },
  selectedResponsibleEmployee: {
    type: Object,
    default: null,
  },
})

const emit = defineEmits([
  'show-selected-components',
  'update:selected-component-rows',
  'update:selected-responsible-employee',
])

const tables = [
  { id: 'v_full_ost', name: 'Остатки на складе' },
  { id: 'v_meh_ost', name: 'Остатки механиков' },
  { id: 'v_workers', name: 'Работники' },
]

const columns = ref([])
const rows = ref([])
const totalRows = ref(0)
const currentPage = ref(1)
const pageSize = ref('20')
const searchQuery = ref('')
const errorMessage = ref('')
const isLoading = ref(false)
const responsibleEmployees = ref([])
const responsibleEmployeeError = ref('')
const isLoadingResponsibleEmployees = ref(false)
const signatureUrls = ref(new Map())
const workerFilters = reactive({
  lastName: '',
  firstName: '',
  patronymic: '',
  profession: '',
})
let searchTimer

const selectedTable = computed(
  () => tables.find((table) => table.id === props.selectedTableId) ?? tables[0],
)

const totalPages = computed(() => {
  if (pageSize.value === 'all') {
    return 1
  }

  return Math.max(1, Math.ceil(totalRows.value / Number(pageSize.value)))
})

const isWorkersTable = computed(() => props.selectedTableId === 'v_workers')
const isSelectableTable = computed(
  () =>
    props.selectedTableId === 'v_full_ost' ||
    props.selectedTableId === 'v_meh_ost',
)
const responsibleProfession = computed(() =>
  props.selectedTableId === 'v_full_ost' ? 'Кладовщик' : 'Старший механик',
)
const selectedResponsibleKey = computed({
  get() {
    if (props.selectedResponsibleEmployee?.sourceTable !== props.selectedTableId) {
      return ''
    }
    return getEmployeeKey(props.selectedResponsibleEmployee)
  },
  set(key) {
    const employee = responsibleEmployees.value.find(
      (item) => getEmployeeKey(item) === key,
    )
    emit(
      'update:selected-responsible-employee',
      employee ? { ...employee, sourceTable: props.selectedTableId } : null,
    )
  },
})
const selectedRowKeys = computed(
  () => new Set(props.selectedComponentRows.map((row) => getRowKey(row))),
)
const areAllVisibleRowsSelected = computed(
  () =>
    rows.value.length > 0 &&
    rows.value.every((row) => selectedRowKeys.value.has(getRowKey(row))),
)
const hasActiveFilter = computed(
  () =>
    Boolean(searchQuery.value.trim()) ||
    Object.values(workerFilters).some((value) => value.trim()),
)

const columnLabels = {
  name: 'Наименование',
  unit: 'Ед. изм.',
  amount: 'На остатке',
  Количество: 'На остатке',
  'Ед.изм.': 'Ед. изм.',
  price: 'Цена',
  LastName: 'Фамилия',
  FirstName: 'Имя',
  Patronymic: 'Отчество',
  PhoneNumber: 'Телефон',
  Profession: 'Профессия',
  Роспись: 'Роспись',
}

function getWorkerKey(row) {
  return row.EmployeeId
}

function getWorkerQuery(row) {
  return new URLSearchParams({ employeeId: row.EmployeeId })
}

function clearSignatureUrls() {
  signatureUrls.value.forEach((url) => URL.revokeObjectURL(url))
  signatureUrls.value.clear()
}

async function loadSignature(row) {
  if (!row.EmployeeId) return

  const key = getWorkerKey(row)
  const currentUrl = signatureUrls.value.get(key)
  if (currentUrl) {
    URL.revokeObjectURL(currentUrl)
    signatureUrls.value.delete(key)
  }
  if (!row.Роспись) return

  const response = await fetch(`/api/employees/signature?${getWorkerQuery(row)}`, {
    headers: { Authorization: 'Bearer ' + props.token },
  })
  if (response.ok) {
    signatureUrls.value.set(
      key,
      URL.createObjectURL(await response.blob()),
    )
  }
}

async function uploadSignature(row, event) {
  const file = event.target.files?.[0]
  event.target.value = ''
  if (!file) return

  errorMessage.value = ''
  const body = new FormData()
  body.append('signature', file)
  const response = await fetch(`/api/employees/signature?${getWorkerQuery(row)}`, {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + props.token },
    body,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    errorMessage.value =
      problem?.errors?.signature?.[0] ?? 'Не удалось загрузить роспись.'
    return
  }

  row.Роспись = true
  await loadSignature(row)
}

async function deleteSignature(row) {
  const response = await fetch(`/api/employees/signature?${getWorkerQuery(row)}`, {
    method: 'DELETE',
    headers: { Authorization: 'Bearer ' + props.token },
  })
  if (!response.ok) {
    errorMessage.value = 'Не удалось удалить роспись.'
    return
  }

  row.Роспись = false
  await loadSignature(row)
}

function formatCell(value) {
  if (value === null || value === undefined || value === '') {
    return '—'
  }

  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

function getEmployeeKey(employee) {
  return JSON.stringify([
    employee.firstName,
    employee.patronymic,
    employee.lastName,
  ])
}

function getEmployeeName(employee) {
  return [employee.lastName, employee.firstName, employee.patronymic]
    .filter(Boolean)
    .join(' ')
}

async function loadResponsibleEmployees() {
  responsibleEmployees.value = []
  responsibleEmployeeError.value = ''
  if (!isSelectableTable.value) {
    return
  }

  isLoadingResponsibleEmployees.value = true
  try {
    const query = new URLSearchParams({ sourceTable: props.selectedTableId })
    const response = await fetch(`/api/employees/responsible?${query}`, {
      headers: {
        Authorization: `Bearer ${props.token}`,
      },
    })
    if (!response.ok) {
      throw new Error('Не удалось загрузить список ответственных лиц.')
    }
    responsibleEmployees.value = await response.json()
  } catch (error) {
    responsibleEmployeeError.value =
      error instanceof Error
        ? error.message
        : 'Не удалось загрузить список ответственных лиц.'
  } finally {
    isLoadingResponsibleEmployees.value = false
  }
}

function getRowKey(row) {
  const sourceTable = row.__sourceTable ?? props.selectedTableId
  return JSON.stringify(
    [
      sourceTable,
      Object.keys(row)
        .filter((key) => !key.startsWith('__'))
        .sort()
        .map((key) => [key, row[key]]),
    ],
  )
}

function getSelectedRow(row) {
  const key = getRowKey(row)
  return props.selectedComponentRows.find((selectedRow) => getRowKey(selectedRow) === key)
}

function getCurrentTableSelections() {
  return props.selectedComponentRows.filter(
    (row) => row.__sourceTable === props.selectedTableId,
  )
}

function getQuantity(row) {
  return getSelectedRow(row)?.__requestedQuantity ?? 1
}

function getAvailableQuantity(row) {
  const key = Object.keys(row).find((column) =>
    ['amount', 'количество'].includes(column.trim().toLowerCase()),
  )
  const quantity = Number(key ? row[key] : 0)
  return Number.isFinite(quantity) ? Math.max(quantity, 0) : 0
}

function createSelectedRow(row, requestedQuantity) {
  const availableQuantity = getAvailableQuantity(row)
  const quantity = Math.min(availableQuantity, requestedQuantity)
  return {
    ...row,
    __sourceTable: props.selectedTableId,
    __availableQuantity: availableQuantity,
    __requestedQuantity: requestedQuantity,
    __quantity: quantity,
    __remainingQuantity: Math.max(availableQuantity - quantity, 0),
  }
}

function isRowSelected(row) {
  return selectedRowKeys.value.has(getRowKey(row))
}

function toggleRow(row) {
  const key = getRowKey(row)
  const nextRows = isRowSelected(row)
    ? getCurrentTableSelections().filter(
        (selectedRow) => getRowKey(selectedRow) !== key,
      )
    : [
        ...getCurrentTableSelections(),
        createSelectedRow(row, 1),
      ]
  emit('update:selected-component-rows', nextRows)
}

function updateQuantity(row, value) {
  const parsedValue = Number(String(value).replace(',', '.'))
  const quantity = Number.isFinite(parsedValue) && parsedValue > 0 ? parsedValue : 1
  const key = getRowKey(row)
  const nextRows = getCurrentTableSelections().map((selectedRow) =>
    getRowKey(selectedRow) === key
      ? createSelectedRow(row, quantity)
      : selectedRow,
  )

  if (!isRowSelected(row)) {
    nextRows.push(createSelectedRow(row, quantity))
  }

  emit('update:selected-component-rows', nextRows)
}

function toggleVisibleRows() {
  const visibleKeys = new Set(rows.value.map((row) => getRowKey(row)))
  if (areAllVisibleRowsSelected.value) {
    emit(
      'update:selected-component-rows',
      getCurrentTableSelections().filter(
        (row) => !visibleKeys.has(getRowKey(row)),
      ),
    )
    return
  }

  const nextRows = [...getCurrentTableSelections()]
  const existingKeys = new Set(selectedRowKeys.value)
  rows.value.forEach((row) => {
    const key = getRowKey(row)
    if (!existingKeys.has(key)) {
      nextRows.push(createSelectedRow(row, 1))
      existingKeys.add(key)
    }
  })
  emit('update:selected-component-rows', nextRows)
}

async function loadTable() {
  isLoading.value = true
  errorMessage.value = ''
  clearSignatureUrls()

  const query = new URLSearchParams({
    page: String(currentPage.value),
    pageSize: pageSize.value,
  })
  if (searchQuery.value.trim()) {
    query.set('search', searchQuery.value.trim())
  }
  if (isWorkersTable.value) {
    Object.entries(workerFilters).forEach(([key, value]) => {
      if (value.trim()) {
        query.set(key, value.trim())
      }
    })
  }

  try {
    const response = await fetch(`/api/tables/${props.selectedTableId}?${query}`, {
      headers: {
        Authorization: `Bearer ${props.token}`,
      },
    })

    if (!response.ok) {
      throw new Error(
        response.status === 401
          ? 'Сессия истекла. Выполните вход повторно.'
          : 'Не удалось загрузить данные таблицы.',
      )
    }

    const data = await response.json()
    columns.value = data.columns.filter(
      (column) =>
        props.selectedTableId !== 'v_meh_ost' ||
        !['price', 'цена'].includes(column.trim().toLowerCase()),
    )
    rows.value = data.rows
    totalRows.value = data.total
    if (isWorkersTable.value) {
      await Promise.all(rows.value.map(loadSignature))
    }
  } catch (error) {
    columns.value = []
    rows.value = []
    totalRows.value = 0
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось загрузить данные таблицы.'
  } finally {
    isLoading.value = false
  }

  onBeforeUnmount(clearSignatureUrls)
}

function changePage(nextPage) {
  if (nextPage < 1 || nextPage > totalPages.value || nextPage === currentPage.value) {
    return
  }

  currentPage.value = nextPage
  loadTable()
}

function changePageSize() {
  currentPage.value = 1
  loadTable()
}

watch(
  () => props.selectedTableId,
  () => {
    clearTimeout(searchTimer)
    searchQuery.value = ''
    Object.keys(workerFilters).forEach((key) => {
      workerFilters[key] = ''
    })
    currentPage.value = 1
    loadTable()
    loadResponsibleEmployees()
  },
  { immediate: true },
)

watch(searchQuery, () => {
  currentPage.value = 1
  clearTimeout(searchTimer)
  searchTimer = setTimeout(loadTable, 300)
})

watch(
  workerFilters,
  () => {
    if (!isWorkersTable.value) {
      return
    }

    currentPage.value = 1
    clearTimeout(searchTimer)
    searchTimer = setTimeout(loadTable, 300)
  },
  { deep: true },
)
</script>

<template>
  <main class="home-page tables-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div>
        <p class="eyebrow">ДАННЫЕ</p>
        <h1>Таблицы</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <div class="tables-layout">
      <section class="data-table-panel macos-glass-panel">
        <div class="data-table-header">
          <div>
            <p class="eyebrow">ТАБЛИЦА</p>
            <h2>{{ selectedTable.name }}</h2>
          </div>
          <span class="record-count">{{ totalRows }} записей</span>
        </div>

        <div v-if="isWorkersTable" class="worker-filters">
          <label>
            <span>Фамилия</span>
            <input v-model="workerFilters.lastName" type="search" placeholder="Фамилия" />
          </label>
          <label>
            <span>Имя</span>
            <input v-model="workerFilters.firstName" type="search" placeholder="Имя" />
          </label>
          <label>
            <span>Отчество</span>
            <input v-model="workerFilters.patronymic" type="search" placeholder="Отчество" />
          </label>
          <label>
            <span>Профессия</span>
            <input v-model="workerFilters.profession" type="search" placeholder="Профессия" />
          </label>
        </div>

        <div v-if="isSelectableTable" class="responsible-employee-field">
          <label>
            <span>Ответственный за выдачу — {{ responsibleProfession }}</span>
            <select
              v-model="selectedResponsibleKey"
              :disabled="isLoadingResponsibleEmployees || Boolean(responsibleEmployeeError)"
            >
              <option value="">
                {{ isLoadingResponsibleEmployees ? 'Загрузка...' : 'Выберите сотрудника' }}
              </option>
              <option
                v-for="employee in responsibleEmployees"
                :key="getEmployeeKey(employee)"
                :value="getEmployeeKey(employee)"
              >
                {{ getEmployeeName(employee) }}
              </option>
            </select>
          </label>
          <p v-if="responsibleEmployeeError" class="form-error" role="alert">
            {{ responsibleEmployeeError }}
          </p>
        </div>

        <div class="table-toolbar">
          <label class="table-search">
            <span class="visually-hidden">Поиск по таблице</span>
            <svg v-if="!isWorkersTable" viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="11" cy="11" r="7"></circle>
              <path d="m16 16 5 5"></path>
            </svg>
            <input
              v-if="!isWorkersTable"
              v-model="searchQuery"
              type="search"
              placeholder="Поиск без учёта регистра..."
              :disabled="Boolean(errorMessage)"
            />
            <span v-else class="worker-filter-hint">Фильтры применяются без учёта регистра</span>
          </label>

          <div v-if="isSelectableTable" class="selection-actions">
            <span>Выбрано: {{ selectedComponentRows.length }}</span>
            <button
              class="secondary-button"
              type="button"
              :disabled="selectedComponentRows.length === 0"
              @click="$emit('show-selected-components')"
            >
              Показать выбранные
            </button>
          </div>

          <label class="page-size-select">
            <span>Показывать</span>
            <select v-model="pageSize" :disabled="isLoading" @change="changePageSize">
              <option value="20">20</option>
              <option value="40">40</option>
              <option value="60">60</option>
              <option value="all">Все</option>
            </select>
          </label>
        </div>

        <p v-if="errorMessage" class="table-message table-message--error" role="alert">
          {{ errorMessage }}
        </p>
        <p v-else-if="isLoading" class="table-message">Загрузка данных...</p>

        <div v-else class="data-table-scroll">
          <table v-if="columns.length">
            <thead>
              <tr>
                <th v-if="isSelectableTable" class="selection-column">
                  <input
                    type="checkbox"
                    :checked="areAllVisibleRowsSelected"
                    :disabled="rows.length === 0"
                    aria-label="Выбрать все строки на текущей странице"
                    @change="toggleVisibleRows"
                  />
                </th>
                <th v-for="column in columns" :key="column">
                  {{ columnLabels[column] ?? column }}
                </th>
                <th v-if="isSelectableTable" class="quantity-column">Списать</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, rowIndex) in rows"
                :key="`${getRowKey(row)}-${rowIndex}`"
                :class="{ 'data-row--selected': isSelectableTable && isRowSelected(row) }"
              >
                <td v-if="isSelectableTable" class="selection-column">
                  <input
                    type="checkbox"
                    :checked="isRowSelected(row)"
                    aria-label="Выбрать компонент"
                    @change="toggleRow(row)"
                  />
                </td>
                <td v-for="column in columns" :key="column">
                  <div v-if="column === 'Роспись'" class="signature-cell">
                    <img
                      v-if="signatureUrls.get(getWorkerKey(row))"
                      class="employee-signature"
                      :src="signatureUrls.get(getWorkerKey(row))"
                      alt="Роспись работника"
                    />
                    <span v-else class="signature-placeholder">
                      {{ row.EmployeeId ? 'Нет' : 'ID не найден' }}
                    </span>
                    <label
                      v-if="row.EmployeeId"
                      class="signature-upload-button"
                    >
                      {{ row.Роспись ? 'Заменить' : 'Добавить' }}
                      <input
                        type="file"
                        accept="image/jpeg,image/png,image/webp"
                        @change="uploadSignature(row, $event)"
                      />
                    </label>
                    <button
                      v-if="row.Роспись && row.EmployeeId"
                      class="signature-delete-button"
                      type="button"
                      @click="deleteSignature(row)"
                    >
                      Удалить
                    </button>
                  </div>
                  <template v-else>{{ formatCell(row[column]) }}</template>
                </td>
                <td v-if="isSelectableTable" class="quantity-column">
                  <input
                    class="quantity-input"
                    type="number"
                    min="0.01"
                    step="any"
                    :value="getQuantity(row)"
                    aria-label="Количество компонента"
                    @change="updateQuantity(row, $event.target.value)"
                  />
                </td>
              </tr>
              <tr v-if="rows.length === 0">
                <td class="empty-table" :colspan="columns.length + (isSelectableTable ? 2 : 0)">
                  {{ hasActiveFilter ? 'По вашему запросу ничего не найдено' : 'В таблице пока нет записей' }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <nav
          v-if="!isLoading && !errorMessage && pageSize !== 'all' && totalRows > 0"
          class="table-pagination"
          aria-label="Пагинация таблицы"
        >
          <button
            type="button"
            :disabled="currentPage === 1"
            aria-label="Предыдущая страница"
            @click="changePage(currentPage - 1)"
          >
            ←
          </button>
          <span>Страница {{ currentPage }} из {{ totalPages }}</span>
          <button
            type="button"
            :disabled="currentPage === totalPages"
            aria-label="Следующая страница"
            @click="changePage(currentPage + 1)"
          >
            →
          </button>
        </nav>
      </section>
    </div>
  </main>
</template>
