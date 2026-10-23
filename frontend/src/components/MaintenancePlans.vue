<script setup>
import { computed,onMounted,ref } from 'vue'
import MaintenanceDateReadings from './MaintenanceDateReadings.vue'
import FleetActionMenu from './FleetActionMenu.vue'
import { maintenancePresets,stepLabel,hoursText } from '../utils/maintenance'
import { localDate,vehicleTitle } from '../utils/shiftReport'
const props=defineProps({token:{type:String,required:true}})
const rows=ref([]), canManage=ref(false), loading=ref(false),busy=ref(false),error=ref(''),message=ref(''),query=ref('')
const selectedId=ref(''),draft=ref(null),revision=ref(0),initialStep=ref(0),history=ref([])
const completion=ref(null),completionDialog=ref(null),conflict=ref(false)
const graphDialog=ref(null),newGraph=ref(null),graphError=ref('')
const selected=computed(()=>rows.value.find(row=>row.vehicle.id===selectedId.value))
const matches=computed(()=>rows.value.filter(row=>vehicleTitle(row.vehicle).toLocaleLowerCase('ru').includes(query.value.trim().toLocaleLowerCase('ru'))))
const engineCount=computed(()=>selected.value?.vehicle.engineCount || 1)
const clone=value=>JSON.parse(JSON.stringify(value))
const headers=()=>({Authorization:'Bearer '+props.token,'Content-Type':'application/json'})
async function request(url,options={}) {
 const response=await fetch(url,{...options,headers:headers()})
 if(!response.ok){const body=await response.json().catch(()=>null);if(response.status===409)conflict.value=true;throw Error(body?.message || 'Не удалось выполнить действие.')}
 return response.json()
}
async function load(){
 loading.value=true;error.value=''
 try{const data=await request('/api/maintenance-plans');rows.value=data.rows || [];canManage.value=data.canManage===true}
 catch(e){error.value=e.message}finally{loading.value=false}
}
async function selectVehicle(id){
 if(busy.value)return
 selectedId.value=id;error.value='';message.value='';conflict.value=false;history.value=[]
 const row=selected.value;if(!row){draft.value=null;return}
 revision.value=row.revision
 draft.value=row.plan?clone(row.plan):{
  ...clone(maintenancePresets[0]),nextStepIndex:1,lastDate:localDate(new Date()),lastService:'',
  engines:Array.from({length:row.vehicle.engineCount || 1},(_,i)=>({number:i+1,hours:''})),runIns:[]
 }
 initialStep.value=row.plan ? (row.plan.nextStepIndex+row.plan.steps.length-1)%row.plan.steps.length : 0
 try{history.value=await request('/api/maintenance-plans/'+id+'/history')}catch(e){error.value=e.message}
}
function preset(index){
 const value=maintenancePresets[index];draft.value.name=value.name;draft.value.steps=clone(value.steps)
 draft.value.nextStepIndex=0;initialStep.value=0
}
function openNewGraph(){
 if(!selected.value || !canManage.value || busy.value)return
 graphError.value='';newGraph.value={name:'',steps:[{name:'ТО-250',mark:250}]}
 graphDialog.value.showModal()
}
function addGraphStep(){newGraph.value.steps.push({name:'',mark:''})}
function applyNewGraph(){
 graphError.value=''
 let previous=0;const steps=[]
 for(const step of newGraph.value.steps){
  const mark=Number(step.mark),interval=Math.round((mark-previous)*10)/10
  if(!step.name.trim() || !Number.isFinite(mark) || interval<=0 || interval>100000){graphError.value='Укажите вид ТО и возрастающие отметки моточасов от начала цикла.';return}
  steps.push({name:step.name.trim(),interval});previous=mark
 }
 draft.value.name=newGraph.value.name.trim();draft.value.steps=steps;draft.value.nextStepIndex=0;initialStep.value=-1
 graphDialog.value.close();message.value='Новый график подготовлен. Проверьте шаг ТО и нажмите «Сохранить график».'
}
function removeStep(index){
 if(draft.value.steps.length<=1)return
 draft.value.steps.splice(index,1);draft.value.nextStepIndex=Math.min(draft.value.nextStepIndex,draft.value.steps.length-1);initialStep.value=Math.min(initialStep.value,draft.value.steps.length-1)
}
function addRunIn(){
 draft.value.runIns.push({id:crypto.randomUUID(),reason:'Замена ДВС',startDate:localDate(new Date()),interval:150,engines:Array.from({length:engineCount.value},(_,i)=>({number:i+1,hours:''}))})
}
async function save(){
 if(!canManage.value||busy.value)return
 error.value='';message.value=''
 const plan=clone(draft.value)
 if(!selected.value.plan){plan.lastService=Number(initialStep.value)===-1?'Начало эксплуатации':stepLabel(plan.steps,Number(initialStep.value));plan.nextStepIndex=(Number(initialStep.value)+1)%plan.steps.length}
 if(plan.engines.some(e=>e.hours===''||e.hours===null)||plan.runIns.some(r=>r.engines.some(e=>e.hours===''||e.hours===null))){error.value='Дождитесь загрузки моточасов за выбранные даты. Если показаний нет, сначала загрузите их.';return}
 plan.engines.forEach(e=>e.hours=Number(e.hours));plan.runIns.forEach(r=>r.engines.forEach(e=>e.hours=Number(e.hours)))
 plan.steps.forEach(s=>s.interval=Number(s.interval));plan.nextStepIndex=Number(plan.nextStepIndex)
 busy.value=true
 try{
  await request('/api/maintenance-plans/'+selectedId.value,{method:'PUT',body:JSON.stringify({revision:revision.value,plan})})
  const id=selectedId.value;await load();busy.value=false;await selectVehicle(id);message.value='График сохранён. Изменения действуют для следующего ТО.'
 }catch(e){error.value=e.message}finally{busy.value=false}
}
async function reload(){if(busy.value)return;const id=selectedId.value;await load();await selectVehicle(id)}
function openCompletion(){
 const plan=selected.value.plan
 completion.value={date:localDate(new Date()),description:'',runInId:'',
  engines:plan.engines.map(engine=>({number:engine.number,hours:''}))}
 completionDialog.value.showModal()
}
async function complete(){
 if(!canManage.value||busy.value)return
 error.value=''
 if(completion.value.engines.some(e=>e.hours===''||e.hours===null)){error.value='Для расчёта нужны исходные показания каждого ДВС за выбранную или более раннюю дату.';return}
 busy.value=true
 try{
  await request('/api/maintenance-plans/'+selectedId.value+'/complete',{method:'POST',body:JSON.stringify({...completion.value,revision:revision.value,runInId:completion.value.runInId||null,engines:completion.value.engines.map(e=>({...e,hours:Number(e.hours)}))})})
  completionDialog.value.close();busy.value=false;await reload();message.value='Выполнение ТО зарегистрировано.'
 }catch(e){error.value=e.message}finally{busy.value=false}
}
onMounted(load)
</script>
<template>
<section class="maintenance-plans">
 <header><h2>Графики и проведение ТО</h2><p>График закрепляется за отдельной машиной. Отсчёт ведётся от фактических показаний последнего планового ТО.</p></header>
 <div class="maintenance-toolbar">
  <label>Поиск техники<input v-model="query" type="search" placeholder="Модель или гаражный номер" :disabled="busy" /></label>
  <label>Техника<select aria-label="Техника" :value="selectedId" @change="selectVehicle($event.target.value)" :disabled="busy || loading"><option value="">Выберите технику</option><option v-for="row in matches" :key="row.vehicle.id" :value="row.vehicle.id">{{vehicleTitle(row.vehicle)}}{{row.plan?'':' — график не задан'}}</option></select></label>
  <FleetActionMenu label="Действия с графиком ТО" :disabled="busy || loading">
   <button v-if="canManage" type="button" :disabled="!selected" @click="openNewGraph">Создать график</button>
   <button type="button" @click="reload">Обновить</button>
  </FleetActionMenu>
 </div>
 <p v-if="!selected && !loading">Выберите технику, затем откройте меню ☰ → «Создать график» или используйте готовый цикл.</p>
 <p v-if="loading" role="status">Загрузка…</p>
 <p v-if="error" role="alert" class="form-message--error">{{error}}</p>
 <p v-if="conflict">Сохранённый график изменился. Скопируйте нужные изменения, затем нажмите «Обновить».</p>
 <p v-if="message" role="status">{{message}}</p>
 <template v-if="selected && draft">
  <section class="maintenance-summary">
   <h3>{{vehicleTitle(selected.vehicle)}}</h3>
   <p v-if="!selected.plan">Начните с графика и данных последнего ТО.</p>
   <template v-else>
    <p>Последнее плановое ТО: {{selected.plan.lastService}} · {{selected.plan.lastDate.split('-').reverse().join('.')}}</p>
    <ul><li v-for="due in selected.due" :key="due.runInId || 'scheduled'">{{due.name}}: {{due.remaining<0?'просрочено на '+hoursText(-due.remaining):'осталось '+hoursText(due.remaining)}} м/ч · ДВС № {{due.engineNumber}} · план {{hoursText(due.dueHours)}} м/ч</li></ul>
    <p v-if="selected.incomplete">Недостаточно корректных показаний для расчёта всех ТО. Проверьте даты и наработку каждого ДВС.</p>
    <button v-if="canManage" type="button" class="primary-button" :disabled="busy" @click="openCompletion">Зарегистрировать выполнение ТО</button>
   </template>
  </section>
  <form @submit.prevent="save" class="maintenance-form">
   <fieldset :disabled="!canManage || busy">
    <legend>{{selected.plan?'Редактирование графика':'Начальные данные'}}</legend>
    <p v-if="!canManage">Изменять график и регистрировать выполнение могут механики, старшие механики и администраторы.</p>
    <label>Название графика<input v-model.trim="draft.name" maxlength="200" required /></label>
    <div class="maintenance-presets"><span>Заполнить цикл:</span><button v-for="(item,index) in maintenancePresets" :key="item.name" type="button" class="secondary-button" @click="preset(index)">{{item.name}}</button></div>
    <h3 class="maintenance-steps-heading">Виды ТО по порядку цикла</h3>
    <ol class="maintenance-steps">
     <li v-for="(step,index) in draft.steps" :key="index"><span>{{index+1}}</span><label><span class="maintenance-step-label">Вид ТО</span><input v-model.trim="step.name" :aria-label="'Вид ТО, шаг '+(index+1)" required maxlength="100" /></label><button type="button" class="secondary-button" :aria-label="'Удалить шаг '+(index+1)" :disabled="draft.steps.length===1" @click="removeStep(index)">×</button></li>
    </ol>

    <template v-if="!selected.plan">
     <h3>Последнее плановое ТО</h3>
     <label>Дата последнего ТО<input v-model="draft.lastDate" type="date" :max="localDate(new Date())" required /></label>
     <label>Выполненный шаг цикла<select v-model.number="initialStep"><option :value="-1">Начало эксплуатации — перед первым ТО</option><option v-for="(step,index) in draft.steps" :key="index" :value="index">{{stepLabel(draft.steps,index)}}</option></select></label>
     <MaintenanceDateReadings :token="token" :vehicle-id="selectedId" :date="draft.lastDate" v-model="draft.engines" />
    </template>
    <template v-else>
     <label>Следующий шаг нового графика<select v-model.number="draft.nextStepIndex"><option v-for="(step,index) in draft.steps" :key="index" :value="index">{{stepLabel(draft.steps,index)}}</option></select></label>
     <p>Изменения применяются к следующему ТО. Дата и показания последнего выполненного ТО сохраняются; прошлая история не пересчитывается.</p>
    </template>
    <h3>Разовые обкаточные ТО</h3><p>Выполняются отдельно и не сдвигают основной цикл. По умолчанию — через 150 м/ч. При замене одного ДВС оставьте только его показания.</p>
    <section v-for="(item,index) in draft.runIns" :key="item.id" class="maintenance-runin">
     <label>Причина<input v-model.trim="item.reason" maxlength="200" required /></label>
     <label>Дата начала обкатки<input v-model="item.startDate" type="date" :max="localDate(new Date())" required /></label>
     <label>Обкаточный интервал, м/ч<input v-model.number="item.interval" type="number" min="0.1" max="100000" step="0.1" required /></label>
     <div v-for="engine in item.engines" :key="engine.number" class="maintenance-runin-engine"><span>ДВС № {{engine.number}}</span><button v-if="item.engines.length>1" type="button" class="secondary-button" @click="item.engines=item.engines.filter(e=>e.number!==engine.number)">Исключить ДВС № {{engine.number}}</button></div>
     <MaintenanceDateReadings :token="token" :vehicle-id="selectedId" :date="item.startDate" :snapshot="selected.plan?.runIns.find(r=>r.id===item.id && r.startDate===item.startDate)?.engines" v-model="item.engines" />
     <button type="button" class="secondary-button" @click="draft.runIns.splice(index,1)">Убрать разовое ТО</button>
    </section>
    <button type="button" class="secondary-button" :disabled="draft.runIns.length>=10" @click="addRunIn">Добавить обкаточное ТО</button>
    <button v-if="canManage" type="submit" class="primary-button" :disabled="conflict">{{busy?'Сохранение…':'Сохранить график'}}</button>
   </fieldset>
  </form>
  <section class="maintenance-history"><h3>История графика и обслуживания</h3><p v-if="!history.length">Записей пока нет.</p><details v-for="event in history" :key="event.id"><summary>{{event.date.split('-').reverse().join('.')}} · {{event.description}} · версия {{event.revision}}</summary><p>Зафиксировано: {{new Date(event.createdAt).toLocaleString('ru-RU')}}</p><p v-if="event.readings.length">Показания: {{event.readings.map(e=>'ДВС № '+e.number+': '+hoursText(e.hours)+' м/ч'+(e.isEstimated?' (расчётные, исходная дата '+e.sourceDate+')':'')).join('; ')}}</p><p>График: {{event.after.name}}</p><ol><li v-for="(step,index) in event.after.steps" :key="index">{{step.name}} — через {{hoursText(step.interval)}} м/ч</li></ol></details></section>
 </template>
 <dialog ref="graphDialog" class="maintenance-dialog" aria-labelledby="new-graph-title">
  <form v-if="newGraph" @submit.prevent="applyNewGraph">
   <h2 id="new-graph-title">Создать график</h2>
   <p>{{vehicleTitle(selected.vehicle)}}</p>
   <label>Название нового графика<input v-model.trim="newGraph.name" maxlength="200" required /></label>
   <p>Укажите виды ТО и отметки моточасов от начала цикла: например, 350, 700, 1000. После последнего шага цикл повторяется.</p>
   <div v-for="(step,index) in newGraph.steps" :key="index" class="new-graph-step">
    <label>Вид ТО {{index+1}}<input v-model.trim="step.name" maxlength="100" required /></label>
    <label>Отметка цикла, м/ч<input v-model.number="step.mark" type="number" min="0.1" step="0.1" required /></label>
    <button type="button" class="secondary-button" :aria-label="'Удалить новый шаг '+(index+1)" :disabled="newGraph.steps.length===1" @click="newGraph.steps.splice(index,1)">×</button>
   </div>
   <button type="button" class="secondary-button" :disabled="newGraph.steps.length>=100" @click="addGraphStep">Добавить шаг</button>
   <p v-if="graphError" role="alert" class="form-message--error">{{graphError}}</p>
   <p v-if="selected.plan">Новый график заменит текущий после сохранения. История и последнее выполненное ТО сохранятся.</p>
   <div class="maintenance-toolbar"><button type="submit" class="primary-button">Использовать график</button><button type="button" class="secondary-button" @click="graphDialog.close()">Отмена</button></div>
  </form>
 </dialog>
 <dialog ref="completionDialog" class="maintenance-dialog" @cancel="busy && $event.preventDefault()">
  <form v-if="completion" @submit.prevent="complete">
   <h2>Выполнение ТО</h2><p>{{vehicleTitle(selected.vehicle)}}</p>
   <label>Какое ТО выполнено<select v-model="completion.runInId" :disabled="busy"><option value="">Плановое: {{selected.plan.steps[selected.plan.nextStepIndex].name}}</option><option v-for="item in selected.plan.runIns" :key="item.id" :value="item.id">Обкаточное: {{item.reason}}</option></select></label>
   <label>Дата выполнения<input v-model="completion.date" type="date" :max="localDate(new Date())" required :disabled="busy" /></label>
   <MaintenanceDateReadings :token="token" :vehicle-id="selectedId" :date="completion.date" v-model="completion.engines" />
   <label>Описание выполненного ТО<textarea v-model.trim="completion.description" maxlength="5000" rows="4" required :disabled="busy"></textarea></label>
   <p>{{completion.runInId?'Обкаточное ТО не изменит плановую дату и шаг цикла.':'Интервал следующего шага начнётся от загруженных показаний на дату ТО.'}}</p>
   <p v-if="error" role="alert" class="form-message--error">{{error}}</p>
   <div class="maintenance-toolbar"><button type="submit" class="primary-button" :disabled="busy || conflict">{{busy?'Сохранение…':'Сохранить выполнение'}}</button><button type="button" class="secondary-button" :disabled="busy" @click="completionDialog.close()">Отмена</button></div>
  </form>
 </dialog>
</section>
</template>
<style scoped>
.maintenance-plans{display:grid;gap:16px}.maintenance-toolbar,.maintenance-presets{display:flex;flex-wrap:wrap;align-items:end;gap:12px}.maintenance-toolbar>label{flex:1;min-width:200px}
.maintenance-plans label{display:grid;gap:6px;font-size:13px}.maintenance-plans input,.maintenance-plans select,.maintenance-plans textarea{width:100%;min-width:0;padding:10px;border:1px solid var(--border);border-radius:8px;color:var(--text-main);background:var(--bg-main);font:inherit}
.maintenance-plans button{padding:10px 14px;min-height:44px;height:auto}.maintenance-summary,.maintenance-form,.maintenance-history{padding:18px;border:1px solid var(--border);border-radius:12px;background:var(--bg-surface)}
.maintenance-form fieldset{display:grid;gap:16px;margin:0;padding:0;border:0;min-width:0}.maintenance-form legend{font-weight:700;margin-bottom:16px}.maintenance-steps{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(185px,1fr));gap:8px}.maintenance-steps li{display:grid;grid-template-columns:20px minmax(0,1fr) 32px;gap:6px;align-items:end}.maintenance-steps li>span{align-self:center}
.maintenance-steps-heading{margin:0;font-size:14px}.maintenance-step-label{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}.maintenance-steps input{padding:7px 8px}.maintenance-steps button{padding:4px;min-height:36px}.new-graph-step{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr) 36px;gap:8px;align-items:end}.new-graph-step button{padding:4px}
.maintenance-runin{display:grid;gap:12px;padding:14px;border:1px solid var(--border);border-radius:10px}.maintenance-runin-engine{display:flex;gap:10px;align-items:end}.maintenance-runin-engine>label{flex:1}.maintenance-history details{padding:12px 0;border-bottom:1px solid var(--border)}.maintenance-history summary{cursor:pointer;line-height:1.6}
.maintenance-dialog{width:min(620px,calc(100vw - 24px));max-height:90dvh;overflow:auto;padding:24px;border:1px solid var(--border);border-radius:14px;background:var(--bg-main);color:var(--text-main)}.maintenance-dialog::backdrop{background:#0008}.maintenance-dialog form{display:grid;gap:14px}
@media(max-width:600px){.maintenance-toolbar>label{min-width:100%}.maintenance-summary,.maintenance-form,.maintenance-history{padding:12px}.maintenance-steps li{grid-template-columns:16px minmax(0,1fr) 36px;gap:4px}.maintenance-steps button{padding:6px}.maintenance-runin-engine{flex-wrap:wrap}}
</style>

