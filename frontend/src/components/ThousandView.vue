<script setup>
import { ref } from 'vue'

defineProps({ navigationCollapsed: { type: Boolean, required: true } })
const loaded = ref(false)
</script>

<template>
  <main class="home-page thousand-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="thousand-header">
      <div><p class="eyebrow">КАРТОЧНАЯ ИГРА</p><h1>1000</h1></div>
      <a class="secondary-button" href="/games/1000/index.html" target="_blank" rel="noopener noreferrer">Открыть в новой вкладке ↗</a>
    </header>
    <p class="thousand-hint">
      Русская «Тысяча» на три игрока: торги, прикуп, марьяжи и восемь взяток. Играйте против
      Ворона и Лисы или соберите онлайн-стол с коллегами — для онлайна нужны три человека.
    </p>
    <div class="thousand-frame" :class="{ 'thousand-frame--ready': loaded }">
      <p v-if="!loaded" class="thousand-loading" role="status">Загрузка игры…</p>
      <iframe
        src="/games/1000/index.html"
        title="1000 — карточная игра"
        allow="autoplay; fullscreen"
        allowfullscreen
        @load="loaded = true"
      ></iframe>
    </div>
  </main>
</template>

<style scoped>
.thousand-page{display:flex;flex-direction:column;gap:12px;min-height:100vh;box-sizing:border-box}
.thousand-header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;flex-wrap:wrap}
.thousand-header h1{margin:0;font-size:30px}
.thousand-hint{margin:0;color:var(--text-secondary);font-size:13px;max-width:900px;line-height:1.6}
.thousand-frame{position:relative;flex:1;min-height:70vh;border:1px solid var(--border);border-radius:14px;overflow:hidden;background:#0b3d2e}
.thousand-frame iframe{display:block;width:100%;height:100%;min-height:70vh;border:0}
.thousand-loading{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;margin:0;color:#e6f2ea;font-size:14px;pointer-events:none}
.thousand-frame--ready .thousand-loading{display:none}
@media(max-width:760px){.thousand-header h1{font-size:24px}.thousand-frame,.thousand-frame iframe{min-height:64vh}}
</style>
