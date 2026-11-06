<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  token: {
    type: String,
    required: true,
  },
})

const groups = ref([])
const groupName = ref('')
const selectedGroupId = ref('')
const sourceTable = ref('v_full_ost')
const materialName = ref('')
const materialSuggestions = ref([])
const isLoading = ref(false)
const isSubmitting = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
let searchTimer

const selectedGroup = computed(() =>
  groups.value.find((group) => group.id === selectedGroupId.value),
)

/** Формирует заголовки авторизации и, при необходимости, JSON-запроса. */
function authHeaders(includeContentType = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(includeContentType ? { 'Content-Type': 'application/json' } : {}),
  }
}

/** Извлекает понятное сообщение об ошибке из ответа API. */
async function readError(response, fallback) {
  const problem = await response.json().catch(() => null)
  return problem?.errors
    ? Object.values(problem.errors).flat()[0]
    : problem?.detail || problem?.message || problem?.title || fallback
}

/** Загружает группы материалов и их состав. */
async function loadGroups() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/admin/material-groups', {
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error('Не удалось загрузить группы материалов.')
    }
    groups.value = await response.json()
    if (
      selectedGroupId.value &&
      !groups.value.some((group) => group.id === selectedGroupId.value)
    ) {
      selectedGroupId.value = ''
    }
  } catch (error) {
    errorMessage.value =
      error instanceof Error
        ? error.message
        : 'Не удалось загрузить группы материалов.'
  } finally {
    isLoading.value = false
  }
}

/** Создаёт группу материалов по введённому названию. */
async function createGroup() {
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch('/api/admin/material-groups', {
      method: 'POST',
      headers: authHeaders(true),
      body: JSON.stringify({ name: groupName.value }),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось создать группу.'))
    }
    const result = await response.json()
    groupName.value = ''
    await loadGroups()
    selectedGroupId.value = result.id
    successMessage.value = 'Группа создана.'
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось создать группу.'
  } finally {
    isSubmitting.value = false
  }
}

/** Удаляет выбранную группу материалов после подтверждения. */
async function deleteGroup(group) {
  if (!window.confirm(`Удалить группу «${group.name}» и её состав?`)) {
    return
  }
  errorMessage.value = ''
  successMessage.value = ''
  const response = await fetch(`/api/admin/material-groups/${group.id}`, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  if (!response.ok) {
    errorMessage.value = await readError(response, 'Не удалось удалить группу.')
    return
  }
  await loadGroups()
  successMessage.value = 'Группа удалена.'
}

/** Включает найденный материал в выбранную группу. */
async function addMaterial() {
  if (!selectedGroupId.value) {
    errorMessage.value = 'Выберите группу.'
    return
  }
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const response = await fetch(
      `/api/admin/material-groups/${selectedGroupId.value}/items`,
      {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify({
          sourceTable: sourceTable.value,
          materialName: materialName.value,
        }),
      },
    )
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось добавить материал.'))
    }
    materialName.value = ''
    materialSuggestions.value = []
    await loadGroups()
    successMessage.value = 'Материал добавлен в группу.'
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось добавить материал.'
  } finally {
    isSubmitting.value = false
  }
}

/** Исключает материал из группы и обновляет список. */
async function deleteMaterial(item) {
  errorMessage.value = ''
  successMessage.value = ''
  const response = await fetch(`/api/admin/material-groups/items/${item.id}`, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  if (!response.ok) {
    errorMessage.value = await readError(response, 'Не удалось удалить материал.')
    return
  }
  await loadGroups()
  successMessage.value = 'Материал удалён из группы.'
}

/** Запускает поиск материалов по введённому тексту. */
function searchMaterials() {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(loadMaterialSuggestions, 250)
}

/** Загружает варианты материалов для добавления в группу. */
async function loadMaterialSuggestions() {
  const search = materialName.value.trim()
  if (search.length < 2) {
    materialSuggestions.value = []
    return
  }
  const query = new URLSearchParams({
    page: '1',
    pageSize: '20',
    search,
  })
  const response = await fetch(`/api/tables/${sourceTable.value}?${query}`, {
    headers: authHeaders(),
  })
  if (!response.ok) {
    materialSuggestions.value = []
    return
  }
  const result = await response.json()
  materialSuggestions.value = result.rows
    .map((row) => row['Наименование'])
    .filter(Boolean)
}

loadGroups()
</script>

<template>
  <section class="material-groups-settings">
    <div class="settings-section-header">
      <div>
        <h2>Группы материалов</h2>
        <p>Материалы группы можно выписывать только вместе друг с другом.</p>
      </div>
      <button class="secondary-button" type="button" :disabled="isLoading" @click="loadGroups">
        Обновить
      </button>
    </div>

    <form class="material-group-create" @submit.prevent="createGroup">
      <label>
        <span>Новая группа</span>
        <input
          v-model.trim="groupName"
          type="text"
          maxlength="100"
          placeholder="Название группы"
          required
        />
      </label>
      <button class="primary-button" type="submit" :disabled="isSubmitting">
        Создать группу
      </button>
    </form>

    <p v-if="errorMessage" class="form-message form-message--error" role="alert">
      {{ errorMessage }}
    </p>
    <p v-if="successMessage" class="form-message form-message--success" role="status">
      {{ successMessage }}
    </p>

    <div class="material-groups-layout">
      <div class="material-group-list">
        <button
          v-for="group in groups"
          :key="group.id"
          class="material-group-card"
          :class="{ 'material-group-card--active': selectedGroupId === group.id }"
          type="button"
          @click="selectedGroupId = group.id"
        >
          <span>{{ group.name }}</span>
          <small>{{ group.items.length }} материалов</small>
        </button>
        <p v-if="!isLoading && groups.length === 0" class="table-message">
          Группы пока не созданы
        </p>
      </div>

      <div v-if="selectedGroup" class="material-group-details">
        <div class="settings-section-header">
          <div>
            <h3>{{ selectedGroup.name }}</h3>
            <p>Добавляйте точные наименования из таблиц остатков.</p>
          </div>
          <button
            class="table-action-button table-action-button--danger"
            type="button"
            @click="deleteGroup(selectedGroup)"
          >
            Удалить группу
          </button>
        </div>

        <form class="material-group-item-form" @submit.prevent="addMaterial">
          <label>
            <span>Источник</span>
            <select v-model="sourceTable" @change="materialSuggestions = []">
              <option value="v_full_ost">Остатки на складе</option>
              <option value="v_meh_ost">Остатки механиков</option>
            </select>
          </label>
          <label>
            <span>Наименование материала</span>
            <input
              v-model="materialName"
              type="search"
              list="material-group-suggestions"
              placeholder="Начните вводить наименование"
              required
              @input="searchMaterials"
            />
            <datalist id="material-group-suggestions">
              <option v-for="name in materialSuggestions" :key="name" :value="name" />
            </datalist>
          </label>
          <button class="primary-button" type="submit" :disabled="isSubmitting">
            Добавить
          </button>
        </form>

        <div class="data-table-scroll">
          <table>
            <thead>
              <tr>
                <th>Источник</th>
                <th>Наименование</th>
                <th>Действия</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in selectedGroup.items" :key="item.id">
                <td>
                  {{ item.sourceTable === 'v_full_ost' ? 'Склад' : 'Механики' }}
                </td>
                <td>{{ item.materialName }}</td>
                <td>
                  <button
                    class="table-action-button table-action-button--danger"
                    type="button"
                    @click="deleteMaterial(item)"
                  >
                    Удалить
                  </button>
                </td>
              </tr>
              <tr v-if="selectedGroup.items.length === 0">
                <td class="empty-table" colspan="3">В группе пока нет материалов</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </section>
</template>
