<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  token: { type: String, required: true },
})

const equipment = ref([])
const selectedEquipmentId = ref('')
const selectedIntervalId = ref('')
const equipmentName = ref('')
const intervalName = ref('')
const materialName = ref('')
const quantity = ref(1)
const suggestions = ref([])
const errorMessage = ref('')
const successMessage = ref('')
const isLoading = ref(false)
let searchTimer

const selectedEquipment = computed(() =>
  equipment.value.find((item) => item.id === selectedEquipmentId.value),
)
const selectedInterval = computed(() =>
  selectedEquipment.value?.intervals.find(
    (item) => item.id === selectedIntervalId.value,
  ),
)

/** Формирует заголовки запроса с токеном текущей сессии. */
function headers(json = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  }
}

/** Извлекает понятное сообщение об ошибке из ответа API. */
async function readError(response, fallback) {
  const problem = await response.json().catch(() => null)
  return problem?.errors
    ? Object.values(problem.errors).flat()[0]
    : problem?.detail || problem?.message || problem?.title || fallback
}

/** Выполняет авторизованный запрос управления шаблонами ТО и обрабатывает ответ. */
async function request(url, options, fallback) {
  errorMessage.value = ''
  successMessage.value = ''
  const response = await fetch(url, options)
  if (!response.ok) {
    throw new Error(await readError(response, fallback))
  }
  return response
}

/** Загружает иерархию оборудования, интервалов ТО и материалов. */
async function loadTemplates() {
  isLoading.value = true
  try {
    const response = await request(
      '/api/maintenance-templates',
      { headers: headers() },
      'Не удалось загрузить шаблоны ТО.',
    )
    equipment.value = await response.json()
    if (!equipment.value.some((item) => item.id === selectedEquipmentId.value)) {
      selectedEquipmentId.value = equipment.value[0]?.id ?? ''
    }
    if (
      !selectedEquipment.value?.intervals.some(
        (item) => item.id === selectedIntervalId.value,
      )
    ) {
      selectedIntervalId.value = selectedEquipment.value?.intervals[0]?.id ?? ''
    }
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

/** Создаёт вид оборудования в шаблонах ТО и обновляет список. */
async function createEquipment() {
  try {
    const response = await request(
      '/api/admin/maintenance-templates/equipment',
      {
        method: 'POST',
        headers: headers(true),
        body: JSON.stringify({ name: equipmentName.value }),
      },
      'Не удалось добавить технику.',
    )
    const result = await response.json()
    equipmentName.value = ''
    await loadTemplates()
    selectedEquipmentId.value = result.id
    selectedIntervalId.value = ''
    successMessage.value = 'Техника добавлена.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Создаёт интервал обслуживания выбранного оборудования. */
async function createInterval() {
  if (!selectedEquipmentId.value) return
  try {
    const response = await request(
      `/api/admin/maintenance-templates/equipment/${selectedEquipmentId.value}/intervals`,
      {
        method: 'POST',
        headers: headers(true),
        body: JSON.stringify({ name: intervalName.value }),
      },
      'Не удалось добавить пункт ТО.',
    )
    const result = await response.json()
    intervalName.value = ''
    await loadTemplates()
    selectedIntervalId.value = result.id
    successMessage.value = 'Пункт ТО добавлен.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Запрашивает новое название и сохраняет его для выбранного элемента шаблона. */
async function renameEntity(type, item) {
  const name = window.prompt('Новое название', item.name)?.trim()
  if (!name || name === item.name) return
  try {
    await request(
      `/api/admin/maintenance-templates/${type}/${item.id}`,
      {
        method: 'PUT',
        headers: headers(true),
        body: JSON.stringify({ name }),
      },
      'Не удалось изменить название.',
    )
    await loadTemplates()
    successMessage.value = 'Название изменено.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Удаляет выбранный элемент шаблона ТО после подтверждения. */
async function deleteEntity(type, item) {
  if (!window.confirm(`Удалить «${item.name}»?`)) return
  try {
    await request(
      `/api/admin/maintenance-templates/${type}/${item.id}`,
      { method: 'DELETE', headers: headers() },
      'Не удалось удалить запись.',
    )
    await loadTemplates()
    successMessage.value = 'Запись удалена.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Добавляет найденный материал в интервал обслуживания. */
async function addMaterial() {
  if (!selectedIntervalId.value) return
  try {
    await request(
      `/api/admin/maintenance-templates/intervals/${selectedIntervalId.value}/items`,
      {
        method: 'POST',
        headers: headers(true),
        body: JSON.stringify({
          materialName: materialName.value,
          quantity: Number(quantity.value),
        }),
      },
      'Не удалось добавить материал.',
    )
    materialName.value = ''
    quantity.value = 1
    suggestions.value = []
    await loadTemplates()
    successMessage.value = 'Материал добавлен.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Сохраняет новое количество материала в шаблоне ТО. */
async function saveQuantity(item) {
  try {
    await request(
      `/api/admin/maintenance-templates/items/${item.id}`,
      {
        method: 'PUT',
        headers: headers(true),
        body: JSON.stringify({
          materialName: item.materialName,
          quantity: Number(item.quantity),
        }),
      },
      'Не удалось сохранить количество.',
    )
    await loadTemplates()
    successMessage.value = 'Количество сохранено.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Удаляет материал из интервала обслуживания. */
async function deleteMaterial(item) {
  try {
    await request(
      `/api/admin/maintenance-templates/items/${item.id}`,
      { method: 'DELETE', headers: headers() },
      'Не удалось удалить материал.',
    )
    await loadTemplates()
    successMessage.value = 'Материал удалён.'
  } catch (error) {
    errorMessage.value = error.message
  }
}

/** Переключает выбранный вид оборудования и связанное состояние формы. */
function changeEquipment() {
  selectedIntervalId.value = selectedEquipment.value?.intervals[0]?.id ?? ''
}

/** Запускает поиск материалов по введённому тексту. */
function searchMaterials() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    const search = materialName.value.trim()
    if (search.length < 2) {
      suggestions.value = []
      return
    }
    const query = new URLSearchParams({ page: '1', pageSize: '20', search })
    const response = await fetch(`/api/tables/v_full_ost?${query}`, {
      headers: headers(),
    })
    if (response.ok) {
      const result = await response.json()
      suggestions.value = result.rows
        .map((row) => row['Наименование'])
        .filter(Boolean)
    }
  }, 250)
}

loadTemplates()
</script>

<template>
  <div class="maintenance-settings">
    <div class="settings-section-header">
      <div>
        <h2>Шаблоны технического обслуживания</h2>
        <p>Настройте технику, интервалы ТО и материалы со склада.</p>
      </div>
      <button class="secondary-button" type="button" @click="loadTemplates">
        Обновить
      </button>
    </div>

    <p v-if="errorMessage" class="form-message form-message--error" role="alert">
      {{ errorMessage }}
    </p>
    <p v-if="successMessage" class="form-message form-message--success">
      {{ successMessage }}
    </p>

    <form class="maintenance-create-form" @submit.prevent="createEquipment">
      <input v-model.trim="equipmentName" placeholder="Новая техника" required />
      <button class="primary-button" type="submit">Добавить технику</button>
    </form>

    <div class="maintenance-picker">
      <label>
        <span>Техника</span>
        <select v-model="selectedEquipmentId" @change="changeEquipment">
          <option v-for="item in equipment" :key="item.id" :value="item.id">
            {{ item.name }}
          </option>
        </select>
      </label>
      <div v-if="selectedEquipment" class="maintenance-inline-actions">
        <button class="table-action-button" type="button" @click="renameEntity('equipment', selectedEquipment)">
          Переименовать
        </button>
        <button class="table-action-button table-action-button--danger" type="button" @click="deleteEntity('equipment', selectedEquipment)">
          Удалить
        </button>
      </div>
    </div>

    <form v-if="selectedEquipment" class="maintenance-create-form" @submit.prevent="createInterval">
      <input v-model.trim="intervalName" placeholder="Новый пункт ТО" required />
      <button class="primary-button" type="submit">Добавить ТО</button>
    </form>

    <div v-if="selectedEquipment" class="maintenance-interval-tabs">
      <button
        v-for="interval in selectedEquipment.intervals"
        :key="interval.id"
        type="button"
        :class="{ 'maintenance-tab--active': interval.id === selectedIntervalId }"
        @click="selectedIntervalId = interval.id"
      >
        {{ interval.name }}
      </button>
    </div>

    <section v-if="selectedInterval" class="maintenance-interval-editor">
      <div class="settings-section-header">
        <h3>{{ selectedInterval.name }}</h3>
        <div class="maintenance-inline-actions">
          <button class="table-action-button" type="button" @click="renameEntity('intervals', selectedInterval)">Переименовать</button>
          <button class="table-action-button table-action-button--danger" type="button" @click="deleteEntity('intervals', selectedInterval)">Удалить ТО</button>
        </div>
      </div>

      <form class="maintenance-item-form" @submit.prevent="addMaterial">
        <label>
          <span>Материал со склада</span>
          <input
            v-model="materialName"
            type="search"
            list="maintenance-materials"
            placeholder="Точное наименование"
            required
            @input="searchMaterials"
          />
          <datalist id="maintenance-materials">
            <option v-for="name in suggestions" :key="name" :value="name" />
          </datalist>
        </label>
        <label>
          <span>Количество</span>
          <input v-model.number="quantity" type="number" min="0.01" step="any" required />
        </label>
        <button class="primary-button" type="submit">Добавить материал</button>
      </form>

      <div class="data-table-scroll">
        <table>
          <thead><tr><th>Наименование</th><th>Ед. изм.</th><th>Количество</th><th>Действия</th></tr></thead>
          <tbody>
            <tr v-for="item in selectedInterval.items" :key="item.id">
              <td>{{ item.materialName }}</td>
              <td>{{ item.unit || '—' }}</td>
              <td><input v-model.number="item.quantity" class="quantity-input" type="number" min="0.01" step="any" /></td>
              <td class="row-actions">
                <button class="table-action-button" type="button" @click="saveQuantity(item)">Сохранить</button>
                <button class="table-action-button table-action-button--danger" type="button" @click="deleteMaterial(item)">Удалить</button>
              </td>
            </tr>
            <tr v-if="selectedInterval.items.length === 0">
              <td colspan="4" class="empty-table">Материалы не добавлены</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <p v-else-if="!isLoading" class="table-message">Выберите или добавьте пункт ТО.</p>
  </div>
</template>
