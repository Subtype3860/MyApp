<script setup>
import { computed, onBeforeUnmount, onMounted, provide, ref } from 'vue'
import UserAvatar from './UserAvatar.vue'
import WorkParticipants from './WorkParticipants.vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import { cardMedia, defaultReportSelection, selectAuxiliaryWorks } from '../utils/shiftReport'
import { convertImageToWebp } from '../utils/mediaUpload'

const props = defineProps({ token: { type: String, required: true }, navigationCollapsed: { type: Boolean, required: true } })
const profile = ref(null)
const role = String(localStorage.getItem('myapp.userRole') || '').toLowerCase()
let permissions = []
try { permissions = JSON.parse(localStorage.getItem('myapp.userPermissions') || '[]') } catch { /* No menu permissions. */ }
const canRecord = role === 'administrator' || permissions.includes('menu.auxiliary_works')
provide('workParticipation', { token: computed(() => props.token), userId: computed(() => canRecord ? profile.value?.id || '' : ''), canReview: computed(() => canRecord && profile.value?.position?.trim().toLocaleLowerCase('ru-RU') === 'механик') })
const query = new URLSearchParams(window.location.search)
const initial = defaultReportSelection()
const date = ref(/^\d{4}-\d{2}-\d{2}$/.test(query.get('reportDate') || '') ? query.get('reportDate') : initial.date)
const shift = ref(['1', '2'].includes(query.get('reportShift')) ? query.get('reportShift') : initial.shift)
const works = ref([]), loading = ref(false), saving = ref(false), preparing = ref(false), error = ref(''), success = ref('')
const selectedWorks = computed(() => selectAuxiliaryWorks(works.value, date.value, shift.value))
/** Возвращает местные дату и время в формате поля datetime-local. */
function localTime() { const now = new Date(); return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
const draft = ref({ title: '', location: '', description: '', performedAt: localTime() })
const files = ref([]), savedId = ref(null), fileInput = ref(null)
const viewerOpen = ref(false), viewerItems = ref([]), viewerType = ref('image')
const controller = new AbortController()
/** Формирует заголовки запроса с токеном текущей сессии. */
function headers(json = false) { return { Authorization: `Bearer ${props.token}`, ...(json ? { 'Content-Type': 'application/json' } : {}) } }
/** Читает JSON-ответ и преобразует ошибку API в сообщение для формы. */
async function responseData(response) {
  const data = await response.json().catch(() => null)
  if (!response.ok) throw new Error(Object.values(data?.errors || {}).flat()[0] || data?.message || (typeof data === 'string' ? data : null) || 'Не удалось сохранить работу.')
  return data
}
/** Загружает вспомогательные работы для выбранной даты и обновляет состояние загрузки. */
async function load() {
  loading.value = true
  error.value = ''
  try {
    const response = await fetch('/api/auxiliary-works', { headers: headers(), signal: controller.signal })
    if (!response.ok) throw new Error('Не удалось загрузить вспомогательные работы.')
    works.value = await response.json()
  } catch (failure) { if (failure.name !== 'AbortError') error.value = failure.message }
  finally { loading.value = false }
}
/** Открывает вложения вспомогательной работы начиная с выбранного файла. */
function openMedia(work, type, mediaId) {
  const items = cardMedia({ works: [work] }, type)
  const index = items.findIndex(item => item.id === mediaId)
  if (index > 0) items.unshift(...items.splice(index, 1))
  if (!items.length) return
  viewerItems.value = items; viewerType.value = type; viewerOpen.value = true
}
/** Проверяет вложения, преобразует фотографии и готовит файлы к загрузке. */
async function selectFiles(event) {
  const selected = [...event.target.files]
  event.target.value = ''
  if (!selected.length) return
  preparing.value = true
  error.value = ''
  try {
    const allowed = ['image/jpeg', 'image/png', 'image/webp', 'video/mp4', 'video/webm', 'video/quicktime']
    if (selected.some(file => !allowed.includes(file.type))) throw new Error('Выберите фото JPEG, PNG, WebP или видео MP4, WebM, MOV.')
    if (selected.some(file => file.type.startsWith('video/') && file.size > 100 * 1024 * 1024)) throw new Error('Видео должно быть не больше 100 МБ.')
    const processed = await Promise.all(selected.map(file => file.type.startsWith('image/') ? convertImageToWebp(file) : file))
    if (processed.some(file => file.type.startsWith('image/') && file.size > 8 * 1024 * 1024)) throw new Error('Фото после преобразования должно быть не больше 8 МБ.')
    const allFiles = [...files.value.map(item => item.file), ...processed]
    if (['image/', 'video/'].some(prefix => allFiles.filter(file => file.type.startsWith(prefix)).length > 10)) throw new Error('К одной работе можно приложить до 10 фото и 10 видео.')
    files.value.push(...processed.map(file => ({ file, url: file.type.startsWith('image/') ? URL.createObjectURL(file) : '' })))
  } catch (failure) { error.value = failure.message }
  finally { preparing.value = false }
}
/** Удаляет выбранное вложение и освобождает его временный URL. */
function removeFile(index) { const [item] = files.value.splice(index, 1); if (item.url) URL.revokeObjectURL(item.url) }
/** Сохраняет вспомогательную работу и последовательно загружает вложения, позволяя повторить неудачную загрузку. */
async function save() {
  if (saving.value || preparing.value) return
  saving.value = true; error.value = ''; success.value = ''
  try {
    if (!savedId.value) {
      const response = await fetch('/api/auxiliary-works', { method: 'POST', headers: headers(true), signal: controller.signal,
        body: JSON.stringify({ ...draft.value, performedAt: new Date(draft.value.performedAt).toISOString() }) })
      savedId.value = (await responseData(response)).id
    }
    while (files.value.length) {
      const file = files.value[0].file
      const body = new FormData(); body.append('file', file)
      await responseData(await fetch(`/api/auxiliary-works/${savedId.value}/${file.type.startsWith('video/') ? 'videos' : 'photos'}`, { method: 'POST', headers: headers(), body, signal: controller.signal }))
      removeFile(0)
    }
    const performed = new Date(draft.value.performedAt)
    if (performed.getHours() < 8) performed.setDate(performed.getDate() - 1)
    date.value = `${performed.getFullYear()}-${String(performed.getMonth() + 1).padStart(2, '0')}-${String(performed.getDate()).padStart(2, '0')}`
    shift.value = new Date(draft.value.performedAt).getHours() >= 8 && new Date(draft.value.performedAt).getHours() < 20 ? '1' : '2'
    savedId.value = null
    draft.value = { title: '', location: '', description: '', performedAt: localTime() }
    await load()
    success.value = 'Работа сохранена. Ваше участие ожидает подтверждения механика.'
  } catch (failure) {
    if (failure.name !== 'AbortError') error.value = savedId.value ? `Запись сохранена. ${failure.message} Повторите загрузку оставшихся вложений.` : failure.message
  } finally { saving.value = false }
}
onMounted(async () => {
  await Promise.all([load(), (async () => {
    try { const response = await fetch('/api/profile', { headers: headers(), signal: controller.signal }); if (response.ok) profile.value = await response.json() } catch { /* No review controls without profile. */ }
  })()])
  if (query.get('reportSource') === 'auxiliary' && query.has('reportWork')) {
    const ids = new Set(query.get('reportWork').split(','))
    const items = works.value.filter(work => ids.has(work.id))
    const type = query.get('mediaType') === 'video' ? 'video' : 'image'
    const media = cardMedia({ works: items }, type)
    const target = media.findIndex(item => item.id === query.get('mediaId'))
    if (target > 0) media.unshift(...media.splice(target, 1))
    if (media.length) { viewerItems.value = media; viewerType.value = type; viewerOpen.value = true }
    else error.value = 'Материалы по ссылке не найдены или недоступны.'
  }
})
onBeforeUnmount(() => { controller.abort(); files.value.forEach(item => item.url && URL.revokeObjectURL(item.url)) })
</script>

<template>
  <main class="home-page auxiliary-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header"><div><p class="eyebrow">РАБОТЫ НА УЧАСТКЕ</p><h1>Вспомогательные работы</h1></div><UserAvatar :token="token" /></header>
    <section class="auxiliary-content">
      <form v-if="canRecord" class="auxiliary-form" @submit.prevent="save">
        <h2>Новая запись</h2>
        <div class="auxiliary-fields">
          <label>Название работы<input v-model="draft.title" required maxlength="160" placeholder="Например, уборка территории" :disabled="saving || !!savedId" /></label>
          <label>Место работ<select v-model="draft.location" aria-label="Место работ" required :disabled="saving || !!savedId"><option disabled value="">Выберите место</option><option>КАРЬЕР</option><option>РММ</option></select></label>
          <label>Дата и время работы<input v-model="draft.performedAt" type="datetime-local" required :disabled="saving || !!savedId" /></label>
        </div>
        <label>Выполненные работы<textarea v-model="draft.description" required maxlength="4000" rows="4" placeholder="Что было сделано и какой результат получен?" :disabled="saving || !!savedId"></textarea></label>
        <div class="auxiliary-upload"><button type="button" class="secondary-button" :disabled="saving || preparing" @click="fileInput?.click()">+ Добавить фото или видео</button><input ref="fileInput" type="file" accept="image/jpeg,image/png,image/webp,video/mp4,video/webm,video/quicktime" multiple hidden :disabled="saving || preparing" @change="selectFiles" /></div>
        <ul v-if="files.length" class="auxiliary-files"><li v-for="(item, index) in files" :key="index"><img v-if="item.url" :src="item.url" alt="Превью вложения" /><span>{{ item.file.name }}</span><button type="button" :disabled="saving" :aria-label="`Убрать вложение ${item.file.name}`" @click="removeFile(index)">×</button></li></ul>
        <button type="submit" class="primary-button" :disabled="saving || preparing">{{ saving ? 'Сохранение…' : preparing ? 'Подготовка медиа…' : savedId ? 'Продолжить загрузку' : 'Сохранить работу' }}</button>
      </form>
      <p v-if="error" role="alert" class="auxiliary-error">{{ error }}</p><p v-if="success" role="status" class="auxiliary-success">{{ success }}</p>
      <div class="auxiliary-filters"><label>Дата записей<input v-model="date" type="date" /></label><label>Смена<select v-model="shift"><option value="1">1 смена</option><option value="2">2 смена</option></select></label><button type="button" class="secondary-button" :disabled="loading || saving" @click="load">Обновить</button></div>
      <p v-if="loading" role="status">Загрузка работ…</p>
      <div v-else class="auxiliary-list">
        <article v-for="work in selectedWorks" :key="work.id" class="auxiliary-card">
          <header><h2>{{ work.title }}</h2><span>{{ work.location }} · {{ new Date(work.completedAt || work.createdAt).toLocaleString('ru-RU') }}</span></header>
          <p class="auxiliary-description">{{ work.description }}</p>
          <WorkParticipants :work="work" />
          <div class="auxiliary-media"><button v-if="work.photos?.length" type="button" class="secondary-button" @click="openMedia(work, 'image')">Фото · {{ work.photos.length }}</button><button v-if="work.videos?.length" type="button" class="secondary-button" @click="openMedia(work, 'video')">Видео · {{ work.videos.length }}</button></div>
        </article>
        <p v-if="!selectedWorks.length && !error" class="auxiliary-empty">За выбранную дату и смену вспомогательных работ нет.</p>
      </div>
    </section>
    <RepairMediaViewer :open="viewerOpen" :items="viewerItems" :media-type="viewerType" :token="token" @close="viewerOpen = false" />
  </main>
</template>

<style scoped>
.auxiliary-content { display: grid; gap: 20px; min-width: 0; }
.auxiliary-form, .auxiliary-card { display: grid; gap: 16px; padding: 24px; border: 1px solid var(--border); border-radius: 12px; background: var(--bg-surface); min-width: 0; }
.auxiliary-form h2, .auxiliary-card h2 { margin: 0; font-size: 19px; overflow-wrap: anywhere; }
.auxiliary-fields { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 16px; }
.auxiliary-content label { display: grid; gap: 8px; font-size: 14px; min-width: 0; }
.auxiliary-content input, .auxiliary-content textarea, .auxiliary-content select { box-sizing: border-box; width: 100%; min-width: 0; padding: 10px 12px; border: 1px solid var(--border); border-radius: 8px; font: inherit; color: var(--text-main); background: var(--bg-surface); }
.auxiliary-content textarea { resize: vertical; }
.auxiliary-form > button { justify-self: start; }
.auxiliary-filters, .auxiliary-media { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; }
.auxiliary-list { display: grid; gap: 16px; }
.auxiliary-card header { display: grid; gap: 8px; }
.auxiliary-card header span, .auxiliary-empty { color: var(--text-secondary); font-size: 13px; }
.auxiliary-description { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.6; margin: 0; }
.auxiliary-error { color: var(--system-red); }
.auxiliary-success { color: #25835a; }
.auxiliary-files { display: flex; flex-wrap: wrap; padding: 0; margin: 0; gap: 12px; list-style: none; }
.auxiliary-files li { display: flex; align-items: center; gap: 8px; min-width: 0; max-width: 100%; font-size: 13px; }
.auxiliary-files span { overflow-wrap: anywhere; }
.auxiliary-files img { width: 80px; height: 60px; object-fit: contain; border-radius: 6px; }
.auxiliary-files button { border: 1px solid var(--border); border-radius: 6px; background: var(--bg-surface); color: var(--text-main); font: inherit; padding: 8px; cursor: pointer; }
@media(max-width: 760px) { .auxiliary-fields { grid-template-columns: 1fr; } .auxiliary-form, .auxiliary-card { padding: 18px; } .auxiliary-filters label { flex: 1; min-width: 120px; } }
</style>
