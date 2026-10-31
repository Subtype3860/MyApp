import { test, expect } from '@playwright/test'
import { PDFDocument, PDFName } from 'pdf-lib'
import { readFile } from 'node:fs/promises'

test.use({ timezoneId: 'Asia/Omsk' })
import { defaultReportSelection, selectShiftWorks, groupShiftWorks, stateAt, workPerformers } from '../src/utils/shiftReport.js'

 test('report date uses noon boundary and shifts exclude end boundary', () => {
  expect(defaultReportSelection(new Date(2026, 9, 12, 0, 0))).toEqual({ date: '2026-10-11', shift: '2' })
  expect(defaultReportSelection(new Date(2026, 9, 12, 11, 59))).toEqual({ date: '2026-10-11', shift: '2' })
  expect(defaultReportSelection(new Date(2026, 9, 12, 12, 0))).toEqual({ date: '2026-10-12', shift: '1' })
  expect(defaultReportSelection(new Date(2026, 0, 1, 7))).toEqual({ date: '2025-12-31', shift: '2' })
  const works = ['2026-10-11T19:59:59', '2026-10-11T20:00:00', '2026-10-12T00:42:00', '2026-10-12T07:59:59', '2026-10-12T08:00:00']
    .map((createdAt, index) => ({ id: `${index}`, createdAt, location: index === 1 ? 'РММ' : 'КАРЬЕР' }))
  const selected = selectShiftWorks([{ vehicle: { id: 'v', typeName: 'Экскаваторы' }, works }], '2026-10-11', '2')
  expect(selected.map(work => work.id)).toEqual(['1', '2', '3'])
  const groups = groupShiftWorks(selected)
  expect(groups.map(group => group.location)).toEqual(['КАРЬЕР', 'РММ'])
  expect(groups[0].categories[0].cards[0].works).toHaveLength(2)
  expect(stateAt({ defects: [
    { id: 'd1', createdAt: '2026-10-10T10:00:00', operationalState: 'stopped' },
    { id: 'd2', createdAt: '2026-10-10T10:00:00', operationalState: 'stopped' },
  ], works: [{ defectId: 'd1', completedAt: '2026-10-11T21:00:00', status: 'repaired' }] }, new Date('2026-10-12T08:00:00')).kind).toBe('stopped')
})

const approved = { id: 'p-approved', userId: 'worker1', name: 'Иванов Иван Иванович', position: 'Слесарь', status: 'approved' }
const pending = { id: 'p-pending', userId: 'worker2', name: 'Петров Пётр Петрович', position: 'Электрослесарь', status: 'pending' }
const journals = [{ vehicle: { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 94, typeName: 'Экскаваторы' }, works: [
  { id: 'w1', completedAt: '2026-10-11T21:00:00+06:00', description: 'Обвязка ДВС, сборка системы выпуска, замена масляных фильтров.', participants: [approved, pending], location: 'КАРЬЕР', operationalState: 'stopped', photos: [{ id: 'p1', fileName: 'Ремонт.jpg' }], videos: [{ id: 'vid1', fileName: 'Проверка.mp4' }] },
  { id: 'w2', completedAt: '2026-10-12T00:42:00+06:00', description: 'Проверка электрооборудования ДВС.', participants: [approved], location: 'КАРЬЕР', operationalState: 'stopped' },
  { id: 'out', completedAt: '2026-10-12T08:00:00+06:00', description: 'Следующая смена', location: 'КАРЬЕР' },
] }, { vehicle: { id: 'v2', modelName: 'SANY SET 150S', garageNumber: 903, typeName: 'Автосамосвалы' }, works: [
  { id: 'w3', completedAt: '2026-10-11T23:00:00+06:00', description: 'Подготовка к демонтажу ГСТ № 2', participants: [approved], location: 'РММ', operationalState: 'stopped' },
] }]

/** Настраивает данные и API для проверки сменного отчёта. */
async function prepare(page) {
  const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))
  await page.addInitScript(() => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'admin', role: 'administrator' }))}.test`)
    localStorage.setItem('myapp.userRole', 'Administrator')
  })
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: journals })
    if (path === '/api/vehicles/work-photos/p1') return route.fulfill({ body: photo, contentType: 'image/webp' })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
}

for (const width of [390, 1440]) {
  test(`shift report and PDF at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await prepare(page)
    await page.goto('/?reportWork=w1,w2&reportDate=2026-10-11&reportShift=2')
    // A PDF media link automatically opens the existing authenticated viewer.
    await expect(page.locator('.shift-report .repair-media-dialog')).toBeVisible()
    await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
    await expect(page.locator('.shift-report__location')).toHaveCount(2)
    await expect(page.locator('.shift-report__card')).toHaveCount(2)
    await expect(page.locator('.shift-report__paper')).toContainText('Проверка электрооборудования ДВС.')
    await expect(page.locator('.shift-report__paper')).not.toContainText('Следующая смена')
    await expect(page.locator('.shift-report__performers').first()).toContainText('Слесарь · Иванов Иван Иванович')
    await expect(page.locator('.shift-report__paper')).not.toContainText('Петров Пётр Петрович')
    const downloadEvent = page.waitForEvent('download')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
    await page.getByRole('button', { name: 'Скачать PDF', exact: true }).click()
    const download = await downloadEvent
    expect(download.suggestedFilename()).toBe('Отчёт за смену 2026-10-11 (2).pdf')
    await download.saveAs(`test-results/shift-report-${width}.pdf`)
    const document = await PDFDocument.load(await readFile(await download.path()))
    expect(document.getPageCount()).toBeGreaterThan(0)
    expect(document.getPages().some(p => p.node.get(PDFName.of('Annots')))).toBe(true)
    expect(document.getPages().some(p => p.node.Resources().get(PDFName.of('XObject')))).toBe(true)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: `test-results/shift-report-${width}.png`, fullPage: true })
  })
}

test('old works require an explicit location before PDF export', async ({ page }) => {
  await prepare(page)
  await page.route('**/api/vehicles/repair-journal*', route => route.fulfill({ json: [{ ...journals[0], works: [{ ...journals[0].works[0], location: '' }] }] }))
  await page.goto('/?reportWork=w1&reportDate=2026-10-11&reportShift=2')
  await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
  await expect(page.getByRole('button', { name: 'Скачать PDF' })).toBeDisabled()
  await page.locator('.shift-report__missing select').selectOption('РММ')
  await expect(page.locator('.shift-report__location h3')).toHaveText('РММ')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
  await expect(page.getByRole('button', { name: 'Скачать PDF' })).toBeEnabled()
})

 test('only confirmed performers enter the report', () => {
  expect(workPerformers({ participants: [approved, pending, { ...pending, status: 'rejected' }] })).toEqual(['Слесарь · Иванов Иван Иванович'])
  expect(workPerformers({ performerName: 'Автор записи', participants: [pending] })).toEqual([])
})
