<script setup>
import { ref } from 'vue'

const props = defineProps({
  navigationCollapsed: { type: Boolean, required: true },
  token: { type: String, required: true },
  selectedCount: { type: Number, required: true },
})
const emit = defineEmits(['apply-template', 'show-selected'])

const equipment = ref([])
const errorMessage = ref('')
const successMessage = ref('')
const isLoading = ref(false)

/** Загружает иерархию оборудования, интервалов ТО и материалов. */
async function loadTemplates() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/maintenance-templates', {
      headers: { Authorization: `Bearer ${props.token}` },
    })
    if (!response.ok) throw new Error('Не удалось загрузить список ТО.')
    equipment.value = await response.json()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

/** Передаёт материалы выбранного интервала ТО для формирования требования. */
function applyInterval(equipmentItem, interval) {
  if (interval.items.length === 0) {
    errorMessage.value = 'Для этого ТО материалы ещё не настроены.'
    return
  }
  const rows = interval.items.map((item) => {
    const available = Math.max(Number(item.availableQuantity), 0)
    const requested = Number(item.quantity)
    const actual = Math.min(available, requested)
    return {
      Наименование: item.materialName,
      'Ед.изм.': item.unit,
      Количество: available,
      __sourceTable: 'v_full_ost',
      __availableQuantity: available,
      __requestedQuantity: requested,
      __quantity: actual,
      __remainingQuantity: Math.max(available - actual, 0),
      __maintenanceName: interval.name,
    }
  })
  emit('apply-template', rows)
  errorMessage.value = ''
  successMessage.value =
    `${equipmentItem.name}, ${interval.name}: добавлено ${rows.length} материалов.`
}

loadTemplates()
</script>

<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div><p class="eyebrow">ТЕХНИЧЕСКОЕ ОБСЛУЖИВАНИЕ</p><h1>Шаблоны ТО</h1></div>
      <button class="secondary-button" type="button" :disabled="selectedCount === 0" @click="$emit('show-selected')">
        Выбранные компоненты: {{ selectedCount }}
      </button>
    </header>

    <section class="macos-glass-panel maintenance-page-panel">
      <div class="settings-section-header">
        <div>
          <h2>Выберите технику и пункт ТО</h2>
          <p>Материалы шаблона заменят текущий список выбранных компонентов.</p>
        </div>
      </div>
      <p v-if="errorMessage" class="form-message form-message--error">{{ errorMessage }}</p>
      <p v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</p>
      <p v-if="isLoading" class="table-message">Загрузка...</p>
      <div v-else class="maintenance-table-groups">
        <section
          v-for="equipmentItem in equipment"
          :key="equipmentItem.id"
          class="maintenance-table-group"
        >
          <h3>{{ equipmentItem.name }}</h3>
          <div class="data-table-scroll">
            <table class="maintenance-template-table">
              <thead>
                <tr>
                  <th>Пункт ТО</th>
                  <th>Наименования материалов</th>
                  <th>Действие</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="interval in equipmentItem.intervals" :key="interval.id">
                  <td class="maintenance-name-cell">{{ interval.name }}</td>
                  <td>
                    <ul v-if="interval.items.length" class="maintenance-material-list">
                      <li v-for="item in interval.items" :key="item.id">
                        {{ item.materialName }}
                      </li>
                    </ul>
                    <span v-else class="maintenance-empty-materials">
                      Материалы не настроены
                    </span>
                  </td>
                  <td class="maintenance-action-cell">
                    <button
                      class="primary-button maintenance-select-button"
                      type="button"
                      :disabled="interval.items.length === 0"
                      @click="applyInterval(equipmentItem, interval)"
                    >
                      Выбрать
                    </button>
                  </td>
                </tr>
                <tr v-if="equipmentItem.intervals.length === 0">
                  <td colspan="3" class="empty-table">Пункты ТО не настроены</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </section>
  </main>
</template>
