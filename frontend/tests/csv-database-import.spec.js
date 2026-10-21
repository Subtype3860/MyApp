import { test, expect } from '@playwright/test'

/** Открывает настройки импорта табличных данных в тесте. */
async function openImportSettings(page, previewError = false) {
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', 'test-token')
    localStorage.setItem('myapp.userRole', 'administrator')
  })
  const imports = [], previews = []
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (path === '/api/admin/imports/sheets') return route.fulfill({ json: ['Лист1', 'Остатки'] })
    if (path === '/api/admin/imports/warehouse/preview') {
      previews.push(request.postData())
      return route.fulfill(previewError
        ? { status: 400, json: { errors: { file: ['Количество должно быть неотрицательным числом.'] } } }
        : { json: { added: 2, updated: 3, zeroed: 4, imported: 5, columns: ['Наименование', 'Количество'], rows: [{ Наименование: 'Filter', Количество: 10 }] } })
    }
    if (path === '/api/admin/imports/warehouse' && request.method() === 'POST') {
      imports.push(request.postData())
      return route.fulfill({ json: { added: 2, updated: 3, zeroed: 4, imported: 5 } })
    }
    return route.fulfill({ json: [] })
  })
  await page.goto('/')
  await page.getByRole('button', { name: 'Настройки', exact: true }).click()
  await page.getByRole('button', { name: /CSV/ }).click()
  return { imports, previews }
}
const /** Формирует тестовый ответ складского источника. */ warehouse = page => page.locator('.csv-file-card').filter({ hasText: 'Остатки на складе' })

test('snapshot requires preview and explains absent-position zeroing', async ({ page }) => {
  const { imports } = await openImportSettings(page)
  await expect(page.getByText(/отсутствующие позиции получат количество 0/)).toBeVisible()
  const card = warehouse(page)
  await card.locator('input[type=file]').setInputFiles({ name: 'Октябрь.csv', mimeType: 'text/csv', buffer: Buffer.from('name;unit;amount\nFilter;pcs;10') })
  await expect(card.getByRole('button', { name: 'Импортировать в БД' })).toBeDisabled()
  await card.getByRole('button', { name: 'Проверить файл' }).click()
  await expect(card).toContainText('Обнулится: 4')
  expect(imports).toHaveLength(0)
  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('отсутствующих в файле, станет нулевым')
    await dialog.accept()
  })
  await card.getByRole('button', { name: 'Импортировать в БД' }).click()
  await expect(card).toContainText('Добавлено 2, обновлено 3, обнулено 4')
  expect(imports).toHaveLength(1)
  expect(imports[0]).toContain('Октябрь.csv')
})

test('invalid snapshot cannot be saved', async ({ page }) => {
  const { imports } = await openImportSettings(page, true)
  const card = warehouse(page)
  await card.locator('input[type=file]').setInputFiles({ name: 'stock.csv', mimeType: 'text/csv', buffer: Buffer.from('name;unit;amount\nFilter;pcs;-1') })
  await card.getByRole('button', { name: 'Проверить файл' }).click()
  await expect(card).toContainText('Количество должно быть неотрицательным числом.')
  await expect(card.getByRole('button', { name: 'Импортировать в БД' })).toBeDisabled()
  expect(imports).toHaveLength(0)
})

test('Excel accepts any filename and chosen sheet; changing sheet invalidates preview', async ({ page }) => {
  const { imports, previews } = await openImportSettings(page)
  const card = warehouse(page)
  await card.locator('input[type=file]').setInputFiles({ name: 'Склад.xlsx', mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', buffer: Buffer.from('mock workbook') })
  await card.getByLabel('Лист Excel').selectOption('Остатки')
  await card.getByRole('button', { name: 'Проверить файл' }).click()
  await expect(card).toContainText('Обнулится: 4')
  expect(previews[0]).toContain('Остатки')
  await card.getByLabel('Лист Excel').selectOption('Лист1')
  await expect(card.getByRole('button', { name: 'Импортировать в БД' })).toBeDisabled()
  await card.getByRole('button', { name: 'Проверить файл' }).click()
  await expect(card).toContainText('Обнулится: 4')
  page.once('dialog', dialog => dialog.accept())
  await card.getByRole('button', { name: 'Импортировать в БД' }).click()
  await expect(card).toContainText('Добавлено 2')
  expect(imports[0]).toContain('Склад.xlsx')
  expect(imports[0]).toContain('Лист1')
})
