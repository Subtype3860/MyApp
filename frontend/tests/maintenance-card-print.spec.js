import { test, expect } from '@playwright/test'

// Проверяет печатную форму карты ТО: она повторяет бланк «Техническое обслуживание»
// из исходных карт Excel, а название операции со вложением становится ссылкой.
test('printed card follows the maintenance form and links operations to media', async ({ page }) => {
  test.setTimeout(60000)
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'administrator')
  })
  const vehicle = { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 98, typeName: 'Экскаватор', engineCount: 2 }
  const plan = { name: 'Цикл', steps: [{ name: 'ТО-1000', interval: 250 }], nextStepIndex: 0, lastDate: '2026-10-01', lastService: 'ТО-500', engines: [{ number: 1, hours: 750 }], runIns: [] }
  const template = {
    model: vehicle.modelName, service: 'ТО-1000', source: 'test', reviewNotes: '',
    operations: [
      { id: 'o1', section: 'ДВС', text: 'Проверить уровень масла в поддоне ДВС', target: '', control: false },
      { id: 'o2', section: 'ДВС', text: 'Продуть воздушные фильтры', target: '', control: false },
      { id: 'o3', section: 'Шасси', text: 'Проверить натяжение гусениц', target: '', control: false },
      { id: 'o4', section: 'Контроль', text: 'Проверить уровень масла в ДВС', target: '', control: true },
    ],
    materials: [
      { id: 'm1', assembly: 'Система смазки ДВС', name: 'Масло моторное OILWAY 15W40', partNumber: '', unit: 'л', quantity: 100, note: '' },
      { id: 'm2', assembly: 'Масляный фильтр ДВС', name: 'Фильтр масляный ST10802', partNumber: 'ST10802', unit: 'шт', quantity: 2, note: '' },
    ],
  }
  const templates = [{ id: 't1', revision: 3, published: true, template }]
  const answers = template.operations.map(operation => ({ id: operation.id, status: operation.id === 'o4' ? '' : 'done', condition: '', executor: operation.id === 'o4' ? '' : 'Иванов И.И.', reason: '', note: '', measurement: null, unit: '' }))
  answers[1] = { ...answers[1], status: 'not-done', reason: 'Нет доступа к фильтрам', executor: 'Петров П.П.' }
  let card = {
    id: 'c1', vehicleId: 'v1', vehicleLabel: 'SANY SY1250H №98', templateId: 't1', templateRevision: 3, planRevision: 1,
    status: 'accepted', revision: 2, acceptedBy: 'Сидоров С.С.', acceptedAt: '2026-10-30T06:00:00Z', audit: [], defects: {},
    readings: [{ number: 1, hours: 750 }, { number: 2, hours: 1000 }],
    template,
    data: {
      date: '2026-10-30', driver: 'Понкратьев С.А.', engineModel: 'Cummins QSK23', mileage: 15234,
      stoppedAt: '2026-10-30T00:00:00Z', startedAt: '2026-10-30T01:00:00Z', finishedAt: '2026-10-30T05:00:00Z',
      releasedAt: '2026-10-30T07:30:00Z', notes: 'Обнаружен износ ремня генератора', answers,
      materials: [
        { id: 'm1', actual: 95, substitute: '', executor: 'Иванов И.И.' },
        { id: 'm2', actual: 2, substitute: '', executor: 'Иванов И.И.' },
      ],
    },
  }
  const media = [{ id: 'p1', operationId: 'o1', contentType: 'image/png' }]
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname, method = route.request().method()
    if (path === '/api/maintenance-plans/card-template-options') return route.fulfill({ json: { canManage: true, canFill: true, canUseLibrary: false, templates } })
    if (path === '/api/material-groups/map') return route.fulfill({ json: [{ materialName: 'Масло моторное OILWAY 15W40', groupId: 'g1', groupName: 'Нефтепродукты' }] })
    if (path === '/api/maintenance-plans') return route.fulfill({ json: { canManage: true, rows: [{ vehicle, plan, revision: 1, due: [], incomplete: false }] } })
    if (path.endsWith('/history')) return route.fulfill({ json: [] })
    if (path.endsWith('/readings')) return route.fulfill({ json: { engines: [{ number: 1, hours: 750 }, { number: 2, hours: 1000 }], missing: [] } })
    if (path.endsWith('/v1/cards')) return route.fulfill({ json: [card] })
    if (path.endsWith('/photos')) return route.fulfill({ json: media })
    if (path.endsWith('/media-links')) {
      return route.fulfill({ json: media.map(item => ({ id: item.id, url: `/api/maintenance-plans/cards/c1/media/${item.id}/signature` })) })
    }
    return route.fulfill({ json: path === '/api/profile' ? { firstName: 'Тест' } : [] })
  })

  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Проведение ТО', exact: true }).click()
  await page.getByLabel('Техника', { exact: true }).selectOption('v1')
  await page.getByRole('button', { name: /2026-10-30 · ТО-1000 · Принята/ }).click()

  const popup = page.waitForEvent('popup')
  await page.getByRole('button', { name: 'Печать / PDF', exact: true }).click()
  const printed = await popup
  await printed.waitForSelector('h1')
  await printed.screenshot({ path: 'test-results/maintenance-card-print.png', fullPage: true })

  await expect(printed.locator('h1')).toHaveText('Техническое обслуживание SANY SY1250H ТО-1000 от 30.10.2026')
  await expect(printed.locator('th', { hasText: 'ДА' }).first()).toBeVisible()
  await expect(printed.locator('th', { hasText: 'НЕТ' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'ДВС' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'Шасси' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'Смазывающие' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'Фильтры' }).first()).toBeVisible()
  await expect(printed.locator('th', { hasText: 'Чек-лист' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'Понкратьев С.А.' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'Нет доступа к фильтрам' }).first()).toBeVisible()
  await expect(printed.locator('td', { hasText: 'ST10802' }).first()).toBeVisible()
  // Наработка моточасов — в формате «xxxxx / xxxxx».
  expect(await printed.locator('td.pre').textContent()).toBe('  750 /  1000')
  await expect(printed.locator('table', { hasText: 'Смазывающие' })).toContainText('Масло моторное OILWAY 15W40')
  await expect(printed.locator('table', { hasText: 'Смазывающие' })).not.toContainText('Фильтр масляный ST10802')
  const filtersTable = printed.locator('table', { hasText: 'Каталожный номер' }).last()
  await expect(filtersTable).toContainText('Фильтр масляный ST10802')
  await expect(filtersTable).not.toContainText('Масло моторное OILWAY 15W40')

  const operation = printed.locator('a', { hasText: 'Проверить уровень масла в поддоне ДВС' })
  await expect(operation).toHaveAttribute('href', /\/api\/maintenance-plans\/cards\/c1\/media\/p1\/signature$/)
  await expect(printed.locator('a', { hasText: 'Продуть воздушные фильтры' })).toHaveCount(0)
})
