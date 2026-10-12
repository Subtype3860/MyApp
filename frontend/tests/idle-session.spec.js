import { test, expect } from '@playwright/test'

async function openSession(page) {
  await page.clock.install()
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  await page.route('**/api/**', route => route.fulfill({ json: [] }))
  await page.goto('/')
  await expect(page.locator('.app-shell')).toBeVisible()
}

test('idle warning appears after 10 minutes and logs out 30 seconds later', async ({ page }) => {
  await openSession(page)
  await page.clock.fastForward(599000)
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.clock.fastForward(1000)
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog')).toContainText('30 сек.')
  await page.keyboard.press('Escape')
  await page.clock.fastForward(29000)
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('dialog')).toContainText('1 сек.')
  await page.clock.fastForward(1000)
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
  expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBeNull()
})

test('activity resets the timer and continue working starts another 10 minutes', async ({ page }) => {
  await openSession(page)
  await page.clock.fastForward(9 * 60 * 1000)
  await page.keyboard.press('Shift')
  await page.clock.fastForward(599000)
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.clock.fastForward(1000)
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('button', { name: 'Продолжить работу' }).click()
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.clock.fastForward(599000)
  await expect(page.locator('.app-shell')).toBeVisible()
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.clock.fastForward(1000)
  await expect(page.getByRole('dialog')).toBeVisible()
})

test('returning after the full deadline logs out despite delayed timers', async ({ page }) => {
  await openSession(page)
  await page.clock.setSystemTime(new Date(Date.now() + 631000))
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toBeVisible()
})
