import { test, expect } from '@playwright/test'

test('games menu opens the 1000 card game page', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/profile') return route.fulfill({ json: { id: 'u1', firstName: 'Тест', position: 'Механик', hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Игры', exact: true }).click()
  await page.getByRole('button', { name: '1000', exact: true }).click()
  await expect(page.getByRole('heading', { name: '1000', level: 1 })).toBeVisible()
  await expect(page.getByTitle('1000 — карточная игра')).toHaveAttribute('src', '/games/1000/index.html')
  const response = await page.request.get('/games/1000/index.html')
  expect(response.ok()).toBeTruthy()
  expect(await response.text()).toContain('js/engine.js')
})
