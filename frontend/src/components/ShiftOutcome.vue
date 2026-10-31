<script setup>
import { ref, watch } from 'vue'
import { requestLocalAdvice } from '../utils/localAdvice'
const props = defineProps({ token: String, vehicleId: String, date: String, shift: [String, Number] })
const choice = ref(''), confirmed = ref(false), busy = ref(false), message = ref('')
let generation = 0
watch(() => [props.vehicleId, props.date, props.shift, props.token], () => { generation++; choice.value = ''; confirmed.value = false; message.value = ''; busy.value = false })
async function save() {
  if (!confirmed.value || !choice.value || busy.value) return
  const current = generation; busy.value = true; message.value = ''
  try {
    await requestLocalAdvice(`/api/local-advice/vehicles/${props.vehicleId}/outcomes`, props.token,
      { date: props.date, shift: Number(props.shift), readyWholeShift: choice.value === 'ready', confirmed: true })
    if (generation === current) { message.value = 'Фактический результат сохранён отдельно от прогноза.'; confirmed.value = false }
  } catch (error) { if (generation === current) message.value = error.message }
  finally { if (generation === current) busy.value = false }
}
</script>
<template>
  <details>
    <summary>Подтвердить фактический результат завершённой смены</summary>
    <p>Готовность означает техническую возможность работать всю смену. Простой по организационным причинам не считается неисправностью. Если результат неизвестен, не заполняйте.</p>
    <label>Факт<select v-model="choice" :disabled="busy"><option value="">Не установлен</option><option value="ready">Готова всю смену</option><option value="notReady">Не была готова всю смену</option></select></label>
    <label><input type="checkbox" v-model="confirmed" :disabled="busy" /> Проверено мной по фактическим данным</label>
    <button type="button" :disabled="busy || !choice || !confirmed" @click="save">Подтвердить факт смены</button>
    <p role="status">{{ message }}</p>
  </details>
</template>
