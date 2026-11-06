<script setup>
import { computed, ref } from 'vue'
import atlasAuthLogo from '../assets/atlas-auth-logo.png'

const props = defineProps({
  sessionMessage: {
    type: String,
    default: '',
  },
})

const emit = defineEmits(['authenticated'])

const userNameOrEmail = ref('')
const password = ref('')
const loginHasCyrillic = computed(() => /\p{Script=Cyrillic}/u.test(userNameOrEmail.value))
const passwordHasCyrillic = computed(() => /\p{Script=Cyrillic}/u.test(password.value))
const errorMessage = ref('')
const isSubmitting = ref(false)

/** Отправляет учётные данные и сообщает родителю об успешном входе. */
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
    <section class="auth-intro" aria-labelledby="auth-heading">
      <img class="auth-emblem auth-emblem--intro" :src="atlasAuthLogo" alt="Атлас — учёт и обслуживание техники" width="1254" height="1254" />
      <p class="eyebrow">РАБОЧЕЕ ПРОСТРАНСТВО МЕХАНИКА</p>
      <h2 id="auth-heading">Техника<br /><span>под контролем</span></h2>
      <p>
        Обслуживание, ремонт и учёт техники<br />в единой системе предприятия.
      </p>
      <div class="auth-intro__tags">
        <span>Парк техники</span><span>ТО и ремонт</span><span>Материалы</span>
      </div>
    </section>
    <section class="auth-card macos-glass-panel" aria-labelledby="login-title">
      <img class="auth-emblem auth-emblem--card" :src="atlasAuthLogo" alt="Атлас — учёт и обслуживание техники" width="1254" height="1254" />
      <p class="eyebrow">ВХОД В СИСТЕМУ</p>
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
          :aria-describedby="loginHasCyrillic ? 'login-layout-warning' : undefined"
          placeholder="name@example.com"
          required
        />

        <p v-if="loginHasCyrillic" id="login-layout-warning" class="layout-warning" role="status">
          В логине введены русские буквы. Проверьте раскладку и при необходимости переключитесь на английскую.
        </p>

        <label for="password">Пароль</label>
        <input
          id="password"
          v-model="password"
          name="password"
          type="password"
          autocomplete="current-password"
          :aria-describedby="passwordHasCyrillic ? 'password-layout-warning' : undefined"
          placeholder="Введите пароль"
          required
        />

        <p v-if="passwordHasCyrillic" id="password-layout-warning" class="layout-warning" role="status">
          В пароле введены русские буквы. Проверьте раскладку и при необходимости переключитесь на английскую.
        </p>

        <p v-if="errorMessage" class="form-error" role="alert">
          {{ errorMessage }}
        </p>
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

<style scoped>
.auth-emblem {
  display: block;
  height: auto;
  object-fit: contain;
}

.auth-emblem--intro {
  width: clamp(180px, 19vw, 250px);
  margin: 0 0 28px;
  filter: drop-shadow(0 10px 22px rgb(0 0 0 / 12%));
}

.auth-intro > .eyebrow {
  margin-top: 0;
}

.auth-emblem--card {
  display: none;
}

@media (max-width: 760px) {
  .auth-emblem--card {
    display: block;
    width: 154px;
    max-width: 100%;
    margin: 0 auto 24px;
  }
}

.layout-warning {
  margin: 0;
  padding: 0.65rem 0.8rem;
  border-radius: 0.5rem;
  background: #fff4df;
  color: #855000;
  font-size: 0.9rem;
  line-height: 1.4;
}
</style>
