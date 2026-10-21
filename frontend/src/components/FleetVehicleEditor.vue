<script setup>
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
const props = defineProps({ open: Boolean, vehicle: { type: Object, default: null }, options: { type: Object, required: true }, token: { type: String, required: true } })
const emit = defineEmits(['close', 'saved'])
const dialog = ref(null), busy = ref(false), error = ref('')
const draft = ref({ groupName: '', typeName: '', modelName: '', garageNumber: '', stateNumber: '', vin: '' })
const controller = new AbortController()
const /** Обрезает пробелы и приводит текст к нижнему регистру для сравнения значений справочника. */ normalize = value => String(value || '').trim().toLocaleLowerCase('ru-RU')
const group = computed(() => (props.options.groups || []).find(item => normalize(item.name) === normalize(draft.value.groupName)))
const types = computed(() => (props.options.types || []).filter(item => item.parentId === group.value?.id))
const type = computed(() => types.value.find(item => normalize(item.name) === normalize(draft.value.typeName)))
const models = computed(() => (props.options.models || []).filter(item => item.parentId === type.value?.id))
watch(() => props.open, async open => {
  if (!open) { dialog.value?.close(); return }
  error.value = ''
  draft.value = { groupName: props.vehicle?.groupName || '', typeName: props.vehicle?.typeName || '', modelName: props.vehicle?.modelName || '', garageNumber: props.vehicle?.garageNumber == null ? '' : String(props.vehicle.garageNumber), stateNumber: props.vehicle?.stateNumber || '', vin: props.vehicle?.vin || '' }
  await nextTick()
  dialog.value?.showModal()
})
/** Закрывает форму редактирования техники. */
function close() { if (!busy.value) emit('close') }
/** Проверяет поля карточки техники и отправляет запрос создания или изменения. */
async function save() {
  if (busy.value) return
  busy.value = true; error.value = ''
  try {
    const response = await fetch(props.vehicle ? `/api/vehicles/${props.vehicle.id}` : '/api/vehicles', {
      method: props.vehicle ? 'PUT' : 'POST', headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' }, signal: controller.signal,
      body: JSON.stringify({ ...draft.value, garageNumber: draft.value.garageNumber === '' ? null : Number(draft.value.garageNumber) }),
    })
    const result = await response.json().catch(() => null)
    if (!response.ok) throw new Error(Object.values(result?.errors || {}).flat()[0] || result?.message || 'Не удалось сохранить технику.')
    emit('saved', { id: result.id, editing: !!props.vehicle })
    emit('close')
  } catch (failure) { if (failure.name !== 'AbortError') error.value = failure.message }
  finally { busy.value = false }
}
onBeforeUnmount(() => { controller.abort(); dialog.value?.close() })
</script>

<template>
  <dialog ref="dialog" class="fleet-editor" @cancel.prevent="close" @click.self="close">
    <form @submit.prevent="save">
      <header><h2>{{ vehicle ? 'Редактировать технику' : 'Добавить технику' }}</h2><button type="button" class="secondary-button" aria-label="Закрыть редактор техники" :disabled="busy" @click="close">×</button></header>
      <p>Выберите группу, тип и модель из подсказок или введите новые названия.</p>
      <div class="fleet-editor__fields">
        <label>Группа техники<input v-model="draft.groupName" list="fleet-groups" required maxlength="50" :disabled="busy" /></label>
        <datalist id="fleet-groups"><option v-for="item in options.groups || []" :key="item.id" :value="item.name" /></datalist>
        <label>Тип техники<input v-model="draft.typeName" list="fleet-types" required maxlength="50" :disabled="busy" /></label>
        <datalist id="fleet-types"><option v-for="item in types" :key="item.id" :value="item.name" /></datalist>
        <label>Модель техники<input v-model="draft.modelName" list="fleet-models" required maxlength="50" :disabled="busy" /></label>
        <datalist id="fleet-models"><option v-for="item in models" :key="item.id" :value="item.name" /></datalist>
        <label>Гаражный номер<input v-model="draft.garageNumber" type="number" min="0" max="2147483647" step="1" :disabled="busy" /></label>
        <label>Государственный номер<input v-model="draft.stateNumber" maxlength="15" :disabled="busy" /></label>
        <label>VIN<input v-model="draft.vin" maxlength="20" :disabled="busy" /></label>
      </div>
      <p v-if="error" role="alert" class="fleet-editor__error">{{ error }}</p>
      <footer><button type="button" class="secondary-button" :disabled="busy" @click="close">Отмена</button><button type="submit" class="primary-button" :disabled="busy">{{ busy ? 'Сохранение…' : 'Сохранить технику' }}</button></footer>
    </form>
  </dialog>
</template>

<style scoped>
.fleet-editor { box-sizing: border-box; width: min(640px, calc(100% - 24px)); max-height: calc(100dvh - 32px); overflow: auto; border: 1px solid var(--border); border-radius: 16px; padding: 24px; color: var(--text-main); background: var(--bg-surface); }
.fleet-editor::backdrop { background: #07121cbb; }
.fleet-editor form { display: grid; gap: 16px; }
.fleet-editor header, .fleet-editor footer { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.fleet-editor h2 { margin: 0; font-size: 22px; }
.fleet-editor p { margin: 0; color: var(--text-secondary); font-size: 13px; line-height: 1.5; }
.fleet-editor__fields { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
.fleet-editor label { display: grid; gap: 7px; font-size: 13px; min-width: 0; }
.fleet-editor input { box-sizing: border-box; width: 100%; min-width: 0; border: 1px solid var(--border); border-radius: 8px; padding: 10px 12px; font: inherit; color: var(--text-main); background: var(--bg-surface); }
.fleet-editor p.fleet-editor__error { color: var(--system-red); }
.fleet-editor footer { justify-content: flex-end; }
@media(max-width: 600px) { .fleet-editor { padding: 18px; } .fleet-editor__fields { grid-template-columns: 1fr; } }
</style>
