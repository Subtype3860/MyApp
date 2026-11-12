import { test, expect } from '@playwright/test'
import { PDFDocument } from 'pdf-lib'

// После выписки требования список очищается, чтобы тот же материал можно было
// выписать на другую технику.
test('issuing a requirement clears the selected components', async ({ page }) => {
  const template = await PDFDocument.create(); template.addPage()
  const bytes = Buffer.from(await template.save())
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/documents/components/template') return route.fulfill({ body: bytes, contentType: 'application/pdf' })
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON()
      return route.fulfill({ json: { document: { ...body, issuerName: '', issuerPosition: '', authorName: 'Автор', authorPosition: 'Механик' } } })
    }
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.evaluate(async () => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Selected } = await import('/src/components/SelectedComponentsView.vue')
    window.__events = []
    document.body.innerHTML = '<div id="selected-test"></div>'
    createApp({ render: () => h(Selected, {
      token: 'test', navigationCollapsed: true, responsibleEmployee: null,
      rows: [{ name: 'Фильтр', unit: 'шт', __quantity: 1, __availableQuantity: 5, __sourceTable: 'v_full_ost' }],
      onClear: () => window.__events.push('clear'),
      onIssued: () => window.__events.push('issued'),
    }) }).mount('#selected-test')
  })
  await page.getByLabel('Номер техники').fill('94')
  await page.getByRole('button', { name: 'Просмотреть PDF' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Создать PDF без ответственного' }).click()
  await expect.poll(() => page.evaluate(() => window.__events)).toEqual(['clear', 'issued'])
})
