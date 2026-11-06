import { test, expect } from '@playwright/test'

/** Подготавливает тестовую сессию для проверки её отзыва. */
async function session(page, options = {}) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'current-token')
    localStorage.setItem('myapp.userRole', 'administrator')
  })
  const profile = { id: 'user', userName: 'worker', firstName: 'Иван', lastName: 'Иванов', position: 'Механик', hasAvatar: false }
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (path === '/api/auth/logout') return route.fulfill({ status: options.logoutStatus || 204 })
    if (path === '/api/profile') return route.fulfill({ json: request.method() === 'PUT' ? { ...profile, userName: JSON.parse(request.postData()).userName } : profile })
    if (path === '/api/vehicles/fleet-options') return route.fulfill({ json: { canManage: false, canManageHours: false } })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle: { id: 'v1', modelName: 'SANY', typeName: 'Экскаватор', garageNumber: 94 }, defects: [], works: [], hours: [] }] })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await expect(page.locator('.app-shell')).toBeVisible()
}

test('logout revokes the server session before clearing credentials', async ({ page }) => {
  await session(page)
  const request = page.waitForRequest('**/api/auth/logout')
  await page.getByRole('button', { name: 'Выйти', exact: true }).click()
  expect((await request).headers().authorization).toBe('Bearer current-token')
  await expect(page.locator('.app-shell')).toHaveCount(0)
  expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBeNull()
})

test('failed server logout reports the error and allows retry', async ({ page }) => {
  await session(page, { logoutStatus: 503 })
  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('Повторите выход')
    await dialog.dismiss()
  })
  await page.getByRole('button', { name: 'Выйти', exact: true }).click()
  await expect(page.locator('.app-shell')).toBeVisible()
  expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBe('current-token')
  await page.route('**/api/auth/logout', route => route.fulfill({ status: 204 }))
  await page.getByRole('button', { name: 'Выйти', exact: true }).click()
  await expect(page.locator('.app-shell')).toHaveCount(0)
})

test('password change clears the revoked session and requests a new login', async ({ page }) => {
  await session(page)
  await page.getByRole('button', { name: /Редактировать профиль/ }).click()
  await page.getByLabel('Текущий пароль').fill('oldpassword')
  await page.getByLabel('Новый пароль', { exact: true }).fill('newpassword')
  await page.getByLabel('Повторите новый пароль').fill('newpassword')
  await page.getByRole('button', { name: 'Сохранить профиль', exact: true }).click()
  await expect(page.locator('.app-shell')).toHaveCount(0)
  await expect(page.getByText('Данные входа изменены. Войдите снова.')).toBeVisible()
})

test('hours editing uses server access even if the browser role says administrator', async ({ page }) => {
  await session(page)
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Моточасы', exact: true }).click()
  await expect(page.locator('.hours-row')).toHaveCount(1)
  await expect(page.locator('.hours-row input')).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Сохранить', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Импорт Excel / CSV' })).toHaveCount(0)
})
