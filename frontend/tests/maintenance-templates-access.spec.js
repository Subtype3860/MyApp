import { test, expect } from '@playwright/test'

async function openTables(page, profile) {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'u1' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.tables']))
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/profile') return route.fulfill({ json: profile })
    if (path === '/api/maintenance-plans/card-templates') return route.fulfill({ json: { templates: [], materialSources: [] } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Таблицы', exact: true }).click()
  return page.locator('#tables-submenu')
}

test('mechanic with library access sees templates menu', async ({ page }) => {
  const menu = await openTables(page, { id: 'u1', firstName: 'Пётр', lastName: 'Механиков', position: 'Механик', canUseMaintenanceLibrary: true })
  const item = menu.getByRole('button', { name: 'Шаблоны ТО', exact: true })
  await expect(item).toBeVisible()
  await item.click()
  await expect(page.getByRole('heading', { name: 'Шаблоны технического обслуживания' })).toBeVisible()
})

test('employee without library access has no templates menu', async ({ page }) => {
  const menu = await openTables(page, { id: 'u1', firstName: 'Иван', lastName: 'Слесарев', position: 'Слесарь', canUseMaintenanceLibrary: false })
  await expect(menu.getByRole('button', { name: 'Шаблоны ТО', exact: true })).toHaveCount(0)
})
