import { test, expect } from '@playwright/test'

test.use({ timezoneId: 'Asia/Omsk' })

/** Подготавливает страницу и ответы API для сценариев браузерного теста. */
async function setup(page, position, width = 1440) {
  await page.setViewportSize({ width, height: 1000 })
  await page.clock.setFixedTime(new Date('2026-10-11T14:00:00+06:00'))
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'employee', role: 'user' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles', 'vehicles.works', 'vehicles.repairHistory', 'vehicles.report']))
  })
  const vehicle = { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 901, typeName: 'Экскаваторы' }
  const participants = [{ id: 'approved', userId: 'author', name: 'Иванов Иван Иванович', position: 'Слесарь', status: 'approved' }]
  if (position === 'Механик') participants.push({ id: 'pending', userId: 'employee', name: 'Петров Пётр Петрович', position: 'Электрослесарь', status: 'pending' })
  const work = { id: 'w1', defectId: 'd1', description: 'Замена масляных фильтров', location: 'КАРЬЕР', createdAt: '2026-10-11T10:00:00+06:00', completedAt: '2026-10-11T10:00:00+06:00', status: 'faulty', operationalState: 'operating', participants }
  const journal = { vehicle, defects: [{ id: 'd1', symptoms: 'Неисправность ДВС', createdAt: '2026-10-10T12:00:00+06:00', operationalState: 'operating', status: 'faulty' }], works: [work] }
  const mutations = []
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (request.method() === 'POST' && path.endsWith('/participants')) {
      mutations.push({ path, body: request.postData() })
      const own = participants.find(row => row.userId === 'employee')
      if (own) { own.status = 'pending'; own.reviewedBy = null; own.reviewedAt = null }
      else participants.push({ id: 'pending', userId: 'employee', name: 'Петров Пётр Петрович', position: 'Электрослесарь', status: 'pending' })
      return route.fulfill({ json: {} })
    }
    if (request.method() === 'POST' && path.endsWith('/review')) {
      const body = request.postDataJSON()
      mutations.push({ path, body })
      participants.find(row => row.id === 'pending').status = body.approved ? 'approved' : 'rejected'
      return route.fulfill({ json: {} })
    }
    if (path.endsWith('/participants')) return route.fulfill({ json: participants })
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    if (path === '/api/vehicles/v1/journal') return route.fulfill({ json: journal })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [journal] })
    if (path === '/api/profile') return route.fulfill({ json: { id: 'employee', position, hasAvatar: false } })
    return route.fulfill({ status: path.endsWith('/avatar') ? 404 : 200, json: [] })
  })
  await page.goto('/')
  if (width < 761) await page.getByRole('button', { name: 'Открыть меню' }).click()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'История ремонта', exact: true }).click()
  await page.getByLabel('Госномер или внутренний номер').fill('901')
  await page.locator('.repair-request__details > summary').click()
  return { mutations, participants }
}

for (const width of [390, 1440]) {
  test(`employee requests participation, pending name stays out of report at ${width}px`, async ({ page }) => {
    const { mutations } = await setup(page, 'Электрослесарь', width)
    await page.getByRole('button', { name: 'Я участвовал в работе' }).click()
    await expect(page.getByText('Ваше участие ожидает подтверждения механика.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Я участвовал в работе' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Подтвердить участие: Петров Пётр Петрович' })).toHaveCount(0)
    expect(mutations).toEqual([{ path: '/api/vehicles/works/w1/participants', body: null }])
    if (width < 761) await page.getByRole('button', { name: 'Открыть меню' }).click()
    await page.getByRole('button', { name: 'Отчёты', exact: true }).click()
    await page.getByRole('button', { name: 'Отчёт за смену', exact: true }).click()
    await expect(page.locator('.shift-report__paper')).toContainText('Иванов Иван Иванович')
    await expect(page.locator('.shift-report__paper')).not.toContainText('Петров Пётр Петрович')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  })
}

test('mechanic confirms a colleague and the confirmed performer appears in report', async ({ page }) => {
  const { mutations } = await setup(page, 'Механик')
  await page.getByRole('button', { name: 'Подтвердить участие: Петров Пётр Петрович' }).click()
  await expect(page.locator('.work-participants__status--approved')).toHaveCount(2)
  expect(mutations[0]).toEqual({ path: '/api/vehicles/works/w1/participants/pending/review', body: { approved: true } })
  await page.getByRole('button', { name: 'Отчёты', exact: true }).click()
  await page.getByRole('button', { name: 'Отчёт за смену', exact: true }).click()
  await expect(page.locator('.shift-report__performers')).toContainText('Электрослесарь · Петров Пётр Петрович')
})

test('mechanic can reject participation and employee can apply again', async ({ page }) => {
  const { mutations } = await setup(page, 'Механик')
  await page.getByRole('button', { name: 'Отклонить участие: Петров Пётр Петрович' }).click()
  await expect(page.locator('.work-participants__status--rejected')).toHaveText('Отклонено')
  expect(mutations[0].body).toEqual({ approved: false })
  await page.getByRole('button', { name: 'Повторно заявить участие' }).click()
  await expect(page.getByText('Ваше участие ожидает подтверждения механика.')).toBeVisible()
})
