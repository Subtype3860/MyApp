<script setup>
import { fetchReportMediaLinks } from '../utils/reportMedia'
import { pageBlockVisible, reportPresentation } from '../utils/pageVisibility'
import FleetActionMenu from './FleetActionMenu.vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import RepairMediaViewer from './RepairMediaViewer.vue'
import { cardMedia, cardState, defaultReportSelection, formatReportDate, selectShiftWorks, vehicleTitle, cardWorkSections, selectAuxiliaryWorks, groupReportWorks, reportCardTitle, maintenanceReportGroups } from '../utils/shiftReport'
import { compactPhoto, createShiftReportPdf } from '../utils/shiftReportPdf'

const props = defineProps({ token: { type: String, required: true } })
const visible = key => pageBlockVisible(props.token, `shift_report.${key}`)
const query = new URLSearchParams(window.location.search)
const initial = defaultReportSelection()
const date = ref(/^\d{4}-\d{2}-\d{2}$/.test(query.get('reportDate') ?? '') ? query.get('reportDate') : initial.date)
const shift = ref(['1', '2'].includes(query.get('reportShift')) ? query.get('reportShift') : initial.shift)
const journals = ref([])
const auxiliaryJournal = ref([])
const maintenanceJournal = ref([])
const maintenanceGroups = computed(() => visible('maintenance') ? maintenanceReportGroups(maintenanceJournal.value, date.value, shift.value) : [])
const loading = ref(false), exporting = ref(false), error = ref(''), dataLoaded = ref(false)
const locations = ref({}), photos = ref({})
const mediaLinks = ref({})
const viewerOpen = ref(false), viewerItems = ref([]), viewerType = ref('image')
const works = computed(() => visible('repair') ? selectShiftWorks(journals.value, date.value, shift.value) : [])
const auxiliaryWorks = computed(() => visible('auxiliary') ? selectAuxiliaryWorks(auxiliaryJournal.value, date.value, shift.value) : [])
const workCount = computed(() => works.value.length + auxiliaryWorks.value.length + maintenanceGroups.value.flatMap(g => g.categories.flatMap(c => c.cards)).length)
const legacyWorks = computed(() => works.value.filter(work => !['КАРЬЕР', 'РММ'].includes(work.location)))
const missingLocations = computed(() => legacyWorks.value.filter(work => !locations.value[work.id]))
const groups = computed(() => reportPresentation(groupReportWorks(works.value, auxiliaryWorks.value, locations.value, maintenanceGroups.value), props.token))
const cards = computed(() => groups.value.flatMap(group => group.categories.flatMap(category => category.cards)))
const controller = new AbortController()
let previewGeneration = 0
let mediaLinkGeneration = 0
const previewPromises = new Map()

/** Загружает данные ремонтных и вспомогательных работ для сменного отчёта. */
async function load() {
  loading.value = true
  dataLoaded.value = false
  error.value = ''
  try {
    const options = { headers: { Authorization: `Bearer ${props.token}` }, signal: controller.signal }
    const [response, auxiliaryResponse, maintenanceResponse] = await Promise.all([fetch('/api/vehicles/repair-journal?includeArchived=true', options), fetch('/api/auxiliary-works', options), fetch('/api/maintenance-plans/shift-report', options)])
    if (!response.ok || !auxiliaryResponse.ok || !maintenanceResponse.ok) throw new Error('Не удалось загрузить все работы для отчёта. Попробуйте обновить.')
    const [repairData, auxiliaryData, maintenanceData] = await Promise.all([response.json(), auxiliaryResponse.json(), maintenanceResponse.json()])
    journals.value = repairData
    auxiliaryJournal.value = auxiliaryData
    maintenanceJournal.value = maintenanceData
    await refreshMediaLinks()
    dataLoaded.value = true
    if (visible('media') && query.has('reportWork')) {
      const ids = new Set(query.get('reportWork').split(','))
      const items = [...(visible('repair') ? journals.value.flatMap(journal => journal.works ?? []) : []), ...(visible('auxiliary') ? auxiliaryJournal.value : [])].filter(work => ids.has(work.id))
      const type = query.get('mediaType') === 'video' ? 'video' : 'image'
      const media = cardMedia({ works: items }, type)
      const target = media.findIndex(item => item.id === query.get('mediaId'))
      if (target > 0) media.unshift(...media.splice(target, 1))
      if (media.length) openMedia(media, type)
      else error.value = 'Материалы по ссылке не найдены или недоступны.'
    }
  } catch (failure) { if (failure.name !== 'AbortError') error.value = failure.message }
  finally { loading.value = false }
}

async function refreshMediaLinks() {
  const generation = ++mediaLinkGeneration
  const links = await fetchReportMediaLinks(cards.value, props.token, controller.signal)
  if (generation === mediaLinkGeneration) mediaLinks.value = links
}

/** Загружает изображения для предварительного просмотра вложений отчёта. */
async function loadPreviews() {
  const generation = ++previewGeneration
  const items = cards.value.flatMap(card => card.works.flatMap(work => cardMedia({ works: [work] }, 'image')))
  for (const item of items) {
    if (generation !== previewGeneration || controller.signal.aborted) return
    if (photos.value[item.id]) continue
    if (!previewPromises.has(item.id)) {
      previewPromises.set(item.id, (async () => {
        try {
          const url = item.source === 'auxiliary' ? `/api/auxiliary-works/photos/${item.id}` :
            item.source === 'maintenance' ? `/api/maintenance-plans/cards/${item.cardId}/photos/${item.id}` :
            `/api/vehicles/work-photos/${item.id}`
          const response = await fetch(url, { headers: { Authorization: `Bearer ${props.token}` }, signal: controller.signal })
          if (!response.ok) throw new Error('Фото недоступно')
          const blob = await compactPhoto(await response.blob())
          if (!controller.signal.aborted) photos.value[item.id] = { blob, url: URL.createObjectURL(blob) }
        } catch (failure) {
          if (failure.name !== 'AbortError') photos.value[item.id] = { error: true }
        } finally { previewPromises.delete(item.id) }
      })())
    }
    await previewPromises.get(item.id)
  }
}

/** Открывает выбранное вложение карточки сменного отчёта. */
function openMedia(items, type) {
  viewerItems.value = items
  viewerType.value = type
  viewerOpen.value = true
}
/** Формирует и скачивает PDF текущего сменного отчёта. */
async function download() {
  if (!dataLoaded.value || !date.value || !workCount.value || missingLocations.value.length || loading.value || exporting.value) return
  exporting.value = true
  error.value = ''
  try {
    await loadPreviews()
    await refreshMediaLinks()
    const blob = await createShiftReportPdf({ groups: groups.value, date: date.value, shift: shift.value, photos: photos.value, mediaLinks: mediaLinks.value })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `Отчёт за смену ${date.value} (${shift.value}).pdf`
    anchor.click()
    setTimeout(() => URL.revokeObjectURL(url), 60000)
  } catch (failure) { error.value = failure.message || 'Не удалось сформировать PDF.' }
  finally { exporting.value = false }
}
watch(cards, loadPreviews)
watch([date, shift, locations], async () => {
  try { await refreshMediaLinks() }
  catch (failure) { if (failure.name !== 'AbortError') error.value = failure.message }
}, { deep: true })
onMounted(load)
onBeforeUnmount(() => {
  controller.abort()
  previewGeneration++
  Object.values(photos.value).forEach(photo => photo.url && URL.revokeObjectURL(photo.url))
})
</script>

<template>
  <section class="shift-report">
    <div class="shift-report__toolbar">
      <label>Дата отчёта<input v-model="date" type="date" :disabled="exporting" /></label>
      <label>Смена<select v-model="shift" :disabled="exporting"><option value="1">1 смена</option><option value="2">2 смена</option></select></label>
      <FleetActionMenu label="Меню отчёта за смену">
<button type="button" class="secondary-button" :disabled="loading || exporting" @click="load">Обновить</button>
      <button type="button" class="primary-button" :disabled="loading || exporting || !dataLoaded || !workCount || !!missingLocations.length || !date" @click="download">{{ exporting ? 'Формирование…' : 'Скачать PDF' }}</button>
</FleetActionMenu>
    </div>
    <p class="shift-report__hint">Ремонт, проведённые ТО и вспомогательные работы за выбранную дату и смену. ТО включаются по принятым картам и времени завершения работ. Фото и видео открываются по ссылкам без входа на сайт.</p>
    <p v-if="error" role="alert" class="shift-report__error">{{ error }}</p>
    <p v-if="loading" role="status">Загрузка истории ремонта…</p>
    <div v-else-if="legacyWorks.length" class="shift-report__missing">
      <strong>Укажите место работ для старых записей</strong>
      <p>Выбор действует для этого отчёта. В новых работах место сохраняется в истории ремонта.</p>
      <label v-for="work in legacyWorks" :key="work.id">{{ vehicleTitle(work.vehicle) }} — {{ work.description }}
        <select v-model="locations[work.id]" :disabled="exporting"><option disabled value="">Выберите место</option><option>КАРЬЕР</option><option>РММ</option></select>
      </label>
    </div>
    <div v-if="!loading && groups.length" class="shift-report__paper">
      <header class="shift-report__heading"><span>РАБОТЫ НА УЧАСТКЕ</span><h2>Отчёт за смену</h2><p>{{ formatReportDate(date) }} <span>·</span> {{ shift }} смена</p></header>
      <section v-for="group in groups" :key="group.location" class="shift-report__location">
        <h3>{{ group.location }}</h3>
        <section v-for="category in group.categories" :key="category.name" class="shift-report__category">
          <h4>{{ category.name }}</h4>
          <article v-for="card in category.cards" :key="card.id || card.vehicle.id" class="shift-report__card">
            <header><h5>{{ reportCardTitle(card) }}</h5><span v-if="!card.auxiliary && !card.maintenance" class="shift-report__state" :class="`shift-report__state--${cardState(card).kind}`">{{ cardState(card).label }}</span></header>
            <section v-for="section in cardWorkSections(card)" :key="section.works[0].id" class="shift-report__work-section">
              <div v-for="work in section.works" :key="work.id" class="shift-report__work">
                <ul class="shift-report__works"><li class="shift-report__description">{{ work.description || work.cause || 'Описание не указано.' }}</li></ul>
                <div v-if="cardMedia({ works: [work] }, 'image').length" class="shift-report__photos">
                  <a v-for="photo in cardMedia({ works: [work] }, 'image')" :key="photo.id" :href="mediaLinks[`image:${photo.id}`]" target="_blank" rel="noopener noreferrer" :aria-label="`Открыть фото: ${photo.fileName}`">
                    <img v-if="photos[photo.id]?.url" :src="photos[photo.id].url" :alt="photo.fileName" />
                    <span v-else>{{ photos[photo.id]?.error ? 'Превью недоступно' : 'Загрузка фото…' }}</span>
                  </a>
                </div>
                <div class="shift-report__links">
                  <a v-for="video in cardMedia({ works: [work] }, 'video')" :key="video.id" :href="mediaLinks[`video:${video.id}`]" target="_blank" rel="noopener noreferrer" :aria-label="`Открыть видео: ${video.fileName}`">▶</a>
                </div>
              </div>
              <div v-if="!card.hidePerformers" class="shift-report__performers">
                <template v-if="section.performers.groups.length">
                  <p>Исполнители:</p>
                  <ul class="shift-report__professions">
                    <li v-for="group in section.performers.groups" :key="group.position">
                      {{ group.position }}:
                      <p class="shift-report__names">+ {{ group.names.join('; ') }}</p>
                    </li>
                  </ul>
                </template>
                <p v-else>Исполнители пока не подтверждены</p>
              </div>
            </section>
          </article>
        </section>
      </section>
    </div>
    <p v-else-if="!loading && !error && !workCount" class="shift-report__empty">За выбранную дату и смену работ нет.</p>
    <RepairMediaViewer :open="viewerOpen" :items="viewerItems" :media-type="viewerType" :token="token" @close="viewerOpen = false" />
  </section>
</template>

<style scoped>
.shift-report { display: grid; gap: 16px; min-width: 0; }
.shift-report__toolbar { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; }
.shift-report label { display: grid; gap: 6px; font-size: 14px; }
.shift-report input, .shift-report select { padding: 10px 12px; border: 1px solid var(--border); border-radius: 8px; font: inherit; background: var(--bg-surface); color: var(--text-main); }
.shift-report__hint { margin: 0; font-size: 13px; color: var(--text-secondary); }
.shift-report__error { color: var(--system-red); }
.shift-report__missing { display: grid; gap: 12px; padding: 20px; border: 1px solid var(--border); border-radius: 12px; }
.shift-report__missing p { margin: 0; }
.shift-report__paper { width: 100%; max-width: 850px; box-sizing: border-box; margin: 8px auto; padding: 44px 48px; border: 1px solid #e1e6eb; border-radius: 6px; background: white; color: #202d3c; box-shadow: 0 6px 28px #1723330a; }
.shift-report__heading { padding-bottom: 24px; border-bottom: 2px solid #263b49; }
.shift-report__heading > span { font-size: 10px; letter-spacing: 2px; color: #667580; }
.shift-report__heading h2 { margin: 10px 0; font-size: 28px; }
.shift-report__heading p { margin: 0; font-size: 14px; color: #5d6b77; }
.shift-report__heading p span { padding: 0 8px; }
.shift-report__location h3 { margin: 28px 0 16px; font-size: 18px; color: #155f6b; letter-spacing: 1px; }
.shift-report__category h4 { margin: 16px 0 0; color: #667580; font-size: 13px; font-weight: 500; }
.shift-report__card { padding: 18px 0; border-bottom: 1px solid #e7ebee; }
.shift-report__card > header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; }
.shift-report__card h5 { margin: 0; font-size: 16px; overflow-wrap: anywhere; }
.shift-report__state { font-size: 11px; padding: 5px 9px; border-radius: 5px; background: #f0f2f4; color: #65717c; }
.shift-report__state--ready { background: #e9f4ef; color: #2d6b4f; }
.shift-report__state--stopped { background: #fbefed; color: #9b4840; }
.shift-report__description { margin: 12px 0; font-size: 16px; font-weight: 700; line-height: 1.6; white-space: pre-wrap; overflow-wrap: anywhere; }
.shift-report__work-section + .shift-report__work-section { border-top: 1px dashed #b5bec5; margin-top: 18px; padding-top: 8px; }
.shift-report__works { padding-left: 20px; }
.shift-report__professions { padding-left: 20px; list-style-type: "- "; }
.shift-report__names { margin: 4px 0 8px; padding-left: 12px; }
.shift-report__performers { font-style: italic; margin: 0 0 14px; font-size: 12px; color: #667580; line-height: 1.5; overflow-wrap: anywhere; }
.shift-report__photos { display: flex; flex-wrap: wrap; gap: 10px; margin: 14px 0 10px; }
.shift-report__photos a { display: flex; align-items: center; justify-content: center; text-decoration: none; width: 180px; height: 116px; max-width: calc(50% - 5px); padding: 0; overflow: hidden; border: 1px solid #e1e6eb; border-radius: 6px; background: #f4f6f8; cursor: pointer; color: #65717c; }
.shift-report__photos img { width: 100%; height: 100%; object-fit: contain; }
.shift-report__links { display: flex; flex-wrap: wrap; gap: 18px; }
.shift-report__links a { text-decoration: none; border: 0; background: transparent; padding: 4px 0; font: inherit; font-size: 12px; color: #155f6b; cursor: pointer; }
.shift-report__empty { padding: 32px; text-align: center; color: var(--text-secondary); }
@media(max-width: 600px) { .shift-report__paper { padding: 24px 18px; } .shift-report__heading h2 { font-size: 24px; } .shift-report__toolbar label { flex: 1; min-width: 120px; } .shift-report__toolbar button { flex: 1; } }
</style>
