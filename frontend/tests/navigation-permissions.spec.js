import { test, expect } from '@playwright/test'
for (const scenario of [
  { name: 'independent history and GTO', permissions: ['vehicles.repair_history', 'reports.gto'], repair: false, history: true, maintenance: false, shift: false, gto: true },
  { name: 'independent maintenance and shift', permissions: ['vehicles.maintenance_execution', 'reports.shift'], repair: false, history: false, maintenance: true, shift: true, gto: false },
  { name: 'legacy combined grants', permissions: ['menu.vehicles', 'vehicles.works', 'vehicles.report'], repair: true, history: true, maintenance: true, shift: true, gto: true },
]) {
  test(scenario.name, async ({ page }) => {
    await page.addInitScript(permissions => {
      localStorage.setItem('myapp.authToken', 'test-token')
      localStorage.setItem('myapp.userRole', 'user')
      localStorage.setItem('myapp.userPermissions', JSON.stringify(permissions))
    }, scenario.permissions)
    await page.route('**/api/**', route => route.fulfill({ json: new URL(route.request().url()).pathname === '/api/profile' ? { id: 'user', firstName: 'Иван', hasAvatar: false } : [] }))
    await page.goto('/')
    await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
    for (const [label, visible] of [['Ремонт', scenario.repair], ['История ремонта', scenario.history], ['Проведение ТО', scenario.maintenance]]) {
      await expect(page.locator('#transport-submenu').getByRole('button', { name: label, exact: true })).toHaveCount(visible ? 1 : 0)
    }
    await page.getByRole('button', { name: 'Отчёты', exact: true }).click()
    for (const [label, visible] of [['Отчёт за смену', scenario.shift], ['Выход ГТО', scenario.gto]]) {
      await expect(page.locator('#reports-submenu').getByRole('button', { name: label, exact: true })).toHaveCount(visible ? 1 : 0)
    }
  })
}
