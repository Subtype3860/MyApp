import { test, expect } from '@playwright/test'

for (const width of [390, 1024, 1440]) for (const role of ['administrator', 'chief', 'senior', 'user']) test(`sites menu: ${role} at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 })
  await page.addInitScript(role => {
    localStorage.setItem('myapp.authToken', 'test-token')
    localStorage.setItem('myapp.userRole', role === 'administrator' ? role : 'user')
  }, role)
  const sites = []
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/fleet-regions/capabilities') return route.fulfill({ json: { canCreate: role !== 'user' } })
    if (path === '/api/fleet-regions/management') return route.fulfill({ json: {canManage:true,regions:sites,vehicles:[],assignments:[],users:[]} })
    if (path === '/api/fleet-regions' && route.request().method() === 'POST') {
      sites.push({ id: 'new', name: route.request().postDataJSON().name })
      return route.fulfill({ json: { id: 'new' } })
    }
    return route.fulfill({ json: {} })
  })
  await page.goto('/')
  if (width < 761) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
  const entry = page.getByRole('button', { name: 'Участок', exact: true })
  if (role === 'user') { await expect(entry).toHaveCount(0); return }
  await entry.click()
  await page.getByLabel('Название региона / участка', { exact: true }).fill('Новый северный участок')
  await page.getByRole('button', { name: 'Сохранить участок', exact: true }).click()
  await expect(page.getByRole('status')).toHaveText('Участок сохранён.')
  await expect(page.getByRole('combobox',{name:'Участок',exact:true}).locator('option')).toContainText(['Новый участок','Новый северный участок'])
  if (width >= 761) {
    const checkLayout = async () => {
      const sidebar = await page.locator('#main-navigation').boundingBox()
      const form = await page.locator('.fleet-regions').boundingBox()
      expect(form.x).toBeGreaterThanOrEqual(sidebar.x + sidebar.width)
    }
    await checkLayout()
    await page.getByRole('button', { name: 'Свернуть меню', exact: true }).click()
    await expect(page.locator('.regions-page')).toHaveClass(/home-page--expanded/)
    await checkLayout()
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
