import { test, expect } from '@playwright/test'

async function setup(page) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'admin', role: 'administrator' }))}.test`)
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const calls = []
  const vehicle = { id: 'v1', garageNumber: 901, modelName: 'Самосвал', operationalState: 'operating_with_defect' }
  await page.route('**/api/**', route => {
    const req = route.request()
    const path = new URL(req.url()).pathname
    if (req.method() === 'POST') { calls.push({ path, body: req.postDataJSON() }); return route.fulfill({ status: 201, json: { id: 'new' } }) }
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects: [{ id: 'd1', symptoms: 'Некритичная неисправность', status: 'awaiting_parts', operationalState: 'operating', downtimeStartedAt: null }], works: [] }] })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  return calls
}

for (const operating of [true, false]) {
  test(`repair request records continued operation: ${operating}`, async ({ page }) => {
    const calls = await setup(page)
    await page.getByRole('button', { name: 'Создать заявку', exact: true }).click()
    await page.locator('.vehicle-search input').fill('901')
    await page.getByRole('button', { name: 'Найти транспорт', exact: true }).click()
    await page.getByLabel('Описание неисправности').fill('Некритичная неисправность')
    const select = page.getByLabel('Эксплуатация машины')
    await expect(select).toHaveValue('operating')
    await expect(page.getByLabel('Дата начала простоя')).toHaveCount(0)
    if (!operating) {
      await select.selectOption('stopped')
      await page.getByLabel('Дата начала простоя').fill('2026-10-01')
      await page.getByLabel('Время начала простоя').fill('10:00')
    }
    await page.getByRole('button', { name: 'Отправить заявку', exact: true }).click()
    await expect.poll(() => calls.length).toBe(1)
    expect(calls[0].body.operationalState).toBe(operating ? 'operating' : 'stopped')
    if (operating) expect(calls[0].body.downtimeStartedAt).toBeNull()
    else expect(calls[0].body.downtimeStartedAt).toContain('2026-10-01')
  })
}

test('waiting for parts can coexist with continued operation, and a later work can stop the machine', async ({ page }) => {
  const calls = await setup(page)
  await page.locator('.dashboard-shortcut').filter({ hasText: 'Работы, история' }).click()
  await expect(page.locator('.repair-state-badges')).toContainText('Работает с неисправностью')
  await expect(page.locator('.repair-state-badges')).toContainText('Заявка: Ожидает запчасти')
  await expect(page.locator('.repair-overview-meta')).not.toContainText('Простой с')
  await page.getByLabel('Описание выполненных работ', { exact: true }).fill('Машина остановлена для ремонта')
  await page.getByLabel('Эксплуатация машины').selectOption('stopped')
  await page.getByLabel('Начало простоя', { exact: true }).fill('2026-10-01T10:00')
  await page.getByLabel('Ожидает запчасти', { exact: true }).check()
  await page.getByRole('button', { name: 'Сохранить этап', exact: true }).click()
  await expect.poll(() => calls.length).toBe(1)
  expect(calls[0].body).toMatchObject({ status: 'awaiting_parts', operationalState: 'stopped' })
  expect(calls[0].body.downtimeStartedAt).toContain('2026-10-01')
})
