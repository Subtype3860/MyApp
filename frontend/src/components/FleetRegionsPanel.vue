<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
const props = defineProps({ token: { type: String, required: true }, showCatalog: { type: Boolean, default: true } })
const emit = defineEmits(['changed'])
const controller = new AbortController()
const regions = ref([]), vehicles = ref([]), assignments = ref([]), users = ref([])
const canManage = ref(false), busy = ref(false), error = ref(''), success = ref('')
const filter = ref(''), search = ref(''), selected = ref(null), history = ref([])
const regionId = ref(''), regionName = ref(''), archived = ref(false)
const userId = ref(''), grants = ref([]), grantsLoaded = ref(false)
const destination = ref(''), transferDate = ref(''), note = ref('')
const current = computed(() => assignments.value.find(a => a.vehicleId === selected.value?.id))
const assignmentMap = computed(() => new Map(assignments.value.map(a => [a.vehicleId, a])))
const visible = computed(() => vehicles.value.filter(v => {
  const a = assignmentMap.value.get(v.id)
  return (!filter.value || (filter.value === 'unassigned' ? !a : a?.regionId === filter.value)) &&
    `${v.garageNumber ?? ''} ${v.modelName} ${v.stateNumber}`.toLocaleLowerCase('ru-RU').includes(search.value.toLocaleLowerCase('ru-RU'))
}))
const selectedUser = computed(() => users.value.find(u => u.id === userId.value))
const label = v => `${v.modelName} · борт № ${v.garageNumber ?? '—'}`
const dateLabel = value => value ? new Date(value).toLocaleString('ru-RU') : 'По настоящее время'
async function request(path, method = 'GET', body) {
  const response = await fetch(`/api/fleet-regions${path}`, { method, signal: controller.signal,
    headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
  const value = await response.json().catch(() => null)
  if (!response.ok) {
    if (response.status === 403) { canManage.value = false; regions.value = []; vehicles.value = []; users.value = []; selected.value = null }
    throw new Error(response.status === 403 ? 'Раздел доступен Администратору, Старшему механику и Главному механику.' : value?.message || `Не удалось выполнить запрос (${response.status}).`)
  }
  return value
}
async function load() {
  const data = await request('/management')
  regions.value = data.regions; canManage.value = data.canManage
  vehicles.value = data.vehicles; assignments.value = data.assignments; users.value = data.users
}
async function run(action) {
  if (busy.value) return
  busy.value = true; error.value = ''; success.value = ''
  try { await action() } catch (e) { if (e.name !== 'AbortError') error.value = e.message }
  finally { busy.value = false }
}
function editRegion() {
  const value = regions.value.find(r => r.id === regionId.value)
  regionName.value = value?.name || ''; archived.value = value?.isArchived || false
}
async function saveRegion() {
  await run(async () => {
    await request(regionId.value ? `/${regionId.value}` : '', regionId.value ? 'PUT' : 'POST', { name: regionName.value, isArchived: archived.value })
    await load(); success.value = 'Участок сохранён.'
  })
}
async function loadGrants() {
  grantsLoaded.value = false; grants.value = []
  if (!userId.value) return
  await run(async () => { grants.value = await request(`/users/${userId.value}`); grantsLoaded.value = true })
}
async function saveGrants() {
  await run(async () => { await request(`/users/${userId.value}`, 'PUT', { regionIds: grants.value }); success.value = 'Доступ к участкам сохранён и действует со следующего запроса.' })
}
async function openVehicle(vehicle) {
  await run(async () => {
    selected.value = vehicle; history.value = []; destination.value = ''; transferDate.value = ''; note.value = ''
    history.value = await request(`/vehicles/${vehicle.id}/history`)
  })
}
async function transfer() {
  await run(async () => {
    await request(`/vehicles/${selected.value.id}/transfer`, 'POST', { regionId: destination.value,
      expectedAssignmentId: current.value?.assignmentId ?? null,
      startedAt: transferDate.value ? new Date(transferDate.value).toISOString() : null, note: note.value })
    await load(); history.value = await request(`/vehicles/${selected.value.id}/history`)
    destination.value = ''; note.value = ''; transferDate.value = ''
    success.value = 'Техника перемещена. Вся история доступна новому участку.'; emit('changed')
  })
}
watch(() => props.token, () => run(load), { immediate: true })
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <details open class="fleet-regions macos-glass-panel">
    <summary>Регионы / участки и закрепление техники</summary>
    <p>Доступ определяется текущим участком техники. После переезда новый участок видит всю историю машины.</p>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="success" role="status">{{ success }}</p>
    <button type="button" class="secondary-button" :disabled="busy" @click="run(load)">Обновить участки и технику</button>
    <template v-if="canManage">
      <details v-if="showCatalog" open>
        <summary>Справочник участков</summary>
        <form @submit.prevent="saveRegion">
          <label>Участок<select v-model="regionId" :disabled="busy" @change="editRegion"><option value="">Новый участок</option><option v-for="r in regions" :key="r.id" :value="r.id">{{ r.name }}{{ r.isArchived ? ' (архив)' : '' }}</option></select></label>
          <label>Название региона / участка<input v-model="regionName" required maxlength="200" :disabled="busy" /></label>
          <label><input v-model="archived" type="checkbox" :disabled="busy" />Архивный участок</label>
          <button class="primary-button" :disabled="busy">Сохранить участок</button>
        </form>
      </details>
      <details>
        <summary>Доступ пользователей к участкам</summary>
        <label>Пользователь<select v-model="userId" :disabled="busy" @change="loadGrants"><option value="">Выберите пользователя</option><option v-for="u in users" :key="u.id" :value="u.id">{{ u.lastName }} {{ u.firstName }} ({{ u.userName }})</option></select></label>
        <p v-if="selectedUser?.role === 'administrator'">Администратор видит все участки, включая технику без закрепления.</p>
        <form v-else-if="userId && grantsLoaded" @submit.prevent="saveGrants">
          <p>Отметьте доступные участки. Права на разделы и действия задаются отдельно в карточке пользователя.</p>
          <label v-for="r in regions" :key="r.id"><input v-model="grants" type="checkbox" :value="r.id" :disabled="busy" />{{ r.name }}{{ r.isArchived ? ' (архив)' : '' }}</label>
          <button class="primary-button" :disabled="busy">Сохранить доступ</button>
        </form>
      </details>
    </template>
    <template v-if="canManage">
    <div class="region-filters">
      <label>Показать участок<select v-model="filter"><option value="">Все доступные участки</option><option v-if="canManage" value="unassigned">Без закрепления</option><option v-for="r in regions" :key="r.id" :value="r.id">{{ r.name }}</option></select></label>
      <label>Поиск техники<input v-model="search" placeholder="Бортовой номер или модель" /></label>
    </div>
    <p v-if="!visible.length">По выбранным условиям техника не найдена.</p>
    <div v-else class="region-table"><table><thead><tr><th>Техника</th><th>Текущий участок</th><th>История</th></tr></thead><tbody>
      <tr v-for="v in visible" :key="v.id"><td>{{ label(v) }}{{ v.isArchived ? ' (архив)' : '' }}</td><td>{{ assignmentMap.get(v.id)?.regionName || 'Без закрепления' }}</td><td><button type="button" class="secondary-button" :disabled="busy" @click="openVehicle(v)">Перемещения</button></td></tr>
    </tbody></table></div>
    <section v-if="selected" class="region-history">
      <h3>{{ label(selected) }} — история закрепления</h3>
      <p v-if="!history.length">Техника ещё не закреплена за участком.</p>
      <ol><li v-for="item in history" :key="item.id"><b>{{ item.regionName }}</b>: {{ dateLabel(item.startedAt) }} — {{ dateLabel(item.endedAt) }}<br />{{ item.recordedBy }} · {{ item.note }}</li></ol>
      <form v-if="canManage && !selected.isArchived" @submit.prevent="transfer">
        <label>Новый участок<select v-model="destination" required :disabled="busy"><option value="">Выберите участок</option><option v-for="r in regions.filter(r => !r.isArchived && r.id !== current?.regionId)" :key="r.id" :value="r.id">{{ r.name }}</option></select></label>
        <label>Дата и время перемещения<input v-model="transferDate" type="datetime-local" :disabled="busy" /><small>Если не заполнено — текущее время. Время отображается в часовом поясе устройства.</small></label>
        <label>Примечание<textarea v-model="note" maxlength="2000" :disabled="busy" /></label>
        <button class="primary-button" :disabled="busy || !destination">{{ current ? 'Переместить технику' : 'Закрепить технику' }}</button>
      </form>
    </section>
    </template>
  </details>
</template>

<style scoped>
.fleet-regions{padding:18px;margin-bottom:18px;min-width:0}.fleet-regions summary{cursor:pointer;font-weight:600;padding:8px 0}.fleet-regions p,.fleet-regions small{color:var(--text-secondary);line-height:1.5}.fleet-regions form{display:grid;gap:12px;max-width:650px;padding:12px 0}.fleet-regions label{display:grid;gap:6px}.fleet-regions label:has(input[type=checkbox]){display:flex;align-items:center}.fleet-regions input:not([type=checkbox]),.fleet-regions select,.fleet-regions textarea{box-sizing:border-box;min-width:0;width:100%;padding:9px;border:1px solid var(--border);border-radius:8px;background:var(--bg-surface);color:var(--text-main);font:inherit}.region-filters{display:flex;flex-wrap:wrap;gap:14px;margin:16px 0}.region-filters label{flex:1;min-width:180px}.region-table{overflow:auto;max-height:360px}.fleet-regions table{border-collapse:collapse;width:100%;font-size:14px}.fleet-regions th,.fleet-regions td{padding:10px;text-align:left;border-bottom:1px solid var(--border)}.region-history{margin-top:18px}.region-history li{margin-bottom:12px}.fleet-regions [role=alert]{color:var(--system-red)}
</style>
