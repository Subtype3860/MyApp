import { test, expect } from '@playwright/test'
import { PDFDocument } from 'pdf-lib'

for (const scenario of ['without', 'add', 'selected']) test(`requirement PDF responsible: ${scenario}`, async ({ page }) => {
  const requests = []
  const template = await PDFDocument.create(); template.addPage()
  const bytes = Buffer.from(await template.save())
  await page.setViewportSize({ width: 390, height: 900 })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/employees/responsible') return route.fulfill({ json: [{ firstName: 'Иван', patronymic: 'Иванович', lastName: 'Иванов' }] })
    if (path === '/api/documents/components/template') return route.fulfill({ body: bytes, contentType: 'application/pdf' })
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON(); requests.push({ path, body })
      return route.fulfill({ json: { document: { ...body, issuerName: body.responsibleEmployee ? 'И. И. Иванов' : '', issuerPosition: '', authorName: 'Автор', authorPosition: 'Механик' } } })
    }
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.evaluate(async scenario => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Selected } = await import('/src/components/SelectedComponentsView.vue')
    document.body.innerHTML = '<div id="selected-test"></div>'
    createApp({ render: () => h(Selected, { token: 'test', navigationCollapsed: true,
      responsibleEmployee: scenario === 'selected' ? { firstName: 'Иван', patronymic: 'Иванович', lastName: 'Иванов', sourceTable: 'v_full_ost' } : null,
      rows: [{ name: 'Фильтр', unit: 'шт', __quantity: 1, __availableQuantity: 2, __sourceTable: 'v_full_ost' }],
    }) }).mount('#selected-test')
  }, scenario)
  await page.getByLabel('Номер техники').fill('94')
  await page.getByRole('button', { name: 'Просмотреть PDF' }).click()
  if (scenario !== 'selected') {
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    expect(requests).toHaveLength(0)
    await page.keyboard.press('Escape')
    await expect(dialog).not.toBeVisible()
    expect(requests).toHaveLength(0)
    await page.getByRole('button', { name: 'Просмотреть PDF' }).click()
    if (scenario === 'add') {
      await dialog.getByRole('button', { name: 'Добавить ответственного' }).click()
      await dialog.getByLabel('Ответственное лицо', { exact: true }).selectOption('0')
      await dialog.getByRole('button', { name: 'Создать PDF с ответственным' }).click()
    } else await dialog.getByRole('button', { name: 'Создать PDF без ответственного' }).click()
  }
  await expect.poll(() => requests.length).toBe(2)
  expect(requests.map(r => r.path)).toEqual(['/api/documents/components/preview', '/api/documents/components'])
  expect(requests[0].body).toEqual(requests[1].body)
  expect(requests[0].body.withoutResponsibleEmployee).toBe(scenario === 'without')
  expect(requests[0].body.responsibleEmployee).toEqual(scenario === 'without' ? null : { firstName: 'Иван', patronymic: 'Иванович', lastName: 'Иванов' })
  await expect(page.getByRole('dialog')).not.toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
