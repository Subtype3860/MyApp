import { test, expect } from '@playwright/test'

const vehicles = Array.from({ length: 12 }, (_, index) => ({
  id: String(index),
  modelName: index === 0 ? 'Sany SET 150' : `Модель ${index}`,
  typeName: 'Карьерный самосвал',
  groupName: 'Карьерная техника',
  garageNumber: index + 1,
  stateNumber: `А${index}АА`,
  vin: `VIN${index}`,
}))
async function mockApi(page, { status = 200, items = vehicles } = {}) {
  await page.route('**/api/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/vehicles') return route.fulfill({ status, json: items })
    if (path === '/api/profile/avatar') return route.fulfill({ status: 404 })
    if (path === '/api/profile')
      return route.fulfill({ json: { firstName: 'Тест', hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
}
async function authenticate(page, role = 'Administrator', permissions = []) {
  await page.addInitScript(
    ({ role, permissions }) => {
      localStorage.setItem('myapp.authToken', 'ui-test-token')
      localStorage.setItem('myapp.userRole', role)
      localStorage.setItem('myapp.userPermissions', JSON.stringify(permissions))
    },
    { role, permissions },
  )
}
async function expectNoOverflow(page) {
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true)
}
for (const width of [320, 390, 768, 1440]) {
  test(`dashboard and login fit ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await mockApi(page)
    await page.goto('/')
    await expect(
      page.getByRole('heading', { name: 'Добро пожаловать' }),
    ).toBeVisible()
    await expectNoOverflow(page)
    await authenticate(page)
    await page.reload()
    await expect(
      page.getByRole('heading', { name: 'Sany SET 150' }),
    ).toBeVisible()
    await expectNoOverflow(page)
    await expect(
      page.locator('.dashboard-metric').first().locator('strong'),
    ).toHaveText('12')
    if (width > 760) {
      await expect.poll(() => page.locator('.sidebar').evaluate(el => Math.round(el.getBoundingClientRect().width))).toBe(268)
    }
    await page.screenshot({
      animations: 'disabled',
      path: `test-results/dashboard-${width}.png`,
      fullPage: true,
    })
  })
}
test('search, empty results and pagination use API data', async ({ page }) => {
  await mockApi(page)
  await authenticate(page)
  await page.goto('/')
  await expect(page.locator('.fleet-row')).toHaveCount(8)
  await page.getByRole('button', { name: 'Следующая страница' }).click()
  await expect(page.locator('.fleet-row')).toHaveCount(4)
  await page.getByRole('searchbox', { name: 'Поиск техники' }).fill('sany')
  await expect(page.locator('.fleet-row')).toHaveCount(1)
  await expect(
    page.getByRole('heading', { name: 'Sany SET 150' }),
  ).toBeVisible()
  await page
    .getByRole('searchbox', { name: 'Поиск техники' })
    .fill('нет такой модели')
  await expect(page.getByRole('status')).toHaveText(
    'По вашему запросу ничего не найдено.',
  )
})
test('mobile navigation traps focus, closes with Escape and navigates', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockApi(page)
  await authenticate(page)
  await page.goto('/')
  const trigger = page.getByRole('button', { name: 'Открыть меню' })
  await trigger.click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Закрыть меню' })).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await expect(
    page.getByRole('button', { name: 'Выйти', exact: true }),
  ).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Закрыть меню' })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(trigger).toBeFocused()
  await expect(page.locator('.page-content')).not.toHaveAttribute('inert')
  await trigger.click()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page
    .getByRole('button', { name: 'Заявка на ремонт', exact: true })
    .click()
  await expect(
    page.getByRole('heading', { name: 'Заявки на ремонт', exact: true }),
  ).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expectNoOverflow(page)
})
test('resizing an open mobile menu restores desktop interaction', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mockApi(page)
  await authenticate(page)
  await page.goto('/')
  await page.getByRole('button', { name: 'Открыть меню' }).click()
  await page.setViewportSize({ width: 1440, height: 900 })
  await expect(page.locator('.page-content')).not.toHaveAttribute('inert')
  await expect(page.locator('body')).not.toHaveClass(/navigation-open/)
  await page.getByRole('button', { name: 'Свернуть меню' }).click()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await expect(
    page.getByRole('button', { name: 'Заявка на ремонт', exact: true }),
  ).toBeVisible()
})
test('no vehicle permission: no vehicle request and no vehicle shortcut', async ({
  page,
}) => {
  const requests = []
  page.on('request', (request) =>
    requests.push(new URL(request.url()).pathname),
  )
  await mockApi(page)
  await authenticate(page, 'Mechanic', ['menu.maintenance'])
  await page.goto('/')
  await expect(
    page.getByRole('heading', { name: 'Ваши разделы' }),
  ).toBeVisible()
  await expect(page.locator('.fleet-panel')).toHaveCount(0)
  await expect(
    page.getByRole('button', { name: /Создать заявку/ }),
  ).toHaveCount(0)
  expect(requests).not.toContain('/api/vehicles')
})
test('API error is recoverable and does not display fake zero metrics', async ({
  page,
}) => {
  await mockApi(page, { status: 500 })
  await authenticate(page)
  await page.goto('/')
  await expect(page.getByRole('alert')).toContainText('Не удалось загрузить')
  await expect(
    page.locator('.dashboard-metric').first().locator('strong'),
  ).toHaveText('—')
  await page.route('**/api/vehicles', (route) =>
    route.fulfill({ json: vehicles }),
  )
  await page.getByRole('button', { name: 'Обновить' }).click()
  await expect(
    page.getByRole('heading', { name: 'Sany SET 150' }),
  ).toBeVisible()
})
test('expired session returns to login', async ({ page }) => {
  await mockApi(page, { status: 401 })
  await authenticate(page)
  await page.goto('/')
  await expect(
    page.getByRole('heading', { name: 'Добро пожаловать' }),
  ).toBeVisible()
  await expect(page.getByRole('alert')).toContainText('повторная авторизация')
})
