import { test, expect } from '@playwright/test'

for (const width of [390, 1440]) {
  test(`repair timeline, upload and save at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'admin', role: 'administrator' }))}.test`)
      localStorage.setItem('myapp.userRole', 'Administrator')
    })
    const vehicle = { id: 'v1', modelName: 'Sany SET150', garageNumber: 901, stateNumber: '5113 СС 65' }
    const works = [
      { id: 'w1', defectId: 'd1', description: 'Первичный осмотр', createdAt: '2026-10-05T10:51:00Z', photos: [], videos: [] },
      { id: 'w2', defectId: 'd1', description: 'Проверена топливная система.', createdAt: '2026-10-06T02:25:00Z', photos: [], videos: [] },
    ]
    let saved
    const errors = []
    page.on('pageerror', e => errors.push(e.message))
    await page.route('**/api/**', route => {
      const request = route.request()
      const path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path.endsWith('/works')) {
        saved = request.postDataJSON()
        return route.fulfill({ json: { id: 'w3' } })
      }
      if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
      if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects: [{ id: 'd1', symptoms: 'Потеря мощности', status: 'faulty', downtimeStartedAt: '2026-10-01T11:36:00Z', photos: [], videos: [] }], works }] })
      if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
      return route.fulfill({ status: path.endsWith('/avatar') ? 404 : 200, json: [] })
    })
    await page.goto('/')
    await page.locator('.dashboard-shortcut').filter({ hasText: 'Работы, история' }).click()
    const stages = page.locator('.repair-timeline-stage')
    await expect(stages).toHaveCount(2)
    await expect(stages.first()).toHaveAttribute('open', '')
    await expect(stages.first().locator('.repair-stage-content')).toContainText('Проверена топливная система.')
    await stages.last().locator('summary').click()
    await expect(stages.last().locator('.repair-stage-content')).toBeVisible()
    await stages.last().locator('summary').click()
    await expect(stages.last().locator('.repair-stage-content')).not.toBeVisible()
    if (width < 761) {
      await expect(page.locator('.repair-new-stage')).not.toHaveAttribute('open')
      await page.locator('.repair-new-stage > summary').click()
    }
    await page.getByLabel('Описание выполненных работ', { exact: true }).fill('Проверка выполнена')
    await page.getByLabel('Место работ', { exact: true }).selectOption('РММ')
    const fileChooser = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Добавить фото или видео' }).click()
    expect((await fileChooser).isMultiple()).toBe(true)
    await page.getByLabel('Ожидает запчасти', { exact: true }).check()
    await page.getByRole('button', { name: 'Сохранить этап' }).click()
    await expect.poll(() => saved).toMatchObject({ defectId: 'd1', description: 'Проверка выполнена', status: 'awaiting_parts' })
    await expect(page.locator('.repair-timeline-stage')).toHaveCount(2)
    const deletion = await page.locator('.repair-timeline-delete').first().boundingBox()
    expect(deletion.width).toBeGreaterThanOrEqual(48)
    expect(deletion.height).toBeGreaterThanOrEqual(48)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(errors).toEqual([])
    await page.screenshot({ path: `test-results/repair-timeline-${width}.png`, fullPage: true, animations: 'disabled' })
  })
}

test('administrator edits and deletes requests and works in repair history', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'admin', role: 'administrator' }))}.test`)
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const vehicle = { id: 'v1', garageNumber: 901, stateNumber: 'А123ВС' }
  const defect = { id: 'd1', symptoms: 'Неисправность', createdAt: '2026-10-01T10:00:00Z' }
  const work = { id: 'w1', defectId: 'd1', description: 'Осмотр', performerName: 'Иванов Иван Иванович', performerPosition: 'Слесарь', participants: [{ id: 'legacy-p', userId: 'author', name: 'Иванов Иван Иванович', position: 'Слесарь', status: 'approved' }], status: 'awaiting_parts', completedAt: '2026-10-02T10:00:00Z', createdAt: '2026-10-02T10:00:00Z' }
  let defects = [defect]
  let works = [work]
  const mutations = []
  await page.route('**/api/**', route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (['PUT', 'DELETE'].includes(request.method())) {
      mutations.push({ path, method: request.method(), body: request.method() === 'PUT' ? request.postDataJSON() : null })
      if (request.method() === 'PUT') {
        if (path.includes('/works/')) work.description = request.postDataJSON().description
        else defect.symptoms = request.postDataJSON().description
      } else if (path.includes('/works/')) works = []
      else defects = []
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects, works }] })
    if (path === '/api/vehicles/v1/journal') return route.fulfill({ json: { defects, works } })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'История ремонта', exact: true }).click()
  await page.getByLabel('Госномер или внутренний номер').fill('901')
  await expect(page.getByText('Неисправность', { exact: true })).toBeVisible()
  await expect(page.locator('.repair-request__status')).toHaveText('Ожидает запчасти')
  await expect(page.getByRole('button', { name: 'Редактировать заявку', exact: true })).toBeHidden()
  await page.locator('.repair-request__details > summary').click()
  await expect(page.locator('.work-item__status')).toHaveCount(0)
  await expect(page.locator('.work-participants')).toContainText('Слесарь · Иванов Иван Иванович')
  page.on('dialog', dialog => dialog.accept(dialog.type() === 'prompt' ? 'Изменённое описание' : undefined))
  await page.getByLabel('Действия с работой', { exact: true }).click()
  await page.getByRole('button', { name: 'Редактировать работу', exact: true }).click()
  await expect(page.getByText('Изменённое описание', { exact: true })).toHaveCount(1)
  await page.getByLabel('Действия с заявкой', { exact: true }).click()
  await page.getByRole('button', { name: 'Редактировать заявку', exact: true }).click()
  await expect(page.locator('.repair-request__description')).toHaveText('Изменённое описание')
  await expect(page.locator('.repair-history__loading')).toHaveCount(0)
  await page.locator('.repair-request__details > summary').click()
  await page.getByLabel('Действия с работой', { exact: true }).click()
  await page.getByRole('button', { name: 'Удалить работу', exact: true }).click()
  await expect(page.getByText('Работы по заявке не зарегистрированы.')).toBeVisible()
  await expect(page.locator('.repair-request__status')).toHaveText('Зарегистрирована')
  await page.getByLabel('Действия с заявкой', { exact: true }).click()
  await page.getByRole('button', { name: 'Удалить заявку', exact: true }).click()
  await expect(page.getByText('Ремонтов не производилось')).toBeVisible()
  expect(mutations.map(item => [item.method, item.path])).toEqual([
    ['PUT', '/api/vehicles/history/works/w1'], ['PUT', '/api/vehicles/history/defects/d1'],
    ['DELETE', '/api/vehicles/works/w1'], ['DELETE', '/api/vehicles/defects/d1'],
  ])
})

for (const [userId, expectedButtons] of [['creator', 1], ['other', 0]]) {
  test(`repair stages can only be deleted by their creator: ${userId}`, async ({ page }) => {
    await page.addInitScript(id => {
      localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: id, role: 'user' }))}.test`)
      localStorage.setItem('myapp.userRole', 'Administrator')
    }, userId)
    await page.route('**/api/**', route => {
      const path = new URL(route.request().url()).pathname
      if (path === '/api/vehicles') return route.fulfill({ json: [{ id: 'v1', garageNumber: 901 }] })
      if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{
        vehicle: { id: 'v1', garageNumber: 901 },
        defects: [{ id: 'd1', symptoms: 'Неисправность', status: 'faulty', photos: [], videos: [] }],
        works: [{ id: 'w1', defectId: 'd1', createdById: 'creator', performerPosition: 'Слесарь', performerName: 'Иванов Иван Иванович', participants: [{ id: 'legacy-p', userId: 'creator', name: 'Иванов Иван Иванович', position: 'Слесарь', status: 'approved' }], description: 'Осмотр', photos: [], videos: [] }],
      }] })
      if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
      return route.fulfill({ json: [] })
    })
    await page.goto('/')
    await page.locator('.dashboard-shortcut').filter({ hasText: 'Работы, история' }).click()
    await expect(page.locator('.repair-timeline-stage')).toHaveCount(1)
    await expect(page.locator('.repair-timeline-delete')).toHaveCount(expectedButtons)
    await expect(page.locator('.repair-stage-content')).toContainText('Слесарь · Иванов Иван Иванович')
  })
}

test('request status comes from the latest completed work, ignoring newer unfinished work', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'reader', role: 'user' }))}.test`)
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/vehicles') return route.fulfill({ json: [{ id: 'v1', garageNumber: 901 }] })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{
      vehicle: { id: 'v1', garageNumber: 901 },
      defects: [{ id: 'd1', symptoms: 'Проверка статуса', status: 'faulty' }],
      works: [
        { id: 'w3', defectId: 'd1', description: 'Незавершённая работа', status: 'awaiting_parts', createdAt: '2026-10-10T10:00:00Z' },
        { id: 'w2', defectId: 'd1', description: 'Выполненный ремонт', status: 'repaired', completedAt: '2026-10-09T10:00:00Z' },
        { id: 'w1', defectId: 'd1', description: 'Старая работа', status: 'awaiting_parts', completedAt: '2026-10-08T10:00:00Z' },
      ],
    }] })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'История ремонта', exact: true }).click()
  await page.getByLabel('Госномер или внутренний номер').fill('901')
  await expect(page.locator('.repair-request__status')).toHaveText('Выполнена')
  await page.locator('.repair-request__details > summary').click()
  await expect(page.locator('.work-item')).toHaveCount(3)
  await expect(page.locator('.work-item__status')).toHaveCount(0)
  await expect(page.locator('.repair-actions')).toHaveCount(0)
})
