<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'
import { requestLocalAdvice } from '../utils/localAdvice'
const props = defineProps({ token: String, kind: String, source: String, context: String, disabled: Boolean })
const emit = defineEmits(['apply'])
const fields = ref([]), limitations = ref([]), error = ref(''), busy = ref(false)
const names = { nodeName: 'Узел', symptoms: 'Симптомы', failureReason: 'Указанная причина отказа', errorCode: 'Код ошибки', description: 'Описание', requiredParts: 'Запчасти (добавить в описание)' }
let generation = 0
function clear() { generation++; fields.value = []; limitations.value = []; error.value = ''; busy.value = false }
watch(() => [props.source, props.context, props.token, props.kind], clear)
onBeforeUnmount(clear)
async function parse() {
  clear(); const current = generation; busy.value = true
  try {
    const result = await requestLocalAdvice('/api/local-advice/parse', props.token, { kind: props.kind, text: props.source })
    if (current !== generation) return
    fields.value = result.fields.map(field => ({ ...field, accepted: false }))
    limitations.value = result.limitations
    if (!fields.value.length) error.value = 'Недостаточно данных. Укажите симптомы или явные поля: «Узел: …; Симптомы: …».'
  } catch (failure) { if (current === generation) error.value = failure.message }
  finally { if (current === generation) busy.value = false }
}
function apply() {
  emit('apply', Object.fromEntries(fields.value.filter(field => field.accepted).map(field => [field.name, field.value])))
  clear()
}
</script>
<template>
  <div class="local-draft">
    <button type="button" :disabled="busy || disabled || !source?.trim()" @click="parse">{{ busy ? 'Разбор…' : 'Разобрать текст локально' }}</button>
    <p v-if="error" role="status">{{ error }}</p>
    <template v-if="fields.length">
      <p>Черновик. Отметьте значения для переноса в форму; заявку сохраните отдельной кнопкой.</p>
      <div v-for="field in fields" :key="field.name" class="draft-field">
        <label><input type="checkbox" v-model="field.accepted" :disabled="disabled" /> Принять: {{ names[field.name] }}</label>
        <textarea v-model="field.value" :aria-label="names[field.name]" :disabled="disabled" :maxlength="field.name === 'nodeName' ? 200 : field.name === 'errorCode' ? 100 : 4000" />
        <small>Из текста: {{ field.evidence }}</small>
      </div>
      <p v-for="limitation in limitations" :key="limitation">{{ limitation }}</p>
      <button type="button" :disabled="disabled || !fields.some(field => field.accepted)" @click="apply">Перенести выбранное в форму</button>
      <button type="button" @click="clear">Отклонить черновик</button>
    </template>
  </div>
</template>
<style scoped>
.local-draft, .draft-field { display: grid; gap: 8px; margin-block: 10px; }
textarea { width: 100%; min-height: 60px; }
button { padding: 8px; cursor: pointer; }
</style>
