import { test, expect } from '@playwright/test'

for (const width of [390, 1440]) {
for (const media of [true, false]) {
  test(`parts request shows latest work description and its media: ${media}, ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', 'test')
      localStorage.setItem('myapp.userRole', 'Administrator')
    })
    const requestedMedia = []
    await page.route('**/api/**', route => {
      const path = new URL(route.request().url()).pathname
      if (path === '/api/vehicles') return route.fulfill({ json: [{ id: 'v1', garageNumber: 901, modelName: 'Техника' }] })
      if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle: { id: 'v1', garageNumber: 901, modelName: 'Техника' },
        works: [
          { id: 'old', defectId: 'd1', status: 'awaiting_parts', description: 'Старое описание', cause: 'Исходная причина', createdAt: '2026-10-09T10:00:00Z', completedAt: '2026-10-09T10:00:00Z', photos: [{ id: 'old-photo' }] },
          { id: 'latest', defectId: 'd1', status: 'awaiting_parts', description: 'Нужна замена топливного насоса', cause: 'Исходная причина', createdAt: '2026-10-08T10:00:00Z', completedAt: '2026-10-10T10:00:00Z', partsRequests: [{ id: 'p1', requestDate: '2026-10-10', description: 'Заявка № 42\nТопливный насос — 1 шт.\nКомплект уплотнений — 2 шт.' }], photos: media ? [{ id: 'latest-photo', fileName: 'photo.png' }] : [], videos: media ? [{ id: 'latest-video', fileName: 'video.mp4' }] : [] },
        ],
      }] })
      if (path.includes('/work-photos/')) {
        requestedMedia.push(path)
        return route.fulfill({ contentType: 'image/png', body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=', 'base64') })
      }
      if (path.includes('/work-videos/')) {
        requestedMedia.push(path)
        return route.fulfill({ json: { url: '/test-video.mp4' } })
      }
      if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
      return route.fulfill({ json: [] })
    })
    await page.goto('/')
    if (width < 761) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
    await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
    await page.getByRole('button', { name: 'Заявка на закупку ЗЧ', exact: true }).click()
    const card = page.locator('.parts-request-card')
    await expect(card).toHaveCount(1)
    await expect(card).toContainText('Требуется: Нужна замена топливного насоса')
    await expect(card).not.toContainText('Старое описание')
    await expect(card).not.toContainText('Исходная причина')
    await expect(card.locator('.parts-request-history__item time')).toHaveText('10.10.2026')
    await expect(card.locator('.parts-request-new')).toHaveCount(0)
    const cellBounds = await card.locator('.parts-request-cell-orders').boundingBox()
    const menuBounds = await card.locator('.repair-actions > summary').boundingBox()
    expect(cellBounds.x + cellBounds.width - menuBounds.x - menuBounds.width).toBeLessThanOrEqual(12)
    expect(menuBounds.y - cellBounds.y).toBeLessThanOrEqual(12)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: `test-results/parts-request-${width}-${media}.png`, fullPage: true, animations: 'disabled' })
    await page.getByLabel('Действия с заявками на закупку', { exact: true }).click()
    await page.getByRole('button', { name: 'Редактировать заявку', exact: true }).click()
    const dialog = page.locator('.parts-action-dialog')
    await expect(dialog).toBeVisible()
    await expect(dialog.getByLabel('Номер и описание заявки')).toHaveValue('Заявка № 42\nТопливный насос — 1 шт.\nКомплект уплотнений — 2 шт.')
    await dialog.getByRole('button', { name: 'Отмена', exact: true }).click()
    if (media) {
      await card.getByRole('button', { name: 'Фото · 1', exact: true }).click()
      await expect(page.locator('.repair-media-dialog')).toBeVisible()
      await expect.poll(() => requestedMedia).toContain('/api/vehicles/work-photos/latest-photo')
      await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
      await card.getByRole('button', { name: 'Видео · 1', exact: true }).click()
      await expect.poll(() => requestedMedia).toContain('/api/vehicles/work-videos/latest-video/stream-ticket')
      await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
    } else {
      await expect(card.getByRole('button', { name: /^Фото|^Видео/ })).toHaveCount(0)
    }
  })
}

}
