import { test, expect } from '@playwright/test'
import { readFile } from 'node:fs/promises'

for (const failureStatus of [401, 403, 503]) {
  test(`mobile repair handles photo response ${failureStatus} without duplicating stages`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.addInitScript(() => {
      localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'worker', role: 'user' }))}.test`)
      localStorage.setItem('myapp.userRole', 'user')
      localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles', 'vehicles.repair']))
    })
    const vehicle = { id: 'v1', modelName: 'Sany SET150', garageNumber: 901 }
    let stagesCreated = 0
    const uploads = []
    await page.route('**/api/**', route => {
      const request = route.request(), path = new URL(request.url()).pathname
      if (request.method() === 'POST' && path === '/api/vehicles/v1/works') {
        stagesCreated++
        return route.fulfill({ json: { id: 'saved-stage' } })
      }
      if (request.method() === 'POST' && path === '/api/vehicles/works/saved-stage/photos') {
        uploads.push(request.postDataBuffer().toString())
        expect(request.headers().authorization).toContain('Bearer test.')
        return uploads.length === 2
          ? route.fulfill({ status: failureStatus, json: { message: 'Тестовый отказ загрузки' } })
          : route.fulfill({ status: 201, json: { id: `photo-${uploads.length}` } })
      }
      if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
      if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects: [{ id: 'd1', symptoms: 'Ошибка DCUI-660', operationalState: 'operating', photos: [], videos: [] }], works: [] }] })
      if (path === '/api/profile') return route.fulfill({ json: { id: 'worker', position: 'Электрослесарь по ремонту оборудования', hasAvatar: false } })
      return route.fulfill({ json: [] })
    })
    await page.goto('/')
    await page.locator('.dashboard-shortcut').filter({ hasText: 'Работы, история' }).click()
    await page.locator('.repair-request-row').click()
    await page.locator('.repair-new-stage > summary').click()
    await page.getByLabel('Описание выполненных работ', { exact: true }).fill('Чистка отсека ACU')
    await page.getByLabel('Место работ', { exact: true }).selectOption('РММ')
    const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))
    await page.evaluate(() => {
      const decode = window.createImageBitmap.bind(window)
      const gate = new Promise(resolve => { window.releasePhotoPreparation = resolve })
      window.createImageBitmap = async (...args) => { await gate; return decode(...args) }
    })
    await page.locator('.repair-new-stage input[type=file]').setInputFiles([
      { name: 'first.webp', mimeType: 'image/webp', buffer: photo },
      { name: 'second.webp', mimeType: 'image/webp', buffer: photo },
    ])
    await expect(page.getByRole('button', { name: 'Подготовка фото…', exact: true })).toBeDisabled()
    expect(stagesCreated).toBe(0)
    await page.evaluate(() => window.releasePhotoPreparation())
    await expect(page.locator('.repair-card-file-list')).toContainText('second.webp')
    await page.getByRole('button', { name: 'Сохранить этап', exact: true }).click()
    if (failureStatus === 401) {
      await expect(page.locator('.app-shell')).toHaveCount(0)
      expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBeNull()
      expect(stagesCreated).toBe(1)
      return
    }
    await expect(page.getByRole('button', { name: 'Повторить загрузку', exact: true })).toBeVisible()
    await expect(page.getByLabel('Описание выполненных работ', { exact: true })).toBeDisabled()
    expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBeTruthy()
    await page.getByRole('button', { name: 'Повторить загрузку', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Сохранить этап', exact: true })).toBeEnabled()
    expect(stagesCreated).toBe(1)
    expect(uploads).toHaveLength(3)
    expect(uploads[0]).toContain('filename="first.webp"')
    expect(uploads[1]).toContain('filename="second.webp"')
    expect(uploads[2]).toContain('filename="second.webp"')
    expect(await page.evaluate(() => localStorage.getItem('myapp.authToken'))).toBeTruthy()
  })
}
