<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  navigationCollapsed: { type: Boolean, required: true },
  token: { type: String, required: true },
  permissions: { type: Array, default: () => [] },
  isAdmin: { type: Boolean, default: false },
})
const emit = defineEmits(['navigate', 'navigate-vehicle'])
const canView = (permission) =>
  props.isAdmin || props.permissions.includes(permission)
const vehicles = ref([])
const loading = ref(false)
const error = ref('')
const loaded = ref(false)
const query = ref('')
const page = ref(1)
const pageSize = 8
let controller
const filtered = computed(() => {
  const term = query.value.trim().toLocaleLowerCase('ru')
  return vehicles.value.filter((vehicle) =>
    [
      vehicle.modelName,
      vehicle.typeName,
      vehicle.groupName,
      vehicle.garageNumber,
      vehicle.stateNumber,
      vehicle.vin,
    ].some((value) =>
      String(value ?? '')
        .toLocaleLowerCase('ru')
        .includes(term),
    ),
  )
})
const pageCount = computed(() =>
  Math.max(1, Math.ceil(filtered.value.length / pageSize)),
)
const visibleVehicles = computed(() =>
  filtered.value.slice((page.value - 1) * pageSize, page.value * pageSize),
)
const metrics = computed(() => [
  {
    label: 'Вся техника',
    value: vehicles.value.length,
    note: 'единиц в реестре',
    icon: '▤',
  },
  {
    label: 'Модели',
    value: new Set(vehicles.value.map((v) => v.modelName).filter(Boolean)).size,
    note: 'моделей техники',
    icon: '◇',
  },
  {
    label: 'Типы техники',
    value: new Set(vehicles.value.map((v) => v.typeName).filter(Boolean)).size,
    note: 'типов в реестре',
    icon: '▦',
  },
  {
    label: 'Группы',
    value: new Set(vehicles.value.map((v) => v.groupName).filter(Boolean)).size,
    note: 'групп техники',
    icon: '◎',
  },
])
const shortcuts = computed(() =>
  [
    {
      permission: 'vehicles.repair_request',
      parent: 'menu.vehicles',
      title: 'Заявка на ремонт',
      text: 'Зарегистрировать неисправность и приложить материалы.',
      section: 'repairRequest',
      icon: '↗',
    },
    {
      permission: 'vehicles.works',
      parent: 'menu.vehicles',
      title: 'Ремонт',
      text: 'Работы, история и состояние ремонта техники.',
      section: 'works',
      icon: '⚒',
    },
    {
      permission: 'menu.maintenance',
      title: 'Техническое обслуживание',
      text: 'Шаблоны ТО и подбор необходимых материалов.',
      page: 'maintenance',
      icon: '◷',
    },
    {
      permission: 'menu.requirements',
      title: 'Выписанные требования',
      text: 'Журнал документов на выдачу материалов.',
      page: 'requirements',
      icon: '▤',
    },
  ].filter(
    (item) =>
      canView(item.permission) && (!item.parent || canView(item.parent)),
  ),
)

async function loadVehicles() {
  if (!canView('menu.vehicles')) return
  controller?.abort()
  controller = new AbortController()
  loading.value = true
  error.value = ''
  try {
    const response = await fetch('/api/vehicles', {
      headers: { Authorization: `Bearer ${props.token}` },
      signal: controller.signal,
    })
    if (!response.ok)
      throw new Error(
        response.status === 403
          ? 'Нет доступа к реестру техники.'
          : 'Не удалось загрузить технику. Попробуйте ещё раз.',
      )
    const data = await response.json()
    if (!Array.isArray(data))
      throw new Error('Сервер вернул неверный формат данных.')
    vehicles.value = data
    loaded.value = true
    page.value = 1
  } catch (cause) {
    if (cause.name !== 'AbortError') error.value = cause.message
  } finally {
    loading.value = false
  }
}
onMounted(loadVehicles)
onBeforeUnmount(() => controller?.abort())
function openShortcut(item) {
  if (item.section) emit('navigate-vehicle', item.section)
  else emit('navigate', item.page)
}
</script>

<template>
  <main
    class="home-page dashboard-page"
    :class="{ 'home-page--expanded': navigationCollapsed }"
  >
    <header class="home-header">
      <div>
        <p class="eyebrow">РАБОЧЕЕ ПРОСТРАНСТВО</p>
        <h1>Обзор парка</h1>
      </div>
      <UserAvatar :token="token" />
    </header>
    <section class="dashboard-hero" aria-labelledby="dashboard-title">
      <div class="dashboard-hero__content">
        <p class="eyebrow">АРМ МЕХАНИКА</p>
        <h2 id="dashboard-title">Техника<br /><span>под контролем</span></h2>
        <p>
          Единое рабочее пространство механика.<br />Техника, обслуживание и
          ремонт — в одном месте.
        </p>
        <div class="dashboard-hero__actions">
          <a
            v-if="canView('menu.vehicles')"
            class="primary-button"
            href="#fleet"
            >Парк техники <span aria-hidden="true">↓</span></a
          >
          <button
            v-if="
              canView('menu.vehicles') && canView('vehicles.repair_request')
            "
            class="secondary-button"
            type="button"
            @click="emit('navigate-vehicle', 'repairRequest')"
          >
            Создать заявку <span aria-hidden="true">↗</span>
          </button>
        </div>
      </div>
      <svg
        class="dashboard-blueprint"
        viewBox="0 0 480 260"
        fill="none"
        aria-hidden="true"
      >
        <g stroke="currentColor" stroke-width="1.4">
          <path
            d="M30 184h410M40 206h400M54 168h280l58-80h-82l-37 62H96l-12-65h163l31 65M96 85l-8-29h226l40 32M316 149h59l26 35M113 85v66m44-66v66m44-66v66M100 56l18-25h181l15 25M361 117h41l20 45v22h-34M373 119v40h44M321 170v-20h45"
          />
          <circle cx="113" cy="183" r="35" />
          <circle cx="113" cy="183" r="19" />
          <circle cx="289" cy="183" r="35" />
          <circle cx="289" cy="183" r="19" />
          <circle cx="387" cy="183" r="28" />
          <circle cx="387" cy="183" r="14" />
          <path stroke-dasharray="4 6" d="M21 233h427M47 16v221M441 16v221" />
        </g>
      </svg>
    </section>

    <template v-if="canView('menu.vehicles')">
      <section
        class="dashboard-metrics"
        aria-label="Сводка реестра техники"
        :aria-busy="loading"
      >
        <article
          v-for="metric in metrics"
          :key="metric.label"
          class="dashboard-metric"
        >
          <span class="dashboard-metric__icon" aria-hidden="true">{{
            metric.icon
          }}</span>
          <div>
            <span>{{ metric.label }}</span
            ><strong>{{
              loaded && !error && !loading ? metric.value : '—'
            }}</strong
            ><small>{{ metric.note }}</small>
          </div>
        </article>
      </section>
      <div class="dashboard-columns">
        <section
          id="fleet"
          class="dashboard-panel fleet-panel"
          aria-labelledby="fleet-title"
          :aria-busy="loading"
        >
          <header class="dashboard-panel__header">
            <div>
              <h2 id="fleet-title">Парк техники</h2>
              <p>Реестр предприятия</p>
            </div>
            <button
              class="secondary-button"
              type="button"
              :disabled="loading"
              @click="loadVehicles"
            >
              Обновить
            </button>
          </header>
          <label class="fleet-search"
            ><span class="visually-hidden">Поиск техники</span
            ><svg viewBox="0 0 24 24" aria-hidden="true">
              <circle cx="10" cy="10" r="6" />
              <path d="m15 15 5 5" /></svg
            ><input
              v-model="query"
              type="search"
              placeholder="Модель, госномер или VIN"
              @input="page = 1"
          /></label>
          <p v-if="loading" class="dashboard-empty" role="status">
            Загрузка техники…
          </p>
          <p
            v-else-if="error"
            class="dashboard-empty form-message--error"
            role="alert"
          >
            {{ error }}
          </p>
          <p v-else-if="!filtered.length" class="dashboard-empty" role="status">
            {{
              query
                ? 'По вашему запросу ничего не найдено.'
                : 'В реестре пока нет техники.'
            }}
          </p>
          <template v-else>
            <div class="fleet-list">
              <article
                v-for="vehicle in visibleVehicles"
                :key="vehicle.id"
                class="fleet-row"
              >
                <span class="fleet-icon" aria-hidden="true"
                  ><svg viewBox="0 0 32 32">
                    <path
                      d="M3 8h16v13H3zM19 13h5l5 6v5h-5M8 24h11M19 21v3M23 14v5h5"
                    />
                    <circle cx="8" cy="24" r="3" />
                    <circle cx="22" cy="24" r="3" /></svg
                ></span>
                <div class="fleet-description">
                  <h3>{{ vehicle.modelName || 'Модель не указана' }}</h3>
                  <p>
                    {{
                      vehicle.typeName || vehicle.groupName || 'Тип не указан'
                    }}
                  </p>
                </div>
                <dl>
                  <div>
                    <dt>Гаражный №</dt>
                    <dd>{{ vehicle.garageNumber ?? '—' }}</dd>
                  </div>
                  <div>
                    <dt>Госномер</dt>
                    <dd>{{ vehicle.stateNumber || '—' }}</dd>
                  </div>
                </dl>
              </article>
            </div>
            <footer class="fleet-pagination">
              <span aria-live="polite"
                >Найдено: {{ filtered.length }} · {{ page }} /
                {{ pageCount }}</span
              >
              <div>
                <button
                  type="button"
                  class="icon-button"
                  aria-label="Предыдущая страница"
                  :disabled="page <= 1"
                  @click="page--"
                >
                  ←</button
                ><button
                  type="button"
                  class="icon-button"
                  aria-label="Следующая страница"
                  :disabled="page >= pageCount"
                  @click="page++"
                >
                  →
                </button>
              </div>
            </footer>
          </template>
        </section>
        <section
          v-if="shortcuts.length"
          class="dashboard-panel dashboard-shortcuts"
          aria-labelledby="shortcut-title"
        >
          <header class="dashboard-panel__header">
            <div>
              <h2 id="shortcut-title">Быстрые действия</h2>
              <p>Основные задачи механика</p>
            </div>
          </header>
          <button
            v-for="item in shortcuts"
            :key="item.title"
            class="dashboard-shortcut"
            type="button"
            @click="openShortcut(item)"
          >
            <span class="dashboard-shortcut__icon" aria-hidden="true">{{
              item.icon
            }}</span
            ><span
              ><strong>{{ item.title }}</strong
              ><small>{{ item.text }}</small></span
            ><span aria-hidden="true">↗</span>
          </button>
          <p class="dashboard-note">
            В сводке отображаются данные реестра. Рабочие статусы и план ТО
            доступны в соответствующих разделах.
          </p>
        </section>
      </div>
    </template>
    <section v-else class="dashboard-panel dashboard-shortcuts">
      <h2>Ваши разделы</h2>
      <button
        v-for="item in shortcuts"
        :key="item.title"
        type="button"
        class="dashboard-shortcut"
        @click="openShortcut(item)"
      >
        <span
          ><strong>{{ item.title }}</strong
          ><small>{{ item.text }}</small></span
        ><span aria-hidden="true">↗</span>
      </button>
      <p v-if="!shortcuts.length" class="dashboard-note">
        Откройте доступный раздел в меню. Если нужного раздела нет, обратитесь к
        администратору.
      </p>
    </section>
  </main>
</template>
