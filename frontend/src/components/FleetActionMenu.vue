<script setup>
import { onBeforeUnmount, onMounted, ref, useId } from 'vue'
const props = defineProps({ label: { type: String, required: true }, disabled: Boolean })
const trigger = ref(null), panel = ref(null), opened = ref(false)
const panelId = useId()
function close(focus = false) {
  if (panel.value?.matches(':popover-open')) panel.value.hidePopover()
  if (focus) trigger.value?.focus()
}
function position() {
  const rect = trigger.value.getBoundingClientRect()
  const box = panel.value.getBoundingClientRect()
  panel.value.style.left = Math.max(8, Math.min(rect.right - box.width, innerWidth - box.width - 8)) + 'px'
  panel.value.style.top = Math.max(8, rect.bottom + box.height + 8 <= innerHeight ? rect.bottom + 6 : rect.top - box.height - 6) + 'px'
}
function toggle() {
  if (props.disabled) return
  if (panel.value.matches(':popover-open')) close()
  else { panel.value.showPopover(); position() }
}
function choose(event) {
  const button = event.target.closest('button')
  if (button && !button.disabled) close(true)
}
function navigate(event) {
  if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true); return }
  if (!['ArrowDown','ArrowUp','Home','End'].includes(event.key)) return
  event.preventDefault()
  if (!panel.value.matches(':popover-open')) { panel.value.showPopover(); position() }
  const buttons = [...panel.value.querySelectorAll('button:not(:disabled)')].filter(button => button.getClientRects().length)
  if (!buttons.length) return
  const current = buttons.indexOf(document.activeElement)
  const index = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowUp' ? -1 : 1) + buttons.length) % buttons.length
  buttons[index].focus()
}
function dismissOnResize() { close() }
onMounted(() => window.addEventListener('resize', dismissOnResize))
onBeforeUnmount(() => { close(); window.removeEventListener('resize', dismissOnResize) })
</script>
<template>
  <div class="fleet-action-menu">
    <button ref="trigger" class="fleet-action-menu__trigger" type="button" :aria-label="label" :title="label" :aria-expanded="opened" :aria-controls="panelId" :disabled="disabled" @click="toggle" @keydown="navigate">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16" /></svg>
    </button>
    <div :id="panelId" ref="panel" popover="auto" class="fleet-action-menu__items" @toggle="opened = $event.newState === 'open'" @click="choose" @keydown="navigate"><slot /></div>
  </div>
</template>
<style scoped>
.fleet-action-menu { flex-shrink: 0; }
.fleet-action-menu__trigger { display: flex; align-items: center; justify-content: center; width: 44px; height: 44px; padding: 0; border: 1px solid var(--border, #d8dee7); border-radius: 10px; background: var(--bg-surface, #fff); cursor: pointer; color: var(--text-main, #243449); }
.fleet-action-menu__trigger svg { width: 22px; height: 22px; }
.fleet-action-menu__trigger:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.fleet-action-menu__trigger:disabled { opacity: .5; cursor: default; }
.fleet-action-menu__items { position: fixed; inset: auto; margin: 0; box-sizing: border-box; min-width: min(220px, calc(100vw - 16px)); max-width: calc(100vw - 16px); max-height: calc(100dvh - 16px); overflow: auto; padding: 6px; border: 1px solid var(--border, #d8dee7); border-radius: 12px; background: var(--bg-surface, #fff); color: var(--text-main, #243449); box-shadow: 0 8px 24px #172b4d26; }
.fleet-action-menu__items :deep(button) { display: block; width: 100%; min-height: 44px; padding: 10px 12px; border: 0; border-radius: 7px; background: transparent; color: inherit; text-align: left; font: inherit; font-size: 14px; cursor: pointer; white-space: normal; }
.fleet-action-menu__items :deep(button:hover), .fleet-action-menu__items :deep(button:focus-visible) { background: #94a3b826; }
.fleet-action-menu__items :deep(button:disabled) { opacity: .5; cursor: not-allowed; }
.fleet-action-menu__items :deep(.fleet-delete-action), .fleet-action-menu__items :deep(.table-action-button--danger) { color: #c0392b; }
</style>
