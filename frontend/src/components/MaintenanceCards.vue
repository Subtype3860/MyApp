<script setup>
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { localDate } from '../utils/shiftReport'
import MaintenanceDateReadings from './MaintenanceDateReadings.vue'
const props=defineProps({token:String,vehicle:Object,plan:Object,planRevision:Number})
const emit=defineEmits(['completed','materials'])
const templates=ref([]),cards=ref([]),card=ref(null),data=ref(null),photos=ref([]),readings=ref([])
const canManage=ref(false),canFill=ref(false),busy=ref(false),error=ref(''),message=ref(''),filter=ref(''),onlyPending=ref(false)
const selectedTemplate=ref(''),runInId=ref(''),editor=ref(null),showTemplates=ref(false),reason=ref(''),batchExecutor=ref(''),recovery=ref(null)
const requirement=ref(null)
let timer, generation=0, editGeneration=0
const clone=v=>JSON.parse(JSON.stringify(v))
const newId=()=>crypto.randomUUID()
const statusName={draft:'Черновик',submitted:'На проверке',accepted:'Принята',cancelled:'Отменена'}
const answerName={'':'Не заполнено',done:'Выполнено','not-done':'Не выполнено',na:'Не применимо'}
const normalize=v=>(v||'').replace(/[^\p{L}\p{N}]/gu,'').toUpperCase()
const service=computed(()=>runInId.value?'ТО-'+props.plan?.runIns.find(r=>r.id===runInId.value)?.interval:props.plan?.steps[props.plan.nextStepIndex]?.name)
const matching=computed(()=>templates.value.filter(t=>t.published && normalize(t.template.model)===normalize(props.vehicle.modelName) && normalize(t.template.service)===normalize(service.value)))
const editable=computed(()=>card.value?.status==='draft' && canFill.value)
const progress=computed(()=>data.value?.answers.filter(a=>a.status).length||0)
const groups=computed(()=>[...new Set(card.value?.template.operations.map(o=>o.section)||[])])
const localKey=()=>{let user='';try{user=JSON.parse(atob(props.token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/'))).sub||JSON.parse(atob(props.token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')))['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']||''}catch{};return 'atlas.to-card.'+user+'.'+card.value?.id}
async function request(path,options={}){
 const response=await fetch('/api/maintenance-plans/'+path,{...options,headers:{Authorization:'Bearer '+props.token,...(!(options.body instanceof FormData)?{'Content-Type':'application/json'}:{}),...options.headers}})
 const result=await response.json();if(!response.ok)throw Error(result.message||'Не удалось выполнить действие. Проверьте соединение.');return result
}
async function load(){
 const current=++generation; error.value=''
 try{const [library,items]=await Promise.all([request('card-templates'),request(props.vehicle.id+'/cards')]);if(current!==generation)return
  templates.value=library.templates||[];canManage.value=library.canManage;canFill.value=library.canFill;cards.value=Array.isArray(items)?items:[]
 }catch(e){if(current===generation)error.value=e.message}
}
function keepLocal(){try{localStorage.setItem(localKey(),JSON.stringify({revision:card.value.revision,data:data.value}))}catch{error.value='Не удалось сохранить локальную копию. Сохраните карту на сервер.'}}
function changed(){
 editGeneration++;keepLocal();message.value='Черновик сохранён на устройстве';clearTimeout(timer)
 timer=setTimeout(()=>{if(!busy.value&&!recovery.value)save('save')},1200)
}
async function open(item){
 clearTimeout(timer);card.value=clone(item);data.value=clone(item.data);error.value='';message.value='';reason.value='';recovery.value=null;photos.value=[];requirement.value=null
 readings.value=Array.from({length:props.vehicle.engineCount||1},(_,i)=>({number:i+1,hours:''}))
 try{const saved=JSON.parse(localStorage.getItem(localKey())||'null');if(saved && item.status==='draft' && JSON.stringify(saved.data)!==JSON.stringify(item.data))recovery.value=saved}catch{}
 try{const result=await request('cards/'+item.id+'/photos');if(card.value?.id===item.id)photos.value=result}catch(e){error.value=e.message}
}
function restore(){data.value=clone(recovery.value.data);recovery.value=null;changed()}
function discard(){localStorage.removeItem(localKey());recovery.value=null}
async function previewRequirement(){try{requirement.value=await request('cards/'+card.value.id+'/requirement-preview')}catch(e){error.value=e.message}}
function sendRequirement(){
 const rows=requirement.value.filter(m=>m.matched&&m.availableQuantity>0).map(m=>({Наименование:m.materialName,'Ед.изм.':m.unit,Количество:m.availableQuantity,__sourceTable:'v_full_ost',__availableQuantity:m.availableQuantity,__requestedQuantity:m.quantity,__quantity:Math.min(m.quantity,m.availableQuantity),__remainingQuantity:Math.max(m.availableQuantity-m.quantity,0),__maintenanceName:card.value.template.service}))
 emit('materials',rows)
}
async function start(){
 if(busy.value)return;busy.value=true;error.value=''
 try{const t=matching.value.find(t=>t.id===selectedTemplate.value)||matching.value[0];if(!t)throw Error('Сначала утвердите подходящий шаблон.')
  const item=await request(props.vehicle.id+'/cards',{method:'POST',body:JSON.stringify({id:crypto.randomUUID(),templateId:t.id,templateRevision:t.revision,planRevision:props.planRevision,runInId:runInId.value||null})});await load();await open(item)
 }catch(e){error.value=e.message}finally{busy.value=false}
}
async function save(action='save'){
 if(busy.value||!editable.value||recovery.value)return false
 clearTimeout(timer);busy.value=true;error.value='';const version=editGeneration;const id=card.value.id;const payload=clone(data.value)
 try{const item=await request('cards/'+id,{method:'PUT',body:JSON.stringify({revision:card.value.revision,data:payload,action,reason:reason.value})});card.value=item
  cards.value=cards.value.map(c=>c.id===id?item:c)
  if(version===editGeneration){data.value=clone(item.data);localStorage.removeItem(localKey());message.value=action==='submit'?'Карта передана на проверку':'Сохранено на сервере'}else{keepLocal();timer=setTimeout(()=>save(),1200)}
  return true
 }catch(e){error.value=e.message;keepLocal();return false}finally{busy.value=false}
}
async function decide(action){
 if(busy.value)return;busy.value=true;error.value=''
 try{await request('cards/'+card.value.id+'/'+action,{method:'POST',body:JSON.stringify({revision:card.value.revision,reason:reason.value})});const id=card.value.id;await load();await open(cards.value.find(c=>c.id===id));if(action==='accept')emit('completed')}
 catch(e){error.value=e.message}finally{busy.value=false}
}
function operations(section){return card.value.template.operations.filter(o=>o.section===section && (!filter.value||o.text.toLowerCase().includes(filter.value.toLowerCase())) && (!onlyPending.value || !answer(o.id).status || answer(o.id).status==='not-done'))}
const answer=id=>data.value.answers.find(a=>a.id===id)
const consumption=id=>data.value.materials.find(m=>m.id===id)
function setExecutor(){for(const a of data.value.answers)if(!a.executor)a.executor=batchExecutor.value;for(const m of data.value.materials)if(!m.executor)m.executor=batchExecutor.value;changed()}
const timeValue=value=>value?new Date(new Date(value).getTime()-new Date(value).getTimezoneOffset()*60000).toISOString().slice(0,16):''
function setTime(key,value){data.value[key]=value?new Date(value).toISOString():null;changed()}
function normalizeNumbers(){for(const m of data.value.materials)if(m.actual==='')m.actual=null;for(const a of data.value.answers)if(a.measurement==='')a.measurement=null;if(data.value.mileage==='')data.value.mileage=null;changed()}
async function addPhoto(event,id){
 const file=event.target.files[0];if(!file)return
 if(!await save())return
 busy.value=true;error.value=''
 try{const form=new FormData();form.append('file',file);form.append('operationId',id);await request('cards/'+card.value.id+'/photos/'+crypto.randomUUID(),{method:'POST',body:form});const key=card.value.id;await load();await open(cards.value.find(c=>c.id===key))}catch(e){error.value=e.message}finally{busy.value=false;event.target.value=''}
}
async function viewPhoto(photo){
 try{const response=await fetch('/api/maintenance-plans/cards/'+card.value.id+'/photos/'+photo.id,{headers:{Authorization:'Bearer '+props.token}});if(!response.ok)throw Error('Не удалось загрузить фото.');const url=URL.createObjectURL(await response.blob());const a=document.createElement('a');a.href=url;a.download='ТО-'+photo.id+'.jpg';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}catch(e){error.value=e.message}
}
async function createDefect(id){
 if(!await save())return;busy.value=true;error.value=''
 try{await request('cards/'+card.value.id+'/defects/'+encodeURIComponent(id),{method:'POST'});const key=card.value.id;await load();await open(cards.value.find(c=>c.id===key))}catch(e){error.value=e.message}finally{busy.value=false}
}
function editTemplate(t){editor.value=t?clone(t):{id:crypto.randomUUID(),revision:0,published:false,template:{model:props.vehicle.modelName,service:service.value,source:'Создано в приложении',reviewNotes:'',operations:[],materials:[]}}}
async function saveTemplate(published){
 busy.value=true;error.value=''
 try{const t=editor.value;for(const m of t.template.materials)if(m.quantity==='')m.quantity=null;await request('card-templates/'+t.id,{method:'PUT',body:JSON.stringify({revision:t.revision,published,template:t.template})});editor.value=null;await load();message.value=published?'Шаблон утверждён':'Шаблон сохранён'}catch(e){error.value=e.message}finally{busy.value=false}
}
const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))
function print(){
 const w=window.open('','_blank');if(!w){error.value='Разрешите открытие окна печати.';return}
 const rows=card.value.template.operations.map(o=>{const a=answer(o.id);return '<tr>'+[o.section,o.text,answerName[a.status],a.condition==='defect'?'Дефект: '+a.note:a.note,a.reason,a.executor,a.measurement==null?'':a.measurement+' '+a.unit].map(v=>'<td>'+escape(v)+'</td>').join('')+'</tr>'}).join('')
 const materials=card.value.template.materials.map(m=>{const c=consumption(m.id);return '<tr>'+[m.assembly,m.name,m.partNumber,m.quantity,m.unit,c.actual,c.substitute,c.executor].map(v=>'<td>'+escape(v)+'</td>').join('')+'</tr>'}).join('')
 w.document.write('<!doctype html><html lang="ru"><meta charset="utf-8"><title>Карта ТО</title><style>body{font:12px Arial}table{border-collapse:collapse;width:100%}td,th{border:1px solid #888;padding:5px;text-align:left}tr{break-inside:avoid}thead{display:table-header-group}@page{size:A4 landscape;margin:12mm}</style><h1>'+escape(card.value.template.service)+' · '+escape(card.value.vehicleLabel)+'</h1><p>'+escape(data.value.date)+' · '+escape(statusName[card.value.status])+' · Версия шаблона '+card.value.templateRevision+'</p><p>Водитель: '+escape(data.value.driver)+' · ДВС: '+escape(data.value.engineModel)+' · Пробег: '+escape(data.value.mileage)+'</p><p>'+escape(readings.value.map(e=>'ДВС '+e.number+': '+e.hours+' м/ч'+(e.isEstimated?' (расчётные)':'')).join('; '))+'</p><p>Остановка: '+escape(data.value.stoppedAt)+'; начало: '+escape(data.value.startedAt)+'; окончание: '+escape(data.value.finishedAt)+'; возврат: '+escape(data.value.releasedAt)+'</p><table><thead><tr>'+['Раздел','Работа','Выполнение','Результат','Причина','Исполнитель','Замер'].map(v=>'<th>'+v+'</th>').join('')+'</tr></thead><tbody>'+rows+'</tbody></table><h2>Материалы</h2><table><thead><tr>'+['Агрегат','Материал','Артикул','Норма','Ед.','Факт','Аналог','Исполнитель'].map(v=>'<th>'+v+'</th>').join('')+'</tr></thead><tbody>'+materials+'</tbody></table><p>Замечания: '+escape(data.value.notes)+'</p><p>Приёмка: '+escape(card.value.acceptedBy)+' '+escape(card.value.acceptedAt)+'</p><p>Исполнители: ____________________ Механик: ____________________</p></html>');w.document.close();w.focus();w.print()
}
watch(()=>props.vehicle.id,()=>{clearTimeout(timer);card.value=null;data.value=null;editor.value=null;runInId.value='';load()},{immediate:true})
onBeforeUnmount(()=>{clearTimeout(timer);generation++})
</script>

<template>
 <section class="to-cards">
  <header><h3>Карты ТО</h3><div class="actions"><button type="button" @click="showTemplates=!showTemplates">Библиотека карт</button><button type="button" :disabled="busy" @click="load">Обновить список карт</button></div></header>
  <p v-if="error" role="alert" class="form-message--error">{{error}}</p><p v-if="message" role="status">{{message}}</p>
  <div v-if="plan && canFill" class="actions">
   <label>Обслуживание для карты<select v-model="runInId"><option value="">{{plan.steps[plan.nextStepIndex].name}} · плановое</option><option v-for="r in plan.runIns" :key="r.id" :value="r.id">Обкатка: {{r.reason}}</option></select></label>
   <label v-if="matching.length>1">Шаблон<select v-model="selectedTemplate"><option v-for="t in matching" :value="t.id" :key="t.id">{{t.template.source}} · v{{t.revision}}</option></select></label>
   <button type="button" class="primary-button" :disabled="busy || !matching.length" @click="start">Начать ТО по карте</button>
   <p v-if="!matching.length">Нет утверждённой карты для {{vehicle.modelName}} / {{service}}. Проверьте шаблон в библиотеке.</p>
  </div>
  <p v-if="!cards.length">Заполненных карт пока нет.</p>
  <div class="card-list"><button v-for="item in cards" :key="item.id" type="button" :disabled="busy" @click="open(item)">{{item.data.date}} · {{item.template.service}} · {{statusName[item.status]}}</button></div>
  <details v-if="showTemplates" open class="library"><summary>Библиотека шаблонов</summary>
   <p>Исходные карты загружены как черновики. Перед утверждением проверьте состав работ, нормы, единицы и применимость к модели.</p>
   <button v-if="canManage" type="button" @click="editTemplate(null)">Создать шаблон</button>
   <div v-for="t in templates" :key="t.id" class="template-row"><span>{{t.template.model}} · {{t.template.service}} · {{t.published?'Утверждён':'Черновик'}} · v{{t.revision}}</span><button v-if="canManage" type="button" @click="editTemplate(t)">Редактировать {{t.template.model}} {{t.template.service}}</button></div>
  </details>
  <form v-if="editor" class="template-editor" @submit.prevent="saveTemplate(false)">
   <h4>Редактор шаблона</h4><p>{{editor.template.source}}</p><p>{{editor.template.reviewNotes}}</p>
   <fieldset :disabled="busy"><label>Модель техники<input v-model.trim="editor.template.model" required /></label><label>Вид ТО шаблона<input v-model.trim="editor.template.service" required /></label>
    <label>Замечания к регламенту<textarea v-model="editor.template.reviewNotes" /></label>
    <details open><summary>Операции ({{editor.template.operations.length}})</summary>
     <article v-for="(o,i) in editor.template.operations" :key="o.id"><div class="grid"><label>Раздел<input v-model="o.section" required /></label><label>Агрегат / ДВС<input v-model="o.target" /></label></div><label>Операция<textarea v-model="o.text" required /></label><label class="check"><input v-model="o.control" type="checkbox" /> Обязательный заключительный контроль</label><button type="button" @click="editor.template.operations.splice(i,1)">Удалить операцию</button></article>
     <button type="button" @click="editor.template.operations.push({id:newId(),section:'ДВС',text:'',target:'',control:false})">Добавить операцию</button>
    </details>
    <details><summary>Материалы ({{editor.template.materials.length}})</summary><article v-for="(m,i) in editor.template.materials" :key="m.id"><div class="grid"><label>Агрегат<input v-model="m.assembly" /></label><label>Материал<input v-model="m.name" required /></label><label>Каталожный номер<input v-model="m.partNumber" /></label><label>Единица<input v-model="m.unit" required /></label><label>Норма<input v-model.number="m.quantity" type="number" min="0" step="any" /></label><label>Условие / примечание<input v-model="m.note" /></label></div><button type="button" @click="editor.template.materials.splice(i,1)">Удалить материал</button></article><button type="button" @click="editor.template.materials.push({id:newId(),assembly:'',name:'',partNumber:'',unit:'',quantity:null,note:''})">Добавить материал</button></details>
    <div class="actions"><button type="submit">Сохранить черновик шаблона</button><button type="button" class="primary-button" @click="saveTemplate(true)">Утвердить проверенный шаблон</button><button type="button" @click="editor=null">Закрыть редактор</button></div>
   </fieldset>
  </form>
  <section v-if="card && data" class="card-editor">
   <header><h4>{{card.template.service}} · {{statusName[card.status]}}</h4><p>{{card.vehicleLabel}} · версия шаблона {{card.templateRevision}}</p><button type="button" @click="print">Печать / PDF</button></header>
   <aside v-if="recovery"><p>На устройстве есть несохранённые изменения (версия {{recovery.revision}}; на сервере {{card.revision}}). Проверьте их перед отправкой.</p><details><summary>Посмотреть локальные изменения</summary><pre>{{JSON.stringify(recovery.data,null,2)}}</pre></details><button type="button" :disabled="recovery.revision!==card.revision" @click="restore">Восстановить черновик</button><button type="button" @click="discard">Оставить серверную версию</button></aside>
   <fieldset :disabled="!editable || busy || !!recovery" @input="changed" @change="normalizeNumbers">
    <div class="grid"><label>Дата ТО<input v-model="data.date" type="date" :max="localDate(new Date())" /></label><label>Водитель / машинист<input v-model="data.driver" /></label><label>Марка ДВС<input v-model="data.engineModel" /></label><label>Пробег, км<input v-model.number="data.mileage" type="number" min="0" /></label></div>
    <div class="grid"><label v-for="(label,key) in {stoppedAt:'Остановка техники',startedAt:'Начало работ',finishedAt:'Окончание работ',releasedAt:'Возврат в работу'}" :key="key">{{label}}<input type="datetime-local" :value="timeValue(data[key])" @change="setTime(key,$event.target.value)" /></label></div>
    <p v-if="data.startedAt&&data.finishedAt">Длительность работ: {{((new Date(data.finishedAt)-new Date(data.startedAt))/3600000).toFixed(1)}} ч.</p><p v-if="data.stoppedAt&&data.releasedAt">Простой: {{((new Date(data.releasedAt)-new Date(data.stoppedAt))/3600000).toFixed(1)}} ч.</p>
    <MaintenanceDateReadings :key="card.id+card.status" :token="token" :vehicle-id="vehicle.id" :date="data.date" :snapshot="card.status==='accepted'?card.readings:undefined" v-model="readings" />
    <div class="actions"><label>Исполнитель для незаполненных строк<input v-model="batchExecutor" /></label><button type="button" @click="setExecutor">Назначить исполнителя</button></div>
   </fieldset>
   <div class="actions"><strong>Заполнено {{progress}} из {{data.answers.length}}</strong><label>Найти работу<input v-model="filter" type="search" /></label><label class="check"><input v-model="onlyPending" type="checkbox" /> Только пропуски и невыполненные</label></div>
   <details v-for="section in groups" :key="section" open><summary>{{section}}</summary>
    <article v-for="o in operations(section)" :key="o.id" class="operation"><p><strong>{{o.text}}</strong><span v-if="o.target"> · {{o.target}}</span><small v-if="o.control"> · Контроль перед завершением</small></p>
     <fieldset :disabled="!editable || busy || !!recovery" @input="changed" @change="normalizeNumbers"><div class="grid"><label>Выполнение<select v-model="answer(o.id).status"><option v-for="(label,value) in answerName" :key="value" :value="value">{{label}}</option></select></label><label>Состояние узла<select v-model="answer(o.id).condition"><option value="">Не указано</option><option value="ok">Исправен</option><option value="defect">Обнаружен дефект</option></select></label><label>Исполнитель<input v-model="answer(o.id).executor" /></label></div>
      <label v-if="['not-done','na'].includes(answer(o.id).status)">Причина<input v-model="answer(o.id).reason" /></label><label>Замечание<textarea v-model="answer(o.id).note" rows="2" /></label><div class="grid"><label>Замер<input v-model.number="answer(o.id).measurement" type="number" step="any" /></label><label>Единица замера<input v-model="answer(o.id).unit" /></label></div>
     </fieldset>
     <div class="actions"><label v-if="editable && !busy">Фото JPEG/PNG до 4 МБ<input type="file" accept="image/jpeg,image/png" @change="addPhoto($event,o.id)" /></label><button v-for="photo in photos.filter(p=>p.operationId===o.id)" :key="photo.id" type="button" @click="viewPhoto(photo)">Скачать фото</button><span v-if="card.defects[o.id]">Дефект зарегистрирован: {{card.defects[o.id]}}</span><button v-else-if="editable && answer(o.id).condition==='defect'" type="button" :disabled="busy" @click="createDefect(o.id)">Создать заявку без остановки техники</button></div>
    </article>
   </details>
   <button v-if="canFill" type="button" @click="previewRequirement">Подготовить требование по нормам</button>
   <section v-if="requirement"><h4>Материалы для требования</h4><p>Сопоставление по точному наименованию и единице. Подтвердите доступные позиции; остальные выберите на складе вручную. Фактический расход карты не меняется.</p><p v-for="m in requirement" :key="m.materialName+m.unit">{{m.materialName}}: требуется {{m.quantity}} {{m.unit}}, доступно {{m.availableQuantity}}. {{m.matched?'':'Нет однозначного совпадения со складом.'}}</p><button type="button" :disabled="!requirement.some(m=>m.matched&&m.availableQuantity>0)" @click="sendRequirement">Заменить выбранные компоненты доступными материалами</button></section>
   <details open><summary>Материалы: норма и факт</summary><article v-for="m in card.template.materials" :key="m.id"><strong>{{m.assembly}} · {{m.name}}</strong><p>{{m.partNumber}} · Норма: {{m.quantity??'не задана'}} {{m.unit}}. {{m.note}}</p><fieldset :disabled="!editable||busy||!!recovery" @input="changed" @change="normalizeNumbers"><div class="grid"><label>Факт, {{m.unit}}<input v-model.number="consumption(m.id).actual" type="number" min="0" step="any" /></label><label>Использованный аналог<input v-model="consumption(m.id).substitute" /></label><label>Исполнитель<input v-model="consumption(m.id).executor" /></label></div></fieldset></article></details>
   <fieldset :disabled="!editable||busy||!!recovery" @input="changed"><label>Общие замечания<textarea v-model="data.notes" rows="3" /></label></fieldset>
   <label v-if="editable || (canManage&&card.status==='submitted')">Решение механика / причина возврата или отмены<textarea v-model="reason" rows="2" /></label>
   <div class="actions"><template v-if="editable"><button type="button" :disabled="busy||!!recovery" @click="save()">Сохранить карту</button><button type="button" class="primary-button" :disabled="busy||!!recovery" @click="save('submit')">Передать на проверку</button><button type="button" :disabled="busy||!reason||!!recovery" @click="save('cancel')">Отменить карту</button></template><template v-if="canManage&&card.status==='submitted'"><button type="button" class="primary-button" :disabled="busy" @click="decide('accept')">Принять карту и завершить ТО</button><button type="button" :disabled="busy||!reason" @click="decide('return')">Вернуть на доработку</button></template></div>
   <p v-if="card.status==='accepted'">Карта принята {{new Date(card.acceptedAt).toLocaleString('ru-RU')}}. График обновлён. Сохранённые результаты доступны только для чтения.</p>
   <details><summary>История изменений ({{card.audit.length}})</summary><article v-for="a in [...card.audit].reverse()" :key="a.revision"><strong>Версия {{a.revision}} · {{a.action}}</strong><p>{{new Date(a.at).toLocaleString('ru-RU')}} · {{a.actor}} · {{a.reason}}</p><details><summary>Данные на момент изменения</summary><pre>{{JSON.stringify(a.data,null,2)}}</pre></details></article></details>
  </section>
 </section>
</template>
<style scoped>
.to-cards{display:grid;gap:16px;border:1px solid var(--border);border-radius:12px;padding:18px;min-width:0}.actions,.card-list,.template-row{display:flex;flex-wrap:wrap;gap:10px;align-items:center}.template-row{justify-content:space-between;padding:8px 0;border-bottom:1px solid var(--border)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}label{display:grid;gap:5px;font-size:13px}input,select,textarea{box-sizing:border-box;width:100%;min-width:0;padding:9px;border:1px solid var(--border);border-radius:8px;background:var(--bg-main);color:var(--text-main);font:inherit}fieldset{border:0;padding:0;min-width:0;display:grid;gap:12px;margin:12px 0}button{min-height:44px;padding:8px 12px;border:1px solid var(--border);border-radius:8px;background:var(--bg-surface);color:var(--text-main);cursor:pointer}button:disabled{opacity:.55;cursor:default}article{padding:12px 0;border-bottom:1px solid var(--border)}summary{cursor:pointer;padding:12px 0;font-weight:600}.check{display:flex;align-items:center}.check input{width:auto}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px;max-height:350px;overflow:auto}aside{padding:14px;background:var(--bg-surface);border:1px solid var(--border)}.card-editor,.template-editor{min-width:0}h4{font-size:18px}p{overflow-wrap:anywhere}@media(max-width:600px){.to-cards{padding:10px}.grid{grid-template-columns:1fr}.actions>label{width:100%}}
</style>
