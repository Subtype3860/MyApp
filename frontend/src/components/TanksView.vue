<script setup>
import { onMounted, ref } from 'vue'
const props = defineProps({ token: { type: String, required: true }, navigationCollapsed: { type: Boolean, required: true } })
const loading = ref(true), error = ref(''), lobby = ref(null)
async function refresh() {
  loading.value = true; error.value = ''; lobby.value = null
  try {
    const response = await fetch('/api/tanks/lobby', { headers: { Authorization: `Bearer ${props.token}` } })
    if (response.status === 403) throw new Error('Администратор должен выдать допуск: Настройки → Пользователи → «Танки — доступ к сетевой игре». После выдачи войдите заново.')
    if (!response.ok) throw new Error('Не удалось получить состояние арены. Проверьте соединение и повторите.')
    lobby.value = await response.json()
  } catch (e) { error.value = e.message } finally { loading.value = false }
}
onMounted(refresh)
</script>
<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
  <section class="macos-glass-panel tanks-lobby">
    <p class="eyebrow">СЕТЕВАЯ ИГРА</p><h1>Танки 3D — «Броня»</h1>
    <p>Общая арена для пользователей Atlas. Собирайте усиления на поле и сражайтесь друг с другом.</p>
    <p v-if="loading" role="status">Проверяем допуск…</p>
    <p v-else-if="error" role="alert">{{ error }}</p>
    <template v-else-if="lobby"><p>На арене: {{ lobby.online }} / {{ lobby.capacity }} игроков.</p><a class="primary-button tanks-link" href="/games/tanks/index.html">Открыть арену ↗</a></template>
    <button class="secondary-button" type="button" :disabled="loading" @click="refresh">Обновить</button>
    <p class="tanks-help">WASD — движение, мышь — прицел, пробел — огонь. На телефоне доступны джойстик и автоматическое прицеливание. После уничтожения танк возрождается через 3 секунды.</p>
  </section>
  </main>
</template>
<style scoped>
.tanks-lobby{max-width:800px;padding:32px}.tanks-lobby h1{font-size:30px}.tanks-lobby p{line-height:1.7}.tanks-help{color:var(--text-secondary);font-size:13px}.tanks-link{display:inline-flex;align-items:center;justify-content:center;padding:0 22px;text-decoration:none;margin:12px 16px 12px 0}button{margin-top:12px}@media(max-width:760px){.tanks-lobby{padding:22px}}
</style>
