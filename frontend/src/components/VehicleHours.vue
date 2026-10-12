<script setup>
import { computed,onMounted,onBeforeUnmount,ref } from 'vue'
import { buildForecastRows, forecastGroups } from '../utils/gtoForecast'
import { localDate,vehicleTitle } from '../utils/shiftReport'
const props=defineProps({token:{type:String,required:true}})
const rows=ref([]),query=ref(''),date=ref(localDate(new Date())),loading=ref(false),error=ref(''),message=ref(''),busy=ref('')
const modal=ref(null),file=ref(null),importing=ref(false),importResult=ref(null)
let controller
const matches=computed(()=>rows.value.filter(row=>[row.vehicle.modelName,row.vehicle.typeName,row.vehicle.garageNumber,row.vehicle.stateNumber].some(value=>String(value??'').toLocaleLowerCase('ru').includes(query.value.trim().toLocaleLowerCase('ru')))))
const headers=()=>({Authorization:`Bearer ${props.token}`})
async function failure(response,fallback){const body=await response.json().catch(()=>null);return new Error(body?.message || Object.values(body?.errors ?? {}).flat()[0] || fallback)}
async function load(){controller?.abort();controller=new AbortController();loading.value=true;error.value='';try{const response=await fetch('/api/vehicles/repair-journal',{headers:headers(),signal:controller.signal});if(!response.ok)throw await failure(response,'Не удалось загрузить технику.');rows.value=forecastGroups(buildForecastRows(await response.json())).flatMap(group=>group.rows);for(const row of rows.value)row.draft=row.engines.map(engine=>engine.hours ?? '')}catch(e){if(e.name!=='AbortError')error.value=e.message}finally{loading.value=false}}
async function save(row){if(busy.value || importing.value)return;if(!date.value){error.value='Выберите дату показаний.';return}const values=row.draft.map(value=>value==='' ? null : Number(value));if(values.some(value=>value!=null && (!Number.isFinite(value)||value<0))){error.value='Введите неотрицательные моточасы.';return}if(values.every(value=>value==null)){error.value='Введите показания хотя бы одного ДВС.';return}busy.value=row.vehicle.id;error.value='';message.value='';let saved=0;try{for(let index=0;index<values.length;index++){if(values[index]==null)continue;const response=await fetch(`/api/vehicles/${row.vehicle.id}/hours`,{method:'POST',headers:{...headers(),'Content-Type':'application/json'},body:JSON.stringify({readingDate:date.value,engineHours:values[index],engineNumber:index+1,note:'Ручной ввод'})});if(!response.ok)throw await failure(response,'Не удалось сохранить моточасы.');saved++}message.value=`${vehicleTitle(row.vehicle)}: показания сохранены.`;const drafts=new Map(rows.value.filter(item=>item.vehicle.id!==row.vehicle.id).map(item=>[item.vehicle.id,[...item.draft]]));await load();for(const item of rows.value)if(drafts.has(item.vehicle.id))item.draft=drafts.get(item.vehicle.id)}catch(e){error.value=`${e.message}${saved?' Часть показаний уже сохранена.':''}`}finally{busy.value=''}}
function openImport(){file.value=null;importResult.value=null;modal.value.showModal()}
async function importFile(){if(!file.value || importing.value)return;if(!/\.(csv|xlsx)$/i.test(file.value.name)||file.value.size>5*1024*1024){error.value='Выберите CSV или XLSX до 5 МБ.';return}importing.value=true;error.value='';importResult.value=null;try{const body=new FormData();body.append('file',file.value);body.append('readingDate',date.value);const response=await fetch('/api/vehicles/hours/import',{method:'POST',headers:headers(),body});if(!response.ok)throw await failure(response,'Не удалось импортировать файл.');importResult.value=await response.json();await load()}catch(e){error.value=e.message}finally{importing.value=false}}
function template(){const content='\uFEFFgarage_number;model;engine_1;engine_2\n901;SANY SET 150S;875;852\n94;SANY SY1250H;20511;\n';const url=URL.createObjectURL(new Blob([content],{type:'text/csv;charset=utf-8'}));const link=document.createElement('a');link.href=url;link.download='Шаблон моточасов.csv';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
onMounted(load);onBeforeUnmount(()=>controller?.abort())
</script>
<template>
 <section class="vehicle-hours">
  <div class="hours-toolbar">
   <label>Дата показаний<input v-model="date" type="date" :disabled="!!busy || importing" /></label>
   <label class="hours-search">Поиск техники<input v-model="query" type="search" placeholder="Модель или номер" /></label>
   <button class="secondary-button" :disabled="loading || !!busy || importing" @click="load">Обновить</button>
   <button class="primary-button" :disabled="!!busy || importing" @click="openImport">Импорт Excel / CSV</button>
  </div>
  <p v-if="error" role="alert" class="form-message--error">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
  <p v-if="loading" role="status">Загрузка…</p>
  <div v-else class="hours-list">
   <form v-for="row in matches" :key="row.vehicle.id" class="hours-row" @submit.prevent="save(row)">
    <div class="hours-vehicle"><strong>{{ vehicleTitle(row.vehicle) }}</strong><small>{{ row.vehicle.typeName }}<template v-if="row.legacyHours!=null"> · Прежнее показание без номера ДВС: {{ row.legacyHours }}</template></small></div>
    <label v-for="(engine,index) in row.engines" :key="engine.number">ДВС № {{ engine.number }}<input v-model="row.draft[index]" type="number" min="0" step="0.1" :aria-label="`ДВС № ${engine.number}, ${vehicleTitle(row.vehicle)}`" :disabled="!!busy || importing" placeholder="м/ч" /></label>
    <span v-if="row.engines.length===1" class="hours-placeholder"></span>
    <button type="submit" class="secondary-button" :disabled="!!busy || importing">{{busy===row.vehicle.id?'…':'Сохранить'}}</button>
   </form>
   <p v-if="!matches.length">Техника не найдена.</p>
  </div>
  <dialog ref="modal" class="hours-import" @cancel="importing && $event.preventDefault()">
   <header><h2>Импорт моточасов</h2><button type="button" aria-label="Закрыть импорт" :disabled="importing" @click="modal.close()">×</button></header>
   <p>CSV в UTF-8 или Excel XLSX, до 5 МБ. Дата показаний: {{date.split('-').reverse().join('.')}}.</p>
   <p>Колонки: <b>garage_number, model, engine_1, engine_2</b>. Для одного ДВС последняя колонка пустая. Также поддерживается значение <b>875/852</b> в одной колонке наработки.</p>
   <p>Пустые значения двух колонок не изменяют показания. Техника определяется по гаражному номеру и модели. Ошибочные строки пропускаются.</p>
   <button class="secondary-button" type="button" @click="template">Скачать шаблон CSV</button>
   <label class="hours-file">Файл<input type="file" accept=".csv,.xlsx" :disabled="importing" @change="file=$event.target.files[0] ?? null" /></label>
   <p v-if="error" role="alert" class="form-message--error">{{error}}</p>
   <div v-if="importResult" role="status"><p>Сохранено показаний: {{importResult.imported}}</p><ul><li v-for="(item,index) in importResult.errors" :key="index">Строка {{item.line}}: {{item.message}}</li></ul></div>
   <button class="primary-button" type="button" :disabled="!file || importing || !date" @click="importFile">{{importing?'Импорт…':'Импортировать'}}</button>
  </dialog>
 </section>
</template>
<style scoped>
.vehicle-hours{display:grid;gap:12px}.hours-toolbar{display:flex;gap:10px;flex-wrap:wrap;align-items:end}.hours-toolbar label,.hours-row label{display:grid;gap:4px;font-size:12px}.hours-search{flex:1;min-width:180px}input{box-sizing:border-box;width:100%;min-width:0;padding:8px;border:1px solid var(--border);border-radius:7px;background:var(--bg-main);color:var(--text-main);font:inherit}.hours-list{display:grid;gap:6px}.hours-row{display:grid;grid-template-columns:minmax(0,1fr) 90px 90px 104px;gap:10px;align-items:center;padding:10px 12px;border:1px solid var(--border);border-radius:9px;background:var(--bg-surface)}.hours-vehicle{min-width:0;overflow-wrap:anywhere}.hours-vehicle strong{font-size:13px}.hours-vehicle small{display:block;font-size:11px;color:var(--text-secondary)}.hours-row button{padding:8px;font-size:12px}.hours-import{box-sizing:border-box;width:min(600px,calc(100% - 24px));max-height:90vh;overflow:auto;border:1px solid var(--border);border-radius:14px;padding:20px;background:var(--bg-surface);color:var(--text-main)}.hours-import::backdrop{background:#0008}.hours-import header{display:flex;justify-content:space-between;align-items:center;gap:10px}.hours-import h2{font-size:20px;margin:0}.hours-import header button{background:transparent;border:0;color:inherit;font-size:28px;cursor:pointer}.hours-import p{font-size:13px;line-height:1.6}.hours-file{display:grid;gap:6px;margin:16px 0}@media(max-width:760px){.hours-row{grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.hours-vehicle{grid-column:1/-1}.hours-placeholder{display:none}.hours-row button{grid-column:3}.hours-toolbar{align-items:stretch}.hours-toolbar label{flex:1}.hours-toolbar button{flex:1}}
</style>
