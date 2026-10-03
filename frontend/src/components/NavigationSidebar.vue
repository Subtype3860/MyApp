<script setup>
import { onBeforeUnmount, ref, watch } from 'vue'
import brandLogo from '../assets/brand-logo.png'

const props = defineProps({
  collapsed: {
    type: Boolean,
    required: true,
  },
  activePage: {
    type: String,
    required: true,
  },
  activeTable: {
    type: String,
    required: true,
  },
  activeSettings: {
    type: String,
    required: true,
  },
  activeVehicleSection: {
    type: String,
    required: true,
  },
  isAdmin: {
    type: Boolean,
    required: true,
  },
  permissions: {
    type: Array,
    required: true,
  },
  selectedComponentsCount: {
    type: Number,
    required: true,
  },
  token: { type: String, required: true },
  profileVersion: { type: Number, required: true },
})

const emit = defineEmits([
  'toggle',
  'navigate',
  'navigate-table',
  'navigate-selected-components',
  'navigate-settings',
  'navigate-vehicle',
  'logout',
])
const isTablesExpanded = ref(false)
const isVehiclesExpanded = ref(false)
const isSettingsExpanded = ref(false)
const profile = ref(null)
const avatarUrl = ref('')
const canView = (permission) =>
  props.isAdmin ||
  String(localStorage.getItem('myapp.userRole')).toLowerCase() === 'administrator' ||
  props.permissions.includes(permission)

watch(() => [props.token, props.profileVersion], loadProfile, { immediate: true })
watch(() => props.activePage, (page) => {
  if (page !== 'vehicles') isVehiclesExpanded.value = false
  if (page !== 'tables' && page !== 'selected-components') {
    isTablesExpanded.value = false
  }
  if (page !== 'settings') isSettingsExpanded.value = false
})
onBeforeUnmount(clearAvatar)

function clearAvatar() {
  if (avatarUrl.value) URL.revokeObjectURL(avatarUrl.value)
  avatarUrl.value = ''
}

async function loadProfile() {
  const response = await fetch('/api/profile', {
    headers: { Authorization: `Bearer ${props.token}` },
  })
  if (!response.ok) return
  profile.value = await response.json()
  clearAvatar()
  if (profile.value.hasAvatar) {
    const avatarResponse = await fetch('/api/profile/avatar', {
      headers: { Authorization: `Bearer ${props.token}` },
    })
    if (avatarResponse.ok) {
      avatarUrl.value = URL.createObjectURL(await avatarResponse.blob())
    }
  }
}

function toggleTables() {
  isTablesExpanded.value = !isTablesExpanded.value
}

function toggleVehicles() {
  isVehiclesExpanded.value = !isVehiclesExpanded.value
  if (isVehiclesExpanded.value) emit('navigate', 'vehicles')
}

function toggleSettings() {
  isSettingsExpanded.value = !isSettingsExpanded.value
}
</script>

<template>
  <aside class="sidebar" :class="{ 'sidebar--collapsed': collapsed }">
    <div class="sidebar-header">
      <div class="sidebar-brand">
        <img class="sidebar-brand-logo" :src="brandLogo" alt="" aria-hidden="true" />
        <span class="sidebar-label brand-name">ARM механик ООО "ДВС"</span>
      </div>
    </div>

    <button
      v-if="false"
      class="collapse-handle"
      type="button"
      aria-label="Свернуть меню"
      :aria-expanded="true"
      @click="$emit('toggle')"
    >
      <span aria-hidden="true">‹</span>
    </button>

    <nav class="sidebar-nav" aria-label="Основная навигация">
      <button
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'home' }"
        type="button"
        :aria-current="activePage === 'home' ? 'page' : undefined"
        @click="$emit('navigate', 'home')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M3 10.8 12 3l9 7.8V21h-6v-6H9v6H3V10.8Z" />
        </svg>
        <span class="sidebar-label">Главная</span>
      </button>
          <button
            v-if="canView('menu.tables')"
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'tables' || activePage === 'selected-components' }"
        type="button"
        :aria-expanded="isTablesExpanded"
        aria-controls="tables-submenu"
        @click="toggleTables"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 5h16v14H4V5Zm0 5h16M9 5v14" />
        </svg>
        <span class="sidebar-label">Таблицы</span>
        <span
          class="sidebar-label submenu-chevron"
          :class="{ 'submenu-chevron--expanded': isTablesExpanded }"
          aria-hidden="true"
        >
          ›
        </span>
      </button>
      <div
        v-if="canView('menu.tables') && isTablesExpanded"
        id="tables-submenu"
        class="nav-submenu"
      >
        <button
          v-if="canView('tables.v_full_ost')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'tables' && activeTable === 'v_full_ost' }"
          type="button"
          @click="$emit('navigate-table', 'v_full_ost')"
        >
          Остатки на складе
        </button>
        <button
          v-if="canView('tables.v_meh_ost')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'tables' && activeTable === 'v_meh_ost' }"
          type="button"
          @click="$emit('navigate-table', 'v_meh_ost')"
        >
          Остатки механиков
        </button>
        <button
          v-if="canView('tables.v_workers')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'tables' && activeTable === 'v_workers' }"
          type="button"
          @click="$emit('navigate-table', 'v_workers')"
        >
          Работники
        </button>
        <button
          v-if="canView('tables.selected_components')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'selected-components' }"
          type="button"
          @click="$emit('navigate-selected-components')"
        >
          Выбранные компоненты
          <span v-if="selectedComponentsCount" class="nav-item-count">
            {{ selectedComponentsCount }}
          </span>
        </button>
      </div>
      <button
        v-if="canView('menu.vehicles')"
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'vehicles' }"
        type="button"
        :aria-expanded="isVehiclesExpanded"
        aria-controls="transport-submenu"
        @click="toggleVehicles"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M5 16V9l2-4h10l2 4v7M4 12h16M7 16v3M17 16v3M7.5 9h9M8 14h.01M16 14h.01" />
        </svg>
        <span class="sidebar-label">Транспорт</span>
        <span class="sidebar-label submenu-chevron" aria-hidden="true">›</span>
      </button>
      <div
        v-if="canView('menu.vehicles') && isVehiclesExpanded"
        id="transport-submenu"
        class="nav-submenu"
      >
        <button
          v-if="canView('vehicles.repair_request')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'repairRequest' }"
          type="button"
          @click="$emit('navigate-vehicle', 'repairRequest')"
        >
          Заявка на ремонт
        </button>
        <button
          v-if="canView('vehicles.works')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'works' }"
          type="button"
          @click="$emit('navigate-vehicle', 'works')"
        >
          Ремонт
        </button>
        <button
          v-if="canView('vehicles.parts_request')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'partsRequest' }"
          type="button"
          @click="$emit('navigate-vehicle', 'partsRequest')"
        >
          Заявка на закупку ЗЧ
        </button>
        <button
          v-if="canView('vehicles.hours')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'hours' }"
          type="button"
          @click="$emit('navigate-vehicle', 'hours')"
        >
          Моточасы
        </button>
        <button
          v-if="canView('vehicles.report')"
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'report' }"
          type="button"
          @click="$emit('navigate-vehicle', 'report')"
        >
          Отчёт
        </button>
      </div>
      <button
        v-if="canView('menu.requirements')"
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'requirements' }"
        type="button"
        :aria-current="activePage === 'requirements' ? 'page' : undefined"
        @click="$emit('navigate', 'requirements')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M6 3h9l3 3v15H6V3Zm9 0v4h4M9 11h6M9 15h6" />
        </svg>
        <span class="sidebar-label">Выписанные требования</span>
      </button>
      <button
        v-if="canView('menu.maintenance')"
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'maintenance' }"
        type="button"
        :aria-current="activePage === 'maintenance' ? 'page' : undefined"
        @click="$emit('navigate', 'maintenance')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 5h16v14H4V5Zm4 4h8M8 13h5M8 16h3" />
        </svg>
        <span class="sidebar-label">Техническое обслуживание</span>
      </button>
      <button
        v-if="isAdmin"
        class="nav-link"
        :class="{ 'nav-link--active': activePage === 'settings' }"
        type="button"
        :aria-expanded="isSettingsExpanded"
        aria-controls="settings-submenu"
        @click="toggleSettings"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 15.5A3.5 3.5 0 1 0 12 8a3.5 3.5 0 0 0 0 7.5Zm8-3.5 2-1-2-3.5-2.2.6a8 8 0 0 0-1.8-1L15.5 5h-4L11 7.1a8 8 0 0 0-1.8 1L7 7.5 5 11l2 1a8 8 0 0 0 0 2l-2 1 2 3.5 2.2-.6a8 8 0 0 0 1.8 1l.5 2.1h4l.5-2.1a8 8 0 0 0 1.8-1l2.2.6 2-3.5-2-1a8 8 0 0 0 0-2Z" />
        </svg>
        <span class="sidebar-label">Настройки</span>
        <span
          class="sidebar-label submenu-chevron"
          :class="{ 'submenu-chevron--expanded': isSettingsExpanded }"
          aria-hidden="true"
        >
          ›
        </span>
      </button>
      <div
        v-if="isAdmin && isSettingsExpanded"
        id="settings-submenu"
        class="nav-submenu"
      >
        <button
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'settings' && activeSettings === 'users' }"
          type="button"
          @click="$emit('navigate-settings', 'users')"
        >
          Пользователи
        </button>
        <button
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'settings' && activeSettings === 'registration' }"
          type="button"
          @click="$emit('navigate-settings', 'registration')"
        >
          Регистрация пользователя
        </button>
        <button
          class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'settings' && activeSettings === 'professions' }"
          type="button"
          @click="$emit('navigate-settings', 'professions')"
        >
          Профессии
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'material-groups',
          }"
          type="button"
          @click="$emit('navigate-settings', 'material-groups')"
        >
          Группы материалов
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'maintenance',
          }"
          type="button"
          @click="$emit('navigate-settings', 'maintenance')"
        >
          Шаблоны ТО
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'media-storage',
          }"
          type="button"
          @click="$emit('navigate-settings', 'media-storage')"
        >
          Хранение медиафайлов
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'csv-files',
          }"
          type="button"
          @click="$emit('navigate-settings', 'csv-files')"
        >
          Загрузка CSV
        </button>
      </div>
    </nav>

    <div class="sidebar-footer">
      <button
        class="nav-link profile-nav-link"
        :class="{ 'nav-link--active': activePage === 'profile' }"
        type="button"
        @click="$emit('navigate', 'profile')"
      >
        <span class="profile-avatar profile-avatar--small">
          <img v-if="avatarUrl" :src="avatarUrl" alt="" />
          <span v-else>{{ profile?.firstName?.[0] || 'П' }}</span>
        </span>
        <span class="sidebar-label">Редактировать профиль</span>
      </button>
      <button class="nav-link logout-button" type="button" @click="$emit('logout')">
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M10 5H4v14h6M14 8l4 4-4 4m4-4H9" />
        </svg>
        <span class="sidebar-label">Выйти</span>
      </button>
    </div>
  </aside>
</template>
