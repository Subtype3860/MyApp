<script setup>
import { ref } from 'vue'

defineProps({ navigationCollapsed: { type: Boolean, required: true } })
const loaded = ref(false)
</script>

<template>
  <main class="home-page fortpost-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="fortpost-header">
      <div><p class="eyebrow">3D-ИГРА</p><h1>Форпост</h1></div>
      <a class="secondary-button" href="/games/fortpost/index.html" target="_blank" rel="noopener noreferrer">Открыть в новой вкладке ↗</a>
    </header>
    <p class="fortpost-hint">
      Выживание во льдах: держите форпост, добывайте ресурсы и отражайте атаки. Игра работает прямо здесь,
      управление подскажет сама игра. Если сцена не загрузилась, обновите страницу или откройте игру в новой вкладке.
    </p>
    <div class="fortpost-frame" :class="{ 'fortpost-frame--ready': loaded }">
      <p v-if="!loaded" class="fortpost-loading" role="status">Загрузка игры…</p>
      <iframe
        src="/games/fortpost/index.html"
        title="Форпост — 3D-игра"
        allow="autoplay; fullscreen; gamepad; xr-spatial-tracking"
        allowfullscreen
        @load="loaded = true"
      ></iframe>
    </div>
  </main>
</template>

<style scoped>
.fortpost-page{display:flex;flex-direction:column;gap:12px;min-height:100vh;box-sizing:border-box}
.fortpost-header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;flex-wrap:wrap}
.fortpost-header h1{margin:0;font-size:30px}
.fortpost-hint{margin:0;color:var(--text-secondary);font-size:13px;max-width:900px;line-height:1.6}
.fortpost-frame{position:relative;flex:1;min-height:70vh;border:1px solid var(--border);border-radius:14px;overflow:hidden;background:#0b1220}
.fortpost-frame iframe{display:block;width:100%;height:100%;min-height:70vh;border:0}
.fortpost-loading{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;margin:0;color:#c9d4e3;font-size:14px;pointer-events:none}
.fortpost-frame--ready .fortpost-loading{display:none}
@media(max-width:760px){.fortpost-header h1{font-size:24px}.fortpost-frame,.fortpost-frame iframe{min-height:64vh}}
</style>
