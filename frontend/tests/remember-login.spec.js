import { test, expect } from '@playwright/test'

const session = { token: 'test.remember.token', role: 'Administrator', permissions: ['menu.home'] }

async function mockApi(page) {
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/auth/login') return route.fulfill({ json: session })
    if (path === '/api/auth/logout') return route.fulfill({ status: 204 })
    if (path === '/api/profile') return route.fulfill({ json: { id: 'me', firstName: 'Иван', lastName: 'Иванов', middleName: '', position: 'Механик' } })
    if (path === '/api/profile/avatar') return route.fulfill({ status: 404 })
    if (path === '/api/fleet-regions') return route.fulfill({ json: { regions: [] } })
    if (path === '/api/fleet-regions/capabilities') return route.fulfill({ json: { canCreate: false } })
    if (path === '/api/vehicles/fleet-options') return route.fulfill({ json: { canManage: false, canManageHours: false } })
    return route.fulfill({ json: [] })
  })
}

const stored = (page) => page.evaluate(() => ({
  local: localStorage.getItem('myapp.authToken'),
  session: sessionStorage.getItem('myapp.authToken'),
  localRole: localStorage.getItem('myapp.userRole'),
}))

async function submitLogin(page, remember) {
  const checkbox = page.getByLabel('Запомнить меня')
  await expect(checkbox).toBeVisible()
  await expect(checkbox).toBeChecked()
  if (!remember) await checkbox.uncheck()
  await page.getByLabel('Логин или электронная почта').fill('mechanic')
  await page.getByLabel('Пароль', { exact: true }).fill('secret')
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toHaveCount(0)
}

test('remembered login is stored permanently and cleared on logout', async ({ page }) => {
  await mockApi(page)
  await page.goto('/')
  await submitLogin(page, true)

  expect(await stored(page)).toEqual({
    local: session.token,
    session: null,
    localRole: session.role,
  })

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Выйти', exact: true }).first().click()
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toBeVisible()
  expect(await stored(page)).toEqual({ local: null, session: null, localRole: null })
})

test('unchecked remember keeps the session only for the current tab', async ({ page }) => {
  await mockApi(page)
  await page.goto('/')
  await submitLogin(page, false)

  expect(await stored(page)).toEqual({ local: null, session: session.token, localRole: null })

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Выйти', exact: true }).first().click()
  await expect(page.getByRole('heading', { name: 'Добро пожаловать' })).toBeVisible()
  expect(await stored(page)).toEqual({ local: null, session: null, localRole: null })
})
