<script setup>
import { computed, nextTick, ref } from 'vue'
import { helpArticles } from '../data/helpArticles'
defineProps({ navigationCollapsed: { type: Boolean, required: true } })
const query = ref('')
const activeId = ref('start')
const heading = ref(null)
const imageDialog = ref(null)
const normalize = text => text.toLocaleLowerCase('ru').replaceAll('ё', 'е')
const filtered = computed(() => {
  const words = normalize(query.value).trim().split(/\s+/).filter(Boolean)
  return helpArticles.filter(article => words.every(word => normalize([article.title, article.path, article.intro, ...article.steps, ...article.notes].join(' ')).includes(word)))
})
const active = computed(() => filtered.value.find(article => article.id === activeId.value) || filtered.value[0])
async function choose(id) {
  activeId.value = id
  await nextTick()
  heading.value?.focus({ preventScroll: true })
  heading.value?.scrollIntoView({ block: 'start' })
}
</script>
<template>
  <main class="home-page help-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header"><div><p class="eyebrow">СПРАВОЧНИК</p><h1>Помощь</h1><p>Пошаговые инструкции по работе с приложением «Атлас».</p></div></header>
    <label class="help-search">Поиск по справочнику<input v-model="query" type="search" placeholder="Например: КТГ, закупка, пароль" /></label>
    <p role="status" class="help-count">Найдено тем: {{ filtered.length }}</p>
    <div v-if="active" class="help-layout">
      <nav class="help-contents macos-glass-panel" aria-label="Темы помощи">
        <h2>Содержание</h2>
        <button v-for="article in filtered" :key="article.id" type="button" :aria-current="active.id === article.id ? 'page' : undefined" @click="choose(article.id)">{{ article.title }}</button>
      </nav>
      <article class="help-article macos-glass-panel">
        <p class="help-path">{{ active.path }}</p>
        <h2 ref="heading" tabindex="-1">{{ active.title }}</h2>
        <p>{{ active.intro }}</p>
        <figure>
          <img :src="'/help/' + active.id + '.svg'" :alt="'Схема: ' + active.flow.join(' → ')" width="960" height="260" />
          <button type="button" class="secondary-button help-zoom" @click="imageDialog.showModal()">Увеличить схему</button>
          <figcaption>Схема действий. Расположение элементов интерфейса зависит от размера экрана.</figcaption>
        </figure>
        <h3>Порядок действий</h3>
        <ol><li v-for="step in active.steps" :key="step">{{ step }}</li></ol>
        <section class="help-notes" aria-label="Пояснения"><h3>Полезно знать</h3><ul><li v-for="note in active.notes" :key="note">{{ note }}</li></ul></section>
      </article>
    </div>
    <p v-else class="macos-glass-panel help-empty">По этому запросу ничего не найдено. Попробуйте название раздела или поля.</p>
    <dialog ref="imageDialog" class="help-image-dialog" aria-label="Схема действий">
      <button type="button" class="secondary-button help-zoom" @click="imageDialog.close()">Закрыть схему</button>
      <div v-if="active" class="help-image-scroll" tabindex="0" aria-label="Увеличенная схема, доступна горизонтальная прокрутка">
        <img :src="'/help/' + active.id + '.svg'" :alt="active.flow.join(' → ')" width="960" height="260" />
      </div>
    </dialog>
  </main>
</template>
<style scoped>
.help-search { display:grid; gap:8px; max-width:620px; font-weight:600; }
.help-search input { width:100%; min-height:46px; padding:12px 14px; border:1px solid var(--border); border-radius:10px; background:var(--bg-surface); color:var(--text-main); font:inherit; }
.help-count,.help-path { color:var(--text-secondary); font-size:13px; }
.help-layout { display:grid; grid-template-columns:minmax(220px,280px) minmax(0,1fr); gap:22px; align-items:start; }
.help-contents { display:grid; gap:4px; padding:16px; }
.help-contents h2 { margin:0 0 10px; font-size:18px; }
.help-contents button { min-height:44px; padding:10px 12px; text-align:left; font:inherit; font-size:14px; line-height:1.4; color:var(--text-main); background:transparent; border:1px solid transparent; border-radius:8px; }
.help-contents button:hover,.help-contents button[aria-current] { background:var(--theme-hover); color:var(--accent); border-color:var(--border); }
.help-article { min-width:0; padding:clamp(18px,3vw,34px); line-height:1.7; }
.help-article h2 { margin:8px 0 16px; line-height:1.3; scroll-margin-top:90px; }
.help-article figure { margin:24px 0; }
.help-article img { width:100%; height:auto; display:block; border:1px solid var(--border); border-radius:12px; }
.help-article figcaption { margin-top:8px; font-size:12px; color:var(--text-secondary); }
.help-article ol,.help-article ul { padding-left:24px; }
.help-article li { margin:0 0 14px; padding-left:4px; }
.help-article li::marker { color:var(--accent); font-weight:700; }
.help-notes { padding:4px 18px; border-left:3px solid var(--accent); background:var(--theme-hover); border-radius:0 10px 10px 0; }
.help-zoom { margin-top:12px; padding:10px 14px; min-height:44px; }
.help-image-dialog { width:min(1040px,calc(100vw - 24px)); max-height:90dvh; border:1px solid var(--border); border-radius:14px; padding:16px; background:var(--bg-main); color:var(--text-main); }
.help-image-dialog::backdrop { background:#0009; }
.help-image-scroll { overflow:auto; margin-top:16px; }
.help-image-scroll img { display:block; max-width:none; }
.help-empty { padding:24px; }
@media(max-width:900px) { .help-layout { grid-template-columns:1fr; } .help-contents { max-height:300px; overflow:auto; } }
</style>
