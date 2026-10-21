<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { buildForecastRows, climateLabels, currentStateLabel, forecastGroups, forecastOptions, nextForecastSelection, forecastNoteBlocks, engineHoursText } from '../utils/gtoForecast'
import { formatReportDate, vehicleTitle, localDate } from '../utils/shiftReport'
import { createGtoForecastPdf } from '../utils/gtoForecastPdf'
import FleetActionMenu from './FleetActionMenu.vue'

const props = defineProps({ token: { type: String, required: true } })
const selection = nextForecastSelection()
const date = ref(selection.date), shift = ref(selection.shift)
const rows = ref([]), organisation = ref(''), site = ref(''), mechanic = ref('')
const loading = ref(false), exporting = ref(false), error = ref(''), draftMessage = ref(''), canEdit = ref(false), canManageHours = ref(false), query = ref('')
const loaded = ref(false)
const saving = ref(0)
const saveQueues = new Map()
const failedSaves = new Map()
let settingsReady = false
let controller
let storageKey
try {
  const payload = JSON.parse(atob(props.token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')))
  if(payload.sub) storageKey = `myapp.gtoDraft.${payload.sub}`
} catch { /* A draft requires an identified user. */ }
const draftFields = ['forecast','ktg','maintenance','note']
const groups = computed(() => forecastGroups(rows.value))
const visibleGroups = computed(() => {
  const term = query.value.trim().toLocaleLowerCase('ru')
  return groups.value.map(group => ({ ...group, rows: group.rows.filter(row => term
    ? [row.vehicle.groupName,row.vehicle.typeName,row.vehicle.modelName,row.vehicle.garageNumber,row.vehicle.stateNumber,row.vehicle.vin].some(value => String(value ?? '').toLocaleLowerCase('ru').includes(term))
    : row.isInRepair) })).filter(group => group.rows.length)
})
/** Последовательно сохраняет настройки одного объекта и запоминает неудачные запросы для повтора. */
async function queueSave(key, url, body, afterSaved, allowed = canEdit.value) {
  if (!settingsReady || !allowed) return
  saving.value++
  const previous = saveQueues.get(key) ?? Promise.resolve()
  const pending = previous.catch(() => {}).then(async () => {
    const response = await fetch(url, { method:'PUT', headers:{Authorization:`Bearer ${props.token}`,'Content-Type':'application/json'}, body:JSON.stringify(body) })
    if(!response.ok) { const data=await response.json().catch(()=>null);throw new Error(data?.message || 'Не удалось сохранить настройки отчёта.') }
    if(afterSaved)afterSaved(await response.json())
  })
  saveQueues.set(key,pending)
  try { await pending;failedSaves.delete(key) } catch(failure) { failedSaves.set(key,{url,body,afterSaved});error.value=failure.message }
  finally { saving.value--;if(saveQueues.get(key)===pending)saveQueues.delete(key) }
}
/** Сохраняет организацию и участок пользователя в настройках ГТО. */
function saveUserSettings() { return queueSave('user','/api/gto-settings/user',{organisation:organisation.value,site:site.value}) }
/** Проверяет и сохраняет моточасы двигателя на момент замены. */
function saveEngine(row,engine) {
  const /** Преобразует заполненное значение в число, сохраняя пустое значение как null. */ numeric=value=>value==null || value==='' ? null : Number(value)
  if([engine.hoursAtReplacement].some(value=>value!=='' && value!=null && (!Number.isFinite(Number(value)) || Number(value)<0))) {error.value='Моточасы не могут быть отрицательными.';return}
  const body={readingDate:engine.readingDate || localDate(new Date()),engineHours:null,hoursAtReplacement:numeric(engine.hoursAtReplacement)}
  return queueSave(`${row.vehicle.id}:engine:${engine.number}`,`/api/gto-settings/vehicles/${row.vehicle.id}/engines/${engine.number}`,body,undefined,canManageHours.value)
}
/** Сохраняет климатическую установку и план выпуска для открытых неисправностей техники. */
function saveVehicleSettings(row) { return queueSave(row.vehicle.id,`/api/gto-settings/vehicles/${row.vehicle.id}`,{climate:row.climate,plannedRelease:row.plannedRelease,defectIds:row.openDefectIds}) }
/** Читает совместимую версию локального черновика прогноза ГТО. */
function savedDraft() {
  if (!storageKey) return null
  try { const value=JSON.parse(localStorage.getItem(storageKey));return [1,2].includes(value?.version)?value:null }
  catch { return null }
}
/** Применяет допустимые поля черновика к строке прогноза с учётом старых значений статуса. */
function applyDraft(row, draft) {
  const saved = draft?.rows?.[row.vehicle.id]
  if (!saved) return row
  const legacy = { operating: 'healthy', ready_after_repair: 'healthy', repair: 'faulty', unknown: 'faulty' }
  if (legacy[saved.forecast]) saved.forecast = legacy[saved.forecast]
  for (const field of draftFields) {
    const value=saved[field]
    if(field==='forecast' && forecastOptions.some(option=>option.value===value))row[field]=value
    else if(field==='climate' && Object.hasOwn(climateLabels,value))row[field]=value
    else if(field==='ktg' && (value==='' || (Number.isFinite(Number(value)) && Number(value)>=0 && Number(value)<=100)))row[field]=value
    else if(['maintenance','note','plannedRelease'].includes(field) && typeof value==='string')row[field]=value.slice(0,2000)
  }
  return row
}
/** Сохраняет редактируемые поля прогноза в локальном хранилище браузера. */
function saveDraft() {
  if(!loaded.value || !canEdit.value || !storageKey)return
  try {
    localStorage.setItem(storageKey,JSON.stringify({version:2,
      rows:Object.fromEntries(rows.value.map(row=>[row.vehicle.id,Object.fromEntries(draftFields.map(field=>[field,row[field]]))]))}))
    draftMessage.value='Черновик сохранён в этом браузере.'
  } catch { draftMessage.value='Браузер не смог сохранить черновик. Скачайте PDF перед закрытием формы.' }
}
/** Дожидается сохранений, загружает журналы и настройки ГТО, затем применяет локальный черновик. */
async function load() {
  saveDraft()
  controller?.abort(); controller=new AbortController()
  loading.value=true;error.value=''
  try {
    const headers={Authorization:`Bearer ${props.token}`},signal=controller.signal
    await Promise.all([...saveQueues.values()].map(promise=>promise.catch(()=>{})))
    settingsReady=false
    const [historyResponse,optionsResponse,profileResponse,settingsResponse]=await Promise.all([
      fetch('/api/vehicles/repair-journal',{headers,signal}),fetch('/api/vehicles/fleet-options',{headers,signal}),fetch('/api/profile',{headers,signal}),fetch('/api/gto-settings',{headers,signal}),
    ])
    if(!historyResponse.ok)throw new Error('Не удалось загрузить парк и состояние ремонта.')
    if(!optionsResponse.ok)throw new Error('Не удалось проверить права составления прогноза.')
    const options=await optionsResponse.json();canEdit.value=options.canManage===true;canManageHours.value=options.canManageHours===true
    if(profileResponse.ok) {const profile=await profileResponse.json();mechanic.value=[profile.lastName,profile.firstName,profile.middleName].filter(Boolean).join(' ')}
    if(!settingsResponse.ok)throw new Error('Не удалось загрузить сохранённые настройки отчёта.')
    const settings=await settingsResponse.json()
    const draft=canEdit.value?savedDraft():null
    rows.value=buildForecastRows(await historyResponse.json()).map(row=>applyDraft(row,draft))
    organisation.value=settings.organisation ?? '';site.value=settings.site ?? ''
    const climate=new Map((settings.vehicles ?? []).map(item=>[item.vehicleId,item.climate]))
    const plans=new Map((settings.plans ?? []).map(item=>[item.defectId,item.plannedRelease]))
    const engineSettings=new Map((settings.engines ?? []).map(item=>[`${item.vehicleId}:${item.engineNumber}`,item]))
    for(const row of rows.value){
      for(const engine of row.engines) {engine.hoursAtReplacement=engineSettings.get(`${row.vehicle.id}:${engine.number}`)?.hoursAtReplacement ?? null;engine.readingDate ||= localDate(new Date())}
      row.climate=climate.get(row.vehicle.id) ?? 'unknown';row.plannedRelease=[...new Set(row.openDefectIds.map(id=>plans.get(id)).filter(Boolean))].join('; ')}
    if(draft)draftMessage.value='Загружен черновик текущего прогноза.'
    failedSaves.clear()
    settingsReady=true
    // Transfer existing local settings once; subsequent loads use server values.
    if(canEdit.value && draft?.version===1) {
      if(!settings.hasUserSettings && (draft.organisation || draft.site)) {
        organisation.value=String(draft.organisation ?? '').slice(0,200);site.value=String(draft.site ?? '').slice(0,200)
        await saveUserSettings()
      }
      for(const row of rows.value) {
        const old=draft.rows?.[row.vehicle.id]
        if(!old)continue
        let migrate=false
        if(!climate.has(row.vehicle.id) && Object.hasOwn(climateLabels,old.climate) && old.climate!=='unknown') {row.climate=old.climate;migrate=true}
        if(row.openDefectIds.length && !row.openDefectIds.some(id=>plans.has(id)) && old.plannedRelease) {row.plannedRelease=String(old.plannedRelease).slice(0,2000);migrate=true}
        if(migrate)await saveVehicleSettings(row)
      }
    }
    loaded.value=true
    saveDraft()
  } catch(failure){if(failure.name!=='AbortError')error.value=failure.message}
  finally{loading.value=false}
}
/** После подтверждения сбрасывает прогноз и примечания механика к исходному состоянию. */
function clearDraft() {
  if(!canEdit.value || !window.confirm('Сбросить прогноз и примечания механика? Данные ремонта останутся без изменений.'))return
  for(const row of rows.value)Object.assign(row,{forecast:row.currentState === 'healthy' ? 'healthy' : 'faulty',ktg:'',maintenance:'',note:''})
  saveDraft()
}
/** Повторяет неудачные сохранения настроек и скачивает PDF проверенного прогноза ГТО. */
async function download() {
  if(!loaded.value || loading.value || exporting.value || saving.value || !canEdit.value || !rows.value.length)return
  if(!/^\d{4}-\d{2}-\d{2}$/.test(date.value)){error.value='Выберите дату прогноза.';return}
  if(rows.value.some(row=>row.ktg!=='' && (!Number.isFinite(Number(row.ktg)) || Number(row.ktg)<0 || Number(row.ktg)>100))){error.value='КТГ должен быть от 0 до 100%.';return}
  exporting.value=true;error.value='';saveDraft()
  try {
    for(const [key,item] of [...failedSaves])await queueSave(key,item.url,item.body,item.afterSaved)
    if(failedSaves.size)throw new Error('Настройки не сохранены. Проверьте соединение и попробуйте снова.')
    const blob=await createGtoForecastPdf({date:date.value,shift:shift.value,mechanic:mechanic.value,organisation:organisation.value,site:site.value,groups:groups.value})
    const url=URL.createObjectURL(blob),anchor=document.createElement('a')
    anchor.href=url;anchor.download=`Выход ГТО ${date.value} (${shift.value} смена).pdf`;anchor.click()
    setTimeout(()=>URL.revokeObjectURL(url),60000)
  }catch(failure){error.value=failure.message||'Не удалось сформировать PDF.'}
  finally{exporting.value=false}
}
watch([rows,organisation,site],saveDraft,{deep:true})
onMounted(load)
onBeforeUnmount(()=>{saveDraft();controller?.abort()})
</script>

<template>
  <section class="gto-forecast">
    <div class="gto-panel">
      <header class="gto-heading">
        <h2>Прогноз готовности техники</h2>
        <FleetActionMenu label="Меню Выход ГТО" :disabled="loading || exporting">
        <button type="button" class="secondary-button" :disabled="loading || exporting" @click="load">Обновить данные ремонта</button>
        <button v-if="canEdit" type="button" class="secondary-button" :disabled="loading || exporting" @click="clearDraft">Сбросить прогноз</button>
        <button v-if="canEdit" type="button" class="primary-button" :disabled="loading || exporting || !loaded || !rows.length || saving > 0" @click="download">{{ exporting ? 'Формирование…' : 'Скачать PDF' }}</button>
      </FleetActionMenu>
      </header>
      <p>Механик выбирает дату и смену, затем указывает, состояние техники к выбранной смене: исправен или неисправен.</p>
      <div class="gto-fields">
        <label>Дата прогноза<input v-model="date" type="date" :disabled="exporting" /></label>
        <label>Смена<select aria-label="Смена" v-model="shift" :disabled="exporting"><option value="1">1 смена</option><option value="2">2 смена</option></select></label>
        <label>Организация<input v-model="organisation" maxlength="200" @change="saveUserSettings" :disabled="!canEdit || loading || exporting" /></label>
        <label>Участок<input v-model="site" maxlength="200" @change="saveUserSettings" :disabled="!canEdit || loading || exporting" /></label>
      </div>
      <label class="gto-search">Поиск техники<input v-model="query" type="search" placeholder="Группа, тип, модель или номер" /></label>
      <p>Механик: {{ mechanic || 'ФИО не указано в профиле' }}</p>

      <p v-if="saving" class="gto-hint" role="status">Сохранение настроек…</p>
      <p v-if="draftMessage" class="gto-hint" role="status">{{ draftMessage }}</p>
      <p v-if="loaded && !canEdit" class="gto-hint">Составлять прогноз могут механики, старшие механики и администраторы.</p>
    </div>
    <p v-if="error" class="form-message--error" role="alert">{{ error }}</p>
    <p v-if="loading" role="status">Загрузка парка техники…</p>
    <template v-else-if="loaded">
      <p v-if="!rows.length">В парке пока нет техники.</p>
      <p v-else-if="!visibleGroups.length">Техники в ремонте по выбранным условиям нет.</p>
      <section v-for="group in visibleGroups" :key="group.name" class="gto-group">
        <h3>{{ group.name }}</h3>
        <article v-for="row in group.rows" :key="row.vehicle.id" class="gto-panel gto-row">
          <header><h4>{{ vehicleTitle(row.vehicle) }}</h4><span>{{ currentStateLabel(row.currentState) }}</span></header>
          <p class="gto-hint">Наработка: <template v-for="(hours,index) in (engineHoursText(row) || 'не указана').split(' / ')" :key="index"><template v-if="index"> / </template><strong>{{ hours }}</strong></template> м/ч · Начало простоя: {{ row.forecast !== 'healthy' && row.downtimeStartedAt ? new Date(row.downtimeStartedAt).toLocaleDateString('ru-RU') : 'не указано' }}</p>
          <div class="gto-engines">
            <fieldset v-for="engine in row.engines" :key="engine.number" class="gto-engine">
              <legend>ДВС № {{ engine.number }}</legend>
              <div class="gto-fields">
                <label>М/ч при замене ДВС<input v-model="engine.hoursAtReplacement" type="number" min="0" step="0.1" :disabled="!canManageHours || loading || exporting" @change="saveEngine(row,engine)" /></label>
              </div>
            </fieldset>
          </div>
          <p v-if="row.legacyHours != null" class="gto-hint">Прежнее показание: {{ row.legacyHours }} м/ч — номер ДВС не указан. Укажите показания отдельно для ДВС № 1 и № 2.</p>
          <div v-if="forecastNoteBlocks(row).length"><strong>Неисправность/примечание</strong><div class="gto-current-notes"><p v-for="(block,index) in forecastNoteBlocks(row)" :key="index" :class="{ 'gto-repair-note': block.divider, 'gto-note-bold': block.bold, 'gto-note-italic': block.italic }"><template v-if="block.spans"><span v-for="(span,spanIndex) in block.spans" :key="spanIndex" :class="{ 'gto-note-italic': span.italic }">{{ span.text }}</span></template><template v-else>{{ block.text }}</template></p></div></div>
            <label class="gto-field-wide">Примечание механика<textarea v-model="row.note" rows="2" maxlength="2000" placeholder="Что планируется сделать или что мешает выходу" :disabled="!canEdit || loading || exporting" /></label>
          <div class="gto-fields">
            <label>Состояние к смене<select aria-label="Состояние к смене" v-model="row.forecast" :disabled="!canEdit || loading || exporting"><option v-for="option in forecastOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select></label>
            <label v-if="row.forecast !== 'healthy' && row.openDefectIds.length">Плановый выход из ремонта<input @change="saveVehicleSettings(row)" v-model="row.plannedRelease" maxlength="2000" placeholder="Дата, смена или условие выхода" :disabled="!canEdit || loading || exporting" /></label>
            <label>КТГ, %<input v-model="row.ktg" type="number" min="0" max="100" step="0.1" placeholder="Не указано" :disabled="!canEdit || loading || exporting" /></label>
            <label>Вид ТО<input v-model="row.maintenance" maxlength="2000" placeholder="При необходимости" :disabled="!canEdit || loading || exporting" /></label>
            <label>Кондиционирование и отопление<select @change="saveVehicleSettings(row)" aria-label="Кондиционирование и отопление" v-model="row.climate" :disabled="!canEdit || loading || exporting"><option v-for="(label,value) in climateLabels" :key="value" :value="value">{{ label }}</option></select></label>

          </div>
        </article>
      </section>
    </template>
  </section>
</template>

<style scoped>
.gto-engines { display:grid; gap:12px; margin:12px 0; }
.gto-engine { min-width:0; border:1px solid var(--border); border-radius:8px; padding:12px; }
.gto-engine legend { font-size:13px; }
.gto-forecast,.gto-group { display:grid; gap:16px; }
.gto-panel { padding:20px; border:1px solid var(--border); border-radius:12px; background:var(--bg-surface); min-width:0; }
.gto-panel h2,.gto-panel h3,.gto-panel h4 { margin:0 0 12px; }
.gto-fields { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:14px; }
.gto-field-wide,.gto-fields label,.gto-search { display:grid; gap:6px; font-size:13px; }
.gto-fields input,.gto-fields select,.gto-fields textarea,.gto-field-wide textarea,.gto-search input { box-sizing:border-box; min-width:0; width:100%; padding:10px; border:1px solid var(--border); border-radius:8px; background:var(--bg-main); color:var(--text-main); font:inherit; }
.gto-fields :disabled { opacity:.6; }
.gto-field-wide { grid-column:1/-1; }
.gto-hint { color:var(--text-secondary); font-size:12px; line-height:1.6; }
.gto-heading { display:flex; align-items:flex-start; justify-content:space-between; gap:12px; }
.gto-heading h2 { min-width:0; }
.gto-summary { display:flex; gap:10px 24px; flex-wrap:wrap; padding-left:18px; }
.gto-row header { display:flex; gap:10px; justify-content:space-between; flex-wrap:wrap; }
.gto-row header span { color:var(--text-secondary); font-size:12px; }
.gto-current-notes p { margin:0; padding:0; }
.gto-note-bold { font-weight:700; }
.gto-note-italic { font-style:italic; }
.gto-repair-note { border-bottom:1px solid var(--text-secondary); line-height:1; }
.gto-current-notes { white-space:pre-wrap; overflow-wrap:anywhere; font-size:13px; }
@media(max-width:760px){.gto-fields{grid-template-columns:minmax(0,1fr)}.gto-panel{padding:14px}}
</style>
