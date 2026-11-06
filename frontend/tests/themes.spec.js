import { test, expect } from '@playwright/test'
for (const width of [390, 1440]) {
  test('profile themes persist and follow the system at ' + width, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.emulateMedia({ colorScheme: 'dark' })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', 'test-token')
      localStorage.setItem('myapp.userRole', 'administrator')
    })
    await page.route('**/api/**', route => route.fulfill({ json:
      new URL(route.request().url()).pathname === '/api/profile'
        ? { id: 'user', userName: 'worker', firstName: 'Иван', position: 'Механик', hasAvatar: false }
        : [] }))
    await page.goto('/')
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    if (width < 600) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
    await page.getByRole('button', { name: /Редактировать профиль/ }).click()
    const select = page.getByLabel('Тема интерфейса')
    for (const theme of ['light', 'macos', 'dark', 'ocean', 'midnight', 'graphite', 'lavender', 'sand', 'forest', 'digital']) {
      await select.selectOption(theme)
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
      expect(await page.evaluate(() => localStorage.getItem('myapp.theme'))).toBe(theme)
      await expect(page.locator('html')).toHaveCSS('color-scheme', ['dark', 'digital', 'midnight', 'graphite'].includes(theme) ? 'dark' : 'light')
    }
    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'digital')
    if (width < 600) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
    await page.getByRole('button', { name: /Редактировать профиль/ }).click()
    await expect(select).toHaveValue('digital')
    await expect(page.locator('html')).toHaveCSS('--accent', '#49e1d2')
    await page.screenshot({ path: 'test-results/theme-digital-' + width + '.png' })
    await select.selectOption('light')
    await expect(page.locator('html')).toHaveCSS('color-scheme', 'light')
    await select.selectOption('macos')
    await expect(page.locator('html')).toHaveCSS('--accent', '#0069d9')
    await page.screenshot({ path: 'test-results/theme-macos-' + width + '.png' })
    await select.selectOption('system')
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    await page.emulateMedia({ colorScheme: 'light' })
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: 'test-results/theme-profile-' + width + '.png' })
  })
}
