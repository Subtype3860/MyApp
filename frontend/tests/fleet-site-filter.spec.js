import { test, expect } from '@playwright/test'

async function setup(page, denied = false) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'mechanic', role: 'user' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles']))
  })
  const vehicles = Array.from({ length: 10 }, (_, i) => ({ id: `v${i}`, modelName: `Самосвал ${i}`, garageNumber: 900 + i }))
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/vehicles') return route.fulfill({ json: vehicles })
    if (path === '/api/fleet-regions') return route.fulfill({ json: { regions: [{ id: 'north', name: 'Северный' }, { id: 'south', name: 'Южный' }, { id: 'empty', name: 'Новый' }] } })
    if (path === '/api/fleet-regions/vehicles') return route.fulfill({ status: denied ? 403 : 200, json: vehicles.map((v, i) => ({ vehicleId: v.id, regionId: i < 8 ? 'north' : 'south', regionName: i < 8 ? 'Северный' : 'Южный' })) })
    if (path === '/api/profile') return route.fulfill({ json: { id: 'mechanic', position: 'Механик' } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
}

for (const width of [390, 1440]) {
  test(`fleet filters sites and search with pagination at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1100 })
    await setup(page)
    await expect(page.locator('.fleet-row')).toHaveCount(8)
    await page.getByLabel('Следующая страница', { exact: true }).click()
    await expect(page.locator('.fleet-row')).toHaveCount(2)
    const filter = page.getByLabel('Участок парка техники')
    await filter.selectOption('north')
    await expect(page.locator('.fleet-row')).toHaveCount(8)
    await expect(page.locator('.fleet-pagination')).toContainText('1 / 1')
    await filter.selectOption('south')
    await expect(page.locator('.fleet-row')).toHaveCount(2)
    await expect(page.locator('.fleet-row').first()).toContainText('Южный')
    await page.getByPlaceholder('Модель, госномер или VIN').fill('909')
    await expect(page.locator('.fleet-row')).toHaveCount(1)
    await expect(page.locator('.fleet-row')).toContainText('Самосвал 9')
    await page.getByPlaceholder('Модель, госномер или VIN').fill('')
    await filter.selectOption('empty')
    await expect(page.getByText('На выбранном участке техника не найдена.')).toBeVisible()
    await expect(filter.locator('option[value="unassigned"]')).toHaveCount(0)
    await filter.selectOption('')
    await expect(page.locator('.fleet-row')).toHaveCount(8)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}

test('failed assignment lookup displays error instead of an unfiltered fleet', async ({ page }) => {
  await setup(page, true)
  await expect(page.getByText('Не удалось загрузить участки и закрепления техники. Повторите обновление.')).toBeVisible()
  await expect(page.locator('.fleet-row')).toHaveCount(0)
})
