<script setup>
import { computed, inject, onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = defineProps({ work: { type: Object, required: true } })
const context = inject('workParticipation', null)
const baseUrl = computed(() => props.work.workKind === 'auxiliary' ? `/api/auxiliary-works/${props.work.id}` : `/api/vehicles/works/${props.work.id}`)
const participants = ref(props.work.participants ?? [])
const loaded = ref(Array.isArray(props.work.participants))
const busy = ref(false), error = ref('')
const controller = new AbortController()
const ownParticipation = computed(() => participants.value.find(row => String(row.userId).toLowerCase() === String(context?.userId.value ?? '').toLowerCase()))
const canJoin = computed(() => context?.userId.value && !['pending', 'approved'].includes(ownParticipation.value?.status))
const statusLabels = { pending: 'Ожидает подтверждения', approved: 'Подтверждено', rejected: 'Отклонено' }
watch(() => props.work.participants, value => {
  if (Array.isArray(value)) { participants.value = value; loaded.value = true }
})
/** Формирует заголовки запроса с токеном текущей сессии. */
function headers() { return { Authorization: `Bearer ${context?.token.value}`, 'Content-Type': 'application/json' } }
/** Обновляет список участников работы. */
async function refresh() {
  if (!context) return
  const response = await fetch(`${baseUrl.value}/participants`, { headers: headers(), signal: controller.signal })
  if (!response.ok) throw new Error('Не удалось загрузить исполнителей.')
  const result = await response.json()
  participants.value = result
  loaded.value = true
}
/** Отправляет действие участия в работе и обновляет список участников. */
async function act(participant, approved) {
  if (busy.value || !context) return
  busy.value = true
  error.value = ''
  try {
    const response = await fetch(participant
      ? `${baseUrl.value}/participants/${participant.id}/review`
      : `${baseUrl.value}/participants`, {
      method: 'POST', headers: headers(), signal: controller.signal,
      ...(participant ? { body: JSON.stringify({ approved }) } : {}),
    })
    if (!response.ok) {
      const result = await response.json().catch(() => null)
      throw new Error(result?.message || (typeof result === 'string' ? result : null) || 'Не удалось сохранить участие.')
    }
    await refresh()
  } catch (failure) {
    if (failure.name !== 'AbortError') error.value = failure.message
  } finally { busy.value = false }
}
onMounted(async () => {
  // Journals already contain the list. Fetch only for legacy clients/data without this field.
  if (loaded.value || !context) return
  try { await refresh() } catch { /* Keep the previously recorded performer if the API is unavailable. */ }
})
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <section class="work-participants" aria-label="Исполнители работы">
    <strong>Исполнители</strong>
    <ul v-if="participants.length">
      <li v-for="participant in participants" :key="participant.id">
        <div><span>{{ [participant.position, participant.name].filter(Boolean).join(' · ') }}</span>
          <small :class="`work-participants__status--${participant.status}`" :title="participant.reviewedByName ? `Рассмотрел: ${participant.reviewedByName}` : ''">{{ participant.status === 'approved' && !participant.reviewedBy ? 'Ранее указанный исполнитель' : statusLabels[participant.status] || participant.status }}</small>
        </div>
        <div v-if="context?.canReview.value && participant.status === 'pending'" class="work-participants__actions">
          <button type="button" :disabled="busy" :aria-label="`Подтвердить участие: ${participant.name}`" @click="act(participant, true)">Подтвердить</button>
          <button type="button" :disabled="busy" :aria-label="`Отклонить участие: ${participant.name}`" @click="act(participant, false)">Отклонить</button>
        </div>
      </li>
    </ul>
    <p v-else-if="!loaded && (work.performerName || work.performer)" class="work-item__performer">{{ work.performerPosition || 'Должность не указана' }} · {{ work.performerName || work.performer }}</p>
    <p v-else>Подтверждённых исполнителей пока нет.</p>
    <button v-if="canJoin" class="work-participants__join" type="button" :disabled="busy" @click="act(null)">{{ ownParticipation?.status === 'rejected' ? 'Повторно заявить участие' : 'Я участвовал в работе' }}</button>
    <p v-if="ownParticipation?.status === 'pending'" class="work-participants__hint">Ваше участие ожидает подтверждения механика.</p>
    <p v-if="error" role="alert" class="work-participants__error">{{ error }}</p>
  </section>
</template>

<style scoped>
.work-participants { display: grid; gap: 8px; font-size: 13px; color: var(--text-secondary); }
.work-participants strong { font-weight: 600; }
.work-participants ul { padding: 0; margin: 0; list-style: none; display: grid; gap: 8px; }
.work-participants li { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; }
.work-participants li > div:first-child { display: grid; gap: 3px; min-width: 0; overflow-wrap: anywhere; }
.work-participants small { font-size: 11px; }
.work-participants__status--approved { color: #25835a; }
.work-participants__status--pending { color: #a56b20; }
.work-participants__status--rejected, .work-participants__error { color: var(--system-red); }
.work-participants p { margin: 0; }
.work-participants__actions { display: flex; flex-wrap: wrap; gap: 8px; }
.work-participants button { padding: 8px 12px; border: 1px solid var(--border); border-radius: 6px; font: inherit; color: var(--accent); background: var(--bg-surface); cursor: pointer; }
.work-participants button:disabled { opacity: .6; cursor: wait; }
.work-participants__join { justify-self: start; }
.work-participants__hint { font-size: 12px; }
</style>
