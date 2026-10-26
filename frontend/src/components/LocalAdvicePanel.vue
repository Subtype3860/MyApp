<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'
import { isAdviceFresh, requestLocalAdvice } from '../utils/localAdvice'
const props = defineProps({ token: String, url: String, title: String, context: [String, Number, Boolean] })
const result = ref(null), error = ref(''), busy = ref(false)
let generation = 0, controller
function clear(message = '') {
  generation++; controller?.abort(); result.value = null; busy.value = false; error.value = message
}
watch(() => [props.url, props.token, props.context], () => clear())
const offline = () => clear('Нет связи. Получите новую рекомендацию после подключения.')
window.addEventListener('offline', offline)
const timer = setInterval(() => {
  if (result.value && !isAdviceFresh(result.value)) clear('Результат устарел. Обновите рекомендацию.')
}, 1000)
onBeforeUnmount(() => { clear(); clearInterval(timer); window.removeEventListener('offline', offline) })
async function load() {
  clear(); const current = generation; busy.value = true
  controller = new AbortController()
  const activeController = controller
  const timeout = setTimeout(() => activeController.abort(), 15000)
  try {
    const value = await requestLocalAdvice(props.url, props.token, undefined, controller.signal)
    if (current !== generation) return
    if (!isAdviceFresh(value)) throw new Error('Результат устарел. Повторите запрос.')
    result.value = value
  } catch (failure) {
    if (current === generation) error.value = failure.name === 'AbortError' ? 'Сервер не ответил. Повторите запрос.' : failure.message
  } finally { clearTimeout(timeout); if (current === generation) busy.value = false }
}
</script>
<template>
  <aside class="local-advice" :aria-label="title">
    <button type="button" :disabled="busy || !url" @click="load">{{ busy ? 'Проверка…' : title }}</button>
    <p v-if="error" role="status">{{ error }}</p>
    <div v-if="result" role="status">
      <strong>{{ result.advice.recommendation }}</strong>
      <p>Недостаточно данных для надёжного прогноза. Уверенность: не оценена.</p>
      <ul><li v-for="factor in result.advice.factors" :key="factor">{{ factor }}</li></ul>
      <small>По данным на {{ new Date(result.advice.generatedAt).toLocaleString('ru-RU') }}. Обновите после изменений в журнале. Решение принимает механик.</small>
    </div>
  </aside>
</template>
<style scoped>
.local-advice { padding: 12px; border: 1px solid var(--border); border-radius: 8px; margin-block: 8px; }
button { cursor: pointer; padding: 8px 12px; }
p, ul { margin-block: 8px; }
</style>
