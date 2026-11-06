import { test, expect } from '@playwright/test'

async function mount(page, admin = true) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'test', role: 'administrator' }))}.test`)
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  const regions = [{ id: 'a', name: 'Север', isArchived: false }, { id: 'b', name: 'Юг', isArchived: false }]
  let assignments = [{ vehicleId: 'v1', assignmentId: 'old', regionId: 'a', regionName: 'Север', startedAt: '2026-10-01T00:00:00Z' }]
  let history = [{ id: 'old', regionId: 'a', regionName: 'Север', startedAt: '2026-10-01T00:00:00Z', endedAt: null, recordedBy: 'Администратор', note: 'Первое закрепление' }]
  const writes = []
  await page.route('**/api/**', async route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (req.method() !== 'GET') {
      const body = req.postDataJSON(); writes.push({ path, body })
      if (path.endsWith('/transfer')) {
        assignments = [{ vehicleId: 'v1', assignmentId: 'new', regionId: 'b', regionName: 'Юг' }]
        history = [{ id: 'new', regionName: 'Юг', startedAt: '2026-11-05T00:00:00Z', recordedBy: 'Администратор', note: body.note }, { ...history[0], endedAt: '2026-11-05T00:00:00Z' }]
      }
      return route.fulfill({ json: { id: 'new' } })
    }
    if (path === '/api/fleet-regions/capabilities') return route.fulfill({json:{canCreate:admin}})
    if (path === '/api/fleet-regions/management') return route.fulfill({json:{canManage:admin,regions,assignments,vehicles:[{id:'v1',modelName:'SANY',garageNumber:911},{id:'v2',modelName:'SANY',garageNumber:912}],users:[{id:'u1',firstName:'Иван',lastName:'Иванов',userName:'ivan',role:'user'}]}})
    const json = path === '/api/fleet-regions' ? { canManage: admin, regions: admin ? regions : [regions[0]] }
      : path === '/api/fleet-regions/vehicles' ? assignments
      : path.endsWith('/history') ? history
      : path === '/api/fleet-regions/users/u1' ? ['a']
      : path === '/api/admin/users' ? [{ id: 'u1', firstName: 'Иван', lastName: 'Иванов', userName: 'ivan', role: 'user' }]
      : path === '/api/vehicles' ? [{ id: 'v1', modelName: 'SANY', garageNumber: 911 }, ...(admin ? [{ id: 'v2', modelName: 'SANY', garageNumber: 912 }] : [])] : []
    await route.fulfill({ json })
  })
  await page.goto('/')
  if (page.viewportSize().width < 761) await page.getByRole('button', { name: 'Открыть меню', exact: true }).click()
  await page.getByRole('button', { name: 'Участок', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Перемещения' }).first()).toBeVisible()
  return writes
}

for (const width of [390, 1440]) test(`administrator grants and relocates with history at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 })
  const writes = await mount(page)
  await page.getByText('Доступ пользователей к участкам', { exact: true }).click()
  await page.getByRole('combobox', { name: 'Пользователь', exact: true }).selectOption('u1')
  await expect(page.getByRole('checkbox', { name: 'Север', exact: true })).toBeChecked()
  await page.getByRole('checkbox', { name: 'Юг', exact: true }).check()
  await page.getByRole('button', { name: 'Сохранить доступ', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Доступ к участкам сохранён')
  expect(writes[0].body.regionIds).toEqual(['a', 'b'])
  await page.getByRole('combobox', { name: 'Показать участок', exact: true }).selectOption('unassigned')
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.locator('tbody')).toContainText('912')
  await page.getByRole('combobox', { name: 'Показать участок', exact: true }).selectOption('a')
  await page.getByRole('button', { name: 'Перемещения' }).click()
  await page.getByRole('combobox', { name: 'Новый участок', exact: true }).selectOption('b')
  await page.getByLabel('Примечание', { exact: true }).fill('Переезд на южный участок')
  await page.getByRole('button', { name: 'Переместить технику', exact: true }).click()
  await expect(page.getByRole('status')).toContainText('Техника перемещена')
  expect(writes.at(-1).body).toMatchObject({ regionId: 'b', expectedAssignmentId: 'old', startedAt: null })
  await expect(page.locator('.region-history li')).toHaveCount(2)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})

test('assignment panel is absent from transport and users', async ({ page }) => {
 await mount(page)
 await page.getByRole('button', {name:'Транспорт',exact:true}).click()
 await page.getByRole('button', {name:'Моточасы',exact:true}).click()
 await expect(page.getByText('Регионы / участки и закрепление техники',{exact:true})).toHaveCount(0)
 await page.getByRole('button', {name:'Настройки',exact:true}).click()
 await page.getByRole('button', {name:'Пользователи',exact:true}).click()
 await expect(page.getByText('Регионы / участки и закрепление техники',{exact:true})).toHaveCount(0)
})
