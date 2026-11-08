<script setup>
import { onMounted, ref } from 'vue'
const props = defineProps({ token: { type: String, required: true } })
const catalog = ref({ professions: [], permissions: [], blocks: [] })
const selected = ref([]), permissions = ref([]), hidden = ref([]), individual = ref(false)
const busy = ref(false), loaded = ref(false), error = ref(''), message = ref('')
async function load() {
  busy.value = true; error.value = ''; loaded.value = false
  try {
    const response = await fetch('/api/admin/profession-access', { headers: { Authorization: `Bearer ${props.token}` } })
    if (!response.ok) throw new Error('Не удалось загрузить настройки профессий.')
    catalog.value = await response.json(); selected.value = []; loaded.value = true
  } catch (e) { error.value = e.message } finally { busy.value = false }
}
function edit(item) {
  selected.value = [item.id]; permissions.value = [...(item.permissions || [])]
  hidden.value = [...item.hiddenBlocks]; individual.value = item.permissions === null
  error.value = ''; message.value = ''
}
function togglePermission(item, enabled) {
  const keys = [item.key, ...(item.children || []).map(child => child.key)]
  permissions.value = enabled ? [...new Set([...permissions.value, ...keys])] : permissions.value.filter(key => !keys.includes(key))
}
function toggleBlock(key, enabled) {
  hidden.value = enabled ? hidden.value.filter(x => x !== key) : [...new Set([...hidden.value, key])]
}
async function save(reset = false) {
  busy.value = true; error.value = ''; message.value = ''
  try {
    const response = await fetch('/api/admin/profession-access', {
      method: 'PUT', headers: { Authorization: `Bearer ${props.token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ professions: catalog.value.professions.filter(x => selected.value.includes(x.id)).map(x => ({ id: x.id, revision: x.revision })),
        permissions: reset || individual.value ? null : permissions.value, hiddenBlocks: reset ? [] : hidden.value }),
    })
    const result = await response.json()
    if (!response.ok) throw new Error(result.message || 'Не удалось сохранить настройки.')
    await load(); message.value = result.message
  } catch (e) { error.value = e.message } finally { busy.value = false }
}
onMounted(load)
</script>

<template>
  <section class="profession-access">
    <h2>Разделы и блоки страниц для профессий</h2>
    <p>Выберите профессию и нажмите «Настроить». Чтобы применить один набор сразу к группе профессий, отметьте дополнительные профессии перед сохранением.</p>
    <p>Настройки разделов профессии заменяют индивидуальный набор разделов сотрудника. Администратор видит всё. После изменения настроек сотрудникам потребуется повторный вход.</p>
    <button type="button" :disabled="busy" @click="load">Обновить настройки доступа</button>
    <p v-if="error" role="alert">{{ error }}</p><p v-if="message" role="status">{{ message }}</p>
    <fieldset :disabled="busy || !loaded">
      <legend>Профессии</legend>
      <div v-for="item in catalog.professions" :key="item.id" class="profession-access__row">
        <label><input v-model="selected" type="checkbox" :value="item.id" /> {{ item.name }}</label>
        <span>{{ item.permissions === null ? 'Индивидуальные разделы' : 'Разделы по профессии' }} · скрыто блоков: {{ item.hiddenBlocks.length }}</span>
        <button type="button" :aria-label="`Настроить ${item.name}`" @click="edit(item)">Настроить</button>
      </div>
      <p v-if="!catalog.professions.length">Сначала добавьте профессии.</p>
    </fieldset>
    <fieldset v-if="selected.length" :disabled="busy">
      <legend>Настройки для выбранных профессий ({{ selected.length }})</legend>
      <label><input v-model="individual" type="checkbox" /> Использовать индивидуальные разделы пользователей</label>
      <p>Главная страница и профиль доступны всем. Выбор подраздела включает необходимое родительское меню.</p>
      <div class="profession-access__columns">
        <fieldset :disabled="individual"><legend>Доступные разделы</legend>
          <div v-for="menu in catalog.permissions" :key="menu.key">
            <label><input type="checkbox" :checked="permissions.includes(menu.key)" @change="togglePermission(menu, $event.target.checked)" /> {{ menu.label }}</label>
            <label v-for="child in menu.children || []" :key="child.key" class="profession-access__child"><input v-model="permissions" type="checkbox" :value="child.key" /> {{ child.label }}</label>
          </div>
        </fieldset>
        <fieldset><legend>Видимые блоки страниц</legend>
          <p>Блоки доступны только при доступе к соответствующему разделу. Для отчёта настройка действует и в PDF.</p>
          <label v-for="block in catalog.blocks" :key="block.key"><input type="checkbox" :checked="!hidden.includes(block.key)" @change="toggleBlock(block.key, $event.target.checked)" /> {{ block.label }}</label>
        </fieldset>
      </div>
      <div class="profession-access__buttons">
        <button class="primary-button" type="button" @click="save(false)">Применить к выбранным профессиям</button>
        <button type="button" @click="save(true)">Вернуть индивидуальные разделы и все блоки</button>
      </div>
    </fieldset>
  </section>
</template>

<style scoped>
.profession-access { display: grid; gap: 14px; margin-top: 24px; min-width: 0; }
.profession-access p { margin: 0; line-height: 1.5; }
.profession-access fieldset { display: grid; gap: 12px; min-width: 0; border: 1px solid var(--border); border-radius: 10px; padding: 16px; }
.profession-access label { display: flex; gap: 8px; align-items: center; padding: 6px 0; }
.profession-access__row, .profession-access__buttons { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.profession-access__row span { font-size: 12px; color: var(--text-secondary); }
.profession-access__columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr)); gap: 16px; }
.profession-access__child { margin-left: 20px; }
.profession-access button { padding: 8px 12px; cursor: pointer; }
</style>
