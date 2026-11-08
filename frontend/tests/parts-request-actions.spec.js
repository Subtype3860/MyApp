import { test, expect } from '@playwright/test'

/** Подготавливает страницу и ответы API для раздела заявок на закупку ЗЧ. */
async function setup(page, { fail = false, requests = [] } = {}) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const mutations = []
  const fleet = [
    { id: 'v1', modelName: 'Самосвал', garageNumber: 901, operationalState: 'stopped' },
    { id: 'v2', modelName: 'Экскаватор', garageNumber: 902, operationalState: 'healthy' },
  ]
  await page.route('**/api/**', route => {
    const req = route.request()
    const path = new URL(req.url()).pathname
    if (['POST', 'PUT', 'DELETE'].includes(req.method())) {
      mutations.push({ method: req.method(), path, body: req.method() === 'DELETE' ? null : req.postDataJSON() })
      if (fail) return route.fulfill({ status: 403, json: { detail: 'Недостаточно прав.' } })
      if (req.method() === 'POST') {
        requests.push({ id: 'p2', vehicleId: 'v1', ...req.postDataJSON() })
        return route.fulfill({ status: 201, json: { id: 'p2' } })
      }
      const target = requests.find(item => path.endsWith(item.id))
      if (req.method() === 'PUT') Object.assign(target, req.postDataJSON())
      else requests.splice(requests.indexOf(target), 1)
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/vehicles') return route.fulfill({ json: fleet })
    if (path === '/api/vehicles/repair-journal') {
      return route.fulfill({ json: fleet.map(vehicle => ({ vehicle, defects: [], works: [], partsRequests: requests.filter(item => item.vehicleId === vehicle.id) })) })
    }
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
  return {
    mutations,
    requests,
    /** Строка таблицы для выбранной техники. */
    row: id => page.locator('.parts-request-card').filter({ hasText: id === 'v1' ? '901' : '902' }),
  }
}

/** Выполняет действие с заявкой на запчасти в браузерном тесте. */
async function action(page, row, name) {
  await row.getByLabel('Действия с заявками на закупку', { exact: true }).click()
  await page.getByRole('button', { name, exact: true }).click()
  return page.locator('.parts-action-dialog')
}

test('procurement lists every vehicle and reports API errors', async ({ page }) => {
  const state = await setup(page)
  await expect(page.locator('.parts-request-card')).toHaveCount(2)
  await expect(state.row('v1')).toContainText('Заявок пока нет.')

  let fail = false
  await page.route('**/api/vehicles/repair-journal', route =>
    fail ? route.fulfill({ status: 500 }) : route.fulfill({ json: [] }))
  fail = true
  await page.reload()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
  await expect(page.locator('.table-message--error')).toHaveText('Не удалось загрузить заявки на закупку ЗЧ. Попробуйте открыть раздел повторно.')
})

test('request is attached to the vehicle and supports create, edit and delete', async ({ page }) => {
  const state = await setup(page, { requests: [{ id: 'p1', vehicleId: 'v1', requestDate: '2026-10-10', requestNumber: 'ЗЧ-1', description: 'Насос — 1 шт.' }] })
  await expect(state.row('v1')).toContainText('Насос — 1 шт.')
  await expect(state.row('v1')).toContainText('Самосвал')

  let dialog = await action(page, state.row('v1'), 'Добавить заявку')
  await dialog.getByLabel('Дата заявки').fill('2026-10-11')
  await dialog.getByLabel('Номер и описание заявки').fill('ЗЧ-2: фильтр — 2 шт.')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations.at(-1)).toMatchObject({ method: 'POST', path: '/api/vehicles/v1/parts-requests' })

  dialog = await action(page, state.row('v1'), 'Редактировать заявку')
  await dialog.getByLabel('Заявка', { exact: true }).selectOption('p1')
  await dialog.getByLabel('Номер и описание заявки').fill('ЗЧ-1: насос — 2 шт.')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(state.row('v1')).toContainText('ЗЧ-1: насос — 2 шт.')

  dialog = await action(page, state.row('v1'), 'Удалить заявку')
  await dialog.getByLabel('Заявка', { exact: true }).selectOption('p1')
  page.once('dialog', item => item.accept())
  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations.map(item => [item.method, item.path])).toEqual([
    ['POST', '/api/vehicles/v1/parts-requests'],
    ['PUT', '/api/vehicles/parts-requests/p1'],
    ['DELETE', '/api/vehicles/parts-requests/p1'],
  ])
})

test('a failed request mutation shows the server message and keeps the dialog open', async ({ page }) => {
  const state = await setup(page, { fail: true })
  const dialog = await action(page, state.row('v1'), 'Добавить заявку')
  await dialog.getByLabel('Дата заявки').fill('2026-10-11')
  await dialog.getByLabel('Номер и описание заявки').fill('ЗЧ-3: ролик')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).toBeVisible()
  await expect(page.locator('.form-message--error, .parts-action-dialog [role="alert"]')).toContainText('Недостаточно прав.')
})
