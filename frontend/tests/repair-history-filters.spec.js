import { test, expect } from '@playwright/test'

for (const width of [390, 1440]) {
  test(`history filters combine, include archived vehicles and reset at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'admin', role: 'administrator' }))}.test`)
      localStorage.setItem('myapp.userRole', 'Administrator')
    })
    const journals = Array.from({ length: 10 }, (_, i) => ({
      vehicle: { id: `v${i}`, garageNumber: i + 90, stateNumber: `А${i}ВС`, modelName: `Техника ${i}`, isArchived: i === 0 },
      defects: [{ id: `d${i}`, symptoms: i % 2 ? 'Неисправность тормозов' : 'Утечка антифриза', createdAt: `2026-10-${String(i + 1).padStart(2, '0')}T10:00:00Z` }],
      works: [],
    }))
    // Two requests on a vehicle: reason filtering must keep only the matching request.
    journals[0].defects.push({ id: 'extra', symptoms: 'Замена насоса', createdAt: '2026-10-01T11:00:00Z' })
    const calls = []
    await page.route('**/api/**', route => {
      const url = new URL(route.request().url())
      if (url.pathname === '/api/vehicles/repair-journal') {
        calls.push(url.searchParams.get('includeArchived'))
        return route.fulfill({ json: journals })
      }
      if (url.pathname === '/api/vehicles') return route.fulfill({ json: journals.map(item => item.vehicle) })
      return route.fulfill({ json: {} })
    })
    await page.goto('/')
    if (width < 761) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
    await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
    await page.getByRole('button', { name: 'История ремонта', exact: true }).click()
    await expect(page.locator('.vehicle-card')).toHaveCount(8)
    await expect(page.getByText('Техника: 10 · Заявки: 11')).toBeVisible()
    expect(calls).toEqual(['true'])
    await page.getByRole('button', { name: 'Далее', exact: true }).click()
    await expect(page.locator('.vehicle-card')).toHaveCount(2)
    await page.getByLabel('Причина простоя', { exact: true }).fill('АНТИФРИЗА')
    await expect(page.locator('.vehicle-card')).toHaveCount(5)
    await page.getByLabel('Госномер или внутренний номер').fill('90')
    await expect(page.locator('.vehicle-card')).toHaveCount(1)
    await expect(page.locator('.vehicle-card')).toContainText('История сохранена')
    await expect(page.locator('.repair-request__description')).toHaveText('Утечка антифриза')
    await page.getByLabel('Госномер или внутренний номер').fill('а 0-вс')
    await expect(page.locator('.vehicle-card')).toHaveCount(1)
    await page.getByLabel('Причина простоя', { exact: true }).fill('нет такой причины')
    await expect(page.getByText('По выбранным фильтрам ничего не найдено.')).toBeVisible()
    await page.getByRole('button', { name: 'Вся история', exact: true }).click()
    await expect(page.locator('.vehicle-card')).toHaveCount(8)
    await expect(page.getByLabel('Причина простоя', { exact: true })).toHaveValue('')
    expect(calls).toHaveLength(1)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}
