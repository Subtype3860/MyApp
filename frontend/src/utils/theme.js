import { ref } from 'vue'
const storageKey = 'myapp.theme'
export const themeOptions = [
  { id: 'system', label: 'Как в системе' },
  { id: 'light', label: 'Светлая' },
  { id: 'macos', label: 'macOS' },
  { id: 'dark', label: 'Тёмная' },
  { id: 'digital', label: 'Цифровая' },
  { id: 'midnight', label: 'Полночь' },
  { id: 'graphite', label: 'Графитовая' },
  { id: 'lavender', label: 'Лавандовая' },
  { id: 'sand', label: 'Песочная' },
  { id: 'ocean', label: 'Морская' },
  { id: 'forest', label: 'Лесная' },
]
const normalize = value => themeOptions.some(theme => theme.id === value) ? value : 'system'
export const selectedTheme = ref('system')
let systemTheme
function applyTheme() {
  document.documentElement.dataset.theme = selectedTheme.value === 'system'
    ? (systemTheme.matches ? 'dark' : 'light') : selectedTheme.value
}
/** Применяет тему даже при запрете хранения настроек в браузере. */
export function selectTheme(value) {
  selectedTheme.value = normalize(value)
  applyTheme()
  try { localStorage.setItem(storageKey, selectedTheme.value) } catch { /* Действует до перезагрузки. */ }
}
/** Восстанавливает тему и отслеживает изменения системы и соседних вкладок. */
export function initializeTheme() {
  systemTheme = window.matchMedia('(prefers-color-scheme: dark)')
  try { selectedTheme.value = normalize(localStorage.getItem(storageKey)) } catch { selectedTheme.value = 'system' }
  applyTheme()
  systemTheme.addEventListener('change', applyTheme)
  window.addEventListener('storage', event => {
    if (event.key === storageKey || event.key === null) {
      selectedTheme.value = normalize(event.newValue)
      applyTheme()
    }
  })
}
