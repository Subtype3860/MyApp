<script setup>
import FleetRegionsPanel from './FleetRegionsPanel.vue'
defineProps({ token: { type: String, required: true }, navigationCollapsed: { type: Boolean, default: false } })
</script>

<template>
  <main class="home-page regions-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <h1>Участок</h1>
    <FleetRegionsPanel :token="token" />
  </main>
</template>

<style scoped>
.regions-page{box-sizing:border-box}
</style>
