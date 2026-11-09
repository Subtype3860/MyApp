import { createApp } from 'vue'
import './style.css'
import './styles/themes.css'
import { initializeTheme } from './utils/theme'
import { readAuth } from './utils/authStorage'
import './styles/digital.css'
import App from './App.vue'

const originalFetch = window.fetch.bind(window)
let sessionExpiredEventSent = false

window.fetch = async (...args) => {
  const response = await originalFetch(...args)
  const request = args[0]
  const requestUrl = typeof request === 'string' ? request : request?.url || ''
  const hasStoredToken = Boolean(readAuth('token'))
  const isLoginRequest = requestUrl.includes('/api/auth/login')
  // Ошибки интеграции SANY не означают, что сессия Атласа завершена.
  const isSanyRequest = requestUrl.includes('/api/sany/')

  if (isLoginRequest && response.ok) {
    sessionExpiredEventSent = false
  }

  if (response.status === 401 && hasStoredToken && !isLoginRequest && !isSanyRequest && !sessionExpiredEventSent) {
    sessionExpiredEventSent = true
    window.dispatchEvent(
      new CustomEvent('myapp:session-expired', {
        detail: {
          message: 'Сессия завершена или отозвана. Требуется повторная авторизация.',
        },
      }),
    )
  }

  return response
}

initializeTheme()
createApp(App).mount('#app')

// После обновления сайта старые вкладки могут ссылаться на уже пересобранные файлы:
// один раз перезагружаем страницу, чтобы подхватить новый index.html.
window.addEventListener('vite:preloadError', event => {
  event.preventDefault()
  if (sessionStorage.getItem('myapp:preload-reloaded')) return
  sessionStorage.setItem('myapp:preload-reloaded', '1')
  window.location.reload()
})