import { createApp } from 'vue'
import './style.css'
import './styles/digital.css'
import App from './App.vue'

const TOKEN_KEY = 'myapp.authToken'
const originalFetch = window.fetch.bind(window)
let sessionExpiredEventSent = false

window.fetch = async (...args) => {
  const response = await originalFetch(...args)
  const request = args[0]
  const requestUrl = typeof request === 'string' ? request : request?.url || ''
  const hasStoredToken = Boolean(localStorage.getItem(TOKEN_KEY))
  const isLoginRequest = requestUrl.includes('/api/auth/login')

  if (isLoginRequest && response.ok) {
    sessionExpiredEventSent = false
  }

  if (response.status === 401 && hasStoredToken && !isLoginRequest && !sessionExpiredEventSent) {
    sessionExpiredEventSent = true
    window.dispatchEvent(
      new CustomEvent('myapp:session-expired', {
        detail: {
          message: 'Вас давно не было. Требуется повторная авторизация.',
        },
      }),
    )
  }

  return response
}

createApp(App).mount('#app')
