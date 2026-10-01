<script setup>
import { ref } from 'vue'
import brandLogo from '../assets/brand-logo.png'

const props = defineProps({
  sessionMessage: {
    type: String,
    default: '',
  },
})

const emit = defineEmits(['authenticated'])

const userNameOrEmail = ref('')
const password = ref('')
const errorMessage = ref('')
const isSubmitting = ref(false)

async function submitLogin() {
  errorMessage.value = ''
  isSubmitting.value = true

  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        userNameOrEmail: userNameOrEmail.value,
        password: password.value,
      }),
    })

    if (!response.ok) {
      errorMessage.value =
        response.status === 401
          ? 'Неверный логин или пароль.'
          : 'Не удалось выполнить вход. Попробуйте ещё раз.'
      return
    }

    const data = await response.json()
    if (!data.token) {
      errorMessage.value = 'Сервер не вернул токен авторизации.'
      return
    }

    emit('authenticated', data)
  } catch {
    errorMessage.value = 'Сервер недоступен. Проверьте подключение.'
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <main class="auth-page">
    <section class="auth-card macos-glass-panel" aria-labelledby="login-title">
      <img class="auth-brand-logo" :src="brandLogo" alt="ООО «Даль-Восток Сервис»" />
      <p class="eyebrow">ARM МЕХАНИК ООО "ДВС"</p>
      <h1 id="login-title">Добро пожаловать</h1>
      <p class="auth-subtitle">Войдите в аккаунт, чтобы продолжить работу.</p>

      <form class="auth-form" @submit.prevent="submitLogin">
        <label for="login">Логин или электронная почта</label>
        <input
          id="login"
          v-model.trim="userNameOrEmail"
          name="login"
          type="text"
          autocomplete="username"
          placeholder="name@example.com"
          required
        />

        <label for="password">Пароль</label>
        <input
          id="password"
          v-model="password"
          name="password"
          type="password"
          autocomplete="current-password"
          placeholder="Введите пароль"
          required
        />

        <p v-if="errorMessage" class="form-error" role="alert">{{ errorMessage }}</p>
        <p v-if="props.sessionMessage" class="form-error" role="alert">
          {{ props.sessionMessage }}
        </p>

        <button class="primary-button" type="submit" :disabled="isSubmitting">
          {{ isSubmitting ? 'Выполняется вход…' : 'Войти' }}
        </button>
      </form>
    </section>
  </main>
</template>
