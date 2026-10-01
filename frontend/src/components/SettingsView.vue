<script setup>
import { reactive, ref, watch } from 'vue'
import CsvFilesSettings from './CsvFilesSettings.vue'
import MaterialGroupsSettings from './MaterialGroupsSettings.vue'
import MaintenanceTemplatesSettings from './MaintenanceTemplatesSettings.vue'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  navigationCollapsed: {
    type: Boolean,
    required: true,
  },
  selectedSection: {
    type: String,
    required: true,
  },
  token: {
    type: String,
    required: true,
  },
})

const users = ref([])
const professions = ref([])
const permissionCatalog = ref([])
const editingUserId = ref(null)
const editingProfessionId = ref(null)
const professionName = ref('')
const isLoading = ref(false)
const isSubmitting = ref(false)
const errorMessage = ref('')
const successMessage = ref('')

const registrationForm = reactive({
  firstName: '',
  middleName: '',
  lastName: '',
  userName: '',
  password: '',
  professionId: '',
})

const editUserForm = reactive({
  firstName: '',
  middleName: '',
  lastName: '',
  userName: '',
  password: '',
  professionId: '',
  role: 'user',
  permissions: [],
})

const transliterationMap = {
  а: 'a',
  б: 'b',
  в: 'v',
  г: 'g',
  д: 'd',
  е: 'e',
  ё: 'yo',
  ж: 'zh',
  з: 'z',
  и: 'i',
  й: 'y',
  к: 'k',
  л: 'l',
  м: 'm',
  н: 'n',
  о: 'o',
  п: 'p',
  р: 'r',
  с: 's',
  т: 't',
  у: 'u',
  ф: 'f',
  х: 'h',
  ц: 'ts',
  ч: 'ch',
  ш: 'sh',
  щ: 'sch',
  ъ: '',
  ы: 'y',
  ь: '',
  э: 'e',
  ю: 'yu',
  я: 'ya',
}

function transliterate(value) {
  return [...value.toLocaleLowerCase('ru')]
    .map((character) => transliterationMap[character] ?? character)
    .join('')
    .replace(/[^a-z0-9]/g, '')
}

function buildUserName(firstName, middleName, lastName) {
  return [firstName, middleName, lastName]
    .map((part) => transliterate([...part.trim()].slice(0, 2).join('')))
    .join('.')
}

function buildTemporaryPassword(date = new Date()) {
  const pad = (value) => String(value).padStart(2, '0')
  return `${pad(date.getDate())}${pad(date.getMonth() + 1)}${date.getFullYear()}${pad(date.getMinutes())}`
}

function authHeaders(includeContentType = false) {
  return {
    Authorization: `Bearer ${props.token}`,
    ...(includeContentType ? { 'Content-Type': 'application/json' } : {}),
  }
}

async function readError(response, fallback) {
  const problem = await response.json().catch(() => null)
  return problem?.errors
    ? Object.values(problem.errors).flat()[0]
    : problem?.message || fallback
}

async function loadUsers() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await fetch('/api/admin/users', {
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error('Не удалось загрузить пользователей.')
    }

    users.value = await response.json()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось загрузить пользователей.'
  } finally {
    isLoading.value = false
  }
}

async function loadPermissionCatalog() {
  try {
    const response = await fetch('/api/admin/users/permissions', {
      headers: authHeaders(),
    })
    if (!response.ok) throw new Error('Не удалось загрузить каталог прав.')
    permissionCatalog.value = await response.json()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось загрузить каталог прав.'
  }
}

async function registerUser() {
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''
  registrationForm.password = buildTemporaryPassword()

  try {
    const response = await fetch('/api/admin/users', {
      method: 'POST',
      headers: authHeaders(true),
      body: JSON.stringify(registrationForm),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось зарегистрировать пользователя.'))
    }

    const result = await response.json()
    Object.keys(registrationForm).forEach((field) => {
      registrationForm[field] = field === 'password' ? result.temporaryPassword : ''
    })
    successMessage.value = `Пользователь зарегистрирован. Временный пароль: ${result.temporaryPassword}`
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось зарегистрировать пользователя.'
  } finally {
    isSubmitting.value = false
  }
}

function editUser(user) {
  editingUserId.value = user.id
  Object.assign(editUserForm, {
    firstName: user.firstName,
    middleName: user.middleName,
    lastName: user.lastName,
    userName: user.userName,
    password: '',
    professionId: user.positionId,
    role: user.role,
    permissions: [...(user.permissions || [])],
  })
  errorMessage.value = ''
  successMessage.value = ''
}

function cancelUserEdit() {
  editingUserId.value = null
  editUserForm.password = ''
}

async function saveUser() {
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''

  try {
    const response = await fetch(`/api/admin/users/${editingUserId.value}`, {
      method: 'PUT',
      headers: authHeaders(true),
      body: JSON.stringify(editUserForm),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось сохранить пользователя.'))
    }

    cancelUserEdit()
    successMessage.value = 'Данные пользователя сохранены.'
    await loadUsers()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось сохранить пользователя.'
  } finally {
    isSubmitting.value = false
  }
}

async function loadProfessions() {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await fetch('/api/admin/professions', {
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error('Не удалось загрузить профессии.')
    }

    professions.value = await response.json()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось загрузить профессии.'
  } finally {
    isLoading.value = false
  }
}

async function ensureProfessionsLoaded() {
  if (professions.value.length === 0) {
    await loadProfessions()
  }
}

async function saveProfession() {
  isSubmitting.value = true
  errorMessage.value = ''
  successMessage.value = ''

  const isEditing = Boolean(editingProfessionId.value)
  const url = isEditing
    ? `/api/admin/professions/${editingProfessionId.value}`
    : '/api/admin/professions'

  try {
    const response = await fetch(url, {
      method: isEditing ? 'PUT' : 'POST',
      headers: authHeaders(true),
      body: JSON.stringify({ profession: professionName.value }),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось сохранить профессию.'))
    }

    professionName.value = ''
    editingProfessionId.value = null
    successMessage.value = isEditing ? 'Профессия изменена.' : 'Профессия добавлена.'
    await loadProfessions()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось сохранить профессию.'
  } finally {
    isSubmitting.value = false
  }
}

function editProfession(item) {
  editingProfessionId.value = item.id
  professionName.value = item.profession
  errorMessage.value = ''
  successMessage.value = ''
}

function cancelProfessionEdit() {
  editingProfessionId.value = null
  professionName.value = ''
}

async function deleteProfession(item) {
  if (!window.confirm(`Удалить профессию «${item.profession}»?`)) {
    return
  }

  errorMessage.value = ''
  successMessage.value = ''

  try {
    const response = await fetch(`/api/admin/professions/${item.id}`, {
      method: 'DELETE',
      headers: authHeaders(),
    })
    if (!response.ok) {
      throw new Error(await readError(response, 'Не удалось удалить профессию.'))
    }

    if (editingProfessionId.value === item.id) {
      cancelProfessionEdit()
    }
    successMessage.value = 'Профессия удалена.'
    await loadProfessions()
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'Не удалось удалить профессию.'
  }
}

function sectionTitle(section) {
  return {
    users: 'Пользователи',
    registration: 'Регистрация пользователя',
    professions: 'Профессии',
    'material-groups': 'Группы материалов',
    maintenance: 'Шаблоны ТО',
    'csv-files': 'Загрузка CSV',
  }[section]
}

watch(
  () => props.selectedSection,
  (section) => {
    errorMessage.value = ''
    successMessage.value = ''
    cancelUserEdit()
    cancelProfessionEdit()

    if (section === 'users') {
      loadUsers()
      ensureProfessionsLoaded()
      loadPermissionCatalog()
    } else if (section === 'registration') {
      registrationForm.password = buildTemporaryPassword()
      ensureProfessionsLoaded()
    } else if (section === 'professions') {
      loadProfessions()
    }
  },
  { immediate: true },
)

watch(
  () => [
    registrationForm.firstName,
    registrationForm.middleName,
    registrationForm.lastName,
  ],
  ([firstName, middleName, lastName]) => {
    registrationForm.userName = buildUserName(firstName, middleName, lastName)
  },
)
</script>

<template>
  <main class="home-page" :class="{ 'home-page--expanded': navigationCollapsed }">
    <header class="home-header">
      <div>
        <p class="eyebrow">АДМИНИСТРИРОВАНИЕ</p>
        <h1>{{ sectionTitle(selectedSection) }}</h1>
      </div>
      <UserAvatar :token="token" fallback="А" label="Профиль администратора" />
    </header>

    <section v-if="selectedSection === 'users'" class="macos-glass-panel settings-panel">
      <div class="settings-section-header">
        <div>
          <h2>Все пользователи</h2>
          <p>Зарегистрировано: {{ users.length }}</p>
        </div>
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadUsers">
          Обновить
        </button>
      </div>

      <form v-if="editingUserId" class="user-edit-form" @submit.prevent="saveUser">
        <div class="user-edit-header">
          <div>
            <h3>Редактирование пользователя</h3>
            <p>Оставьте пароль пустым, чтобы не изменять его.</p>
          </div>
          <button class="secondary-button" type="button" @click="cancelUserEdit">Закрыть</button>
        </div>

        <label>
          <span>Фамилия</span>
          <input v-model.trim="editUserForm.lastName" type="text" required />
        </label>
        <label>
          <span>Имя</span>
          <input v-model.trim="editUserForm.firstName" type="text" required />
        </label>
        <label>
          <span>Отчество</span>
          <input v-model.trim="editUserForm.middleName" type="text" />
        </label>
        <label>
          <span>Должность</span>
          <select v-model="editUserForm.professionId" required>
            <option value="" disabled>Выберите профессию</option>
            <option v-for="item in professions" :key="item.id" :value="item.id">
              {{ item.profession }}
            </option>
          </select>
        </label>
        <label>
          <span>Логин</span>
          <input v-model.trim="editUserForm.userName" type="text" required />
        </label>
        <label>
          <span>Роль</span>
          <select v-model="editUserForm.role" :disabled="editUserForm.userName === 'boora'">
            <option value="user">Пользователь</option>
            <option value="administrator">Администратор</option>
          </select>
        </label>
        <fieldset class="user-permissions">
          <legend>Что пользователь может просматривать</legend>
          <template v-for="menu in permissionCatalog" :key="menu.key">
            <label class="permission-option permission-option--menu">
              <input
                v-model="editUserForm.permissions"
                type="checkbox"
                :value="menu.key"
                :disabled="editUserForm.role === 'administrator'"
              />
              <span>{{ menu.label }}</span>
            </label>
            <label
              v-for="item in menu.children || []"
              :key="item.key"
              class="permission-option permission-option--submenu"
            >
              <input
                v-model="editUserForm.permissions"
                type="checkbox"
                :value="item.key"
                :disabled="editUserForm.role === 'administrator'"
              />
              <span>{{ item.label }}</span>
            </label>
          </template>
          <small v-if="editUserForm.role === 'administrator'">
            Администратор получает доступ ко всем разделам.
          </small>
        </fieldset>
        <label>
          <span>Новый пароль</span>
          <input v-model="editUserForm.password" type="password" minlength="6" />
        </label>

        <button class="primary-button user-edit-submit" type="submit" :disabled="isSubmitting">
          {{ isSubmitting ? 'Сохранение...' : 'Сохранить изменения' }}
        </button>
      </form>

      <p v-if="errorMessage" class="table-message table-message--error" role="alert">
        {{ errorMessage }}
      </p>
      <p v-if="successMessage" class="form-message form-message--success" role="status">
        {{ successMessage }}
      </p>
      <p v-if="isLoading" class="table-message">Загрузка пользователей...</p>

      <div v-else class="data-table-scroll">
        <table>
          <thead>
            <tr>
              <th>Фамилия Имя Отчество</th>
              <th>Должность</th>
              <th>Логин</th>
              <th>Роль</th>
              <th aria-label="Действия"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="user in users" :key="user.id">
              <td>{{ [user.lastName, user.firstName, user.middleName].filter(Boolean).join(' ') }}</td>
              <td>{{ user.position }}</td>
              <td>{{ user.userName }}</td>
              <td>{{ user.role === 'administrator' ? 'Администратор' : 'Пользователь' }}</td>
              <td class="user-action-cell">
                <button
                  class="icon-edit-button"
                  type="button"
                  :aria-label="`Редактировать пользователя ${user.userName}`"
                  title="Редактировать"
                  @click="editUser(user)"
                >
                  <svg viewBox="0 0 24 24" aria-hidden="true">
                    <path d="m4 20 4.5-1 10-10a2.1 2.1 0 0 0-3-3l-10 10L4 20Zm10-12 3 3" />
                  </svg>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section
      v-else-if="selectedSection === 'registration'"
      class="macos-glass-panel settings-panel registration-panel"
    >
      <h2>Новый пользователь</h2>
      <p>Заполните данные для создания учётной записи.</p>

      <form class="registration-form" @submit.prevent="registerUser">
        <label>
          <span>Имя</span>
          <input v-model.trim="registrationForm.firstName" type="text" autocomplete="given-name" required />
        </label>
        <label>
          <span>Отчество</span>
          <input v-model.trim="registrationForm.middleName" type="text" autocomplete="additional-name" required />
        </label>
        <label>
          <span>Фамилия</span>
          <input v-model.trim="registrationForm.lastName" type="text" autocomplete="family-name" required />
        </label>
        <label>
          <span>Логин</span>
          <input
            v-model="registrationForm.userName"
            type="text"
            autocomplete="username"
            readonly
            required
          />
        </label>
        <label>
          <span>Пароль</span>
          <input
            v-model="registrationForm.password"
            type="text"
            autocomplete="off"
            readonly
            required
          />
        </label>
        <label>
          <span>Должность</span>
          <select v-model="registrationForm.professionId" required>
            <option value="" disabled>
              {{ professions.length ? 'Выберите профессию' : 'Сначала добавьте профессию' }}
            </option>
            <option v-for="item in professions" :key="item.id" :value="item.id">
              {{ item.profession }}
            </option>
          </select>
        </label>

        <p v-if="errorMessage" class="form-message form-message--error" role="alert">
          {{ errorMessage }}
        </p>
        <p v-if="successMessage" class="form-message form-message--success" role="status">
          {{ successMessage }}
        </p>

        <button class="primary-button registration-submit" type="submit" :disabled="isSubmitting">
          {{ isSubmitting ? 'Регистрация...' : 'Зарегистрировать' }}
        </button>
      </form>
    </section>

    <section
      v-else-if="selectedSection === 'professions'"
      class="macos-glass-panel settings-panel professions-panel"
    >
      <div class="profession-create-section">
        <h2>{{ editingProfessionId ? 'Редактирование профессии' : 'Создание профессии' }}</h2>
        <p>Введите название профессии длиной до 40 символов.</p>

        <form class="profession-form" @submit.prevent="saveProfession">
          <label>
            <span class="visually-hidden">Название профессии</span>
            <input
              v-model.trim="professionName"
              type="text"
              maxlength="40"
              placeholder="Название профессии"
              required
            />
          </label>
          <button class="primary-button profession-save-button" type="submit" :disabled="isSubmitting">
            {{ editingProfessionId ? 'Сохранить' : 'Добавить' }}
          </button>
          <button
            v-if="editingProfessionId"
            class="secondary-button"
            type="button"
            @click="cancelProfessionEdit"
          >
            Отмена
          </button>
        </form>
      </div>

      <p v-if="errorMessage" class="form-message form-message--error" role="alert">
        {{ errorMessage }}
      </p>
      <p v-if="successMessage" class="form-message form-message--success" role="status">
        {{ successMessage }}
      </p>

      <div class="profession-list-header">
        <div>
          <h2>Список профессий</h2>
          <p>Записей: {{ professions.length }}</p>
        </div>
        <button class="secondary-button" type="button" :disabled="isLoading" @click="loadProfessions">
          Обновить
        </button>
      </div>

      <p v-if="isLoading" class="table-message">Загрузка профессий...</p>
      <div v-else class="data-table-scroll professions-table">
        <table>
          <thead>
            <tr>
              <th>Профессия</th>
              <th>Действия</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in professions" :key="item.id">
              <td>{{ item.profession }}</td>
              <td class="row-actions">
                <button type="button" class="table-action-button" @click="editProfession(item)">
                  Изменить
                </button>
                <button
                  type="button"
                  class="table-action-button table-action-button--danger"
                  @click="deleteProfession(item)"
                >
                  Удалить
                </button>
              </td>
            </tr>
            <tr v-if="professions.length === 0">
              <td class="empty-table" colspan="2">Профессии пока не добавлены</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
    <section
      v-else-if="selectedSection === 'material-groups'"
      class="macos-glass-panel settings-panel"
    >
      <MaterialGroupsSettings :token="token" />
    </section>
    <section
      v-else-if="selectedSection === 'maintenance'"
      class="macos-glass-panel settings-panel"
    >
      <MaintenanceTemplatesSettings :token="token" />
    </section>
    <section v-else class="macos-glass-panel settings-panel">
      <CsvFilesSettings :token="token" />
    </section>
  </main>
</template>
