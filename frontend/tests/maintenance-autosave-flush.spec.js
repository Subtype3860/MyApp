import { test, expect } from '@playwright/test'

// Правка перед уходом с карты должна уйти на сервер, а не остаться только в черновике.
test('unsaved card edit is flushed when the form is left', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const card = {
    id: 'c1', vehicleId: 'v1', vehicleLabel: 'SANY №902', revision: 1, status: 'draft', auditCount: 0,
    template: { model: 'SANY', service: 'ТО-1000', source: 'test', operations: [], materials: [] },
    data: { date: '2026-11-12', driver: '', engineModel: '', mileage: null, answers: [], materials: [] },
    readings: [], defects: {},
  }
  const writes = []
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() === 'PUT' && path.endsWith('/cards/c1')) {
      writes.push(req.postDataJSON())
      return route.fulfill({ json: { ...card, revision: card.revision + 1, data: req.postDataJSON().data } })
    }
    if (path.endsWith('/card-template-options')) return route.fulfill({ json: { canManage: true, canFill: true, canUseLibrary: false, templates: [{ id: 't1', revision: 1, published: true, template: { model: 'SANY', service: 'ТО-1000', source: 'test' } }] } })
    if (path.endsWith('/v1/cards')) return route.fulfill({ json: [card] })
    if (path.endsWith('/photos')) return route.fulfill({ json: [] })
    return route.fulfill({ json: [] })
  })

  await page.goto('/')
  await page.evaluate(async () => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Cards } = await import('/src/components/MaintenanceCards.vue')
    document.body.innerHTML = '<div id="cards-test"></div>'
    window.__cardsApp = createApp({ render: () => h(Cards, { token: 'test', vehicle: { id: 'v1', modelName: 'SANY', engineCount: 1 }, plan: { steps: [{ name: 'ТО-1000' }], nextStepIndex: 0, runIns: [] }, planRevision: 1 }) })
    window.__cardsApp.mount('#cards-test')
  })
  const panel = page.locator('#cards-test')
  await panel.getByRole('button', { name: /ТО-1000/ }).first().click()
  await panel.getByLabel('Водитель / машинист').fill('Иванов Иван')

  // Уходим с формы сразу, не дожидаясь задержки автосохранения.
  await page.evaluate(() => window.__cardsApp.unmount())
  await expect.poll(() => writes.length, { timeout: 10000 }).toBeGreaterThan(0)
  expect(writes[0].data.driver).toBe('Иванов Иван')
})
// На телефоне выбор времени может не сопровождаться change/blur — значение всё равно должно сохраниться.
test('time field keeps its value without change and blur events', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const card = {
    id: 'c1', vehicleId: 'v1', vehicleLabel: 'SANY №902', revision: 1, status: 'draft', auditCount: 0,
    template: { model: 'SANY', service: 'ТО-1000', source: 'test', operations: [], materials: [] },
    data: { date: '2026-11-12', driver: '', engineModel: '', mileage: null, answers: [], materials: [] },
    readings: [], defects: {},
  }
  const writes = []
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() === 'PUT' && path.endsWith('/cards/c1')) {
      writes.push(req.postDataJSON())
      return route.fulfill({ json: { ...card, revision: card.revision + 1, data: req.postDataJSON().data } })
    }
    if (path.endsWith('/card-template-options')) return route.fulfill({ json: { canManage: true, canFill: true, canUseLibrary: false, templates: [{ id: 't1', revision: 1, published: true, template: { model: 'SANY', service: 'ТО-1000', source: 'test' } }] } })
    if (path.endsWith('/v1/cards')) return route.fulfill({ json: [card] })
    if (path.endsWith('/photos')) return route.fulfill({ json: [] })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.evaluate(async () => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Cards } = await import('/src/components/MaintenanceCards.vue')
    document.body.innerHTML = '<div id="cards-test"></div>'
    createApp({ render: () => h(Cards, { token: 'test', vehicle: { id: 'v1', modelName: 'SANY', engineCount: 1 }, plan: { steps: [{ name: 'ТО-1000' }], nextStepIndex: 0, runIns: [] }, planRevision: 1 }) }).mount('#cards-test')
  })
  const panel = page.locator('#cards-test')
  await panel.getByRole('button', { name: /ТО-1000/ }).first().click()
  const finish = panel.getByLabel('Окончание работ')
  await finish.focus()
  await finish.evaluate(input => {
    input.value = '2026-11-12T15:30'
    input.dispatchEvent(new Event('input', { bubbles: true }))
  })
  await expect.poll(() => writes.length, { timeout: 10000 }).toBeGreaterThan(0)
  expect(writes[0].data.finishedAt).toBeTruthy()
})