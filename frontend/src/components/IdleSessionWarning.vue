<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'

const emit = defineEmits(['expired'])
const idleMilliseconds = 10 * 60 * 1000
const warningMilliseconds = 30 * 1000
const activityEvents = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'scroll']
const dialog = ref(null)
const warningVisible = ref(false)
const remainingSeconds = ref(30)
let lastActivity = Date.now()
let idleTimer
let warningTimer
let countdownTimer
let expired = false

/** Останавливает таймеры контроля бездействия и обратного отсчёта. */
function clearTimers() {
  clearTimeout(idleTimer)
  clearTimeout(warningTimer)
  clearInterval(countdownTimer)
}
/** Сообщает об истечении времени пользовательской сессии. */
function expire() {
  if (expired) return
  expired = true
  clearTimers()
  dialog.value?.close()
  emit('expired')
}
/** Обновляет оставшееся время до завершения сессии. */
function updateCountdown() {
  remainingSeconds.value = Math.max(0, Math.ceil((lastActivity + idleMilliseconds + warningMilliseconds - Date.now()) / 1000))
  if (remainingSeconds.value === 0) expire()
}
/** Проверяет срок бездействия и переключает предупреждение или завершает сессию. */
async function checkDeadline() {
  if (expired) return
  const now = Date.now()
  const warningDeadline = lastActivity + idleMilliseconds
  const logoutDeadline = warningDeadline + warningMilliseconds
  if (now >= logoutDeadline) {
    expire()
    return
  }
  if (now >= warningDeadline) {
    if (!warningVisible.value) {
      warningVisible.value = true
      updateCountdown()
      warningTimer = setTimeout(expire, logoutDeadline - now)
      countdownTimer = setInterval(updateCountdown, 250)
      await nextTick()
      if (!expired && dialog.value && !dialog.value.open) dialog.value.showModal()
    } else updateCountdown()
    return
  }
  clearTimeout(idleTimer)
  idleTimer = setTimeout(checkDeadline, warningDeadline - now)
}
/** Учитывает действие пользователя при контроле времени бездействия. */
function activity() {
  if (expired) return
  // Activity after the deadline must show the warning, rather than cancel it.
  if (warningVisible.value || Date.now() >= lastActivity + idleMilliseconds) {
    checkDeadline()
    return
  }
  lastActivity = Date.now()
  clearTimeout(idleTimer)
  idleTimer = setTimeout(checkDeadline, idleMilliseconds)
}
/** Продлевает активную работу после подтверждения пользователя. */
function continueWorking() {
  if (Date.now() >= lastActivity + idleMilliseconds + warningMilliseconds) {
    expire()
    return
  }
  clearTimers()
  warningVisible.value = false
  dialog.value?.close()
  lastActivity = Date.now()
  idleTimer = setTimeout(checkDeadline, idleMilliseconds)
}
/** Перепроверяет срок сессии при возвращении к вкладке приложения. */
function checkOnReturn() {
  if (!document.hidden) checkDeadline()
}
onMounted(() => {
  lastActivity = Date.now()
  idleTimer = setTimeout(checkDeadline, idleMilliseconds)
  activityEvents.forEach(event => document.addEventListener(event, activity, { passive: true, capture: true }))
  document.addEventListener('visibilitychange', checkOnReturn)
  window.addEventListener('focus', checkOnReturn)
})
onBeforeUnmount(() => {
  clearTimers()
  activityEvents.forEach(event => document.removeEventListener(event, activity, true))
  document.removeEventListener('visibilitychange', checkOnReturn)
  window.removeEventListener('focus', checkOnReturn)
  dialog.value?.close()
})
</script>

<template>
  <dialog ref="dialog" class="idle-session-warning" aria-labelledby="idle-session-title" aria-describedby="idle-session-description" @cancel.prevent>
    <h2 id="idle-session-title">Вы ещё работаете?</h2>
    <p id="idle-session-description">Вы не проявляли активность 10 минут. Через {{ remainingSeconds }} сек. сеанс завершится и откроется страница авторизации.</p>
    <button type="button" autofocus @click="continueWorking">Продолжить работу</button>
  </dialog>
</template>

<style scoped>
.idle-session-warning { width: min(440px, calc(100vw - 2rem)); box-sizing: border-box; padding: 1.5rem; border: 1px solid var(--border); border-radius: 1rem; background: var(--bg-surface); color: var(--text-main); box-shadow: 0 12px 48px rgb(0 0 0 / 20%); }
.idle-session-warning::backdrop { background: rgb(0 0 0 / 45%); }
.idle-session-warning h2 { margin: 0 0 0.75rem; font-size: 1.25rem; }
.idle-session-warning p { margin: 0 0 1.25rem; line-height: 1.5; }
.idle-session-warning button { width: 100%; padding: 0.75rem 1rem; border: 0; border-radius: 0.65rem; background: var(--accent); color: var(--bg-surface); font: inherit; font-weight: 650; cursor: pointer; }
</style>
