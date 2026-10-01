<script setup>
import UserAvatar from './UserAvatar.vue'

defineProps({
  navigationCollapsed: {
    type: Boolean,
    required: true,
  },
  token: {
    type: String,
    required: true,
  },
})
</script>

<template>
<!--   <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div>
        <p class="eyebrow">ОБЗОР</p>
        <h1>Главная</h1>
      </div>
      <UserAvatar :token="token" />
    </header>

    <section class="welcome-card">
      <div>
        <p class="eyebrow">ДОБРО ПОЖАЛОВАТЬ</p>
        <h2>Рады видеть вас снова!</h2>
        <p>Вы успешно авторизованы. Здесь будет отображаться основная информация приложения.</p>
      </div>
      <span class="welcome-symbol" aria-hidden="true">✓</span>
    </section>

    <section class="stats-grid" aria-label="Сводная информация">
      <article class="stat-card macos-glass-panel">
        <span>Активные проекты</span>
        <strong>0</strong>
        <p>Создайте первый проект</p>
      </article>
      <article class="stat-card macos-glass-panel">
        <span>Задачи</span>
        <strong>0</strong>
        <p>Нет текущих задач</p>
      </article>
      <article class="stat-card macos-glass-panel">
        <span>Уведомления</span>
        <strong>0</strong>
        <p>Всё просмотрено</p>
      </article>
    </section>
  </main> -->
</template>
