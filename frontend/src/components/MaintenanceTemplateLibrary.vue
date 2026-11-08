<script setup>
import { onMounted, ref } from 'vue'

const props = defineProps({
  token: { type: String, required: true },
  vehicle: { type: Object, default: null },
  service: { type: String, default: '' },
})

const templates = ref([])
const materialSources = ref([])
const busy = ref(false)
const error = ref('')
const message = ref('')
const editor = ref(null)
const clone = value => JSON.parse(JSON.stringify(value))
const newId = () => crypto.randomUUID()
const normalize = value => (value || '').replace(/[^\p{L}\p{N}]/gu, '').toUpperCase()
const headers = () => ({ Authorization: 'Bearer ' + props.token, 'Content-Type': 'application/json' })

async function request(path, options = {}) {
  const response = await fetch('/api/maintenance-plans/' + path, { ...options, headers: headers() })
  const result = await response.json().catch(() => null)
  if (!response.ok) throw Error(result?.message || 'Не удалось выполнить действие. Проверьте соединение.')
  return result
}

async function load() {
  busy.value = true
  error.value = ''
  try {
    const full = await request('card-templates')
    templates.value = full.templates || []
    materialSources.value = full.materialSources || []
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/** Открывает редактор существующего шаблона или создаёт новый для выбранной машины. */
function editTemplate(template) {
  editor.value = template ? clone(template) : {
    id: newId(),
    revision: 0,
    published: false,
    template: {
      model: props.vehicle?.modelName || '',
      service: props.service || '',
      source: 'Создано в приложении',
      reviewNotes: '',
      operations: [],
      materials: [],
      materialIntervalId: null,
    },
  }
  editor.value.template.materialIntervalId ??= null
}

async function saveTemplate(published) {
  busy.value = true
  error.value = ''
  try {
    const item = editor.value
    item.template.materials = []
    await request('card-templates/' + item.id, { method: 'PUT', body: JSON.stringify({ revision: item.revision, published, template: item.template }) })
    editor.value = null
    await load()
    message.value = published ? 'Шаблон утверждён' : 'Шаблон сохранён'
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="template-library">
    <header>
      <h3>Библиотека шаблонов</h3>
      <button type="button" :disabled="busy || !vehicle" @click="editTemplate(null)">Создать шаблон</button>
    </header>
    <p>Исходные карты загружены как черновики. Перед утверждением проверьте состав работ, нормы, единицы и применимость к модели.</p>
    <p v-if="!vehicle" class="template-hint">Выберите технику выше, чтобы создать шаблон для её модели.</p>
    <p v-if="error" role="alert" class="form-message--error">{{error}}</p>
    <p v-if="message" role="status">{{message}}</p>
    <p v-if="busy && !editor" role="status">Загрузка шаблонов…</p>
    <div v-for="t in templates" :key="t.id" class="template-row">
      <span>{{t.template.model}} · {{t.template.service}} · {{t.published?'Утверждён':'Черновик'}} · v{{t.revision}}</span>
      <button type="button" :aria-label="`Редактировать ${t.template.model} ${t.template.service}`" :disabled="busy" @click="editTemplate(t)">Изменить</button>
    </div>
    <form v-if="editor" class="template-editor" @submit.prevent="saveTemplate(false)">
      <h4>Редактор шаблона</h4>
      <p v-if="editor.template.source" class="template-source">{{editor.template.source}}</p>
      <fieldset :disabled="busy">
        <div class="template-heading-fields">
          <label>Модель техники<input v-model.trim="editor.template.model" required /></label>
          <label>Вид ТО шаблона<input v-model.trim="editor.template.service" required /></label>
        </div>
        <label>Замечания к регламенту<textarea v-model="editor.template.reviewNotes" rows="1" /></label>
        <details open>
          <summary>Операции ({{editor.template.operations.length}})</summary>
          <article v-for="(o,i) in editor.template.operations" :key="o.id" class="template-operation">
            <label>Раздел<input v-model="o.section" required /></label>
            <label>Описание операции<textarea v-model="o.text" rows="2" required /></label>
            <div class="template-operation-actions">
              <label class="check"><input v-model="o.control" type="checkbox" /> Заключительный контроль</label>
              <button type="button" :aria-label="`Удалить операцию ${i+1}`" @click="editor.template.operations.splice(i,1)">Удалить</button>
            </div>
          </article>
          <button type="button" @click="editor.template.operations.push({id:newId(),section:'ДВС',text:'',target:'',control:false})">Добавить операцию</button>
        </details>
        <section class="material-source-editor">
          <h4>Материалы из технического обслуживания</h4>
          <p>Наименования, единицы и нормы ведутся в разделе «Техническое обслуживание». При начале ТО в карту загружается актуальный список.</p>
          <label>Источник материалов<select v-model="editor.template.materialIntervalId"><option :value="null">Автоматически по модели / типу техники и виду ТО</option><optgroup v-for="equipment in materialSources" :key="equipment.id" :label="equipment.name"><option v-for="interval in equipment.intervals.filter(i=>normalize(i.name)===normalize(editor.template.service))" :key="interval.id" :value="interval.id">{{equipment.name}} / {{interval.name}} · {{interval.items.length}} материалов</option></optgroup></select></label>
        </section>
        <div class="actions">
          <button type="submit">Сохранить черновик шаблона</button>
          <button type="button" class="primary-button" @click="saveTemplate(true)">Утвердить проверенный шаблон</button>
          <button type="button" @click="editor=null">Закрыть редактор</button>
        </div>
      </fieldset>
    </form>
  </section>
</template>

<style scoped>
.template-library{display:grid;gap:10px;padding:14px;border:1px solid var(--border);border-radius:12px;background:var(--bg-surface);min-width:0}
.template-library>header{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-wrap:wrap}
.template-library h3{margin:0;font-size:17px}
.template-library p{margin:0;font-size:12px;color:var(--text-secondary);overflow-wrap:anywhere}
.template-hint{color:var(--text-secondary)}
.template-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:8px;align-items:center;padding:4px 0;border-bottom:1px solid var(--border);font-size:12px}
.template-row>span{overflow-wrap:anywhere}
.template-library button{min-height:36px;padding:6px 10px;border:1px solid var(--border);border-radius:8px;background:var(--bg-surface);color:var(--text-main);cursor:pointer}
.template-library button:disabled{opacity:.55;cursor:default}
.template-editor{border:1px solid var(--border);border-radius:8px;padding:10px}
.template-editor h4{margin:8px 0;font-size:16px}
.template-editor p{margin:6px 0;font-size:12px}
.template-heading-fields{display:grid;grid-template-columns:2fr 1fr;gap:10px}
.template-operation{display:grid;grid-template-columns:minmax(120px,1fr) minmax(0,3fr) minmax(150px,1fr);gap:10px;align-items:start;padding:8px 0}
.template-operation-actions{display:grid;gap:8px}
.template-library label{display:grid;gap:5px;font-size:13px}
.template-library input,.template-library select,.template-library textarea{box-sizing:border-box;width:100%;min-width:0;padding:6px 8px;border:1px solid var(--border);border-radius:6px;background:var(--bg-main);color:var(--text-main);font:inherit}
.template-library textarea{resize:vertical}
.template-library summary{cursor:pointer;padding:8px 0;font-weight:600}
.template-library fieldset{border:0;padding:0;min-width:0;display:grid;gap:8px;margin:8px 0}
.template-library .check{display:flex;align-items:center;font-size:12px}
.template-library .check input{width:auto}
.template-source{color:var(--text-secondary)}
.actions{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.material-source-editor{border-top:1px solid var(--border);padding-top:6px}
@media(max-width:700px){.template-operation,.template-heading-fields{grid-template-columns:1fr}.template-operation-actions{display:flex;justify-content:space-between;align-items:center}.template-library button{min-height:44px}}
</style>
