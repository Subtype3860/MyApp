<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'

defineProps({ label: { type: String, required: true }, disabled: Boolean })
const menu = ref(null)
function dismiss(event) {
  if (!menu.value?.open) return
  if (event.type === 'keydown') {
    if (event.key !== 'Escape') return
    if (menu.value.contains(document.activeElement)) menu.value.querySelector('summary').focus()
  } else if (menu.value.contains(event.target)) return
  menu.value.open = false
}
function close() { menu.value.open = false }
onMounted(() => {
  document.addEventListener('click', dismiss)
  document.addEventListener('keydown', dismiss)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', dismiss)
  document.removeEventListener('keydown', dismiss)
})
</script>

<template>
  <details ref="menu" class="fleet-action-menu">
    <summary :aria-label="label" :aria-disabled="disabled" @click="disabled && $event.preventDefault()">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
    </summary>
    <div class="fleet-action-menu__items" @click="close"><slot /></div>
  </details>
</template>

<style scoped>
.fleet-action-menu { position: relative; flex-shrink: 0; }
summary { display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; border: 1px solid var(--border, #d8dee7); border-radius: 10px; cursor: pointer; color: var(--text-main, #243449); list-style: none; }
summary::-webkit-details-marker { display: none; }
summary svg { width: 22px; height: 22px; }
summary:hover, details[open] summary { background: var(--bg-surface, #f1f5f9); }
summary:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
summary[aria-disabled="true"] { opacity: .5; cursor: default; }
.fleet-action-menu__items { position: absolute; right: 0; top: calc(100% + 6px); z-index: 20; min-width: 190px; padding: 6px; border: 1px solid var(--border, #d8dee7); border-radius: 12px; background: var(--bg-surface, #fff); color: var(--text-main, #243449); box-shadow: 0 8px 24px #172b4d26; }
.fleet-action-menu__items :deep(button) { display: block; width: 100%; padding: 10px 12px; border: 0; border-radius: 7px; background: transparent; color: inherit; text-align: left; font: inherit; font-size: 13px; cursor: pointer; }
.fleet-action-menu__items :deep(button:hover), .fleet-action-menu__items :deep(button:focus-visible) { background: #94a3b826; }
.fleet-action-menu__items :deep(.fleet-delete-action) { color: #c0392b; }
</style>
