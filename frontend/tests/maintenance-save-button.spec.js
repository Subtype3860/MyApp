import { test, expect } from '@playwright/test'

// Кнопка ручного сохранения нужна только механику: остальные сохраняются автосохранением.
test('save card button is visible to mechanics only', async ({ page }) => {
  const card = {
    id: 'c1', vehicleId: 'v1', vehicleLabel: 'SANY №911', revision: 1, status: 'draft', auditCount: 0,
    template: { model: 'SANY', service: 'ТО-700', source: 'test', operations: [], materials: [] },
    data: { date: '2026-11-09', driver: '', engineModel: '', mileage: null, answers: [], materials: [] },
    readings: [], defects: {},
  }
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/card-template-options')) return route.fulfill({ json: { canManage: false, canFill: true, canUseLibrary: false, templates: [{ id: 't1', revision: 1, published: true, template: { model: 'SANY', service: 'ТО-700', source: 'test' } }] } })
    if (path.endsWith('/v1/cards')) return route.fulfill({ json: [card] })
    if (path.endsWith('/photos')) return route.fulfill({ json: [] })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.evaluate(async card => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Cards } = await import('/src/components/MaintenanceCards.vue')
    document.body.innerHTML = '<div id="cards-test"></div>'
    createApp({ render: () => h(Cards, { token: 'test', vehicle: { id: 'v1', modelName: 'SANY', engineCount: 1 }, plan: { steps: [{ name: 'ТО-700' }], nextStepIndex: 0, runIns: [] }, planRevision: 1 }) }).mount('#cards-test')
    void card
  }, card)
  const panel = page.locator('#cards-test')
  await panel.getByRole('button', { name: /ТО-700/ }).first().click()
  await expect(panel.getByRole('button', { name: 'Передать на проверку', exact: true })).toBeVisible()
  await expect(panel.getByRole('button', { name: 'Сохранить карту', exact: true })).toHaveCount(0)
})
