<script setup>
import { computed, onMounted, ref } from 'vue'

const props = defineProps({ token: { type: String, required: true } })
const emit = defineEmits(['navigate'])

const loading = ref(true)
const saving = ref(false)
const error = ref('')
const message = ref('')
const query = ref('')
const vehicles = ref([])
const vehicle = ref(null)
const symptoms = ref('')
const work = ref('')
const status = ref('В ремонте')
const files = ref([])
const input = ref(null)

const matches = computed(() => {
  const q = query.value.trim().toLowerCase()
  const list = vehicles.value.filter(v => !v.isArchived)
  if (!q) return list.slice(0, 30)
  return list.filter(v => `${v.garageNumber} ${v.modelName} ${v.stateNumber} ${v.vin}`.toLowerCase().includes(q)).slice(0, 30)
})

const canSend = computed(() => vehicle.value && symptoms.value.trim().length > 1 && !saving.value)

const PIN_KEY = 'atlas-repair-pin'
const pinInput = ref('')
const pinError = ref('')
const pinSet = ref(false)
const locked = ref(false)
const showPinSetup = ref(false)
const pinNew = ref('')

async function hashPin(value) {
  const data = new TextEncoder().encode('atlas-pin:' + value)
  const digest = await crypto.subtle.digest('SHA-256', data)
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, '0')).join('')
}

function readPin() {
  return localStorage.getItem(PIN_KEY) || ''
}

async function unlock() {
  pinError.value = ''
  if (pinInput.value.length < 4) { pinError.value = 'Введите 4 цифры'; return }
  const stored = readPin()
  if (await hashPin(pinInput.value) !== stored) { pinError.value = 'Неверный PIN'; pinInput.value = ''; return }
  sessionStorage.setItem('atlas-repair-unlocked', '1')
  locked.value = false
  pinInput.value = ''
}

async function savePin() {
  pinError.value = ''
  if (!/^\d{4}$/.test(pinNew.value)) { pinError.value = 'PIN — 4 цифры'; return }
  localStorage.setItem(PIN_KEY, await hashPin(pinNew.value))
  sessionStorage.setItem('atlas-repair-unlocked', '1')
  pinSet.value = true
  showPinSetup.value = false
  locked.value = false
  pinNew.value = ''
}

function typeDigit(d) {
  if (pinInput.value.length < 4) pinInput.value += d
}
const pending = ref(0)
const DB_NAME = 'atlas-repair-queue'

function openDb() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => request.result.createObjectStore('reports', { keyPath: 'id', autoIncrement: true })
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function enqueue(report) {
  const db = await openDb()
  await new Promise((resolve, reject) => {
    const tx = db.transaction('reports', 'readwrite')
    tx.objectStore('reports').add(report)
    tx.oncomplete = resolve
    tx.onerror = () => reject(tx.error)
  })
  await refreshPending()
}

async function refreshPending() {
  try {
    const db = await openDb()
    pending.value = await new Promise((resolve, reject) => {
      const tx = db.transaction('reports', 'readonly')
      const count = tx.objectStore('reports').count()
      count.onsuccess = () => resolve(count.result || 0)
      count.onerror = () => reject(count.error)
    })
  } catch { pending.value = 0 }
}

async function readQueue() {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction('reports', 'readonly')
    const all = tx.objectStore('reports').getAll()
    all.onsuccess = () => resolve(all.result || [])
    all.onerror = () => reject(all.error)
  })
}

async function removeQueued(id) {
  const db = await openDb()
  await new Promise((resolve, reject) => {
    const tx = db.transaction('reports', 'readwrite')
    tx.objectStore('reports').delete(id)
    tx.oncomplete = resolve
    tx.onerror = () => reject(tx.error)
  })
  await refreshPending()
}

async function flushQueue() {
  if (!navigator.onLine) return
  const items = await readQueue()
  for (const item of items) {
    try {
      await submitReport(item)
      await removeQueued(item.id)
    } catch {
      break
    }
  }
  await refreshPending()
}

async function submitReport(report) {
  const defect = await api(`/${report.vehicleId}/defects`, {
    method: 'POST',
    body: JSON.stringify({ errorCode: null, symptoms: report.symptoms, downtimeStartedAt: report.createdAt, operationalState: 'stopped', nodeName: '', failureReason: '' }),
  })
  const created = await api(`/${report.vehicleId}/works`, {
    method: 'POST',
    body: JSON.stringify({ defectId: defect?.id || defect, cause: '', description: report.work, status: report.status, requiredParts: null, repairDateTime: report.createdAt, location: '' }),
  })
  const workId = created?.id || created
  for (const file of report.files || []) {
    const form = new FormData()
    form.append('file', file)
    await api(`/works/${workId}/photos`, { method: 'POST', body: form })
  }
}
async function api(path, options = {}) {
  const response = await fetch('/api/vehicles' + path, {
    ...options,
    headers: {
      Authorization: 'Bearer ' + props.token,
      ...(options.body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
      ...(options.headers || {}),
    },
    signal: AbortSignal.timeout(60000),
  })
  const data = await response.json().catch(() => null)
  if (!response.ok) throw Error(data?.message || 'Сервер не выполнил действие.')
  return data
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    vehicles.value = await api('', {})
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

function pick(v) {
  vehicle.value = v
  query.value = ''
  error.value = ''
  message.value = ''
}

function chooseFiles(event) {
  files.value = [...event.target.files].slice(0, 8)
}

function vehicleTitle(v) {
  return v ? `№ ${v.garageNumber} · ${v.modelName || 'Техника'}` : ''
}

async function send() {
  if (!canSend.value) return
  saving.value = true
  error.value = ''
  message.value = ''
  const report = {
    vehicleId: vehicle.value.id,
    symptoms: symptoms.value.trim(),
    work: work.value.trim(),
    status: status.value,
    createdAt: new Date().toISOString(),
    files: [...files.value],
  }
  try {
    try {
      await submitReport(report)
    } catch (failure) {
      if (failure instanceof TypeError || !navigator.onLine) {
        await enqueue(report)
        message.value = 'Нет связи: отчёт сохранён и уйдёт автоматически'
        vehicle.value = null
        symptoms.value = ''
        work.value = ''
        files.value = []
        if (input.value) input.value.value = ''
        return
      }
      throw failure
    }
    message.value = 'Отчёт отправлен'
    vehicle.value = null
    symptoms.value = ''
    work.value = ''
    files.value = []
    if (input.value) input.value.value = ''
  } catch (e) {
    error.value = e.message
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  window.addEventListener('online', flushQueue)
  await refreshPending()
  const stored = readPin()
  pinSet.value = !!stored
  locked.value = !!stored && sessionStorage.getItem('atlas-repair-unlocked') !== '1'
  if (!locked.value) { await load(); await flushQueue() }
})
</script>

<template>
  <main class="repair-mobile">
    <header class="repair-head">
      <h1>Ремонт</h1>
      <button type="button" class="link" @click="emit('navigate', 'vehicles')">Всё приложение</button>
    </header>

    <section v-if="locked" class="pin">
      <p class="pin-title">Введите PIN</p>
      <p class="pin-dots">{{ '•'.repeat(pinInput.length) }}</p>
      <div class="pin-pad">
        <button v-for="d in ['1','2','3','4','5','6','7','8','9']" :key="d" type="button" @click="typeDigit(d)">{{ d }}</button>
        <button type="button" @click="pinInput = pinInput.slice(0, -1)">⌫</button>
        <button type="button" @click="typeDigit('0')">0</button>
        <button type="button" @click="pinInput = ''">Сброс</button>
      </div>
      <button type="button" class="send" @click="unlock">Войти</button>
      <p v-if="pinError" class="err">{{ pinError }}</p>
    </section>

    <section v-else-if="showPinSetup" class="pin">
      <p class="pin-title">Новый PIN (4 цифры)</p>
      <input v-model="pinNew" class="search" inputmode="numeric" maxlength="4" placeholder="••••" aria-label="Новый PIN" />
      <button type="button" class="send" @click="savePin">Сохранить PIN</button>
      <p v-if="pinError" class="err">{{ pinError }}</p>
    </section>

    <p v-if="!pinSet && !locked && !showPinSetup" class="hint">
      <button type="button" class="link" @click="showPinSetup = true">Задать PIN для быстрого входа</button>
    </p>
    <p v-if="loading" role="status">Загрузка техники…</p>
    <p v-else-if="error" role="alert" class="err">{{ error }}</p>

    <template v-if="!vehicle">
      <input v-model="query" class="search" type="search" placeholder="Номер, модель или госномер" aria-label="Поиск техники" />
      <ul class="list">
        <li v-for="v in matches" :key="v.id">
          <button type="button" class="card" @click="pick(v)">
            <strong>№ {{ v.garageNumber }}</strong>
            <span>{{ v.modelName }}</span>
          </button>
        </li>
      </ul>
      <p v-if="!matches.length" class="hint">Ничего не найдено</p>
    </template>

    <template v-else>
      <div class="picked">
        <div>
          <p class="eyebrow">Техника</p>
          <strong>{{ vehicleTitle(vehicle) }}</strong>
          <p class="hint">{{ vehicle.stateNumber }} {{ vehicle.vin ? '· ' + vehicle.vin : '' }}</p>
        </div>
        <button type="button" class="link" @click="vehicle = null">Сменить</button>
      </div>

      <label class="field">
        <span>Что случилось</span>
        <textarea v-model="symptoms" rows="3" placeholder="Например: течь масла из гидроцилиндра" />
      </label>

      <label class="field">
        <span>Что сделали</span>
        <textarea v-model="work" rows="3" placeholder="Например: заменили сальник, долили масло" />
      </label>

      <label class="field">
        <span>Статус</span>
        <select v-model="status">
          <option>В ремонте</option>
          <option>Выполнено</option>
          <option>Ожидает запчасти</option>
        </select>
      </label>

      <label class="photo">
        <input ref="input" type="file" accept="image/*" capture="environment" multiple @change="chooseFiles" />
        <span>📷 Сделать фото<span v-if="files.length"> · {{ files.length }}</span></span>
      </label>

      <p v-if="error" role="alert" class="err">{{ error }}</p>
      <p v-if="message" role="status" class="ok">{{ message }}</p>
      <p v-if="pending" class="hint">В очереди: {{ pending }}</p>

      <button type="button" class="send" :disabled="!canSend" @click="send">
        {{ saving ? 'Отправляем…' : 'Отправить' }}
      </button>
    </template>
  </main>
</template>

<style scoped>
.pin{display:grid;gap:14px;justify-items:center;padding:32px 0}
.pin-title{margin:0;font-size:18px}
.pin-dots{letter-spacing:.4em;font-size:28px;margin:0;min-height:34px}
.pin-pad{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;width:100%}
.pin-pad button{min-height:56px;font-size:22px;border:1px solid var(--border);border-radius:14px;background:var(--bg-surface);color:var(--text-main)}
.repair-mobile{max-width:520px;margin:0 auto;padding:16px 14px 96px;display:grid;gap:14px}
.repair-head{display:flex;align-items:center;justify-content:space-between}
.repair-head h1{margin:0;font-size:24px}
.link{background:none;border:0;color:var(--accent, #7cc8ff);font:inherit;padding:8px;cursor:pointer}
.search{width:100%;padding:14px;border:1px solid var(--border);border-radius:12px;background:var(--bg-main);color:var(--text-main);font-size:16px}
.list{list-style:none;margin:0;padding:0;display:grid;gap:10px}
.card{width:100%;display:grid;gap:4px;text-align:left;padding:16px;border:1px solid var(--border);border-radius:14px;background:var(--bg-surface);color:var(--text-main);font:inherit;font-size:16px;min-height:64px}
.card span{opacity:.7;font-size:14px}
.picked{display:flex;gap:12px;justify-content:space-between;align-items:flex-start;padding:14px;border:1px solid var(--border);border-radius:14px;background:var(--bg-surface)}
.eyebrow{margin:0 0 4px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;opacity:.6}
.field{display:grid;gap:6px}
.field span{font-size:14px;opacity:.8}
.field textarea,.field select{width:100%;box-sizing:border-box;padding:14px;border:1px solid var(--border);border-radius:12px;background:var(--bg-main);color:var(--text-main);font:inherit;font-size:16px}
.photo{display:block;position:relative}
.photo input{position:absolute;inset:0;opacity:0;width:100%;height:100%}
.photo span{display:grid;place-items:center;min-height:64px;border:1px dashed var(--border);border-radius:14px;background:var(--bg-surface);font-size:16px}
.send{position:sticky;bottom:16px;width:100%;min-height:56px;border:0;border-radius:16px;background:var(--accent,#2f7ec4);color:#fff;font:inherit;font-size:18px;font-weight:600}
.send:disabled{opacity:.5}
.err{color:#ff8a8a}
.ok{color:#8fe3a8}
.hint{opacity:.6;font-size:14px}
</style>
