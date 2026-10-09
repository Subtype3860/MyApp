import { test, expect } from '@playwright/test'

for (const width of [390, 1440]) {
  test(`repair timeline, upload and save at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', 'test')
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
