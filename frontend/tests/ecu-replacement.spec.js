import { test, expect } from '@playwright/test'

test('engine control unit replacement can be recorded and removed', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'Administrator')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles', 'vehicles.hours']))
  })
  const vehicle = { id: 'v1', modelName: 'Sany SET150', typeName: 'Самосвал', garageNumber: 902, engineCount: 2 }
  const marks = []
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (path === '/api/profile') return route.fulfill({ json: { id: 'u1', firstName: 'Тест', position: 'Механик' } })
    if (path === '/api/vehicles/fleet-options') return route.fulfill({ json: { canManage: true, canManageHours: true } })
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: [{ vehicle, defects: [], works: [], hours: [] }] })
    if (path === '/api/vehicles/v1/ecu-replacements' && req.method() === 'GET') return route.fulfill({ json: marks })
    if (path === '/api/vehicles/v1/ecu-replacements' && req.method() === 'POST') {
      const body = req.postDataJSON()
      marks.push({ id: 'e1', vehicleId: 'v1', engineNumber: body.engineNumber, replacedOn: body.replacedOn, note: body.note, createdAt: '2026-11-11T00:00:00Z' })
      return route.fulfill({ status: 201, json: { id: 'e1' } })
    }
    if (path === '/api/vehicles/ecu-replacements/e1' && req.method() === 'DELETE') {
      marks.length = 0
      return route.fulfill({ status: 204, body: '' })
    }
    return route.fulfill({ json: [] })
  })

  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Моточасы', exact: true }).click()
  await page.getByRole('button', { name: /Действия с моточасами/ }).click()
  await page.getByRole('button', { name: 'Замена ЭБУ', exact: true }).click()

  const dialog = page.getByRole('dialog', { name: 'Замена ЭБУ' })
  await dialog.getByLabel('Дата замены').fill('2026-11-11')
  await dialog.getByLabel('Двигатель').selectOption('2')
  await dialog.getByLabel('Комментарий').fill('Замена по гарантии')
  await dialog.getByRole('button', { name: 'Отметить замену', exact: true }).click()
  await expect(dialog.locator('li').first()).toContainText('11.11.2026')
  await expect(dialog.locator('li').first()).toContainText('ДВС № 2')
  await expect(dialog.locator('li').first()).toContainText('Замена по гарантии')

  await dialog.getByRole('button', { name: 'Удалить', exact: true }).click()
  await expect(dialog.locator('li')).toHaveCount(0)
})
