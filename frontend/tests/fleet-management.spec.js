import { test, expect } from '@playwright/test'

async function setup(page, { width = 1440, manager = true, denied = false } = {}) {
  await page.setViewportSize({ width, height: 1100 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'mechanic', role: 'user' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles', 'vehicles.works', 'vehicles.report']))
  })
  let vehicles = [{ id: 'v1', groupName: 'Спецтехника', typeName: 'Экскаваторы', modelName: 'SANY SY1250H', garageNumber: 94, stateNumber: '', vin: '', operationalState: 'stopped' }]
  const options = { canManage: manager, groups: [{ id: 'g1', name: 'Спецтехника' }], types: [{ id: 't1', name: 'Экскаваторы', parentId: 'g1' }], models: [{ id: 'm1', name: 'SANY SY1250H', parentId: 't1' }] }
  const mutations = []
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (['POST', 'PUT', 'DELETE'].includes(request.method())) {
      const body = request.method() === 'DELETE' ? null : request.postDataJSON()
      mutations.push({ path, method: request.method(), body })
      if (denied) return route.fulfill({ status: 403, json: { message: 'Недостаточно прав для управления парком.' } })
      if (request.method() === 'POST') { vehicles.push({ ...body, id: 'v2', operationalState: 'healthy' }); return route.fulfill({ status: 201, json: { id: 'v2' } }) }
      if (request.method() === 'PUT') { vehicles[0] = { ...vehicles[0], ...body }; return route.fulfill({ json: { id: 'v1' } }) }
      vehicles = vehicles.filter(item => item.id !== 'v1')
      return route.fulfill({ status: 204 })
    }
    if (path === '/api/vehicles') return route.fulfill({ json: vehicles })
    if (path === '/api/vehicles/fleet-options') return route.fulfill({ json: options })
    if (path === '/api/profile') return route.fulfill({ json: { id: 'mechanic', position: manager ? 'Механик' : 'Слесарь', hasAvatar: false } })
    return route.fulfill({ status: path.endsWith('/avatar') ? 404 : 200, json: [] })
  })
  await page.goto('/')
  return mutations
}

for (const width of [390, 1440]) {
  test(`mechanic adds edits and removes fleet vehicles at ${width}px`, async ({ page }) => {
    const mutations = await setup(page, { width })
    await page.getByLabel('Меню парка техники', { exact: true }).click()
    await page.getByRole('button', { name: 'Добавить технику', exact: true }).click()
    const dialog = page.locator('.fleet-editor')
    await dialog.getByLabel('Группа техники', { exact: true }).fill('Вспомогательная техника')
    await dialog.getByLabel('Тип техники', { exact: true }).fill('Погрузчики')
    await dialog.getByLabel('Модель техники', { exact: true }).fill('XCMG ZL60GV')
    await dialog.getByLabel('Гаражный номер', { exact: true }).fill('97')
    await dialog.getByRole('button', { name: 'Сохранить технику' }).click()
    await expect(dialog).not.toBeVisible()
    await expect(page.locator('.fleet-row')).toHaveCount(2)
    expect(mutations[0]).toMatchObject({ path: '/api/vehicles', method: 'POST', body: { garageNumber: 97, groupName: 'Вспомогательная техника', typeName: 'Погрузчики', modelName: 'XCMG ZL60GV' } })
    await page.getByLabel('Действия с SANY SY1250H № 94', { exact: true }).click()
    await page.getByRole('button', { name: 'Редактировать SANY SY1250H № 94' }).click()
    await expect(dialog.getByLabel('Гаражный номер', { exact: true })).toHaveValue('94')
    await dialog.getByLabel('Государственный номер', { exact: true }).fill('А123ВС65')
    await dialog.getByRole('button', { name: 'Сохранить технику' }).click()
    await expect(page.locator('.fleet-list')).toContainText('А123ВС65')
    expect(mutations[1]).toMatchObject({ path: '/api/vehicles/v1', method: 'PUT' })
    page.once('dialog', dialog => { expect(dialog.message()).toContain('История ремонтов'); dialog.accept() })
    await page.getByLabel('Действия с SANY SY1250H № 94', { exact: true }).click()
    await page.getByRole('button', { name: 'Удалить из парка SANY SY1250H № 94' }).click()
    await expect(page.locator('.fleet-row')).toHaveCount(1)
    await expect(page.getByText('Техника удалена из активного парка. История сохранена.')).toBeVisible()
    expect(mutations[2]).toMatchObject({ path: '/api/vehicles/v1', method: 'DELETE' })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: `test-results/fleet-management-${width}.png`, fullPage: true })
  })
}

test('ordinary worker does not see fleet management controls', async ({ page }) => {
  await setup(page, { manager: false })
  await expect(page.locator('.fleet-row')).toHaveCount(1)
  await expect(page.getByRole('button', { name: 'Добавить технику', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Редактировать SANY/ })).toHaveCount(0)
})

test('server denial keeps the editor open with an error', async ({ page }) => {
  await setup(page, { denied: true })
  await page.getByLabel('Действия с SANY SY1250H № 94', { exact: true }).click()
    await page.getByRole('button', { name: 'Редактировать SANY SY1250H № 94' }).click()
  await page.locator('.fleet-editor').getByRole('button', { name: 'Сохранить технику' }).click()
  await expect(page.locator('.fleet-editor')).toBeVisible()
  await expect(page.locator('.fleet-editor [role="alert"]')).toHaveText('Недостаточно прав для управления парком.')
})
