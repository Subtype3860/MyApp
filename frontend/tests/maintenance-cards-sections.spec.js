import { test, expect } from '@playwright/test'

// Карточка ТО: разделы, медиа раздела и участие с подтверждением механиком.
for (const width of [390, 1440]) test(`maintenance card by sections at ${width}`, async ({ page }) => {
  test.setTimeout(60000)
  await page.setViewportSize({ width, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'administrator')
  })
  const vehicle = { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 98, typeName: 'Экскаватор', engineCount: 1 }
  const plan = { name: 'Цикл', steps: [{ name: 'ТО-1000', interval: 250 }], nextStepIndex: 0, lastDate: '2026-10-01', engines: [{ number: 1, hours: 750 }], runIns: [] }
  const template = {
    model: vehicle.modelName, service: 'ТО-1000', source: 'test', reviewNotes: '',
    operations: [
      { id: 'o1', section: 'ДВС', text: 'Проверить масло', target: '', control: false },
      { id: 'o2', section: 'ДВС', text: 'Проверить ремень', target: '', control: false },
      { id: 'o3', section: 'Шасси', text: 'Смазать подшипник', target: '', control: false },
      { id: 'o4', section: 'Заключительный контроль', text: 'Осмотреть', target: '', control: true },
    ],
    materials: [],
  }
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=', 'base64')
  let card = null, revision = 1
  const media = []
  const participation = []
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname, method = route.request().method()
    if (path === '/api/maintenance-plans/card-template-options') return route.fulfill({ json: { canManage: true, canFill: true, canUseLibrary: true, templates: [{ id: 't1', revision: 1, published: true, template }] } })
    if (path === '/api/maintenance-plans') return route.fulfill({ json: { canManage: true, canUseLibrary: true, rows: [{ vehicle, plan, revision, due: [], incomplete: false }] } })
    if (path.endsWith('/history') || path.endsWith('/readings')) return route.fulfill({ json: [] })
    if (path.endsWith('/v1/cards') && method === 'POST') {
      const body = route.request().postDataJSON()
      card = {
        id: body.id, vehicleId: 'v1', vehicleLabel: 'SANY SY1250H №98', templateId: 't1', templateRevision: 1, planRevision: 1,
        status: 'draft', revision: 1, currentUserId: 'u1', template,
        data: { date: '2026-10-30', driver: '', engineModel: '', mileage: null, stoppedAt: null, startedAt: '2026-10-30T00:00:00Z', finishedAt: null, releasedAt: null, notes: '', answers: [], materials: [], sections: [] },
        audit: [], defects: {}, readings: [],
      }
      return route.fulfill({ json: card })
    }
    if (path.endsWith('/v1/cards')) return route.fulfill({ json: card ? [card] : [] })
    if (path.endsWith('/photos')) return route.fulfill({ json: media })
    if (path.includes('/photos/') && method === 'POST') {
      const form = route.request().postData()
      const match = /name="operationId"\r?\n\r?\n([^\r\n]+)/.exec(form || '')
      media.push({ id: path.split('/').at(-1), operationId: match ? match[1] : 'section:ДВС', contentType: 'image/png' })
      card.revision += 1
      return route.fulfill({ json: { id: media.at(-1).id } })
    }
    if (path.includes('/photos/')) return route.fulfill({ body: png, contentType: 'image/png' })
    if (path.endsWith('/participation') && method === 'POST') {
      const body = route.request().postDataJSON()
      participation.push(body.section)
      const section = card.data.sections.find(item => item.name === body.section) ?? { name: body.section, participants: [] }
      if (!card.data.sections.includes(section)) card.data.sections.push(section)
      if (!section.participants.some(person => person.userId === 'u1')) section.participants.push({ userId: 'u1', name: 'Тестов Тест', position: 'Механик', status: 'pending' })
      return route.fulfill({ json: { sections: card.data.sections } })
    }
    if (path.endsWith('/participation/decision') && method === 'POST') {
      const body = route.request().postDataJSON()
      const section = card.data.sections.find(item => item.name === body.section)
      section.participants = section.participants.map(person => person.userId === body.userId
        ? { ...person, status: body.approve ? 'approved' : 'rejected', approvedBy: 'Механиков М.' } : person)
      return route.fulfill({ json: { sections: card.data.sections } })
    }
    if (card && path.endsWith('/cards/' + card.id) && method === 'PUT') {
      const body = route.request().postDataJSON()
      card.data = body.data; card.revision += 1
      if (body.action === 'submit') card.status = 'submitted'
      return route.fulfill({ json: card })
    }
    if (path.endsWith('/accept')) { card.status = 'accepted'; card.revision += 1; return route.fulfill({ json: { revision: ++revision } }) }
    return route.fulfill({ json: path === '/api/profile' ? { firstName: 'Тест' } : [] })
  })
  await page.goto('/')
  if (width < 760) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Проведение ТО', exact: true }).click()
  await page.getByLabel('Техника', { exact: true }).selectOption('v1')
  await page.getByRole('button', { name: 'ТО по картам', exact: true }).click()
  await page.getByRole('button', { name: 'Начать ТО по карте', exact: true }).click()
  const panel = page.locator('.card-editor')

  // Разделы вместо операций: заключительный контроль не выводится.
  await expect(panel.locator('.card-section summary')).toHaveText(['ДВС', 'Шасси'])
  await expect(panel).not.toContainText('Заключительный контроль')

  // Медиа привязывается к разделу.
  await panel.getByLabel('Медиа раздела: ДВС', { exact: true }).setInputFiles({ name: 'f.png', mimeType: 'image/png', buffer: png })
  await expect(panel.getByRole('button', { name: /Фото 1 · Открыть/ })).toBeVisible()
  expect(media[0].operationId).toBe('section:ДВС')

  // Участие: отметка работника и подтверждение механиком.
  await panel.getByRole('button', { name: 'Я участвовал', exact: true }).first().click()
  await expect.poll(() => participation).toEqual(['ДВС'])
  await expect(panel).toContainText('ожидает')
  await panel.getByRole('button', { name: 'Подтвердить', exact: true }).click()
  await expect(panel).toContainText('подтверждён')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
