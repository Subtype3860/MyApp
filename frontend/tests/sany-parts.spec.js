import { test, expect } from '@playwright/test'

const captcha = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64')

test('SANY page connects with captcha and searches equipment', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.tables', 'menu.vehicles']))
  })
  const calls = []
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (path === '/api/sany/session' && req.method() === 'POST') {
      calls.push(req.postDataJSON())
      return route.fulfill({ json: { sessionId: 's1', captchaImage: captcha.toString('base64'), contentType: 'image/png', expiresAt: '2026-11-09T20:00:00Z' } })
    }
    if (path === '/api/sany/session/s1/login') {
      calls.push(req.postDataJSON())
      return route.fulfill({ json: { connected: true, expiresAt: '2026-11-09T20:00:00Z' } })
    }
    if (path === '/api/sany/session/s1/query') {
      calls.push(req.postDataJSON())
      return route.fulfill({ json: { code: '0', data: [{ equipmentCode: 'ET150SE30008', equipmentName: 'Самосвал' }] } })
    }
    if (path === '/api/profile') return route.fulfill({ json: { id: 'u1', firstName: 'Тест', position: 'Механик' } })
    return route.fulfill({ json: [] })
  })

  await page.goto('/')
  await page.getByRole('button', { name: 'Таблицы', exact: true }).click()
  await page.getByRole('button', { name: 'SANY: запчасти', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'SANY: запчасти', level: 1 })).toBeVisible()

  await page.getByLabel('Логин SANY').fill('Alexey_G')
  await page.getByLabel('Пароль SANY').fill('secret')
  await page.getByRole('button', { name: 'Продолжить', exact: true }).click()
  await expect(page.getByRole('img', { name: 'Капча SANY' })).toBeVisible()
  expect(calls[0]).toEqual({ account: 'Alexey_G', password: 'secret' })

  await page.getByLabel('Код с картинки').fill('1234')
  await page.getByRole('button', { name: 'Войти в SANY', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Подключение к SANY установлено')
  expect(calls[1]).toEqual({ captchaCode: '1234' })

  await page.screenshot({ path: 'test-results/sany-connect.png' })
  await page.getByLabel('Код техники SANY').fill('ET150SE30008')
  await page.getByRole('button', { name: 'Найти', exact: true }).click()
  await expect(page.locator('.sany-response')).toContainText('ET150SE30008')
  expect(calls[2].path).toBe('/customer/equipmentInfo/queryByCondition')
  expect(calls[2].body.equipmentCode).toBe('ET150SE30008')

  await page.getByRole('button', { name: 'Отключить', exact: true }).click()
  await expect(page.getByLabel('Логин SANY')).toBeVisible()
})
