<script setup>
import { computed, ref } from 'vue'
import { readDailyWorkbook, extractDailyRows, detectBlocks, parseRange, cellAddress } from '../utils/dailyHoursImport'
const props = defineProps({ token: { type: String, required: true } })
const emit = defineEmits(['busy', 'saved'])
const sheets = ref([]), mappings = ref({}), range = ref('A1:Z50'), busy = ref(false), error = ref('')
const preview = ref(null), parsed = ref(null), issues = ref([]), accepted = ref(false), result = ref(null)
const year = new Date().getFullYear()
const columns = computed(() => {
  try { const bounds = parseRange(range.value); return Array.from({length: bounds.right-bounds.left+1}, (_,i) => ({value:i+bounds.left,label:cellAddress(0,i+bounds.left).replace(/1$/,'')})) }
  catch { return [] }
})
const ready = computed(() => preview.value?.rows.filter(r => ['ready', 'warning'].includes(r.status)).length ?? 0)
const needsAcceptance = computed(() => issues.value.length || preview.value?.rows.some(r => ['error', 'warning'].includes(r.status)))
const statusLabel = { error: 'Ошибка', warning: 'Проверить', ready: 'Готово', unchanged: 'Уже есть' }
/** Сбрасывает результаты проверки и сохранения ежедневного импорта. */
function reset() { preview.value = null; parsed.value = null; accepted.value = false; result.value = null; error.value = ''; issues.value = [] }
/** Обновляет признак обработки и сообщает о нём родительскому компоненту. */
function setBusy(value) { busy.value = value; emit('busy', value) }
/** Распознаёт блоки техники и двигателей в выбранном диапазоне листов Excel. */
function detect() {
  reset()
  try { const bounds = parseRange(range.value); mappings.value = Object.fromEntries(sheets.value.map(sheet => [sheet.name, detectBlocks(sheet, bounds)])) }
  catch (e) { error.value = e.message }
}
/** Читает выбранный XLSX и распознаёт столбцы ежедневных показаний. */
async function selectFile(event) {
  reset(); sheets.value = []; mappings.value = {}
  const file = event.target.files[0]
  if (!file) return
  setBusy(true)
  try { sheets.value = await readDailyWorkbook(file); detect() } catch (e) { error.value = `Не удалось прочитать файл: ${e.message}` }
  finally { setBusy(false) }
}
/** Добавляет ручное сопоставление столбцов техники и двигателя на листе. */
function addBlock(name) {
  reset()
  try { const bounds=parseRange(range.value); mappings.value[name].push({headerRow:bounds.top,vehicleColumn:bounds.left,engines:[{column:bounds.left+1,number:1}]}) }
  catch(e) { error.value=e.message }
}
/** Переключает сопоставление одного или двух двигателей в блоке импорта. */
function toggleSecondEngine(block) {
  reset()
  if (block.engines.length > 1) { block.engines.pop(); return }
  block.engines[0].number = 1
  block.engines.push({column: Math.min(parseRange(range.value).right, block.engines[0].column+1), number: 2})
}
/** Отправляет строки ежедневного импорта на проверку или сохранение и обрабатывает ошибку API. */
async function request(action, body) {
  const response = await fetch(`/api/vehicles/hours/daily/${action}`, { method: 'POST', headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  const value = await response.json().catch(() => null)
  if (!response.ok) throw new Error(typeof value === 'string' ? value : value?.message || 'Не удалось проверить или сохранить показания.')
  return value
}
/** Извлекает ежедневные показания и запрашивает их проверку на сервере. */
async function check() {
  reset(); setBusy(true)
  try {
    const data = extractDailyRows(sheets.value, range.value, year, mappings.value)
    issues.value = data.issues
    if (!data.rows.length) throw new Error('Показания не найдены. Проверьте диапазон и настройку столбцов.')
    parsed.value = data.rows
    preview.value = await request('preview', { rows: data.rows })
  } catch (e) { error.value = e.message } finally { setBusy(false) }
}
/** Сохраняет допустимые строки по отпечатку проверки с учётом подтверждения предупреждений. */
async function save() {
  if (!preview.value || !ready.value || needsAcceptance.value && !accepted.value) return
  setBusy(true); error.value = ''
  try {
    const response = await request('commit', { rows: parsed.value, fingerprint: preview.value.fingerprint, acceptWarnings: accepted.value })
    result.value = response.imported; preview.value = null; emit('saved')
  } catch (e) { error.value = e.message; preview.value = null }
  finally { setBusy(false) }
}
</script>
<template>
 <div class="daily-import">
  <p>Листы ДД.ММ за {{ year }} год. Показания на <b>08:00</b>, начало первой смены. Одна запись по технике за дату; ДВС1 и ДВС2 учитываются отдельно.</p>
  <p>Лишние пробелы и знаки препинания в названии листа допустимы: « 08 . 10.. », «08,10», «08 — 10» распознаются как 08.10.</p>
  <p>Пустая ячейка — простой: берём предыдущее показание этого двигателя. Техника определяется по бортовому номеру. Excel читается в браузере; на сервер передаются распознанные записи.</p>
  <label>Диапазон поиска<input v-model="range" :disabled="busy" @change="detect" @input="reset" placeholder="A1:Z50" /></label>
  <label>Ежедневный Excel<input type="file" accept=".xlsx" :disabled="busy" @change="selectFile" /></label>
  <details v-if="sheets.length">
   <summary>Проверить или настроить столбцы ({{sheets.length}} листов)</summary>
   <fieldset v-for="sheet in sheets" :key="sheet.name" :disabled="busy">
    <legend>{{sheet.name}}</legend>
    <p v-if="!mappings[sheet.name]?.length">Столбцы не распознаны. Добавьте блок таблицы.</p>
    <div v-for="(block,index) in mappings[sheet.name]" :key="index" class="mapping" @change="reset">
     <label>Строка заголовков<input type="number" min="1" max="1048576" :value="block.headerRow+1" @input="block.headerRow=Number($event.target.value)-1; reset()" /></label>
     <label>Техника<select v-model="block.vehicleColumn"><option v-for="col in columns" :key="col.value" :value="col.value">{{col.label}}</option></select></label>
     <label v-for="(engine,e) in block.engines" :key="e">{{engine.number ? `ДВС${engine.number}` : 'Моточасы'}}<select v-model="engine.column"><option v-for="col in columns" :key="col.value" :value="col.value">{{col.label}}</option></select></label>
     <button type="button" class="secondary-button" @click="toggleSecondEngine(block)">{{block.engines.length===1?'Добавить ДВС2':'Убрать ДВС2'}}</button>
     <button type="button" class="secondary-button" @click="mappings[sheet.name].splice(index,1); reset()">Убрать блок</button>
    </div>
    <button type="button" class="secondary-button" @click="addBlock(sheet.name)">Добавить блок</button>
   </fieldset>
  </details>
  <p v-if="error" role="alert">{{error}}</p>
  <ul v-if="issues.length"><li v-for="(issue,index) in issues" :key="index">{{issue}}</li></ul>
  <button type="button" class="secondary-button" :disabled="busy || !sheets.length" @click="check">{{busy?'Обработка…':'Проверить файл'}}</button>
  <template v-if="preview">
   <p role="status">К сохранению: {{ready}} показаний. Ошибки: {{preview.rows.filter(r=>r.status==='error').length}}. Уже есть: {{preview.rows.filter(r=>r.status==='unchanged').length}}.</p>
   <div class="preview-table" tabindex="0" aria-label="Результат проверки моточасов">
    <table><thead><tr><th>Источник</th><th>Дата, 08:00</th><th>Борт №</th><th>ДВС</th><th>М/ч</th><th>Результат</th></tr></thead>
     <tbody><tr v-for="(row,index) in preview.rows" :key="index"><td>{{row.source}}</td><td>{{row.readingDate}}</td><td>{{row.garageNumber}}</td><td>{{row.engineNumber}}</td><td>{{row.engineHours ?? '—'}}</td><td><b>{{statusLabel[row.status]}}</b>: {{row.message}}</td></tr></tbody>
    </table>
   </div>
   <label v-if="needsAcceptance" class="accept"><input v-model="accepted" type="checkbox" :disabled="busy" />Проверил предупреждения. Сохранить допустимые показания, пропустив ошибки.</label>
   <button type="button" class="primary-button" :disabled="busy || !ready || needsAcceptance && !accepted" @click="save">Сохранить {{ready}} показаний</button>
  </template>
  <p v-if="result!==null" role="status">Сохранено показаний: {{result}}. Время показаний — 08:00.</p>
 </div>
</template>
<style scoped>
.daily-import{display:grid;gap:12px;min-width:0}.daily-import p{margin:0;font-size:13px;line-height:1.5}.daily-import label{display:grid;gap:4px;font-size:13px}.daily-import input,.daily-import select{box-sizing:border-box;max-width:100%;min-width:0;padding:7px;border:1px solid var(--border);border-radius:6px;background:var(--bg-main);color:var(--text-main)}.mapping{display:flex;flex-wrap:wrap;align-items:end;gap:8px;margin-bottom:12px}.mapping label{width:110px}fieldset{min-width:0;border:1px solid var(--border);margin:8px 0}.preview-table{max-height:350px;overflow:auto}table{border-collapse:collapse;font-size:12px;width:100%}td,th{padding:6px;text-align:left;border-bottom:1px solid var(--border)}th{position:sticky;top:0;background:var(--bg-surface)}.daily-import .accept{display:flex;align-items:start;gap:8px}.accept input{width:auto;flex-shrink:0}
</style>
