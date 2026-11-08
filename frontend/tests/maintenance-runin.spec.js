import { test, expect } from '@playwright/test'

test('run-in due row is marked and closed from the schedule', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test')
    localStorage.setItem('myapp.userRole', 'administrator')
  })
  const vehicle = { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 904, typeName: 'Экскаватор', engineCount: 1 }
  const plan = {
    name: 'Цикл 350–4000',
    steps: [{ name: 'ТО-250', interval: 250 }, { name: 'ТО-500', interval: 250 }],
    nextStepIndex: 0,
    lastDate: '2026-11-07',
    lastService: 'ТО-2000',
    engines: [{ number: 1, hours: 522 }],
    runIns: [{ id: 'run-1', reason: 'Замена ДВС', startDate: '2026-10-22', interval: 150, engines: [{ number: 1, hours: 174 }] }],
  }
  const due = [
    { kind: 'run-in', runInId: 'run-1', name: 'Обкаточное ТО: Замена ДВС', interval: 150, engineNumber: 1, startHours: 174, currentHours: 555, dueHours: 324, elapsed: 381, remaining: -231, readingDate: '2026-11-09', isEstimated: true },
    { kind: 'scheduled', runInId: null, name: 'ТО-250', interval: 250, engineNumber: 1, startHours: 522, currentHours: 555, dueHours: 772, elapsed: 33, remaining: 217, readingDate: '2026-11-09', isEstimated: true },
  ]
  let completed
  await page.route('**/api/**', route => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (path === '/api/maintenance-plans' && req.method() === 'GET') return route.fulfill({ json: { canManage: true, canUseLibrary: false, rows: [{ vehicle, plan, revision: 4, due, incomplete: false }] } })
    if (path.endsWith('/complete')) {
      completed = req.postDataJSON()
      return route.fulfill({ json: { revision: 5 } })
    }
    if (path.endsWith('/history')) return route.fulfill({ json: [] })
    if (path.endsWith('/readings')) return route.fulfill({ json: { engines: [{ number: 1, hours: 555 }], missing: [] } })
    if (path === '/api/vehicles') return route.fulfill({ json: [vehicle] })
    return route.fulfill({ json: path === '/api/profile' ? { firstName: 'Тест' } : [] })
  })

  await page.goto('/')
  await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
  await page.getByRole('button', { name: 'Проведение ТО', exact: true }).click()
  await page.getByLabel('Техника', { exact: true }).selectOption('v1')
  await page.getByRole('button', { name: 'График и история', exact: true }).click()

  const runInRow = page.locator('.maintenance-due').filter({ hasText: 'Замена ДВС' })
  await expect(runInRow.locator('.maintenance-runin-badge')).toHaveText('обкатка')
  await expect(runInRow).toContainText('просрочено на 231')
  await expect(runInRow).toHaveClass(/maintenance-due--overdue/)
  await expect(page.locator('.maintenance-due').filter({ hasText: 'ТО-250' })).not.toHaveClass(/maintenance-due--overdue/)

  await page.screenshot({ path: 'test-results/runin-badge.png', fullPage: false })
  await runInRow.getByRole('button', { name: 'Закрыть обкатку', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Выполнение ТО' })
  await expect(dialog.getByLabel('Какое ТО выполнено')).toHaveValue('run-1')
  await dialog.getByLabel('Дата выполнения').fill('2026-11-09')
  await dialog.getByLabel('Описание выполненного ТО').fill('Обкатка после замены ДВС')
  await dialog.getByRole('button', { name: 'Сохранить выполнение', exact: true }).click()
  await expect.poll(() => completed?.runInId).toBe('run-1')
})
