<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  token: { type: String, required: true },
  navigationCollapsed: { type: Boolean, default: false },
})

const account = ref('')
const password = ref('')
const captchaCode = ref('')
const sessionId = ref('')
const captchaImage = ref('')
const connected = ref(false)
const expiresAt = ref('')
const equipmentCode = ref('')
const responseText = ref('')
const error = ref('')
const message = ref('')
const busy = ref(false)

const hasSession = computed(() => Boolean(sessionId.value))
const expiryLabel = computed(() => (expiresAt.value ? new Date(expiresAt.value).toLocaleString('ru-RU') : ''))

/** Обращается к нашему шлюзу SANY с токеном Атласа. */
async function api(path, options = {}) {
  const response = await fetch('/api/sany/' + path, {
    ...options,
    headers: { Authorization: 'Bearer ' + props.token, ...(options.body ? { 'Content-Type': 'application/json' } : {}) },
    signal: options.signal ?? AbortSignal.timeout(120000),
  })
  const body = await response.json().catch(() => null)
  if (!response.ok) throw Error(body?.message || 'Портал SANY недоступен.')
  return body
}
const timeoutMessage = e => (e.name === 'TimeoutError' ? 'Портал SANY не ответил.' : (e.message || 'Не удалось выполнить действие.'))

/** Запрашивает капчу и создаёт подключение. */
async function connect() {
  if (busy.value) return
  error.value = ''
  message.value = ''
  if (!account.value.trim() || !password.value) { error.value = 'Укажите логин и пароль SANY.'; return }
  busy.value = true
  try {
    const body = await api('session', {
      method: 'POST',
      body: JSON.stringify({ account: account.value.trim(), password: password.value }),
      signal: AbortSignal.timeout(60000),
    })
    sessionId.value = body.sessionId
    captchaImage.value = `data:${body.contentType};base64,${body.captchaImage}`
    expiresAt.value = body.expiresAt
    captchaCode.value = ''
  } catch (e) {
    error.value = timeoutMessage(e)
  } finally {
    busy.value = false
  }
}

/** Входит в портал с кодом с картинки. */
async function login() {
  if (busy.value) return
  error.value = ''
  message.value = ''
  if (!captchaCode.value.trim()) { error.value = 'Введите код с картинки.'; return }
  busy.value = true
  try {
    const body = await api(`session/${sessionId.value}/login`, {
      method: 'POST',
      body: JSON.stringify({ captchaCode: captchaCode.value.trim() }),
      signal: AbortSignal.timeout(60000),
    })
    connected.value = true
    expiresAt.value = body.expiresAt
    password.value = ''
    message.value = 'Подключение к SANY установлено.'
  } catch (e) {
    error.value = timeoutMessage(e)
  } finally {
    busy.value = false
  }
}

/** Проксирует запрос к порталу через наш шлюз. */
async function query(path, body) {
  return api(`session/${sessionId.value}/query`, {
    method: 'POST',
    body: JSON.stringify({ path, body }),
  })
}

/** Проверяет, что токен принят порталом. */
async function check() {
  if (busy.value || !connected.value) return
  busy.value = true
  error.value = ''
  responseText.value = ''
  try {
    responseText.value = JSON.stringify(await query('/admin-server/language/languageList', null), null, 2)
    message.value = 'Портал принял токен.'
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/** Ищет технику по коду и показывает ответ портала. */
async function search() {
  if (busy.value || !connected.value) return
  busy.value = true
  error.value = ''
  message.value = ''
  responseText.value = ''
  try {
    const data = await query('/customer/equipmentInfo/queryByCondition', {
      equipmentCode: equipmentCode.value.trim(),
      code: equipmentCode.value.trim(),
      equnr: equipmentCode.value.trim(),
      vin: equipmentCode.value.trim(),
      language: 'ru',
      currentPage: 1,
      pageSize: 20,
    })
    responseText.value = JSON.stringify(data, null, 2)
    message.value = 'Ответ портала получен.'
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/** Закрывает подключение и стирает его на сервере. */
async function disconnect() {
  if (!sessionId.value) return
  try {
    await fetch(`/api/sany/session/${sessionId.value}`, {
      method: 'DELETE',
      headers: { Authorization: 'Bearer ' + props.token },
    })
  } catch { /* подключение всё равно сбрасываем. */ }
  sessionId.value = ''
  captchaImage.value = ''
  connected.value = false
  password.value = ''
  equipmentCode.value = ''
  responseText.value = ''
  message.value = ''
  expiresAt.value = ''
}
</script>

<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div><p class="eyebrow">ТАБЛИЦЫ</p><h1>SANY: запчасти</h1></div>
      <button v-if="hasSession" type="button" class="secondary-button" :disabled="busy" @click="disconnect">Отключить</button>
    </header>

    <section class="macos-glass-panel sany-panel">
      <h2>Подключение к SIS</h2>
      <p class="sany-hint">Вход выполняется в портал SANY. Пароль хранится только в памяти до ввода капчи и стирается после входа.</p>
      <p v-if="error" class="form-message form-message--error" role="alert">{{ error }}</p>
      <p v-if="message" class="form-message form-message--success" role="status">{{ message }}</p>

      <ol v-if="!connected" class="sany-steps">
        <li>
          <div class="sany-grid">
            <label class="vehicle-field"><span>Логин SANY</span><input v-model="account" autocomplete="username" :disabled="busy || hasSession" /></label>
            <label class="vehicle-field"><span>Пароль SANY</span><input v-model="password" type="password" autocomplete="current-password" :disabled="busy || hasSession" /></label>
          </div>
          <button type="button" class="primary-button" :disabled="busy" @click="connect">{{ hasSession ? 'Получить капчу заново' : 'Продолжить' }}</button>
        </li>
        <li v-if="hasSession">
          <img v-if="captchaImage" :src="captchaImage" alt="Капча SANY" class="sany-captcha" />
          <div class="sany-grid">
            <label class="vehicle-field"><span>Код с картинки</span><input v-model="captchaCode" autocomplete="off" :disabled="busy" @keyup.enter="login" /></label>
          </div>
          <button type="button" class="primary-button" :disabled="busy" @click="login">{{ busy ? 'Вход…' : 'Войти в SANY' }}</button>
        </li>
      </ol>

      <template v-else>
        <p class="sany-hint">Подключение активно до {{ expiryLabel || 'истечения токена' }}. Поиск выполняется от вашей учётной записи SANY.</p>
        <div class="sany-grid">
          <label class="vehicle-field"><span>Код техники SANY</span><input v-model="equipmentCode" placeholder="Например, ET150SE30008" :disabled="busy" @keyup.enter="search" /></label>
        </div>
        <div class="sany-actions">
          <button type="button" class="primary-button" :disabled="busy || !equipmentCode.trim()" @click="search">{{ busy ? 'Запрос…' : 'Найти' }}</button>
          <button type="button" class="secondary-button" :disabled="busy" @click="check">Проверить подключение</button>
        </div>
        <pre v-if="responseText" class="sany-response" aria-live="polite">{{ responseText }}</pre>
      </template>
    </section>
  </main>
</template>

<style scoped>
.sany-panel { display: grid; gap: 14px; padding: 18px; }
.sany-hint { margin: 0; color: var(--text-secondary); font-size: 13px; }
.sany-steps { display: grid; gap: 16px; margin: 0; padding-left: 18px; }
.sany-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; margin-bottom: 10px; }
.sany-captcha { display: block; width: 180px; height: 60px; object-fit: contain; margin-bottom: 10px; border: 1px solid var(--border); border-radius: 8px; background: #fff; }
.sany-actions { display: flex; flex-wrap: wrap; gap: 10px; }
.sany-response { max-height: 420px; overflow: auto; margin: 0; padding: 12px; border: 1px solid var(--border); border-radius: 8px; background: var(--bg-main); font-size: 12px; white-space: pre-wrap; }
</style>
