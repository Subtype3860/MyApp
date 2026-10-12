<script setup>
import { reactive, ref } from 'vue'

const props = defineProps({
  token: {
    type: String,
    required: true,
  },
})

const files = [
  { name: 'mechanics', description: 'Остатки механиков' },
  { name: 'warehouse', description: 'Остатки на складе' },
  { name: 'workers', description: 'Работники' },
]
const selectedFiles = reactive({})
const uploadingFile = ref('')
const previews = reactive({})
const successes = reactive({})
const sheets = reactive({})
const selectedSheets = reactive({})
const loadingSheets = reactive({})
const messages = reactive({})

async function selectFile(fileName, event) {
  const file = event.target.files?.[0] ?? null
  selectedFiles[fileName] = file
  messages[fileName] = ''
  previews[fileName] = null
  successes[fileName] = false
  sheets[fileName] = []
  selectedSheets[fileName] = ''
  if (!file) return
  if (!/\.(csv|xlsx)$/i.test(file.name) || file.size > 20 * 1024 * 1024 || file.size === 0) {
    messages[fileName] = 'Выберите непустой CSV или XLSX размером не более 20 МБ.'
    selectedFiles[fileName] = null
    return
  }
  if (!/\.xlsx$/i.test(file.name)) return
  loadingSheets[fileName] = true
  try {
    const body = new FormData()
    body.append('file', file)
    const response = await fetch('/api/admin/imports/sheets', {
      method: 'POST', headers: { Authorization: 'Bearer ' + props.token }, body,
    })
    const result = await response.json()
    if (selectedFiles[fileName] !== file) return
    if (!response.ok) throw new Error(result?.errors?.file?.[0] ?? 'Не удалось прочитать листы Excel.')
    sheets[fileName] = result
    selectedSheets[fileName] = result[0] ?? ''
  } catch (error) {
    if (selectedFiles[fileName] === file) {
      messages[fileName] = error.message
      selectedFiles[fileName] = null
    }
  } finally {
    loadingSheets[fileName] = false
  }
}

async function downloadFile(fileName) {
  messages[fileName] = ''
  const response = await fetch(`/api/admin/imports/${fileName}`, {
    headers: { Authorization: 'Bearer ' + props.token },
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    messages[fileName] =
      problem?.detail ??
      problem?.title ??
      'Не удалось скачать текущий CSV-файл.'
    return
  }

  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = `${fileName}.csv`
  document.body.append(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

async function previewFile(fileName) {
  const file = selectedFiles[fileName]
  if (!file) return
  uploadingFile.value = fileName
  messages[fileName] = ''
  successes[fileName] = false
  previews[fileName] = null
  const body = new FormData()
  body.append('file', file)
  if (selectedSheets[fileName]) body.append('sheetName', selectedSheets[fileName])
  try {
    const response = await fetch(`/api/admin/imports/${fileName}/preview`, {
      method: 'POST', headers: { Authorization: 'Bearer ' + props.token }, body,
    })
    const result = await response.json()
    if (!response.ok) throw new Error(result?.errors?.file?.[0] ?? result?.detail ?? 'Не удалось проверить файл.')
    previews[fileName] = result
  } catch (error) {
    messages[fileName] = error.message
  } finally {
    uploadingFile.value = ''
  }
}

async function replaceFile(fileName) {
  const file = selectedFiles[fileName]
  if (!file || !previews[fileName]) {
    messages[fileName] = 'Выберите CSV или Excel.'
    return
  }
  const description = files.find(item => item.name === fileName).description
  const warning = fileName === 'workers'
    ? 'Работники из файла будут добавлены или обновлены. Отсутствующие работники сохранятся.'
    : 'Файл заменит остатки выбранного склада. Количество всех позиций, отсутствующих в файле, станет нулевым.'
  if (!window.confirm(`Импортировать «${file.name}» (${description})? ${warning}`)) return

  uploadingFile.value = fileName
  messages[fileName] = ''
  const body = new FormData()
  body.append('file', file)
  if (selectedSheets[fileName]) body.append('sheetName', selectedSheets[fileName])
  try {
    const response = await fetch(`/api/admin/imports/${fileName}`, {
      method: 'POST',
      headers: { Authorization: 'Bearer ' + props.token },
      body,
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new Error(
        problem?.errors?.file?.[0] ??
          problem?.detail ??
          problem?.title ??
          'Не удалось импортировать CSV-файл.',
      )
    }

    const result = await response.json()
    selectedFiles[fileName] = null
    previews[fileName] = null
    successes[fileName] = true
    messages[fileName] = `Добавлено ${result.added}, обновлено ${result.updated}, обнулено ${result.zeroed ?? 0}.`
  } catch (error) {
    messages[fileName] =
      error instanceof Error ? error.message : 'Не удалось импортировать CSV-файл.'
  } finally {
    uploadingFile.value = ''
  }
}
</script>

<template>
  <div class="csv-files-settings">
    <div>
      <h2>Импорт данных</h2>
      <p class="profile-hint">
        Загрузите CSV или Excel (.xlsx) с любым именем. Для остатков файл — полный снимок выбранного склада: отсутствующие позиции получат количество 0. Работники добавляются или обновляются без удаления отсутствующих. Требуются заголовки колонок; CSV — в UTF-8. ID из выгрузки позволяет обновить запись при переименовании; для новых записей оставьте ID пустым.
      </p>
    </div>
    <div class="csv-file-grid">
      <article v-for="item in files" :key="item.name" class="csv-file-card">
        <div>
          <strong>{{ item.description }}</strong>
        </div>
        <input
          type="file"
          accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          :disabled="uploadingFile !== '' || loadingSheets[item.name]"
          @change="selectFile(item.name, $event)"
        />
        <label v-if="sheets[item.name]?.length">
          Лист Excel
          <select v-model="selectedSheets[item.name]" :disabled="uploadingFile !== ''" @change="previews[item.name] = null">
            <option v-for="sheet in sheets[item.name]" :key="sheet" :value="sheet">{{ sheet }}</option>
          </select>
        </label>
        <p v-if="loadingSheets[item.name]">Чтение листов Excel...</p>
        <button type="button" class="primary-button"
          :disabled="!selectedFiles[item.name] || uploadingFile !== '' || loadingSheets[item.name]"
          @click="previewFile(item.name)">Проверить файл</button>
        <div v-if="previews[item.name]" class="import-preview">
          <p>Строк: {{ previews[item.name].imported }}. Добавится: {{ previews[item.name].added }}.
            Обновится: {{ previews[item.name].updated }}. Обнулится: {{ previews[item.name].zeroed }}.</p>
          <p>Первые 10 строк. Итоговые количества изменений определяются при сохранении.</p>
          <div class="import-preview-table">
            <table>
              <thead><tr><th v-for="column in previews[item.name].columns" :key="column">{{ column }}</th></tr></thead>
              <tbody><tr v-for="(row, index) in previews[item.name].rows" :key="index">
                <td v-for="column in previews[item.name].columns" :key="column">{{ row[column] }}</td>
              </tr></tbody>
            </table>
          </div>
        </div>
        <button
          class="primary-button"
          type="button"
          :disabled="!previews[item.name] || uploadingFile !== '' || loadingSheets[item.name]"
          @click="replaceFile(item.name)"
        >
          {{ uploadingFile === item.name ? 'Загрузка...' : 'Импортировать в БД' }}
        </button>
        <a
          class="secondary-link"
          href="#"
          :aria-disabled="uploadingFile !== ''"
          @click.prevent="uploadingFile === '' && downloadFile(item.name)"
        >
          Скачать данные в CSV
        </a>
        <p
          v-if="messages[item.name]"
          class="form-message"
          :class="{
            'form-message--success': successes[item.name],
            'form-message--error': !successes[item.name],
          }"
        >
          {{ messages[item.name] }}
        </p>
      </article>
    </div>
  </div>
</template>

<style scoped>
.import-preview-table { overflow: auto; max-height: 260px; }
.import-preview table { border-collapse: collapse; font-size: 0.85rem; }
.import-preview th, .import-preview td { padding: 6px; text-align: left; border-bottom: 1px solid #d4d9df; }
</style>
