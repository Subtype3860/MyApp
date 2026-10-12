import { test, expect } from '@playwright/test'

async function setup(page, status, multiple = false) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const history = [{ id: 'p1', requestDate: '2026-10-10', requestNumber: 'ЗЧ-1', description: 'Насос — 1 шт.' }]
  const secondHistory = [{ id: 'p3', requestDate: '2026-10-10', description: 'Вторая заявка' }]
  const mutations = []
  let fail = false
  await page.route('**/api/**', route => {
    const req = route.request()
    const path = new URL(req.url()).pathname
    if (['POST', 'PUT', 'DELETE'].includes(req.method())) {
      mutations.push({ method: req.method(), path, body: req.method() === 'DELETE' ? null : req.postDataJSON() })
      if (fail) return route.fulfill({ status: 403, json: { detail: 'Недостаточно прав.' } })
      if (req.method() === 'POST') {
        const target = path.includes('/d2/') ? secondHistory : history
        target.push({ id: 'p2', ...req.postDataJSON() })
        return route.fulfill({ status: 201, json: { id: 'p2' } })
      }
      const target = [...history, ...secondHistory].find(item => path.endsWith(item.id))
      if (req.method() === 'PUT') Object.assign(target, req.postDataJSON())
      else { const list = history.includes(target) ? history : secondHistory; list.splice(list.indexOf(target), 1) }
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/vehicles') return route.fulfill({ json: [{ id: 'v1', garageNumber: 901, modelName: 'Самосвал' }] })
    if (path === '/api/vehicles/v1/journal') return route.fulfill({ json: { works: [
      { id: 'old', defectId: 'd1', status: 'awaiting_parts', createdAt: '2026-10-08T10:00:00Z', partsRequests: history },
      { id: 'latest', defectId: 'd1', status, description: 'Последний выполненный ремонт', completedAt: '2026-10-10T10:00:00Z', partsRequests: [] },
      ...(multiple ? [{ id: 'second', defectId: 'd2', status: 'awaiting_parts', description: 'Второй ремонт', completedAt: '2026-10-09T10:00:00Z', partsRequests: secondHistory }] : []),
    ] } })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
  return { history, mutations, setFail(value) { fail = value } }
}
async function action(page, name, row = page.locator('.parts-request-card').first()) {
  await row.getByLabel('Действия с заявками на закупку', { exact: true }).click()
  await page.getByRole('button', { name, exact: true }).click()
  return page.locator('.parts-action-dialog')
}

for (const [status, label] of [['repaired', 'Исправна'], ['faulty', 'Простаивает']]) {
  test(`procurement history persists after repair changes to ${status}`, async ({ page }) => {
    await setup(page, status)
    await expect(page.locator('.parts-request-card')).toHaveCount(1)
    await expect(page.locator('.parts-request-card__status')).toHaveText(label)
    await expect(page.locator('.parts-request-cell-orders')).toContainText('Насос — 1 шт.')
    await expect(page.locator('.parts-request-cell-cause')).toContainText('Последний выполненный ремонт')
    await page.reload()
    await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
    await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
    await expect(page.locator('.parts-request-cell-orders')).toContainText('Насос — 1 шт.')
  })
}

test('row menu supports creating, editing and deleting a selected procurement request', async ({ page }) => {
  const state = await setup(page, 'repaired')
  let dialog = await action(page, 'Добавить заявку')
  await dialog.getByLabel('Дата заявки').fill('2026-10-11')
  await dialog.getByLabel('Номер и описание заявки').fill('ЗЧ-2: фильтр — 2 шт.')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.parts-request-cell-orders')).toContainText('ЗЧ-2: фильтр — 2 шт.')
  dialog = await action(page, 'Редактировать заявку')
  await dialog.getByLabel('Заявка', { exact: true }).selectOption('p2')
  await expect(dialog.getByLabel('Номер и описание заявки')).toHaveValue('ЗЧ-2: фильтр — 2 шт.')
  await dialog.getByLabel('Номер и описание заявки').fill('ЗЧ-2: фильтр — 3 шт.')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.parts-request-cell-orders')).toContainText('ЗЧ-2: фильтр — 3 шт.')
  dialog = await action(page, 'Удалить заявку')
  await dialog.getByLabel('Заявка', { exact: true }).selectOption('p2')
  page.once('dialog', item => item.accept())
  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.parts-request-history__item')).toHaveCount(1)
  expect(state.mutations.map(item => [item.method, item.path])).toEqual([
    ['POST', '/api/vehicles/defects/d1/parts-requests'],
    ['PUT', '/api/vehicles/parts-requests/p2'],
    ['DELETE', '/api/vehicles/parts-requests/p2'],
  ])
})

test('failed save keeps the dialog and draft open', async ({ page }) => {
  const state = await setup(page, 'faulty')
  state.setFail(true)
  const dialog = await action(page, 'Добавить заявку')
  await dialog.getByLabel('Номер и описание заявки').fill('Моя заявка')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog.getByRole('alert')).toHaveText('Недостаточно прав.')
  await expect(dialog.getByLabel('Номер и описание заявки')).toHaveValue('Моя заявка')
  await expect(dialog).toBeVisible()
})

test('completion date is saved, survives reload and remains when editing the request', async ({ page }) => {
  const state = await setup(page, 'faulty')
  let dialog = await action(page, 'Выполнено')
  await dialog.getByLabel('Дата выполнения').fill('2026-10-09')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  expect(await dialog.getByLabel('Дата выполнения').evaluate(input => input.validity.rangeUnderflow)).toBe(true)
  expect(state.mutations).toHaveLength(0)
  await dialog.getByLabel('Дата выполнения').fill('2026-10-12')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.parts-request-completed')).toHaveText('Выполнено · 12.10.2026')
  expect(state.mutations[0].body.completedDate).toBe('2026-10-12')
  await page.reload()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
  await expect(page.locator('.parts-request-completed')).toHaveText('Выполнено · 12.10.2026')
  dialog = await action(page, 'Редактировать заявку')
  await expect(dialog.getByLabel('Выполнено', { exact: true })).toBeChecked()
  await dialog.getByLabel('Номер и описание заявки').fill('Насос получен')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations[1].body.completedDate).toBe('2026-10-12')
})

test('each row menu only changes requests belonging to that repair', async ({ page }) => {
  const state = await setup(page, 'faulty', true)
  const rows = page.locator('.parts-request-card')
  await expect(rows).toHaveCount(2)
  await expect(page.locator('.parts-request-toolbar .repair-actions')).toHaveCount(0)
  await expect(page.locator('.parts-request-cell-orders .repair-actions')).toHaveCount(2)
  const first = rows.first()
  const second = rows.last()
  let dialog = await action(page, 'Добавить заявку', second)
  await expect(dialog.locator('.parts-action-context')).toContainText('Второй ремонт')
  await expect(dialog.getByLabel('Техника', { exact: true })).toHaveCount(0)
  await dialog.getByLabel('Номер и описание заявки').fill('Для второго ремонта')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations[0].path).toBe('/api/vehicles/defects/d2/parts-requests')
  await expect(second).toContainText('Для второго ремонта')
  await expect(first).not.toContainText('Для второго ремонта')
  dialog = await action(page, 'Редактировать заявку', second)
  expect(await dialog.getByLabel('Заявка', { exact: true }).locator('option').evaluateAll(items => items.map(item => item.value))).toEqual(['p3', 'p2'])
  await dialog.getByLabel('Номер и описание заявки').fill('Изменена вторая заявка')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations[1].path).toBe('/api/vehicles/parts-requests/p3')
  dialog = await action(page, 'Выполнено', second)
  await dialog.getByLabel('Дата выполнения').fill('2026-10-12')
  await dialog.getByRole('button', { name: 'Сохранить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations[2].path).toBe('/api/vehicles/parts-requests/p3')
  await expect(second.locator('.parts-request-completed')).toHaveText('Выполнено · 12.10.2026')
  await expect(first.locator('.parts-request-completed')).toHaveCount(0)
  dialog = await action(page, 'Удалить заявку', second)
  page.once('dialog', item => item.accept())
  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  expect(state.mutations[3].path).toBe('/api/vehicles/parts-requests/p3')
  await expect(first).toContainText('Насос — 1 шт.')
  await expect(second).not.toContainText('Изменена вторая заявка')
})
