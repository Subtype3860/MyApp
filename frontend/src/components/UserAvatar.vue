<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps({
  token: {
    type: String,
    required: true,
  },
  fallback: {
    type: String,
    default: 'П',
  },
  label: {
    type: String,
    default: 'Профиль пользователя',
  },
})

const avatarUrl = ref('')

/** Освобождает временный URL аватара и сбрасывает его отображение. */
function clearAvatar() {
  if (avatarUrl.value) URL.revokeObjectURL(avatarUrl.value)
  avatarUrl.value = ''
}

/** Загружает аватар пользователя с авторизацией. */
async function loadAvatar() {
  clearAvatar()
  const response = await fetch('/api/profile/avatar', {
    headers: { Authorization: `Bearer ${props.token}` },
  })

  if (response.ok) {
    avatarUrl.value = URL.createObjectURL(await response.blob())
  }
}

watch(() => props.token, loadAvatar, { immediate: true })
onBeforeUnmount(clearAvatar)
</script>

<template>
  <div class="user-avatar" :aria-label="label">
    <img v-if="avatarUrl" :src="avatarUrl" alt="" />
    <span v-else>{{ fallback }}</span>
  </div>
</template>
