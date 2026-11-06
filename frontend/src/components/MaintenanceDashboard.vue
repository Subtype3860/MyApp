<script setup>
import { computed, onMounted, ref } from 'vue'
import { hoursText, nearestMaintenance } from '../utils/maintenance'
import { vehicleTitle } from '../utils/shiftReport'
import FleetActionMenu from './FleetActionMenu.vue'
const props = defineProps({ token: { type:String,required:true }, canOpen: { type:Boolean,default:false } })
defineEmits(['open'])
const rows=ref([]), loading=ref(false), error=ref('')
const nearest=computed(()=>nearestMaintenance(rows.value))
const unconfigured=computed(()=>rows.value.filter(row=>!row.plan).length)
const incomplete=computed(()=>rows.value.filter(row=>row.incomplete).length)
function percent(due) { return Math.max(0,Math.min(100,due.elapsed/due.interval*100)) }
async function load() {
 loading.value=true;error.value=''
 try {
  const r=await fetch('/api/maintenance-plans',{headers:{Authorization:'Bearer '+props.token}})
  if(!r.ok)throw Error('Не удалось загрузить графики ТО.')
  const data=await r.json();rows.value=data.rows || []
 } catch(e){error.value=e.message}finally{loading.value=false}
}
onMounted(load)
</script>
<template>
 <section class="dashboard-panel maintenance-dashboard" aria-labelledby="maintenance-due-title" :aria-busy="loading">
  <header class="dashboard-panel__header">
   <div><h2 id="maintenance-due-title">Ближайшее ТО</h2><p>Пять машин с наименьшим остатком моточасов. Просроченные — первыми.</p></div>
   <FleetActionMenu label="Меню графиков ТО"><button type="button" :disabled="loading" @click="load">Обновить</button><button v-if="canOpen" type="button" @click="$emit('open')">Настроить графики и провести ТО</button></FleetActionMenu>
  </header>
  <p v-if="loading" role="status">Загрузка графиков ТО…</p>
  <p v-else-if="error" role="alert">{{ error }}</p>
  <template v-else>
   <p v-if="!nearest.length">Нет графиков с достаточными данными для расчёта. Укажите последнее ТО, шаг цикла и показания ДВС в разделе «Проведение ТО».</p>
   <ol v-else class="maintenance-timelines">
    <li v-for="row in nearest" :key="row.vehicle.id" :class="{overdue:row.next.remaining<0}">
     <header><strong>{{ vehicleTitle(row.vehicle) }}</strong><span>{{ row.next.name }} · {{ row.next.remaining < 0 ? 'Просрочено на '+hoursText(-row.next.remaining) : 'Осталось '+hoursText(row.next.remaining) }} м/ч</span></header>
     <div class="maintenance-axis" :aria-label="'Начало '+hoursText(row.next.startHours)+', сейчас '+hoursText(row.next.currentHours)+', ТО на '+hoursText(row.next.dueHours)+' м/ч'">
      <div class="maintenance-axis__labels"><span>{{ hoursText(row.next.startHours) }} м/ч<small>Начало отсчёта</small></span><strong>{{ hoursText(row.next.currentHours) }} м/ч<small>Сейчас · ДВС № {{ row.next.engineNumber }}</small></strong><span>{{ hoursText(row.next.dueHours) }} м/ч<small>Следующее ТО</small></span></div>
      <div class="maintenance-axis__line"><span class="maintenance-axis__fill" :style="{width:percent(row.next)+'%'}"></span><i :style="{left:percent(row.next)+'%'}"></i></div>
     </div>
     <small><template v-if="row.next.isEstimated">Расчётные моточасы · исходные данные от {{ row.next.sourceDate?.split('-').reverse().join('.') }} · </template>Показания на {{ row.next.readingDate.split('-').reverse().join('.') }}<template v-if="row.due.length>1"> · Дополнительно запланировано ТО: {{ row.due.length-1 }}</template></small>
    </li>
   </ol>
   <p v-if="unconfigured || incomplete" class="maintenance-missing">Без графика: {{ unconfigured }} · Требуют проверки показаний: {{ incomplete }}. Эти машины не участвуют в расчёте ближайших ТО.</p>
  </template>
 </section>
</template>
<style scoped>
.maintenance-dashboard{margin:20px 0;padding:20px}
.maintenance-timelines{list-style:none;padding:0;margin:0;display:grid;gap:10px}
.maintenance-timelines li{padding:10px 14px;border:1px solid var(--border);border-radius:10px}
.maintenance-timelines header{display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap;font-size:14px}
.maintenance-timelines header span{color:var(--accent);font-weight:600}
.maintenance-timelines small,.maintenance-missing{color:var(--text-secondary);font-size:12px}
.maintenance-axis{margin:10px 0 8px}
.maintenance-axis__labels{display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;font-size:14px}
.maintenance-axis__labels>strong{text-align:center;color:var(--accent)}
.maintenance-axis__labels>span:last-child{text-align:right}
.maintenance-axis__labels small{display:block;font-weight:400;margin-top:4px}
.maintenance-axis__line{position:relative;height:6px;background:var(--border);margin-top:10px;border-left:2px solid var(--text-secondary);border-right:2px solid var(--text-secondary)}
.maintenance-axis__fill{display:block;height:100%;background:var(--accent)}
.maintenance-axis__line i{position:absolute;top:-4px;width:14px;height:14px;border-radius:50%;background:var(--accent);transform:translateX(-50%)}
.overdue header span,.overdue .maintenance-axis__labels>strong{color:var(--system-red)}
.overdue .maintenance-axis__fill,.overdue .maintenance-axis__line i{background:var(--system-red)}
@media(max-width:600px){.maintenance-dashboard{padding:14px}.maintenance-timelines li{padding:10px}.maintenance-axis__labels{font-size:12px}.maintenance-axis__labels small{font-size:10px}}
</style>

