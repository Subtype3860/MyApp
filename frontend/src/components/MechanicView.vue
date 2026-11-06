<script setup>
defineProps({ navigationCollapsed: { type: Boolean, required: true } })
</script>

<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
  <section class="macos-glass-panel mechanic-lobby" aria-labelledby="mechanic-title">
    <p class="eyebrow">СИМУЛЯТОР МЕХАНИКА</p>
    <h1 id="mechanic-title">Смена</h1>
    <p>Ваша мастерская: принимайте технику, находите неисправности, покупайте запчасти и зарабатывайте на ремонте.</p>
    <ol>
      <li>Прочитайте симптомы и проверьте нужную систему.</li>
      <li>Купите деталь и выполните ремонт на точность.</li>
      <li>Выдайте машину в срок, получите премию и улучшите инструмент.</li>
    </ol>
    <a class="primary-button mechanic-launch" href="/games/mechanic/index.html">Играть в «Смену» ↗</a>
    <p class="mechanic-hint">Одиночная игра. Управление мышью или касаниями, при ремонте — кнопкой или пробелом. Прогресс сохраняется в этом браузере.</p>
  </section>
  </main>
</template>

<style scoped>
.mechanic-lobby{max-width:800px;padding:32px}.mechanic-lobby h1{font-size:30px}.mechanic-lobby p,.mechanic-lobby li{line-height:1.7}.mechanic-lobby ol{padding-left:22px}.mechanic-lobby li{margin:8px 0}.mechanic-launch{display:inline-flex;align-items:center;justify-content:center;padding:0 22px;text-decoration:none;margin:12px 0}.mechanic-hint{color:var(--text-secondary);font-size:13px}@media(max-width:760px){.mechanic-lobby{padding:22px}}
</style>
