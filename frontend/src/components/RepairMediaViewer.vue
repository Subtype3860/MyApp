<script setup>
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps({
  open: { type: Boolean, required: true },
  items: { type: Array, required: true },
  mediaType: { type: String, required: true },
  token: { type: String, required: true },
})

const emit = defineEmits(['close'])
const dialog = ref(null)
const videoElement = ref(null)
const mediaUrl = ref('')
const mediaName = ref('')
const currentIndex = ref(0)
const isLoading = ref(false)
const mediaError = ref('')
const zoomScale = ref(1)
const zoomX = ref(0)
const zoomY = ref(0)
const pinchStartDistance = ref(0)
const pinchStartScale = ref(1)
let requestId = 0
let touchStartX = 0
let swipeEnabled = false

watch(() => props.open, async (open) => {
  if (!open) {
    closeDialog()
    return
  }

  await nextTick()
  if (!props.open) return
  if (!dialog.value?.open) dialog.value?.showModal()
  currentIndex.value = 0
  await loadCurrentMedia()
})
onBeforeUnmount(closeDialog)

function mediaRoute(media) {
  const work = media?.source === 'work'
  if (work) return props.mediaType === 'video' ? 'work-videos' : 'work-photos'
  return props.mediaType === 'video' ? 'defect-videos' : 'defect-photos'
}

function authHeaders() {
  return { Authorization: `Bearer ${props.token}` }
}

async function loadCurrentMedia() {
  const item = props.items[currentIndex.value]
  if (!item) return

  const currentRequestId = ++requestId
  stopVideo()
  releaseBlobUrl()
  mediaUrl.value = ''
  mediaName.value = item.fileName || item.media?.fileName || 'Медиафайл'
  isLoading.value = true
  mediaError.value = ''
  zoomScale.value = 1
  zoomX.value = 0
  zoomY.value = 0

  try {
    const route = mediaRoute(item.media ?? item)
    let url
    if (props.mediaType === 'video') {
      const response = await fetch(
        `/api/vehicles/${route}/${item.id ?? item.media?.id}/stream-ticket`,
        { method: 'POST', headers: authHeaders() },
      )
      if (!response.ok) throw new Error('Видео недоступно.')
      const result = await response.json()
      url = result.url
    } else {
      const response = await fetch(
        `/api/vehicles/${route}/${item.id ?? item.media?.id}`,
        { headers: authHeaders() },
      )
      if (!response.ok) throw new Error('Изображение недоступно.')
      url = URL.createObjectURL(await response.blob())
    }

    if (currentRequestId !== requestId) {
      if (url.startsWith('blob:')) URL.revokeObjectURL(url)
      return
    }
    mediaUrl.value = url
    if (props.mediaType === 'image') isLoading.value = false
  } catch (error) {
    if (currentRequestId === requestId) {
      isLoading.value = false
      mediaError.value = error.message || 'Медиафайл недоступен.'
    }
  }
}

function navigate(step) {
  if (props.items.length < 2) return
  currentIndex.value =
    (currentIndex.value + step + props.items.length) % props.items.length
  loadCurrentMedia()
}

function stopVideo() {
  const video = videoElement.value
  if (!video) return
  video.pause()
  if (video.readyState > 0) video.currentTime = 0
  video.removeAttribute('src')
  video.load()
  videoElement.value = null
}

function releaseBlobUrl() {
  if (mediaUrl.value.startsWith('blob:')) URL.revokeObjectURL(mediaUrl.value)
}

function closeDialog() {
  requestId++
  stopVideo()
  releaseBlobUrl()
  if (dialog.value?.open) dialog.value.close()
  mediaUrl.value = ''
  mediaName.value = ''
  isLoading.value = false
  mediaError.value = ''
  currentIndex.value = 0
}

function close() {
  emit('close')
}

function onMediaLoaded() {
  isLoading.value = false
  mediaError.value = ''
}

function onMediaError() {
  isLoading.value = false
  mediaError.value = props.mediaType === 'video'
    ? 'Видео недоступно.'
    : 'Изображение недоступно.'
}

function startSwipe(event) {
  swipeEnabled = event.touches.length === 1
  touchStartX = event.changedTouches[0]?.clientX ?? 0
  if (event.touches.length === 2) {
    pinchStartDistance.value = touchDistance(event.touches)
    pinchStartScale.value = zoomScale.value
  }
}

function endSwipe(event) {
  if (swipeEnabled && props.items.length > 1) {
    const endX = event.changedTouches[0]?.clientX ?? touchStartX
    const distance = endX - touchStartX
    if (Math.abs(distance) > 70) navigate(distance < 0 ? 1 : -1)
  }
  if (event.touches.length < 2) pinchStartDistance.value = 0
}

function touchDistance(touches) {
  const [first, second] = touches
  return Math.hypot(second.clientX - first.clientX, second.clientY - first.clientY)
}

function movePinch(event) {
  if (event.touches.length !== 2 || !pinchStartDistance.value) return
  const scale = pinchStartScale.value *
    (touchDistance(event.touches) / pinchStartDistance.value)
  zoomScale.value = Math.min(Math.max(scale, 1), 4)
  if (zoomScale.value === 1) {
    zoomX.value = 0
    zoomY.value = 0
  }
}

function toggleZoom() {
  zoomScale.value = zoomScale.value === 1 ? 2.5 : 1
  if (zoomScale.value === 1) {
    zoomX.value = 0
    zoomY.value = 0
  }
}
</script>

<template>
  <dialog
    ref="dialog"
    class="repair-zoom repair-media-dialog"
    @click.self="close"
    @cancel.prevent="close"
    @keydown.left.prevent="navigate(-1)"
    @keydown.right.prevent="navigate(1)"
  >
    <div class="repair-media-viewer">
      <div class="repair-media-viewer-header">
        <strong>{{ mediaName }}</strong>
        <span v-if="items.length">{{ currentIndex + 1 }} / {{ items.length }}</span>
      </div>
      <div class="repair-media-viewer-content">
        <button
          v-if="items.length > 1"
          class="repair-media-nav repair-media-nav--prev"
          type="button"
          aria-label="Предыдущее вложение"
          @click="navigate(-1)"
        >
          ‹
        </button>
        <video
          v-if="mediaType === 'video' && mediaUrl"
          :key="mediaUrl"
          ref="videoElement"
          :src="mediaUrl"
          preload="metadata"
          controls
          playsinline
          @loadeddata="onMediaLoaded"
          @error="onMediaError"
          @touchstart="startSwipe"
          @touchend="endSwipe"
        ></video>
        <img
          v-else-if="mediaType === 'image' && mediaUrl"
          :src="mediaUrl"
          :alt="mediaName"
          :style="{ transform: `translate(${zoomX}px, ${zoomY}px) scale(${zoomScale})` }"
          @load="onMediaLoaded"
          @error="onMediaError"
          @dblclick="toggleZoom"
          @touchstart="startSwipe"
          @touchmove.prevent="movePinch"
          @touchend="endSwipe"
        />
        <div v-if="isLoading" class="repair-media-loading" role="status">
          Загрузка...
        </div>
        <div v-if="mediaError" class="repair-media-error" role="alert">
          {{ mediaError }}
        </div>
        <button
          v-if="items.length > 1"
          class="repair-media-nav repair-media-nav--next"
          type="button"
          aria-label="Следующее вложение"
          @click="navigate(1)"
        >
          ›
        </button>
      </div>
      <button class="repair-media-viewer-close" type="button" aria-label="Закрыть" @click="close">
        ×
      </button>
    </div>
  </dialog>
</template>
