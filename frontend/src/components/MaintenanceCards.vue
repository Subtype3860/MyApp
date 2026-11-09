<script setup>
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { localDate } from '../utils/shiftReport'
import { convertImageToWebp } from '../utils/mediaUpload'
import MaintenanceDateReadings from './MaintenanceDateReadings.vue'
import MaintenanceOperationRow from './MaintenanceOperationRow.vue'
const props=defineProps({token:String,vehicle:Object,plan:Object,planRevision:Number})
const emit=defineEmits(['completed','materials'])
const canUseLibrary=ref(false)
const templates=ref([]),cards=ref([]),card=ref(null),data=ref(null),photos=ref([]),readings=ref([])
const canManage=ref(false),canFill=ref(false),busy=ref(false),saving=ref(false),error=ref(''),message=ref(''),filter=ref(''),onlyPending=ref(false)
const selectedTemplate=ref(''),runInId=ref(''),reason=ref(''),batchExecutor=ref(''),recovery=ref(null)
const requirement=ref(null), currentExecutor=ref(''), mediaPreview=ref(null), mediaDialog=ref(null)
let previewGeneration=0
let timer, generation=0, editGeneration=0
let editingTime=false
const invalidTimes=new Set()
const clone=v=>JSON.parse(JSON.stringify(v))
const newId=()=>crypto.randomUUID()
const statusName={draft:'Черновик',submitted:'На проверке',accepted:'Принята',cancelled:'Отменена'}
const normalize=v=>(v||'').replace(/[^\p{L}\p{N}]/gu,'').toUpperCase()
const service=computed(()=>runInId.value?'ТО-'+props.plan?.runIns.find(r=>r.id===runInId.value)?.interval:props.plan?.steps[props.plan.nextStepIndex]?.name)
const matching=computed(()=>templates.value.filter(t=>t.published && normalize(t.template.model)===normalize(props.vehicle.modelName) && normalize(t.template.service)===normalize(service.value)))
const editable=computed(()=>card.value?.status==='draft' && canFill.value)
const progress=computed(()=>data.value?.answers.filter(a=>a.status).length||0)
/** Наработка в бланке: каждое показание в поле из 5 символов, двигатели через « / ». */
const readingsEstimated=computed(()=>(readings.value||[]).some(engine=>engine.isEstimated))
const readingsText=computed(()=>{
 const engines=readings.value||[]
 if(!engines.length)return ''
 return engines.map(engine=>String(engine.hours??'').padStart(5,' ')).join(' / ')+(readingsEstimated.value?'*':'')
})
const groups=computed(()=>[...new Set(card.value?.template.operations.map(o=>o.section)||[])])
const localKey=()=>{let user='';try{user=JSON.parse(atob(props.token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/'))).sub||JSON.parse(atob(props.token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')))['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']||''}catch{};return 'atlas.to-card.'+user+'.'+card.value?.id}
async function request(path,options={}){
 const response=await fetch('/api/maintenance-plans/'+path,{...options,headers:{Authorization:'Bearer '+props.token,...(!(options.body instanceof FormData)?{'Content-Type':'application/json'}:{}),...options.headers},signal:options.signal??AbortSignal.timeout(60000)})
 const result=await response.json().catch(()=>null)
 if(!response.ok)throw Error(result?.message||'Сервер не выполнил действие. Проверьте связь и повторите.')
 return result
}
async function load(){
 const current=++generation; error.value=''
 try{const [library,items]=await Promise.all([request('card-template-options'),request(props.vehicle.id+'/cards')]);if(current!==generation)return
  templates.value=library.templates||[];canManage.value=library.canManage;canFill.value=library.canFill;canUseLibrary.value=!!library.canUseLibrary;cards.value=Array.isArray(items)?items:[]
 }catch(e){if(current===generation)error.value=e.message}
}
function keepLocal(){try{localStorage.setItem(localKey(),JSON.stringify({revision:card.value.revision,data:data.value}))}catch{error.value='Не удалось сохранить локальную копию. Сохраните карту на сервер.'}}
function changed(){
 editGeneration++;keepLocal();message.value='Черновик сохранён на устройстве';clearTimeout(timer)
 timer=setTimeout(()=>{if(!busy.value&&!recovery.value&&!editingTime&&!invalidTimes.size)save('save')},1200)
}
async function open(item){
 clearTimeout(timer);editingTime=false;invalidTimes.clear();card.value=clone(item);data.value=clone(item.data);error.value='';message.value='';reason.value='';recovery.value=null;photos.value=[];requirement.value=null
 readings.value=Array.from({length:props.vehicle.engineCount||1},(_,i)=>({number:i+1,hours:''}))
 try{const saved=JSON.parse(localStorage.getItem(localKey())||'null');if(saved && item.status==='draft' && JSON.stringify(saved.data)!==JSON.stringify(item.data))recovery.value=saved}catch{}
 try{const result=await request('cards/'+item.id+'/photos');if(card.value?.id===item.id)photos.value=result}catch(e){error.value=e.message}
}
function restore(){data.value=clone(recovery.value.data);recovery.value=null;changed()}
function discard(){localStorage.removeItem(localKey());recovery.value=null}
async function refreshMaterials(){
 if(!await save())return
 busy.value=true;error.value=''
 try{const item=await request('cards/'+card.value.id+'/refresh-materials',{method:'POST',body:JSON.stringify({revision:card.value.revision})});cards.value=cards.value.map(c=>c.id===item.id?item:c);await open(item);message.value='Материалы загружены из технического обслуживания.'}catch(e){error.value=e.message}finally{busy.value=false}
}
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
 if(saving.value){timer=setTimeout(()=>save(action),500);return false}
 if(invalidTimes.size){error.value='Введите дату и время полностью.';return false}
 clearTimeout(timer);const auto=action==='save';if(auto)saving.value=true;else busy.value=true;error.value='';const version=editGeneration;const id=card.value.id;const payload=clone(data.value)
 try{const item=await request('cards/'+id,{method:'PUT',body:JSON.stringify({revision:card.value.revision,data:payload,action,reason:reason.value})});card.value=item
  cards.value=cards.value.map(c=>c.id===id?item:c)
  if(version===editGeneration){
   // Автосохранение не подменяет поля формы: иначе ввод даты и времени сбрасывается.
   if(!auto)data.value=clone(item.data)
   localStorage.removeItem(localKey());message.value=action==='submit'?'Карта передана на проверку':'Сохранено на сервере'
  }else{keepLocal();timer=setTimeout(()=>save(),1200)}
  return true
 }catch(e){error.value=e.message;keepLocal();return false}finally{busy.value=false;saving.value=false}
}
async function decide(action){
 if(busy.value)return;busy.value=true;error.value=''
 try{await request('cards/'+card.value.id+'/'+action,{method:'POST',body:JSON.stringify({revision:card.value.revision,reason:reason.value})});const id=card.value.id;await load();await open(cards.value.find(c=>c.id===id));if(action==='accept')emit('completed')}
 catch(e){error.value=e.message}finally{busy.value=false}
}
/** Догружает историю изменений карты по требованию: в списке она не передаётся. */
async function loadAudit(){
 if(!card.value) return
 busy.value=true;error.value=''
 try{
  const full=await request('cards/'+card.value.id)
  if(card.value && full?.id===card.value.id){card.value.audit=full.audit??[];card.value.auditCount=card.value.audit.length}
 }catch(e){error.value=e.message}finally{busy.value=false}
}
function operations(section){return card.value.template.operations.filter(o=>o.section===section && (!filter.value||o.text.toLowerCase().includes(filter.value.toLowerCase())) && (!onlyPending.value || !answer(o.id).status || answer(o.id).status==='not-done'))}
const answer=id=>data.value.answers.find(a=>a.id===id)
const consumption=id=>data.value.materials.find(m=>m.id===id)
function updateAnswer(id,patch){Object.assign(answer(id),patch);changed()}
function setMaterialActual(id,value){
 const material=consumption(id), amount=value===''?null:Number(value)
 material.actual=amount
 if(amount!==null&&Number.isFinite(amount)&&amount>=0&&!material.executor?.trim())material.executor=currentExecutor.value
 changed()
}
function setExecutor(){for(const a of data.value.answers)if(!a.executor)a.executor=batchExecutor.value;for(const m of data.value.materials)if(!m.executor)m.executor=batchExecutor.value;changed()}
const timeValue=value=>value?new Date(new Date(value).getTime()-new Date(value).getTimezoneOffset()*60000).toISOString().slice(0,16):''
function beginTimeEdit(){editingTime=true;clearTimeout(timer)}
/** Запоминает выбранное значение без сохранения: сохраняем только по завершении выбора. */
function syncTime(key,input){
 const value=input.value, parsed=value?new Date(value):null
 if(input.validity.badInput || (parsed&&!Number.isFinite(parsed.getTime()))){invalidTimes.add(key);return}
 invalidTimes.delete(key)
 data.value[key]=parsed?parsed.toISOString():null
}
function setTime(key,input){
 const value=input.value, parsed=value?new Date(value):null
 if(input.validity.badInput || (parsed&&!Number.isFinite(parsed.getTime()))){invalidTimes.add(key);clearTimeout(timer);return}
 invalidTimes.delete(key)
 data.value[key]=parsed?parsed.toISOString():null
 changed()
}
function finishTimeEdit(key,input){
 editingTime=false;setTime(key,input)
 if(invalidTimes.size)error.value='Введите дату и время полностью.'
}

function normalizeNumbers(){for(const m of data.value.materials)if(m.actual==='')m.actual=null;for(const a of data.value.answers)if(a.measurement==='')a.measurement=null;if(data.value.mileage==='')data.value.mileage=null;changed()}
async function addPhoto(event,id){
 let file=event.target.files[0];if(!file)return
 const isVideo=file.type.startsWith('video/')
 if(!isVideo&&!file.type.startsWith('image/')){error.value='Допустимы фото JPEG/PNG и видео MP4/WebM.';event.target.value='';return}
 if(isVideo&&file.size>25_000_000){error.value='Видео до 25 МБ.';event.target.value='';return}
 if(!await save()){event.target.value='';return}
 busy.value=true;error.value=''
 try{
  if(!isVideo){
   try{file=await convertImageToWebp(file,{maxSide:1600})}
   catch{ /* HEIC и другие форматы без поддержки декодирования отправляем как есть. */ }
  }
  if(!isVideo&&file.size>4_000_000)throw Error('Фото до 4 МБ. Выберите снимок меньшего размера или сделайте фото в JPEG.')
  const form=new FormData();form.append('file',file);form.append('operationId',id)
  await request('cards/'+card.value.id+'/photos/'+crypto.randomUUID(),{method:'POST',body:form,signal:AbortSignal.timeout(180000)})
  const key=card.value.id;await load();await open(cards.value.find(c=>c.id===key))
 }catch(e){error.value=e.name==='TimeoutError'?'Сервер не ответил при загрузке. Проверьте связь и повторите.':(e.message||'Не удалось загрузить медиа.')}
 finally{busy.value=false;event.target.value=''}
}
function closeMedia(){previewGeneration++;if(mediaPreview.value)URL.revokeObjectURL(mediaPreview.value.url);mediaPreview.value=null;mediaDialog.value?.close()}
async function viewPhoto(photo){
 const current=++previewGeneration
 try{const response=await fetch('/api/maintenance-plans/cards/'+card.value.id+'/photos/'+photo.id,{headers:{Authorization:'Bearer '+props.token}});if(!response.ok)throw Error('Не удалось загрузить медиа.');const blob=await response.blob();if(current!==previewGeneration)return
  if(mediaPreview.value)URL.revokeObjectURL(mediaPreview.value.url)
  mediaPreview.value={url:URL.createObjectURL(blob),type:blob.type};mediaDialog.value.showModal()
 }catch(e){error.value=e.message}
}
async function createDefect(id){
 if(!await save())return;busy.value=true;error.value=''
 try{await request('cards/'+card.value.id+'/defects/'+encodeURIComponent(id),{method:'POST'});const key=card.value.id;await load();await open(cards.value.find(c=>c.id===key))}catch(e){error.value=e.message}finally{busy.value=false}
}
const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))
const printDate=value=>/^\d{4}-\d{2}-\d{2}$/.test(String(value||''))?String(value).split('-').reverse().join('.'):escape(value)
const printTime=value=>{const date=new Date(value);return value&&!Number.isNaN(date.getTime())?date.toLocaleString('ru-RU',{day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit'}):''}
const printDuration=(from,to)=>{if(!from||!to)return '';const hours=(new Date(to)-new Date(from))/3600000;return Number.isFinite(hours)&&hours>=0?hours.toFixed(1)+' ч':' '}
/** Нормализует название материала для сопоставления с группами. */
const normalizeName=value=>String(value??'').replace(/\s+/g,' ').trim().toLocaleLowerCase('ru')
const isPetroleum=groupName=>/нефтепродукт/i.test(String(groupName??''))
let materialGroupNames
/** Возвращает сопоставление «материал → группа» из справочника «Группы материалов». */
async function materialGroups(){
 if(materialGroupNames)return materialGroupNames
 materialGroupNames=new Map()
 try{
  const response=await fetch('/api/material-groups/map?sourceTable=v_full_ost',{headers:{Authorization:'Bearer '+props.token}})
  if(response.ok)for(const item of await response.json())materialGroupNames.set(normalizeName(item.materialName),item.groupName||'')
 }catch{ /* Печать продолжится: все материалы попадут в «Фильтры». */ }
 return materialGroupNames
}
/** Выдаёт подписанные ссылки на вложения карты для печатной формы. */
async function printLinks(){
 const ids=[...new Set(photos.value.map(photo=>photo.id))],links={}
 if(!ids.length)return links
 try{
  const items=await request('cards/'+card.value.id+'/media-links',{method:'POST',body:JSON.stringify(ids)})
  for(const item of items)if(item.url)links[item.id]=new URL(item.url,window.location.origin).href
 }catch(e){error.value='Ссылки на вложения в печатной форме недоступны: '+e.message}
 return links
}
/** Печатает карту ТО по форме «Техническое обслуживание» из исходных карт Excel. */
async function print(){
 const w=window.open('','_blank');if(!w){error.value='Разрешите открытие окна печати.';return}
 const [links,groupNames]=await Promise.all([printLinks(),materialGroups()])
 const t=card.value.template,d=data.value
 const filesOf=id=>photos.value.filter(p=>p.operationId===id)
 const caption=o=>{
  const files=filesOf(o.id),text=escape(o.text)
  const linked=files.filter(f=>links[f.id])
  if(!linked.length)return text
  const extra=linked.slice(1).map((f,i)=>` <sup><a href="${escape(links[f.id])}">${i+2}</a></sup>`).join('')
  return `<a href="${escape(links[linked[0].id])}">${text}</a>${extra}`
 }
 const reason=a=>[a.reason,a.measurement==null||a.measurement===''?'':`Измерение: ${a.measurement} ${a.unit||''}`.trim(),a.note].filter(Boolean).join('; ')
 const marks=a=>[a.status==='done'?'V':'',a.status==='not-done'?'V':'']
 const operationRows=list=>list.map(o=>{
  const a=answer(o.id)||{},[yes,no]=marks(a)
  return `<tr><td>${caption(o)}</td><td class="c">${yes}</td><td class="c">${no}</td><td>${escape(reason(a))}</td><td>${escape(a.executor||'')}</td></tr>`
 }).join('')
 const planned=t.operations.filter(o=>!o.control),checks=t.operations.filter(o=>o.control)
 const sections=[...new Set(planned.map(o=>o.section))]
 const plannedRows=sections.map(section=>`<tr class="group"><td colspan="5">${escape(section)}</td></tr>${operationRows(planned.filter(o=>o.section===section))}`).join('')
 const consumed=t.materials.map(m=>({material:m,actual:consumption(m.id)||{}}))
 const petroleum=x=>isPetroleum(groupNames.get(normalizeName(x.material.name)))
 const lubricants=consumed.filter(petroleum)
 const filters=consumed.filter(x=>!petroleum(x))
 const materialRow=({material,actual})=>`<tr><td>${escape(material.assembly)}</td><td>${escape(material.name)}</td><td class="c">${escape(material.quantity??'')}</td><td class="c">${escape(material.unit)}</td><td class="c">${escape(actual.actual??'')}</td><td>${escape(actual.executor||'')}</td></tr>`
 const filterRow=({material,actual})=>`<tr><td>${escape(material.assembly)}</td><td>${escape(material.partNumber)}</td><td>${escape(material.name)}</td><td class="c">${escape(material.quantity??'')}</td><td class="c">${escape(material.unit)}</td><td class="c">${escape(actual.actual??'')}</td><td>${escape(actual.executor||'')}</td></tr>`
 const headerValues=[props.vehicle.stateNumber||props.vehicle.garageNumber||'',props.vehicle.modelName,d.engineModel,d.mileage??'',
  readingsText.value,printTime(d.stoppedAt),printTime(d.startedAt),printDuration(d.stoppedAt,d.releasedAt),d.driver]
 const style='@page{size:A4 portrait;margin:9mm}*{box-sizing:border-box}body{font:9.5px/1.25 Arial,Helvetica,sans-serif;color:#000;margin:0}h1{font-size:12.5px;text-align:center;margin:0 0 5px;font-weight:700}table{width:100%;border-collapse:collapse;table-layout:fixed}td,th{border:1px solid #000;padding:2px 3px;vertical-align:middle;overflow-wrap:anywhere}th{font-size:8px;font-weight:700;text-align:center}thead{display:table-header-group}tr{break-inside:avoid}.c{text-align:center}.pre{white-space:pre}.note{font-size:8px;margin:2px 0 0}.group td,.section td{font-weight:700;background:#f2f2f2}a{color:#000}.sign td{border:0;padding:1px 0}.sign .line{border-bottom:1px solid #000;height:16px}.foot{font-size:8px;color:#444;margin-top:4px;text-align:right}@media screen{body{padding:8px}}'
 const cols=widths=>'<colgroup>'+widths.map(width=>`<col style="width:${width}%">`).join('')+'</colgroup>'
 const markHead=title=>`<thead><tr><th>${title}</th><th colspan="2">Отметка о выполнении</th><th>Причина невыполнения</th><th>Исполнитель операции</th></tr><tr><th></th><th>ДА</th><th>НЕТ</th><th></th><th></th></tr></thead>`
 const operationsTable=title=>`<table style="margin-top:5px">${cols([40,4,4,30,22])}${markHead(title)}<tbody>`
 const headLabels=['№ шасси','Модель ТС','Марка ДВС','Пробег, км','Наработка, м/ч','Время остановки','Время запуска','Общее время простоя ТС','ФИО водителя']
 const doc='<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>Карта ТО</title><style>'+style+'</style></head><body>'
  +`<h1>Техническое обслуживание ${escape(props.vehicle.modelName)} ${escape(t.service)} от ${printDate(d.date)}</h1>`
  +`<table>${cols([9,12,12,7,15,11,11,9,14])}<thead><tr>${headLabels.map(v=>`<th>${v}</th>`).join('')}</tr></thead><tbody><tr>${headerValues.map((v,i)=>`<td class="${i===4?'c pre':'c'}">${escape(v||'')}</td>`).join('')}</tr></tbody></table>`
  +(readingsEstimated.value?'<p class="note">* Наработка рассчитана: показаний в журнале моточасов на дату ТО нет.</p>':'')
  +operationsTable('Операции технического обслуживания')
  +(plannedRows||'<tr><td colspan="5">&nbsp;</td></tr>')+'</tbody></table>'
  +`<table style="margin-top:5px">${cols([18,34,9,8,12,19])}<tbody><tr class="section"><td colspan="6">Замена смазочных и расходных материалов</td></tr>`
  +'<tr class="group"><td colspan="6">Смазывающие</td></tr>'
  +'<tr><th>Агрегат</th><th>Наименование</th><th>Кол-во</th><th>Ед.изм</th><th>Кол-во по факту</th><th>Исполнитель операции</th></tr>'
  +(lubricants.map(materialRow).join('')||'<tr><td colspan="6">&nbsp;</td></tr>')
  +'</tbody></table>'
  +`<table style="margin-top:5px">${cols([17,15,30,7,6,10,15])}<tbody><tr class="group"><td colspan="7">Фильтры</td></tr>`
  +'<tr><th>Агрегат</th><th>Каталожный номер</th><th>Наименование</th><th>Кол-во</th><th>Ед.изм</th><th>Кол-во по факту</th><th>Исполнитель операции</th></tr>'
  +(filters.map(filterRow).join('')||'<tr><td colspan="7">&nbsp;</td></tr>')
  +'</tbody></table>'
  +operationsTable('Чек-лист')
  +(operationRows(checks)||'<tr><td colspan="5">&nbsp;</td></tr>')+'</tbody></table>'
  +`<p style="margin:6px 0 0"><b>Примечание, в процессе выполнения ${escape(t.service)} было обнаружено:</b></p><p style="margin:2px 0 6px;min-height:28px;white-space:pre-line">${escape(d.notes||' ')}</p>`
  +'<p style="margin:0 0 2px"><b>Ответственные за проведение ТО:</b></p>'
  +`<table class="sign">${cols([46,10,22,22])}<tbody><tr><td>Электрослесарь (слесарь) дежурный и по ремонту оборудования</td><td>разряда</td><td class="line"></td><td></td></tr>`
  +'<tr><td>Электрослесарь (слесарь) дежурный и по ремонту оборудования</td><td>разряда</td><td class="line"></td><td></td></tr>'
  +'<tr><td>Механик / Сервисный инженер</td><td></td><td class="line"></td><td></td></tr></tbody></table>'
  +(card.value.acceptedBy?`<p style="margin:4px 0 0">Принял: ${escape(card.value.acceptedBy)} ${printTime(card.value.acceptedAt)}</p>`:'')
  +`<p class="foot">${escape(statusName[card.value.status]||'')} · версия шаблона ${card.value.templateRevision} · ${escape(card.value.vehicleLabel||'')} · сформировано ${new Date().toLocaleString('ru-RU')}</p>`
  +'</body></html>'
 w.document.write(doc);w.document.close();w.focus();w.print()
}
watch(()=>props.vehicle.id,()=>{clearTimeout(timer);card.value=null;data.value=null;runInId.value='';load()},{immediate:true})
watch(()=>props.token,async token=>{
 try{const response=await fetch('/api/profile',{headers:{Authorization:'Bearer '+token}});if(!response.ok)return;const profile=await response.json();if(token===props.token)currentExecutor.value=[profile.lastName,profile.firstName,profile.middleName].filter(Boolean).join(' ')}catch{}
},{immediate:true})
onBeforeUnmount(()=>{clearTimeout(timer);generation++;closeMedia()})
</script>

<template>
 <section class="to-cards">
  <header><h3>Карты ТО</h3><div class="actions"><button type="button" :disabled="busy" @click="load">Обновить список карт</button></div></header>
  <p v-if="error && !card" role="alert" class="form-message--error">{{error}}</p><p v-if="message" role="status">{{message}}</p>
  <div v-if="plan && canFill" class="actions">
   <label>Обслуживание для карты<select v-model="runInId"><option value="">{{plan.steps[plan.nextStepIndex].name}} · плановое</option><option v-for="r in plan.runIns" :key="r.id" :value="r.id">Обкатка: {{r.reason}}</option></select></label>
   <label v-if="matching.length>1">Шаблон<select v-model="selectedTemplate"><option v-for="t in matching" :value="t.id" :key="t.id">{{t.template.source}} · v{{t.revision}}</option></select></label>
   <button type="button" class="primary-button" :disabled="busy || !matching.length" @click="start">Начать ТО по карте</button>
   <p v-if="!matching.length">Нет утверждённой карты для {{vehicle.modelName}} / {{service}}. {{canUseLibrary?'Проверьте шаблон в библиотеке.':'Обратитесь к механику или инженеру ППР.'}}</p>
  </div>
  <p v-if="!cards.length">Заполненных карт пока нет.</p>
  <div class="card-list"><button v-for="item in cards" :key="item.id" type="button" :disabled="busy" @click="open(item)">{{item.data.date}} · {{item.template.service}} · {{statusName[item.status]}}</button></div>
  <section v-if="card && data" class="card-editor">
   <header><h4>{{card.template.service}} · {{statusName[card.status]}}</h4><p>{{card.vehicleLabel}} · версия шаблона {{card.templateRevision}}</p><button type="button" @click="print">Печать / PDF</button></header>
   <aside v-if="recovery"><p>На устройстве есть несохранённые изменения (версия {{recovery.revision}}; на сервере {{card.revision}}). Проверьте их перед отправкой.</p><details><summary>Посмотреть локальные изменения</summary><pre>{{JSON.stringify(recovery.data,null,2)}}</pre></details><button type="button" :disabled="recovery.revision!==card.revision" @click="restore">Восстановить черновик</button><button type="button" @click="discard">Оставить серверную версию</button></aside>
   <fieldset :disabled="!editable || busy || !!recovery" @input="changed" @change="normalizeNumbers">
    <div class="grid"><label>Дата ТО<input v-model="data.date" type="date" :max="localDate(new Date())" /></label><label>Водитель / машинист<input v-model="data.driver" name="atlas-driver" autocomplete="off" autocapitalize="words" /></label><label>Марка ДВС<input v-model="data.engineModel" name="atlas-engine" autocomplete="off" /></label><label>Пробег, км<input v-model.number="data.mileage" name="atlas-mileage" autocomplete="off" type="number" min="0" inputmode="numeric" /></label></div>
    <div class="grid"><label v-for="(label,key) in {stoppedAt:'Остановка техники',startedAt:'Начало работ',finishedAt:'Окончание работ',releasedAt:'Возврат в работу'}" :key="key">{{label}}<input type="datetime-local" :value="timeValue(data[key])" @focus="beginTimeEdit" @input.stop="syncTime(key,$event.target)" @change.stop="setTime(key,$event.target)" @blur="finishTimeEdit(key,$event.target)" /></label></div>
    <p v-if="data.startedAt&&data.finishedAt">Длительность работ: {{((new Date(data.finishedAt)-new Date(data.startedAt))/3600000).toFixed(1)}} ч.</p><p v-if="data.stoppedAt&&data.releasedAt">Простой: {{((new Date(data.releasedAt)-new Date(data.stoppedAt))/3600000).toFixed(1)}} ч.</p>
    <MaintenanceDateReadings :key="card.id+card.status" :token="token" :vehicle-id="vehicle.id" :date="data.date" :snapshot="card.status==='accepted'?card.readings:undefined" v-model="readings" />
    <div class="actions"><label>Исполнитель для незаполненных строк<input v-model="batchExecutor" /></label><button type="button" @click="setExecutor">Назначить исполнителя</button></div>
   </fieldset>
   <div class="actions"><strong>Заполнено {{progress}} из {{data.answers.length}}</strong><label>Найти работу<input v-model="filter" type="search" /></label><label class="check"><input v-model="onlyPending" type="checkbox" /> Только пропуски и невыполненные</label></div>
   <details v-for="section in groups" :key="section" open><summary>{{section}}</summary>
    <div class="operation-columns" aria-hidden="true"><span>Описание операции</span><span>Медиа выполнения</span><span>Выполнено / кто выполнял</span></div>
    <MaintenanceOperationRow v-for="o in operations(section)" :key="o.id" :operation="o" :answer="answer(o.id)" :media="photos.filter(p=>p.operationId===o.id)" :disabled="!editable || busy || !!recovery" :default-executor="batchExecutor.trim() || currentExecutor" :defect-id="card.defects[o.id]" @change="updateAnswer(o.id,$event)" @attach="addPhoto($event,o.id)" @preview="viewPhoto" @defect="createDefect(o.id)" />
   </details>
   <button v-if="canFill" type="button" @click="previewRequirement">Подготовить требование по нормам</button>
   <section v-if="requirement"><h4>Материалы для требования</h4><p>Сопоставление по точному наименованию и единице. Подтвердите доступные позиции; остальные выберите на складе вручную. Фактический расход карты не меняется.</p><p v-for="m in requirement" :key="m.materialName+m.unit">{{m.materialName}}: требуется {{m.quantity}} {{m.unit}}, доступно {{m.availableQuantity}}. {{m.matched?'':'Нет однозначного совпадения со складом.'}}</p><button type="button" :disabled="!requirement.some(m=>m.matched&&m.availableQuantity>0)" @click="sendRequirement">Заменить выбранные компоненты доступными материалами</button></section>
   <section class="card-material-source"><p v-if="card.template.materialSource">Источник материалов: <strong>{{card.template.materialSource}}</strong>. Нормы зафиксированы при загрузке в карту.</p><p v-else>Материалы этой карты ещё не связаны с техническим обслуживанием.</p><button v-if="editable" type="button" :disabled="busy||!!recovery||data.materials.some(m=>m.actual!==null&&m.actual!==undefined&&m.actual!==''||m.substitute?.trim())" @click="refreshMaterials">Загрузить материалы из технического обслуживания</button><p v-if="!card.template.materials.length">Список материалов пуст. Настройте его в «Техническом обслуживании» и загрузите в черновик.</p></section>
   <details open><summary>Материалы: норма и факт</summary><article v-for="m in card.template.materials" :key="m.id"><strong>{{m.assembly}} · {{m.name}}</strong><p>{{m.partNumber}} · Норма: {{m.quantity??'не задана'}} {{m.unit}}. {{m.note}}</p><fieldset :disabled="!editable||busy||!!recovery" @input="changed" @change="normalizeNumbers"><div class="grid"><label>Факт, {{m.unit}}<input :value="consumption(m.id).actual" type="number" min="0" step="any" @input.stop="setMaterialActual(m.id,$event.target.value)" /></label><label>Исполнитель<input v-model="consumption(m.id).executor" /></label></div></fieldset></article></details>
   <fieldset :disabled="!editable||busy||!!recovery" @input="changed"><label>Общие замечания<textarea v-model="data.notes" rows="3" /></label></fieldset>
   <label v-if="editable || (canManage&&card.status==='submitted')">Решение механика / причина возврата или отмены<textarea v-model="reason" rows="2" /></label>
   <p v-if="error" role="alert" class="form-message--error">{{error}}</p>
   <div class="actions"><template v-if="editable"><button v-if="canManage" type="button" :disabled="busy||!!recovery" @click="save()">Сохранить карту</button><button type="button" class="primary-button" :disabled="busy||!!recovery" @click="save('submit')">Передать на проверку</button><button type="button" :disabled="busy||!reason||!!recovery" @click="save('cancel')">Отменить карту</button></template><template v-if="canManage&&card.status==='submitted'"><button type="button" class="primary-button" :disabled="busy" @click="decide('accept')">Принять карту и завершить ТО</button><button type="button" :disabled="busy||!reason" @click="decide('return')">Вернуть на доработку</button></template></div>
   <p v-if="card.status==='accepted'">Карта принята {{new Date(card.acceptedAt).toLocaleString('ru-RU')}}. График обновлён. Сохранённые результаты доступны только для чтения.</p>
   <details v-if="card.audit"><summary>История изменений ({{card.audit.length}})</summary><article v-for="a in [...card.audit].reverse()" :key="a.revision"><strong>Версия {{a.revision}} · {{a.action}}</strong><p>{{new Date(a.at).toLocaleString('ru-RU')}} · {{a.actor}} · {{a.reason}}</p><details><summary>Данные на момент изменения</summary><pre>{{JSON.stringify(a.data,null,2)}}</pre></details></article></details>
    <button v-else-if="card.auditCount" type="button" class="secondary-button" :disabled="busy" @click="loadAudit">История изменений ({{ card.auditCount }})</button>
  </section>
  <dialog ref="mediaDialog" class="card-media-dialog" aria-label="Медиа выполнения" @cancel.prevent="closeMedia">
   <header><strong>Медиа выполнения</strong><button type="button" @click="closeMedia">Закрыть медиа</button></header>
   <template v-if="mediaPreview"><video v-if="mediaPreview.type.startsWith('video/')" :src="mediaPreview.url" controls playsinline /><img v-else :src="mediaPreview.url" alt="Фото выполнения операции" /><a :href="mediaPreview.url" :download="mediaPreview.type.startsWith('video/')?'выполнение-видео':'выполнение-фото'">Скачать оригинал</a></template>
  </dialog>
 </section>
</template>
<style scoped>
.to-cards{display:grid;gap:16px;border:1px solid var(--border);border-radius:12px;padding:18px;min-width:0}.actions,.card-list{display:flex;flex-wrap:wrap;gap:10px;align-items:center}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}label{display:grid;gap:5px;font-size:13px}input,select,textarea{box-sizing:border-box;width:100%;min-width:0;padding:9px;border:1px solid var(--border);border-radius:8px;background:var(--bg-main);color:var(--text-main);font:inherit}fieldset{border:0;padding:0;min-width:0;display:grid;gap:12px;margin:12px 0}button{min-height:44px;padding:8px 12px;border:1px solid var(--border);border-radius:8px;background:var(--bg-surface);color:var(--text-main);cursor:pointer}button:disabled{opacity:.55;cursor:default}article{padding:12px 0;border-bottom:1px solid var(--border)}summary{cursor:pointer;padding:12px 0;font-weight:600}.check{display:flex;align-items:center}.check input{width:auto}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px;max-height:350px;overflow:auto}aside{padding:14px;background:var(--bg-surface);border:1px solid var(--border)}.card-editor{min-width:0}h4{font-size:18px}p{overflow-wrap:anywhere}@media(max-width:600px){.to-cards{padding:10px}.grid{grid-template-columns:1fr}.actions>label{width:100%}}
.operation-columns{display:grid;grid-template-columns:minmax(0,1.8fr) minmax(150px,1fr) minmax(170px,1fr);gap:20px;padding:10px 0;border-bottom:1px solid var(--border);font-size:12px;font-weight:600}.card-media-dialog{box-sizing:border-box;width:min(900px,calc(100vw - 24px));max-height:90dvh;padding:16px;border:1px solid var(--border);border-radius:12px;background:var(--bg-main);color:var(--text-main)}.card-media-dialog::backdrop{background:#0009}.card-media-dialog header{display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;gap:12px}.card-media-dialog img,.card-media-dialog video{display:block;max-width:100%;max-height:65dvh;margin:0 auto 12px}.card-media-dialog a{color:inherit}@media(max-width:760px){.operation-columns{display:none}}
.to-cards{gap:10px;padding:12px}.to-cards>header{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap}.to-cards h3{margin:0;font-size:17px}.to-cards p{margin:4px 0}.actions,.card-list{gap:6px}.to-cards>header button,.card-list button{min-height:34px;padding:5px 9px;font-size:12px}.card-list{max-height:150px;overflow:auto}@media(max-width:700px){.to-cards>header button,.card-list button{min-height:40px}}
</style>
