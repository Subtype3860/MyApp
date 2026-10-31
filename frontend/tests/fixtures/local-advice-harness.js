import { createApp, reactive, h } from 'vue'
import LocalAdvicePanel from '../../src/components/LocalAdvicePanel.vue'
import LocalRequestDraft from '../../src/components/LocalRequestDraft.vue'
import ShiftOutcome from '../../src/components/ShiftOutcome.vue'

export function mount(component, props) {
  const Component = { LocalAdvicePanel, LocalRequestDraft, ShiftOutcome }[component]
  document.body.innerHTML = '<div id="advice-test"></div>'
  window.adviceProps = reactive(props)
  window.appliedDraft = null
  createApp({ render: () => h(Component, { ...window.adviceProps, onApply: value => { window.appliedDraft = value } }) }).mount('#advice-test')
}
