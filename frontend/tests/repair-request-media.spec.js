import { test, expect } from '@playwright/test'
import { readFile } from 'node:fs/promises'

test('repair request accepts new media and keeps text editing', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'employee' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles', 'vehicles.works']))
  })
  const vehicle = { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 904 }
  const defect = { id: 'd1', createdById: 'employee', symptoms: 'Течёт гидравлика', createdAt: '2026-11-08T01:00:00Z', photos: [], videos: [] }
  const uploads = []
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() === 'POST' && path === '/api/vehicles/defects/d1/photos') {
      uploads.push(req.headers()['content-type'] ?? '')
      defect.photos.push({ id: 'ph1', fileName: 'photo.webp' })
      return route.fulfill({ status: 201, json: { id: 'ph1' } })
    }
    if (req.method() === 'POST' && path === '/api/vehicles/defects/d1/videos') {
      defect.videos.push({ id: 'vd1', fileName: 'video.mp4' })
      return route.fulfill({ status: 201, json: { id: 'vd1' } })
    }
    if (path === '/api/profile') return route.fulfill({ json: { id: 'employee', position: 'Слесарь' } })
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects: [defect], works: [] }] })
    if (path.startsWith('/api/vehicles/defect-photos/')) return route.fulfill({ body: Buffer.from(''), contentType: 'image/webp' })
    return route.fulfill({ json: [] })
  })
  const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))

  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'История ремонта', exact: true }).click()
  await page.locator('.repair-history__row').first().click()
  await expect(page.locator('.repair-request__description')).toHaveText('Течёт гидравлика')
  await expect(page.getByRole('button', { name: 'Редактировать заявку', exact: true })).toBeVisible()

  await page.getByLabel('Добавить медиа к заявке', { exact: true }).setInputFiles({ name: 'photo.webp', mimeType: 'image/webp', buffer: photo })
  await expect.poll(() => uploads.length).toBe(1)
  expect(uploads[0]).toContain('multipart/form-data')
  await expect(page.getByRole('button', { name: 'Просмотр фото (1)', exact: true })).toBeVisible()
  await page.screenshot({ path: 'test-results/repair-request-media.png' })
})
