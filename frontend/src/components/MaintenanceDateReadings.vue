<script setup>
import { ref,watch } from 'vue'
import { hoursText } from '../utils/maintenance'
const props=defineProps({token:String,vehicleId:String,date:String,modelValue:Array,snapshot:Array})
const emit=defineEmits(['update:modelValue'])
const loading=ref(false),error=ref('')
watch([()=>props.vehicleId,()=>props.date,()=>(props.modelValue||[]).map(e=>e.number).join(',')],async(_,__,onCleanup)=>{
 const controller=new AbortController();onCleanup(()=>controller.abort())
 const numbers=(props.modelValue||[]).map(e=>e.number)
 emit('update:modelValue',numbers.map(number=>({number,hours:''})))
 error.value='';loading.value=true
 try {
  if(props.snapshot && numbers.every(number=>props.snapshot.some(e=>e.number===number))){emit('update:modelValue',numbers.map(number=>({...props.snapshot.find(e=>e.number===number)})));return}
  if(!props.date){error.value='Выберите дату для загрузки моточасов.';return}
  const response=await fetch('/api/maintenance-plans/'+props.vehicleId+'/readings?date='+encodeURIComponent(props.date),{headers:{Authorization:'Bearer '+props.token},signal:controller.signal})
  const data=await response.json()
  if(!response.ok)throw Error(data.message||'Не удалось загрузить моточасы.')
  if(controller.signal.aborted)return
  const values=numbers.map(number=>(data.engines.find(e=>e.number===number) ?? {number,hours:''}))
  emit('update:modelValue',values)
  if(values.some(e=>e.hours===''))error.value='Нет исходных показаний для всех ДВС. Загрузите моточасы за выбранную или более раннюю дату.'
 }catch(e){if(e.name!=='AbortError')error.value=e.message}
 finally{if(!controller.signal.aborted)loading.value=false}
},{immediate:true})
</script>
<template>
 <div class="maintenance-date-readings" aria-live="polite">
  <p v-if="loading">Загрузка моточасов…</p>
  <template v-else><p v-if="error" role="alert" class="form-message--error">{{error}}</p><p v-for="engine in modelValue" :key="engine.number">ДВС № {{engine.number}}: <strong>{{engine.hours===''?'нет данных':hoursText(engine.hours)+' м/ч'}}</strong><small v-if="engine.isEstimated"> · Расчётные моточасы: от {{engine.sourceDate?.split('-').reverse().join('.')}} ({{hoursText(engine.sourceHours)}} м/ч)</small></p></template>
  <small>{{snapshot ? 'Показания, зафиксированные при сохранении обкатки.' : 'При отсутствии данных на выбранную дату: последние загруженные показания + 22 м/ч за каждый полный день после них + 11 м/ч за выбранный день.'}}</small>
 </div>
</template>
