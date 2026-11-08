/** Ключи учётной сессии, которые хранятся постоянно или до закрытия вкладки. */
const KEYS = {
  token: 'myapp.authToken',
  role: 'myapp.userRole',
  permissions: 'myapp.userPermissions',
}

const storages = () => [window.localStorage, window.sessionStorage]

/** Читает значение сессии из постоянного или вкладкового хранилища. */
export function readAuth(name) {
  for (const storage of storages()) {
    try {
      const value = storage.getItem(KEYS[name])
      if (value !== null) return value
    } catch { /* Storage may be unavailable in private mode. */ }
  }
  return null
}

/** Сохраняет сессию: с «Запомнить меня» — постоянно, иначе до закрытия вкладки. */
export function writeAuth({ token, role, permissions }, remember) {
  clearAuth()
  const storage = remember ? window.localStorage : window.sessionStorage
  try {
    storage.setItem(KEYS.token, token)
    storage.setItem(KEYS.role, role ?? '')
    storage.setItem(KEYS.permissions, JSON.stringify(permissions ?? []))
  } catch { /* The session stays in memory when storage is blocked. */ }
}

/** Удаляет сессию из обоих хранилищ. */
export function clearAuth() {
  for (const storage of storages()) {
    for (const key of Object.values(KEYS)) {
      try { storage.removeItem(key) } catch { /* Ignore unavailable storage. */ }
    }
  }
}
