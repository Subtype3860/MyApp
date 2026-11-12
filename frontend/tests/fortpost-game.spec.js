import { test, expect } from '@playwright/test'

test('games menu opens the 3D Fortpost page', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.home']))
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/profile') return route.fulfill({ json: { id: 'u1', firstName: 'Тест', position: 'Механик', hasAvatar: false } })
    return route.fulfill({ json: [] })
  })

  await page.goto('/')
  await page.getByRole('button', { name: 'Игры', exact: true }).click()
  await page.getByRole('button', { name: 'Форпост', exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Форпост', level: 1 })).toBeVisible()
  const frame = page.getByTitle('Форпост — 3D-игра')
  await expect(frame).toHaveAttribute('src', '/games/fortpost/index.html')
  const response = await page.request.get('/games/fortpost/index.html')
  expect(response.ok()).toBeTruthy()
  expect(await response.text()).toContain('unity-canvas')
})
