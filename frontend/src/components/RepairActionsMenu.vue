<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'

defineProps({
  label: { type: String, required: true },
  editLabel: { type: String, required: true },
  deleteLabel: { type: String, required: true },
  busy: { type: Boolean, default: false },
  addLabel: { type: String, default: '' },
  addDisabled: { type: Boolean, default: false },
  editDisabled: { type: Boolean, default: false },
  deleteDisabled: { type: Boolean, default: false },
  completeLabel: { type: String, default: '' },
})
const emit = defineEmits(['add', 'edit', 'delete', 'complete'])
const menu = ref(null)
/** Закрывает меню действий с записью ремонта. */
function close() {
  if (menu.value) menu.value.open = false
}
/** Закрывает меню действий при нажатии за его пределами. */
function handleOutside(event) {
  if (!menu.value?.contains(event.target)) close()
}
/** Сообщает родителю о выбранном действии и закрывает меню. */
function choose(action) {
  close()
  emit(action)
}
onMounted(() => document.addEventListener('pointerdown', handleOutside))
onBeforeUnmount(() => document.removeEventListener('pointerdown', handleOutside))
</script>

<template>
  <details ref="menu" class="repair-actions" @keydown.esc.stop="close" @focusout="event => { if (!menu?.contains(event.relatedTarget)) close() }">
    <summary :aria-label="label" :title="label">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
    </summary>
    <div class="repair-actions__items">
      <button v-if="addLabel" type="button" :disabled="busy || addDisabled" @click="choose('add')">{{ addLabel }}</button>
      <button type="button" :disabled="busy || editDisabled" @click="choose('edit')">{{ editLabel }}</button>
      <button v-if="completeLabel" type="button" :disabled="busy || editDisabled" @click="choose('complete')">{{ completeLabel }}</button>
      <button class="repair-actions__delete" type="button" :disabled="busy || deleteDisabled" @click="choose('delete')">{{ deleteLabel }}</button>
    </div>
  </details>
</template>

<style scoped>
.repair-actions { position: relative; flex: 0 0 auto; }
.repair-actions summary {
  display: grid; place-items: center; width: 44px; height: 44px;
  border: 1px solid var(--border); border-radius: 0.6rem;
  background: var(--bg-surface); color: var(--text-main); cursor: pointer; list-style: none;
}
.repair-actions summary::-webkit-details-marker { display: none; }
.repair-actions svg { width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; }
.repair-actions__items {
  position: absolute; top: calc(100% + 0.3rem); right: 0; z-index: 10;
  display: grid; width: max-content; max-width: min(240px, 75vw); padding: 0.35rem;
  border: 1px solid var(--border); border-radius: 0.65rem; background: var(--bg-surface);
  box-shadow: 0 6px 20px rgb(0 0 0 / 12%);
}
.repair-actions__items button { padding: 0.65rem 0.8rem; border: 0; border-radius: 0.4rem; background: transparent; color: var(--text-main); font: inherit; text-align: left; cursor: pointer; }
.repair-actions__items button:hover { background: var(--bg-main); }
.repair-actions__items button:disabled { opacity: 0.5; cursor: not-allowed; }
.repair-actions__items .repair-actions__delete { color: var(--system-red); }
</style>
