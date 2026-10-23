<script setup>
import { onBeforeUnmount, ref } from 'vue'
import { themeOptions, selectedTheme, selectTheme } from '../utils/theme'

const props = defineProps({
  navigationCollapsed: { type: Boolean, required: true },
  token: { type: String, required: true },
})
const emit = defineEmits(['profile-updated'])

const profile = ref(null)
const userName = ref('')
const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const avatarUrl = ref('')
const selectedAvatar = ref(null)
const errorMessage = ref('')
const successMessage = ref('')
const isLoading = ref(false)
const isSubmitting = ref(false)

/** Формирует заголовки запроса с токеном текущей сессии. */
function headers(json = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  }
}

/** Заменяет URL аватара, освобождая предыдущий адрес объекта Blob. */
function replaceAvatarUrl(url) {
  if (avatarUrl.value?.startsWith('blob:')) {
    URL.revokeObjectURL(avatarUrl.value)
  }
  avatarUrl.value = url
}

/** Извлекает понятное сообщение об ошибке из ответа API. */
async function readError(response, fallback) {
  const problem = await response.json().catch(() => null)
  return problem?.errors
    ? Object.values(problem.errors).flat()[0]
    : problem?.detail || problem?.title || fallback
}

/** Загружает аватар пользователя с авторизацией. */
async function loadAvatar() {
  if (!profile.value?.hasAvatar) {
    replaceAvatarUrl('')
    return
  }
  const response = await fetch('/api/profile/avatar', { headers: headers() })
  if (response.ok) {
    replaceAvatarUrl(URL.createObjectURL(await response.blob()))
  }
}

/** Загружает профиль текущего пользователя. */
async function loadProfile() {
  isLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch('/api/profile', { headers: headers() })
    if (!response.ok) {
      throw new Error('Не удалось загрузить профиль.')
    }
    profile.value = await response.json()
    userName.value = profile.value.userName
    await loadAvatar()
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isLoading.value = false
  }
}

/** Проверяет выбранный файл аватара и готовит его предварительный просмотр. */
function selectAvatar(event) {
  const file = event.target.files?.[0]
  selectedAvatar.value = file ?? null
  if (file) {
    replaceAvatarUrl(URL.createObjectURL(file))
  } else {
    loadAvatar()
  }
}

/** Отправляет выбранное изображение аватара на сервер. */
async function uploadAvatar() {
  if (!selectedAvatar.value) return
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''
  try {
    const body = new FormData()
    body.append('avatar', selectedAvatar.value)
    const response = await fetch('/api/profile/avatar', {
      method: 'POST',
      headers: headers(),
      body,
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось загрузить аватар.'))
    }
    profile.value = await response.json()
    selectedAvatar.value = null
    await loadAvatar()
    emit('profile-updated')
    successMessage.value = 'Аватар сохранён.'
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSubmitting.value = false
  }
}

/** Удаляет аватар на сервере и обновляет его отображение. */
async function deleteAvatar() {
  const response = await fetch('/api/profile/avatar', {
    method: 'DELETE',
    headers: headers(),
  })
  if (!response.ok) {
    errorMessage.value = await readError(response, 'Не удалось удалить аватар.')
    return
  }
  profile.value = await response.json()
  selectedAvatar.value = null
  replaceAvatarUrl('')
  emit('profile-updated')
  successMessage.value = 'Аватар удалён.'
}

/** Сохраняет изменения профиля и обрабатывает ответ сервера. */
async function saveProfile() {
  errorMessage.value = ''
  successMessage.value = ''
  if (newPassword.value !== confirmPassword.value) {
    errorMessage.value = 'Новые пароли не совпадают.'
    return
  }
  isSubmitting.value = true
  try {
    const response = await fetch('/api/profile', {
      method: 'PUT',
      headers: headers(true),
      body: JSON.stringify({
        userName: userName.value,
        currentPassword: currentPassword.value || null,
        newPassword: newPassword.value || null,
      }),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось сохранить профиль.'))
    }
    const saved = await response.json()
    const credentialsChanged = Boolean(newPassword.value) || saved.userName !== profile.value.userName
    if (credentialsChanged) {
      window.dispatchEvent(new CustomEvent('myapp:session-expired', { detail: { message: 'Данные входа изменены. Войдите снова.' } }))
      return
    }
    profile.value = saved
    userName.value = profile.value.userName
    currentPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
    emit('profile-updated')
    successMessage.value = 'Профиль сохранён.'
  } catch (error) {
    errorMessage.value = error.message
  } finally {
    isSubmitting.value = false
  }
}

onBeforeUnmount(() => replaceAvatarUrl(''))
loadProfile()
</script>

<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div><p class="eyebrow">УЧЁТНАЯ ЗАПИСЬ</p><h1>Редактировать профиль</h1></div>
    </header>

    <section class="macos-glass-panel profile-panel">
      <p v-if="isLoading" class="table-message">Загрузка профиля...</p>
      <template v-else-if="profile">
        <div class="profile-summary">
          <div class="profile-avatar profile-avatar--large">
            <img v-if="avatarUrl" :src="avatarUrl" alt="Аватар пользователя" />
            <span v-else>{{ profile.firstName?.[0] || 'П' }}</span>
          </div>
          <div>
            <h2>{{ [profile.lastName, profile.firstName, profile.middleName].filter(Boolean).join(' ') }}</h2>
            <p>{{ profile.position }}</p>
          </div>
        </div>

        <section class="profile-section theme-settings" aria-labelledby="theme-settings-title">
          <h3 id="theme-settings-title">Оформление</h3>
          <label for="profile-theme">Тема интерфейса</label>
          <select id="profile-theme" :value="selectedTheme" @change="selectTheme($event.target.value)">
            <option v-for="theme in themeOptions" :key="theme.id" :value="theme.id">{{ theme.label }}</option>
          </select>
          <p class="profile-hint">Применяется сразу и сохраняется в этом браузере.</p>
        </section>
        <div class="profile-section">
          <h3>Аватар</h3>
          <input
            type="file"
            accept="image/jpeg,image/png,image/webp"
            @change="selectAvatar"
          />
          <p class="profile-hint">JPEG, PNG или WebP, не более 2 МБ.</p>
          <div class="profile-actions">
            <button class="primary-button" type="button" :disabled="!selectedAvatar || isSubmitting" @click="uploadAvatar">
              Сохранить аватар
            </button>
            <button v-if="profile.hasAvatar" class="secondary-button" type="button" @click="deleteAvatar">
              Удалить аватар
            </button>
          </div>
        </div>

        <form class="profile-section profile-form" @submit.prevent="saveProfile">
          <h3>Логин и пароль</h3>
          <label>
            <span>Логин</span>
            <input v-model.trim="userName" type="text" minlength="3" maxlength="50" required />
          </label>
          <label>
            <span>Текущий пароль</span>
            <input v-model="currentPassword" type="password" autocomplete="current-password" />
          </label>
          <label>
            <span>Новый пароль</span>
            <input v-model="newPassword" type="password" minlength="6" autocomplete="new-password" />
          </label>
          <label>
            <span>Повторите новый пароль</span>
            <input v-model="confirmPassword" type="password" minlength="6" autocomplete="new-password" />
          </label>
          <button class="primary-button" type="submit" :disabled="isSubmitting">
            {{ isSubmitting ? 'Сохранение...' : 'Сохранить профиль' }}
          </button>
        </form>
      </template>

      <p v-if="errorMessage" class="form-message form-message--error" role="alert">{{ errorMessage }}</p>
      <p v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</p>
    </section>
  </main>
</template>
