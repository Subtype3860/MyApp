<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { buildForecastRows, climateLabels, currentStateLabel, forecastGroups, forecastOptions, nextForecastSelection, forecastNoteBlocks, engineHoursText, hasReportRequests } from '../utils/gtoForecast'
import { formatReportDate, vehicleTitle, localDate } from '../utils/shiftReport'
import { createGtoForecastPdf } from '../utils/gtoForecastPdf'
import FleetActionMenu from './FleetActionMenu.vue'
import LocalAdvicePanel from './LocalAdvicePanel.vue'
import ShiftOutcome from './ShiftOutcome.vue'

const props = defineProps({ token: { type: String, required: true } })
const selection = nextForecastSelection()
const date = ref(selection.date), shift = ref(selection.shift)
const rows = ref([]), organisation = ref(''), site = ref(''), mechanic = ref('')
const loading = ref(false), exporting = ref(false), error = ref(''), draftMessage = ref(''), canEdit = ref(false), canManageHours = ref(false), query = ref('')
const loaded = ref(false)
const saving = ref(0)
const reportReady = ref(false), reportDirty = ref(false), reportSaving = ref(false), reportConflict = ref(false)
const reportStatus = ref(''), history = ref([]), historyChoice = ref(''), viewingHistory = ref(false), moreHistory = ref(false)
const editable = computed(() => canEdit.value && reportReady.value && !viewingHistory.value && !reportConflict.value)
let activeDate = date.value, activeShift = String(shift.value), revision = 0, reportVersion = 0
let saveTimer, pendingReportSave, ignoreSelection = false
const snapshot = () => JSON.parse(JSON.stringify({version:1,date:activeDate,shift:activeShift,mechanic:mechanic.value,organisation:organisation.value,site:site.value,rows:rows.value}))
const recoveryKey = () => storageKey ? storageKey + '.' + activeDate + '.' + activeShift : null
const headers = () => ({Authorization:`Bearer ${props.token}`,'Content-Type':'application/json'})
function reportChanged() {
  if (!reportReady.value || loading.value || !editable.value) return
  reportVersion++; reportDirty.value = true
  reportStatus.value = 'Есть несохранённые изменения…'
  saveDraft()
  try { if(recoveryKey())localStorage.setItem(recoveryKey(),JSON.stringify({revision,snapshot:snapshot()})) } catch { /* The server remains the primary store. */ }
  clearTimeout(saveTimer); saveTimer = setTimeout(() => persistReport(), 500)
}
async function persistReport() {
  clearTimeout(saveTimer)
  if (pendingReportSave) {const saved=await pendingReportSave; if(!saved)return false; if(reportDirty.value && !reportConflict.value)return persistReport(); return !reportDirty.value}
  if (!reportDirty.value) return true
  if (reportConflict.value) return false
  if(rows.value.some(row=>row.ktg!=='' && row.ktg!=null && (!Number.isFinite(Number(row.ktg)) || Number(row.ktg)<0 || Number(row.ktg)>100))) {
    reportStatus.value='Не сохранено: КТГ должен быть от 0 до 100%.';return false
  }
  reportSaving.value=true
  pendingReportSave=(async()=>{
    try {
      while(reportDirty.value) {
        const version=reportVersion, data=snapshot()
        const response=await fetch(`/api/gto-reports/${activeDate}/${activeShift}`,{method:'PUT',headers:headers(),body:JSON.stringify({revision,snapshot:data}),signal:AbortSignal.timeout(15000)})
        const result=await response.json().catch(()=>null)
        if(!response.ok) {
          reportConflict.value=response.status===409
          throw new Error(result?.message || 'Не удалось сохранить отчёт. Изменения остаются в этом браузере; повторите сохранение.')
        }
        revision=result.revision
        if(version===reportVersion)reportDirty.value=false
        reportStatus.value='Сохранено в базе: '+new Date(result.updatedAt).toLocaleString('ru-RU')
        try { if(recoveryKey()) {if(reportDirty.value)localStorage.setItem(recoveryKey(),JSON.stringify({revision,snapshot:snapshot()}));else localStorage.removeItem(recoveryKey())} } catch {}
      }
      return true
    } catch(failure) {reportStatus.value=failure.name==='TimeoutError'?'Сервер не ответил. Изменения остались в этом браузере; повторите сохранение.':failure.message;return false}
    finally {reportSaving.value=false}
  })()
  const result=await pendingReportSave;pendingReportSave=null;return result
}
async function loadHistory(append=false) {
  try {const response=await fetch('/api/gto-reports'+(append?'?offset='+history.value.length:''),{headers:headers()});if(!response.ok)throw new Error();const items=await response.json();history.value=append?[...history.value,...items]:items;moreHistory.value=items.length===100}
  catch {error.value='Не удалось загрузить историю отчётов.'}
}
async function openHistory() {
  if(!historyChoice.value)return
  if(!await persistReport())return
  const [selectedDate,selectedShift]=historyChoice.value.split('/')
  ignoreSelection=true;date.value=selectedDate;shift.value=selectedShift;ignoreSelection=false
  viewingHistory.value=true;await load()
}
async function returnToCurrent() {
  if(!await persistReport())return
  ignoreSelection=true;date.value=selection.date;shift.value=selection.shift;ignoreSelection=false
  viewingHistory.value=false;await load()
}
function applySnapshot(data) {
  rows.value=data.rows;organisation.value=data.organisation;site.value=data.site;mechanic.value=data.mechanic
}
async function load(refresh=false) {
  if(loading.value)return
  loading.value=true
  if(!await persistReport()) {
    ignoreSelection=true;date.value=activeDate;shift.value=activeShift;ignoreSelection=false
    loading.value=false;return
  }
  reportReady.value=false;loaded.value=false;error.value='';reportConflict.value=false;draftMessage.value=''
  const preserved=refresh ? {rows:Object.fromEntries(rows.value.map(row=>[row.vehicle.id,Object.fromEntries(draftFields.map(field=>[field,row[field]]))]))} : null
  activeDate=date.value;activeShift=String(shift.value);revision=0
  try {
    const [saved,options]=await Promise.all([fetch(`/api/gto-reports/${activeDate}/${activeShift}`,{headers:headers()}),fetch('/api/vehicles/fleet-options',{headers:headers()})])
    if(!options.ok)throw new Error('Не удалось проверить права отчёта.')
    const rights=await options.json();canEdit.value=rights.canManage===true;canManageHours.value=rights.canManageHours===true
    let stored
    if(saved.ok) {stored=await saved.json();if(stored.snapshot?.version!==1)throw new Error('Неподдерживаемый формат сохранённого отчёта.');revision=stored.revision}
    else if(saved.status!==404)throw new Error('Не удалось загрузить сохранённый отчёт.')
    if(stored && !refresh) {
      if(activeDate<localDate(new Date()))viewingHistory.value=true
      applySnapshot(stored.snapshot);settingsReady=true;loaded.value=true
      reportStatus.value='Сохранённый отчёт от '+new Date(stored.updatedAt).toLocaleString('ru-RU')
    } else {
      if(viewingHistory.value || (!stored && activeDate<localDate(new Date())))throw new Error('За выбранную дату и смену отчёт не был сохранён. Восстановить прошлый отчёт из текущих данных невозможно.')
      const response=await fetch(`/api/gto-reports/defaults?date=${activeDate}&shift=${activeShift}`,{headers:headers()})
      if(!response.ok)throw new Error('Не удалось загрузить предыдущие КТГ и вид ТО.')
      const defaults=await response.json()
      const draft=preserved ?? (defaults.rows?.length ? {rows:Object.fromEntries(defaults.rows.map(row=>[row.vehicle.id,{ktg:row.ktg,maintenance:row.maintenance}]))} : activeDate===selection.date && activeShift===selection.shift ? savedDraft() : null)
      await loadLive(draft)
      if(!loaded.value)throw new Error(error.value || 'Не удалось загрузить данные отчёта.')
      reportStatus.value='Новый снимок: данные ремонта загружены сейчас.'
    }
    reportReady.value=true
    if(!viewingHistory.value && canEdit.value) {
      let recovery
      try {recovery=JSON.parse(localStorage.getItem(recoveryKey()))} catch {}
      if(recovery?.snapshot && recovery.revision===revision && !refresh) {applySnapshot(recovery.snapshot);reportDirty.value=true;reportVersion++}
      else if(recovery?.snapshot && !refresh)reportStatus.value+=' В браузере есть несохранённая копия другой версии.'
      if(!stored || refresh) {reportDirty.value=true;reportVersion++}
    }
  } catch(failure) {error.value=failure.message;settingsReady=false;loaded.value=false}
  finally {loading.value=false}
  if(reportDirty.value)await persistReport()
  await loadHistory()
}
function beforeLeave(event) {if(reportDirty.value || reportSaving.value){event.preventDefault();event.returnValue=''}}
async function reloadSaved() {
  if(!window.confirm('Открыть серверную версию? Несохранённая копия останется в этом браузере.'))return
  reportDirty.value=false;reportConflict.value=false
  await load()
}
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
    : viewingHistory.value || hasReportRequests(row)) })).filter(group => group.rows.length)
})
/** Последовательно сохраняет настройки одного объекта и запоминает неудачные запросы для повтора. */
async function queueSave(key, url, body, afterSaved, allowed = canEdit.value) {
  if (!settingsReady || !allowed || viewingHistory.value || reportConflict.value) return
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
  if(!loaded.value || !canEdit.value || !storageKey || viewingHistory.value)return
  try {
    localStorage.setItem(storageKey,JSON.stringify({version:2,
      rows:Object.fromEntries(rows.value.map(row=>[row.vehicle.id,Object.fromEntries(draftFields.map(field=>[field,row[field]]))]))}))
    draftMessage.value=''
  } catch { draftMessage.value='Браузер не смог сохранить черновик. Скачайте PDF перед закрытием формы.' }
}
/** Дожидается сохранений, загружает журналы и настройки ГТО, затем применяет локальный черновик. */
async function loadLive(draft = null) {
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
/** Повторяет неудачные сохранения и скачивает проверенный прогноз в выбранном формате. */
async function download(format = 'pdf') {
  if(!loaded.value || loading.value || exporting.value || saving.value || !rows.value.length)return
  if(!/^\d{4}-\d{2}-\d{2}$/.test(date.value)){error.value='Выберите дату прогноза.';return}
  if(rows.value.some(row=>row.ktg!=='' && (!Number.isFinite(Number(row.ktg)) || Number(row.ktg)<0 || Number(row.ktg)>100))){error.value='КТГ должен быть от 0 до 100%.';return}
  exporting.value=true;error.value='';saveDraft()
  try {
    if(!await persistReport())throw new Error(reportStatus.value)
    for(const [key,item] of [...failedSaves])await queueSave(key,item.url,item.body,item.afterSaved)
    if(failedSaves.size)throw new Error('Настройки не сохранены. Проверьте соединение и попробуйте снова.')
    const report={date:date.value,shift:shift.value,mechanic:mechanic.value,organisation:organisation.value,site:site.value,groups:groups.value}
    let blob, extension=format
    if(format==='pdf')blob=await createGtoForecastPdf(report)
    else {
      const {createGtoForecastExcel,createGtoForecastImage}=await import('../utils/gtoForecastExport')
      if(format==='xlsx')blob=await createGtoForecastExcel(report)
      else {const result=await createGtoForecastImage(report);blob=result.blob;extension=result.extension}
    }
    const url=URL.createObjectURL(blob),anchor=document.createElement('a')
    anchor.href=url;anchor.download=`Выход ГТО ${report.date} (${report.shift} смена).${extension}`;anchor.click()
    setTimeout(()=>URL.revokeObjectURL(url),60000)
  }catch(failure){error.value=failure.message||'Не удалось сформировать отчёт.'}
  finally{exporting.value=false}
}
watch([rows,organisation,site],reportChanged,{deep:true,flush:'sync'})
watch([date,shift],()=>{if(!ignoreSelection && !loading.value)void load()}, {flush:'sync'})
onMounted(()=>{void load();window.addEventListener('beforeunload',beforeLeave)})
onBeforeUnmount(()=>{clearTimeout(saveTimer);void persistReport();controller?.abort();window.removeEventListener('beforeunload',beforeLeave)})
</script>

<template>
  <section class="gto-forecast">
    <div class="gto-panel">
      <header class="gto-heading">
        <h2>Прогноз готовности техники</h2>
        <FleetActionMenu label="Меню Выход ГТО" :disabled="loading || exporting">
        <button type="button" class="secondary-button" :disabled="loading || exporting" v-if="editable" @click="load(true)">Обновить данные ремонта</button>
        <button v-if="loaded" type="button" class="primary-button" :disabled="loading || exporting || !loaded || !rows.length || saving > 0" @click="download('pdf')">Скачать PDF</button>
        <button v-if="loaded" type="button" class="secondary-button" :disabled="loading || exporting || !loaded || !rows.length || saving > 0" @click="download('xlsx')">Скачать Excel</button>
        <button v-if="loaded" type="button" class="secondary-button" :disabled="loading || exporting || !loaded || !rows.length || saving > 0" @click="download('png')">Скачать изображение</button>
      </FleetActionMenu>
      </header>
      <p>Отчёт общий для выбранной даты и смены. Изменения автоматически сохраняются в базе. Новый отчёт использует текущие данные ремонта; КТГ и вид ТО переносятся из предыдущего отчёта.</p>
      <div class="gto-fields">
        <label>История отчётов<select aria-label="История отчётов" v-model="historyChoice" :disabled="loading || exporting"><option value="">Выберите сохранённый отчёт</option><option v-for="item in history" :key="item.date+'/'+item.shift" :value="item.date+'/'+item.shift">{{ formatReportDate(item.date) }} · {{ item.shift }} смена</option></select></label>
        <button v-if="moreHistory" type="button" class="secondary-button" :disabled="loading || exporting" @click="loadHistory(true)">Более ранние отчёты</button>
        <button type="button" class="secondary-button" :disabled="!historyChoice || loading || exporting" @click="openHistory">Открыть отчёт</button>
        <button v-if="viewingHistory" type="button" class="secondary-button" :disabled="loading || exporting" @click="returnToCurrent">К текущему прогнозу</button>
      </div>
      <p v-if="viewingHistory" class="gto-hint">Просмотр сохранённого отчёта. Данные ремонта зафиксированы на момент сохранения.</p>
      <p class="gto-hint" role="status">{{ reportSaving ? 'Сохранение отчёта…' : reportStatus }}</p>
      <button v-if="reportDirty && !reportConflict" type="button" class="secondary-button" :disabled="reportSaving" @click="persistReport">Повторить сохранение</button>
      <button v-if="reportConflict" type="button" class="secondary-button" @click="reloadSaved">Открыть серверную версию</button>
      <div class="gto-fields">
        <label>Дата прогноза<input v-model="date" type="date" :disabled="exporting || loading || viewingHistory" /></label>
        <label>Смена<select aria-label="Смена" v-model="shift" :disabled="exporting || loading || viewingHistory"><option value="1">1 смена</option><option value="2">2 смена</option></select></label>
        <label>Организация<input v-model="organisation" maxlength="200" @change="saveUserSettings" :disabled="!editable || loading || exporting" /></label>
        <label>Участок<input v-model="site" maxlength="200" @change="saveUserSettings" :disabled="!editable || loading || exporting" /></label>
      </div>
      <label class="gto-search">Поиск техники<input v-model="query" type="search" placeholder="Группа, тип, модель или номер" /></label>
      <p>Механик: {{ mechanic || 'ФИО не указано в профиле' }}</p>

      <p v-if="exporting" class="gto-hint" role="status">Формирование отчёта…</p>
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
          <LocalAdvicePanel v-if="!viewingHistory" :token="token" :context="loading" :url="`/api/local-advice/vehicles/${row.vehicle.id}/readiness?date=${date}&shift=${shift}`" title="Локальная рекомендация к смене" />
          <ShiftOutcome v-if="canEdit && date < localDate(new Date())" :token="token" :vehicle-id="row.vehicle.id" :date="date" :shift="shift" />
          <header><h4>{{ vehicleTitle(row.vehicle) }}</h4><span>{{ currentStateLabel(row.currentState) }}</span></header>
          <p class="gto-hint">Наработка: <template v-for="(hours,index) in (engineHoursText(row) || 'не указана').split(' / ')" :key="index"><template v-if="index"> / </template><strong>{{ hours }}</strong></template> м/ч · Начало простоя: {{ row.forecast !== 'healthy' && row.downtimeStartedAt ? new Date(row.downtimeStartedAt).toLocaleDateString('ru-RU') : 'не указано' }}</p>
          <div class="gto-engines">
            <fieldset v-for="engine in row.engines" :key="engine.number" class="gto-engine">
              <legend>ДВС № {{ engine.number }}</legend>
              <div class="gto-fields">
                <label>М/ч при замене ДВС<input class="gto-replacement-hours" v-model="engine.hoursAtReplacement" type="number" min="0" step="0.1" :disabled="!canManageHours || !editable || loading || exporting" @change="saveEngine(row,engine)" /></label>
              </div>
            </fieldset>
          </div>
          <p v-if="row.legacyHours != null" class="gto-hint">Прежнее показание: {{ row.legacyHours }} м/ч — номер ДВС не указан. Укажите показания отдельно для ДВС № 1 и № 2.</p>
          <div v-if="forecastNoteBlocks(row).length"><strong>Неисправность/примечание</strong><div class="gto-current-notes"><p v-for="(block,index) in forecastNoteBlocks(row)" :key="index" :class="{ 'gto-repair-note': block.divider, 'gto-note-bold': block.bold, 'gto-note-italic': block.italic }"><template v-if="block.spans"><span v-for="(span,spanIndex) in block.spans" :key="spanIndex" :class="{ 'gto-note-italic': span.italic }">{{ span.text }}</span></template><template v-else>{{ block.text }}</template></p></div></div>
            <label class="gto-field-wide">Примечание механика<textarea v-model="row.note" rows="2" maxlength="2000" placeholder="Что планируется сделать или что мешает выходу" :disabled="!editable || loading || exporting" /></label>
          <div class="gto-fields">
            <label>Состояние к смене<select aria-label="Состояние к смене" v-model="row.forecast" :disabled="!editable || loading || exporting"><option v-for="option in forecastOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select></label>
            <label v-if="row.forecast !== 'healthy' && row.openDefectIds.length">Плановый выход из ремонта<input @change="saveVehicleSettings(row)" v-model="row.plannedRelease" maxlength="2000" placeholder="Дата, смена или условие выхода" :disabled="!editable || loading || exporting" /></label>
            <label>КТГ, %<input v-model="row.ktg" type="number" min="0" max="100" step="0.1" placeholder="Не указано" :disabled="!editable || loading || exporting" /></label>
            <label>Вид ТО<input v-model="row.maintenance" maxlength="2000" placeholder="При необходимости" :disabled="!editable || loading || exporting" /></label>
            <label>Кондиционирование и отопление<select @change="saveVehicleSettings(row)" aria-label="Кондиционирование и отопление" v-model="row.climate" :disabled="!editable || loading || exporting"><option v-for="(label,value) in climateLabels" :key="value" :value="value">{{ label }}</option></select></label>

          </div>
        </article>
      </section>
    </template>
  </section>
</template>

<style scoped>
.gto-replacement-hours { color: #cc1414; font-family: monospace; font-weight: bold; }
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
