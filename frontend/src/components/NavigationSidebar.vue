<script setup>
import { expandPermissions } from "../utils/permissions"
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import BrandIdentity from './BrandIdentity.vue'

const props = defineProps({
  mobileOpen: { type: Boolean, default: false },
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
  'close',
  'navigate',
  'navigate-table',
  'navigate-selected-components',
  'navigate-settings',
  'navigate-vehicle',
  'logout',
])
const sidebarElement = ref(null)
const mediaQuery = window.matchMedia('(max-width: 760px)')
const isMobile = ref(mediaQuery.matches)
/** Обновляет состояние навигации при изменении ширины экрана. */
function handleViewportChange(event) {
  isMobile.value = event.matches
  if (!event.matches) emit('close')
}
onMounted(() => mediaQuery.addEventListener('change', handleViewportChange))
onBeforeUnmount(() => {
  mediaQuery.removeEventListener('change', handleViewportChange)
  document.body.classList.remove('navigation-open')
})
watch(
  () => props.mobileOpen,
  async (open) => {
    document.body.classList.toggle('navigation-open', open)
    if (open) {
      await nextTick()
      requestAnimationFrame(() => {
        if (props.mobileOpen) sidebarElement.value?.querySelector('.sidebar-close')?.focus()
      })
    }
  },
)
/** Обрабатывает клавиатурное управление меню навигации. */
function handleMenuKeydown(event) {
  if (!isMobile.value || !props.mobileOpen) return
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
  if (event.key !== 'Tab') return
  const buttons = [
    ...sidebarElement.value.querySelectorAll('button:not(:disabled)'),
  ].filter((el) => el.getClientRects().length)
  const first = buttons[0]
  const last = buttons.at(-1)
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last?.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first?.focus()
  }
}
const isTablesExpanded = ref(false)
const isVehiclesExpanded = ref(false)
const isReportsExpanded = ref(false)
const isSettingsExpanded = ref(false)
const isGamesExpanded = ref(['mechanic', 'tanks'].includes(props.activePage))
const profile = ref(null)
const avatarUrl = ref('')
const /** Проверяет наличие разрешения на отображение раздела. */ canView = (permission) =>
  props.isAdmin ||
  String(localStorage.getItem('myapp.userRole')).toLowerCase() ===
    'administrator' ||
  expandPermissions(props.permissions).includes(permission)

watch(() => [props.token, props.profileVersion], loadProfile, {
  immediate: true,
})
watch(
  () => props.activePage,
  (page) => {
    if (!['vehicles', 'maintenance', 'auxiliary-works'].includes(page)) isVehiclesExpanded.value = false
    if (page !== 'tables' && page !== 'selected-components') {
      isTablesExpanded.value = false
    }
    if (page !== 'settings') isSettingsExpanded.value = false
    isGamesExpanded.value = ['mechanic', 'tanks'].includes(page)
  },
)
onBeforeUnmount(clearAvatar)

/** Освобождает временный URL аватара и сбрасывает его отображение. */
function clearAvatar() {
  if (avatarUrl.value) URL.revokeObjectURL(avatarUrl.value)
  avatarUrl.value = ''
}

/** Загружает профиль текущего пользователя. */
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

/** Раскрывает или сворачивает пункты табличных разделов. */
function toggleTables() {
  if (props.collapsed && !isMobile.value) emit('toggle')
  isTablesExpanded.value = !isTablesExpanded.value
}

/** Раскрывает или сворачивает разделы работы с техникой. */
function toggleVehicles() {
  if (props.collapsed && !isMobile.value) emit('toggle')
  isVehiclesExpanded.value = !isVehiclesExpanded.value
}

/** Раскрывает или сворачивает список отчётов. */
function toggleReports() {
  if (props.collapsed && !isMobile.value) {
    isReportsExpanded.value = true
    emit('toggle')
    return
  }
  isReportsExpanded.value = !isReportsExpanded.value
}

/** Раскрывает или сворачивает разделы настроек. */
function toggleSettings() {
  if (props.collapsed && !isMobile.value) emit('toggle')
  isSettingsExpanded.value = !isSettingsExpanded.value
}

/** Открывает список игр, раскрывая свёрнутое меню при первом нажатии. */
function toggleGames() {
  if (props.collapsed && !isMobile.value) {
    isGamesExpanded.value = true
    emit('toggle')
    return
  }
  isGamesExpanded.value = !isGamesExpanded.value
}
</script>

<template>
  <aside
    id="main-navigation"
    ref="sidebarElement"
    class="sidebar"
    :class="{
      'sidebar--collapsed': collapsed,
      'sidebar--mobile-open': mobileOpen,
    }"
    :inert="isMobile && !mobileOpen"
    :role="isMobile && mobileOpen ? 'dialog' : undefined"
    :aria-modal="isMobile && mobileOpen ? true : undefined"
    aria-label="Основная навигация"
    @keydown="handleMenuKeydown"
  >
    <div class="sidebar-header">
      <BrandIdentity />
      <button
        class="icon-button sidebar-close"
        type="button"
        aria-label="Закрыть меню"
        @click="emit('close')"
      >
        ✕
      </button>
    </div>
    <button
      class="icon-button desktop-menu-toggle"
      type="button"
      :aria-label="collapsed ? 'Развернуть меню' : 'Свернуть меню'"
      :aria-expanded="!collapsed"
      @click="emit('toggle')"
    >
      <span aria-hidden="true">{{ collapsed ? '→' : '←' }}</span>
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
        :class="{
          'nav-link--active':
            activePage === 'tables' || activePage === 'selected-components',
        }"
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
          :class="{
            'nav-submenu-link--active':
              activePage === 'tables' && activeTable === 'v_full_ost',
          }"
          type="button"
          @click="$emit('navigate-table', 'v_full_ost')"
        >
          Остатки на складе
        </button>
        <button
          v-if="canView('tables.v_meh_ost')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'tables' && activeTable === 'v_meh_ost',
          }"
          type="button"
          @click="$emit('navigate-table', 'v_meh_ost')"
        >
          Остатки механиков
        </button>
        <button
          v-if="canView('tables.v_workers')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'tables' && activeTable === 'v_workers',
          }"
          type="button"
          @click="$emit('navigate-table', 'v_workers')"
        >
          Работники
        </button>
        <button
          v-if="canView('tables.selected_components')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active': activePage === 'selected-components',
          }"
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
        v-if="(canView('menu.vehicles') || canView('menu.maintenance') || canView('menu.auxiliary_works'))"
        class="nav-link"
        :class="{ 'nav-link--active': ['vehicles', 'maintenance', 'auxiliary-works'].includes(activePage) }"
        type="button"
        :aria-expanded="isVehiclesExpanded"
        aria-controls="transport-submenu"
        @click="toggleVehicles"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path
            d="M5 16V9l2-4h10l2 4v7M4 12h16M7 16v3M17 16v3M7.5 9h9M8 14h.01M16 14h.01"
          />
        </svg>
        <span class="sidebar-label">Транспорт</span>
        <span class="sidebar-label submenu-chevron" :class="{ 'submenu-chevron--expanded': isVehiclesExpanded }" aria-hidden="true">›</span>
      </button>
      <div
        v-if="(canView('menu.vehicles') || canView('menu.maintenance') || canView('menu.auxiliary_works')) && isVehiclesExpanded"
        id="transport-submenu"
        class="nav-submenu"
      >
        <button
          v-if="canView('vehicles.repair_request')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'vehicles' &&
              activeVehicleSection === 'repairRequest',
          }"
          type="button"
          @click="$emit('navigate-vehicle', 'repairRequest')"
        >
          Заявка на ремонт
        </button>
        <button
          v-if="canView('vehicles.repair')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'vehicles' && activeVehicleSection === 'works',
          }"
          type="button"
          @click="$emit('navigate-vehicle', 'works')"
        >
          Ремонт
        </button>
        <button
          v-if="canView('vehicles.repair_history')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'vehicles' &&
              activeVehicleSection === 'repairHistory',
          }"
          type="button"
          @click="$emit('navigate-vehicle', 'repairHistory')"
        >
          История ремонта
        </button>
        <button
          v-if="canView('vehicles.parts_request')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'vehicles' &&
              activeVehicleSection === 'partsRequest',
          }"
          type="button"
          @click="$emit('navigate-vehicle', 'partsRequest')"
        >
          Заявка на закупку ЗЧ
        </button>
        <button
          v-if="canView('vehicles.hours')"
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'vehicles' && activeVehicleSection === 'hours',
          }"
          type="button"
          @click="$emit('navigate-vehicle', 'hours')"
        >
          Моточасы
        </button>
        <button v-if="canView('vehicles.maintenance_execution')" class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'vehicles' && activeVehicleSection === 'maintenanceExecution' }"
          type="button" :aria-current="activePage === 'vehicles' && activeVehicleSection === 'maintenanceExecution' ? 'page' : undefined"
          @click="$emit('navigate-vehicle', 'maintenanceExecution')">Проведение ТО</button>
        <button v-if="canView('menu.maintenance')" class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'maintenance' }" type="button"
          :aria-current="activePage === 'maintenance' ? 'page' : undefined"
          @click="$emit('navigate', 'maintenance')">Техническое обслуживание</button>
        <button v-if="canView('menu.auxiliary_works')" class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'auxiliary-works' }" type="button"
          :aria-current="activePage === 'auxiliary-works' ? 'page' : undefined"
          @click="$emit('navigate', 'auxiliary-works')">Вспомогательные работы</button>
      </div>
      <button v-if="canView('menu.reports')" class="nav-link" :class="{ 'nav-link--active': ['reports', 'requirements'].includes(activePage) }"
        type="button" :aria-expanded="isReportsExpanded" aria-controls="reports-submenu" @click="toggleReports">
        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="size-6" aria-hidden="true" style="stroke-width: 1.5; fill: none">
          <path stroke-linecap="round" stroke-linejoin="round" d="M21 12a2.25 2.25 0 0 0-2.25-2.25H15a3 3 0 1 1-6 0H5.25A2.25 2.25 0 0 0 3 12m18 0v6a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 18v-6m18 0V9M3 12V9m18 0a2.25 2.25 0 0 0-2.25-2.25H5.25A2.25 2.25 0 0 0 3 9m18 0V6a2.25 2.25 0 0 0-2.25-2.25H5.25A2.25 2.25 0 0 0 3 6v3" />
        </svg>
        <span class="sidebar-label">Отчёты</span><span class="sidebar-label submenu-chevron" aria-hidden="true">{{ isReportsExpanded ? '⌄' : '›' }}</span>
      </button>
      <div v-if="canView('menu.reports') && isReportsExpanded" id="reports-submenu" class="nav-submenu">
        <button v-if="canView('reports.shift')" class="nav-submenu-link" :class="{ 'nav-submenu-link--active': activePage === 'reports' && activeVehicleSection === 'report' }"
          type="button" :aria-current="activePage === 'reports' && activeVehicleSection === 'report' ? 'page' : undefined" @click="$emit('navigate-vehicle', 'report')">Отчёт за смену</button>
        <button v-if="canView('reports.gto')" class="nav-submenu-link" :class="{ 'nav-submenu-link--active': activePage === 'reports' && activeVehicleSection === 'gtoForecast' }"
          type="button" :aria-current="activePage === 'reports' && activeVehicleSection === 'gtoForecast' ? 'page' : undefined" @click="$emit('navigate-vehicle', 'gtoForecast')">Выход ГТО</button>
        <button v-if="canView('menu.requirements')" class="nav-submenu-link"
          :class="{ 'nav-submenu-link--active': activePage === 'requirements' }" type="button"
          :aria-current="activePage === 'requirements' ? 'page' : undefined"
          @click="$emit('navigate', 'requirements')">Выписанные требования</button>
      </div>
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
          <path
            d="M12 15.5A3.5 3.5 0 1 0 12 8a3.5 3.5 0 0 0 0 7.5Zm8-3.5 2-1-2-3.5-2.2.6a8 8 0 0 0-1.8-1L15.5 5h-4L11 7.1a8 8 0 0 0-1.8 1L7 7.5 5 11l2 1a8 8 0 0 0 0 2l-2 1 2 3.5 2.2-.6a8 8 0 0 0 1.8 1l.5 2.1h4l.5-2.1a8 8 0 0 0 1.8-1l2.2.6 2-3.5-2-1a8 8 0 0 0 0-2Z"
          />
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
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'users',
          }"
          type="button"
          @click="$emit('navigate-settings', 'users')"
        >
          Пользователи
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'registration',
          }"
          type="button"
          @click="$emit('navigate-settings', 'registration')"
        >
          Регистрация пользователя
        </button>
        <button
          class="nav-submenu-link"
          :class="{
            'nav-submenu-link--active':
              activePage === 'settings' && activeSettings === 'professions',
          }"
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
      <button class="nav-link" :class="{ 'nav-link--active': ['mechanic', 'tanks'].includes(activePage) }" type="button" title="Игры" :aria-expanded="isGamesExpanded" aria-controls="games-submenu" @click="toggleGames">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7h10c3 0 5 10 3 11-2 1-4-3-5-3H9c-1 0-3 4-5 3C2 17 4 7 7 7Z"/><path d="M8 9v5M5.5 11.5h5M15 10h.01M18 13h.01"/></svg>
        <span class="sidebar-label">Игры</span><span class="sidebar-label submenu-chevron" aria-hidden="true">{{ isGamesExpanded ? '⌄' : '›' }}</span>
      </button>
      <div v-if="isGamesExpanded" id="games-submenu" class="nav-submenu">
        <button class="nav-submenu-link" :class="{ 'nav-submenu-link--active': activePage === 'mechanic' }" type="button" :aria-current="activePage === 'mechanic' ? 'page' : undefined" @click="$emit('navigate', 'mechanic')">Смена</button>
        <button class="nav-submenu-link" :class="{ 'nav-submenu-link--active': activePage === 'tanks' }" type="button" :aria-current="activePage === 'tanks' ? 'page' : undefined" @click="$emit('navigate', 'tanks')">Танки 3D</button>
      </div>
      <button class="nav-link" :class="{ 'nav-link--active': activePage === 'help' }"
        type="button" :aria-current="activePage === 'help' ? 'page' : undefined" title="Помощь"
        @click="$emit('navigate', 'help')">
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M9.5 9a2.5 2.5 0 1 1 4 2c-1 .6-1.5 1-1.5 2.5M12 16v1" /></svg>
        <span class="sidebar-label">Помощь</span>
      </button>
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
      <button
        class="nav-link logout-button"
        type="button"
        @click="$emit('logout')"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path d="M10 5H4v14h6M14 8l4 4-4 4m4-4H9" />
        </svg>
        <span class="sidebar-label">Выйти</span>
      </button>
    </div>
  </aside>
</template>
