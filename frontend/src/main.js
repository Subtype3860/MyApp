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

  if (isLoginRequest && response.ok) {
    sessionExpiredEventSent = false
  }

  if (response.status === 401 && hasStoredToken && !isLoginRequest && !sessionExpiredEventSent) {
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
