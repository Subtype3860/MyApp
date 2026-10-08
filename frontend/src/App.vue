<script setup>
import { nextTick, onBeforeUnmount, ref } from 'vue'
import BrandIdentity from './components/BrandIdentity.vue'
import AuthLogin from './components/AuthLogin.vue'
import HomeView from './components/HomeView.vue'
import MaintenanceView from './components/MaintenanceView.vue'
import NavigationSidebar from './components/NavigationSidebar.vue'
import ProfileView from './components/ProfileView.vue'
import RequirementsJournalView from './components/RequirementsJournalView.vue'
import SelectedComponentsView from './components/SelectedComponentsView.vue'
import SettingsView from './components/SettingsView.vue'
import TablesView from './components/TablesView.vue'
import VehicleJournalView from './components/VehicleJournalView.vue'

const TOKEN_KEY = 'myapp.authToken'
const ROLE_KEY = 'myapp.userRole'
const PERMISSIONS_KEY = 'myapp.userPermissions'

const storedToken = localStorage.getItem(TOKEN_KEY)
const storedRole = localStorage.getItem(ROLE_KEY)
const storedPermissions = localStorage.getItem(PERMISSIONS_KEY)
const legacyPermissionMap = {
  'tables.view': 'menu.tables',
  'vehicles.view': 'menu.vehicles',
  'requirements.view': 'menu.requirements',
  'maintenance.view': 'menu.maintenance',
}
const parsePermissions = (value) => {
  try {
    const permissions = JSON.parse(value || '[]')
    return [
      ...new Set(
        permissions.map(
          (permission) => legacyPermissionMap[permission] || permission,
        ),
      ),
    ]
  } catch {
    return []
  }
}
if (storedToken && !storedRole) {
  localStorage.removeItem(TOKEN_KEY)
}

const token = ref(storedRole ? storedToken : null)
const role = ref(storedRole)
const permissions = ref(parsePermissions(storedPermissions))
const isNavigationCollapsed = ref(false)
const mobileMenuOpen = ref(false)
const mobileMenuButton = ref(null)
function closeMobileMenu() {
  const wasOpen = mobileMenuOpen.value
  mobileMenuOpen.value = false
  if (wasOpen) nextTick(() => mobileMenuButton.value?.focus())
}
function navigate(page) {
  currentPage.value = page
  closeMobileMenu()
}
const currentPage = ref('home')
const currentTable = ref('v_full_ost')
const currentSettings = ref('users')
const currentVehicleSection = ref('defects')
const selectedComponentRows = ref([])
const selectedResponsibleEmployee = ref(null)
const profileVersion = ref(0)
const sessionExpiredMessage = ref('')

function handleAuthenticated(authData) {
  localStorage.setItem(TOKEN_KEY, authData.token)
  localStorage.setItem(ROLE_KEY, authData.role)
  localStorage.setItem(
    PERMISSIONS_KEY,
    JSON.stringify(authData.permissions || []),
  )
  token.value = authData.token
  role.value = authData.role
  permissions.value = authData.permissions || []
  sessionExpiredMessage.value = ''
}

function handleLogout() {
  closeMobileMenu()
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(ROLE_KEY)
  localStorage.removeItem(PERMISSIONS_KEY)
  token.value = null
  role.value = null
  permissions.value = []
  isNavigationCollapsed.value = true
  currentPage.value = 'home'
  selectedComponentRows.value = []
  selectedResponsibleEmployee.value = null
}

function handleSessionExpired(event) {
  sessionExpiredMessage.value =
    event.detail?.message ||
    'Срок действия сессии истёк. Требуется повторная авторизация.'
  handleLogout()
}

window.addEventListener('myapp:session-expired', handleSessionExpired)
onBeforeUnmount(() => {
  window.removeEventListener('myapp:session-expired', handleSessionExpired)
})

function openTable(tableName) {
  closeMobileMenu()
  if (
    tableName !== currentTable.value &&
    (tableName === 'v_full_ost' || tableName === 'v_meh_ost')
  ) {
    selectedResponsibleEmployee.value = null
  }
  currentTable.value = tableName
  currentPage.value = 'tables'
}

function openSettings(section) {
  closeMobileMenu()
  currentSettings.value = section
  currentPage.value = 'settings'
}

function openVehicleSection(section) {
  closeMobileMenu()
  currentVehicleSection.value = section
  currentPage.value = 'vehicles'
}

function openSelectedComponents() {
  closeMobileMenu()
  currentPage.value = 'selected-components'
}

function applyMaintenanceTemplate(rows) {
  currentTable.value = 'v_full_ost'
  updateSelectedComponentRows(rows)
}

function updateResponsibleEmployee(employee) {
  if (
    employee &&
    selectedComponentRows.value.some(
      (row) => row.__sourceTable !== employee.sourceTable,
    )
  ) {
    selectedComponentRows.value = []
  }
  selectedResponsibleEmployee.value = employee
}

function updateSelectedComponentRows(rows) {
  selectedComponentRows.value = rows
  const sourceTable = rows[0]?.__sourceTable
  if (
    sourceTable &&
    selectedResponsibleEmployee.value?.sourceTable !== sourceTable
  ) {
    selectedResponsibleEmployee.value = null
  }
}
</script>

<template>
  <AuthLogin
    v-if="!token"
    :session-message="sessionExpiredMessage"
    @authenticated="handleAuthenticated"
  />

  <div v-else class="app-shell">
    <header class="mobile-topbar" :inert="mobileMenuOpen">
      <BrandIdentity />
      <button
        ref="mobileMenuButton"
        type="button"
        class="icon-button"
        aria-label="Открыть меню"
        aria-controls="main-navigation"
        :aria-expanded="mobileMenuOpen"
        @click="mobileMenuOpen = true"
      >
        <span class="hamburger" aria-hidden="true"
          ><span></span><span></span><span></span
        ></span>
      </button>
    </header>
    <div
      v-if="mobileMenuOpen"
      class="navigation-backdrop"
      aria-hidden="true"
      @click="closeMobileMenu"
    ></div>
    <NavigationSidebar
      :collapsed="isNavigationCollapsed"
      :mobile-open="mobileMenuOpen"
      @close="closeMobileMenu"
      :active-page="currentPage"
      :active-table="currentTable"
      :active-settings="currentSettings"
      :active-vehicle-section="currentVehicleSection"
      :is-admin="String(role).toLowerCase() === 'administrator'"
      :permissions="permissions"
      :selected-components-count="selectedComponentRows.length"
      :token="token"
      :profile-version="profileVersion"
      @toggle="isNavigationCollapsed = !isNavigationCollapsed"
      @navigate="navigate"
      @navigate-table="openTable"
      @navigate-selected-components="openSelectedComponents"
      @navigate-settings="openSettings"
      @navigate-vehicle="openVehicleSection"
      @logout="handleLogout"
    />
    <div class="page-content" :inert="mobileMenuOpen">
      <HomeView
        v-if="currentPage === 'home'"
        :permissions="permissions"
        :is-admin="String(role).toLowerCase() === 'administrator'"
        @navigate="navigate"
        @navigate-vehicle="openVehicleSection"
        :navigation-collapsed="isNavigationCollapsed"
        :token="token"
      />
      <TablesView
        v-else-if="currentPage === 'tables'"
        :navigation-collapsed="isNavigationCollapsed"
        :selected-table-id="currentTable"
        :selected-component-rows="selectedComponentRows"
        :selected-responsible-employee="selectedResponsibleEmployee"
        :token="token"
        @select-table="currentTable = $event"
        @show-selected-components="openSelectedComponents"
        @update:selected-component-rows="updateSelectedComponentRows"
        @update:selected-responsible-employee="updateResponsibleEmployee"
      />
      <SelectedComponentsView
        v-else-if="currentPage === 'selected-components'"
        :navigation-collapsed="isNavigationCollapsed"
        :rows="selectedComponentRows"
        :responsible-employee="selectedResponsibleEmployee"
        :token="token"
        @back="openTable(currentTable)"
        @clear="selectedComponentRows = []"
        @update:rows="updateSelectedComponentRows"
      />
      <RequirementsJournalView
        v-else-if="currentPage === 'requirements'"
        :navigation-collapsed="isNavigationCollapsed"
        :token="token"
      />
      <MaintenanceView
        v-else-if="currentPage === 'maintenance'"
        :navigation-collapsed="isNavigationCollapsed"
        :selected-count="selectedComponentRows.length"
        :token="token"
        @apply-template="applyMaintenanceTemplate"
        @show-selected="openSelectedComponents"
      />
      <VehicleJournalView
        v-else-if="currentPage === 'vehicles'"
        :navigation-collapsed="isNavigationCollapsed"
        :section="currentVehicleSection"
        :token="token"
      />
      <SettingsView
        v-else-if="
          currentPage === 'settings' &&
          String(role).toLowerCase() === 'administrator'
        "
        :navigation-collapsed="isNavigationCollapsed"
        :selected-section="currentSettings"
        :token="token"
      />
      <ProfileView
        v-else-if="currentPage === 'profile'"
        :navigation-collapsed="isNavigationCollapsed"
        :token="token"
        @profile-updated="profileVersion++"
      />
    </div>
  </div>
</template>
