import { test, expect } from '@playwright/test'
import { PDFDocument, PDFName } from 'pdf-lib'
import { readFile } from 'node:fs/promises'

test.use({ timezoneId: 'Asia/Omsk' })
import { defaultReportSelection, selectShiftWorks, groupShiftWorks, groupReportWorks, stateAt, workPerformers, maintenanceReportGroups } from '../src/utils/shiftReport.js'

test('report orders areas and vehicle types, with additional works last', () => {
  const types = ['Вспомогательная техника', 'Карьерный самосвал', 'Бульдозер', 'Экскаватор']
  const works = ['РММ', 'КАРЬЕР'].flatMap(location => types.flatMap(typeName => [10, 2].map(garageNumber => ({
    id: `${location}-${typeName}-${garageNumber}`, location,
    vehicle: { id: `${location}-${typeName}-${garageNumber}`, typeName, garageNumber },
  }))))
  const groups = groupReportWorks(works, [{ id: 'aux', location: 'КАРЬЕР', title: 'Уборка' }])
  expect(groups.map(g => g.location)).toEqual(['КАРЬЕР', 'РММ', 'Дополнительные работы'])
  for (const group of groups.slice(0, 2)) {
    expect(group.categories.map(c => c.name)).toEqual(['Экскаватор', 'Карьерный самосвал', 'Бульдозер', 'Вспомогательная техника'])
    for (const category of group.categories) expect(category.cards.map(c => c.vehicle.garageNumber)).toEqual([2, 10])
  }
  expect(groups[2].categories[0].name).toBe('КАРЬЕР')
  expect(groups[2].categories[0].cards[0].auxiliary).toBe(true)
  const maintenance = [{ location: 'Техническое обслуживание', categories: [] }]
  expect(groupReportWorks([], [{ id: 'aux', location: 'РММ' }], {}, maintenance).map(g => g.location))
    .toEqual(['Техническое обслуживание', 'Дополнительные работы'])
})

test('maintenance completion shift boundaries and duplicate cards', () => {
  const records = ['2026-10-11T19:59:59', '2026-10-11T20:00:00', '2026-10-12T07:59:59', '2026-10-12T08:00:00']
    .map((finishedAt, id) => ({ id, finishedAt, service: 'ТО-700' }))
  const groups = maintenanceReportGroups([...records, records[1]], '2026-10-11', '2')
  expect(groups[0].categories[0].cards.map(c => c.id)).toEqual(['maintenance:1', 'maintenance:2'])
})

test('area and equipment order is identical in preview and PDF', async ({ page }) => {
  await prepare(page)
  const types = ['Вспомогательная техника', 'Самосвалы', 'Бульдозеры', 'Экскаваторы']
  await page.route('**/api/vehicles/repair-journal*', route => route.fulfill({ json:
    ['РММ', 'КАРЬЕР'].flatMap(location => types.map((typeName, index) => ({
      vehicle: { id: `${location}-${index}`, modelName: `${location} ${typeName}`, typeName, garageNumber: index + 1 },
      works: [{ id: `${location}-${index}`, location, description: 'Проверка', completedAt: '2026-10-11T21:00:00+06:00' }],
    })))
  }))
  await page.route('**/api/auxiliary-works', route => route.fulfill({ json: [{ id: 'extra', workKind: 'auxiliary', location: 'КАРЬЕР', title: 'Уборка', description: 'Убрана площадка', completedAt: '2026-10-11T21:00:00+06:00' }] }))
  await page.goto('/?reportWork=unused&reportDate=2026-10-11&reportShift=2')
  await expect(page.locator('.shift-report__location h3')).toHaveText(['КАРЬЕР', 'РММ', 'Дополнительные работы'])
  const order = ['Экскаваторы', 'Самосвалы', 'Бульдозеры', 'Вспомогательная техника']
  const expected = ['КАРЬЕР', 'РММ'].flatMap(location => order.map(type => `${location} ${type}`))
  const titles = await page.locator('.shift-report__card h5').allTextContents()
  expected.forEach((title, i) => expect(titles[i]).toContain(title))
  await page.getByLabel('Меню отчёта за смену', { exact: true }).click()
  const downloadEvent = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Скачать PDF', exact: true }).click()
  const download = await downloadEvent
  const { getDocument } = await import('pdfjs-dist/legacy/build/pdf.mjs')
  const task = getDocument({ data: new Uint8Array(await readFile(await download.path())), useSystemFonts: true })
  const pdf = await task.promise
  let text = ''
  for (let i = 1; i <= pdf.numPages; i++) text += (await (await pdf.getPage(i)).getTextContent()).items.map(x => x.str).join(' ')
  let position = -1
  for (const title of [...expected, 'Дополнительные работы', 'Уборка']) {
    const next = text.indexOf(title)
    expect(next).toBeGreaterThan(position)
    position = next
  }
  await task.destroy()
})

test('maintenance-only report contains full names in preview and PDF', async ({ page }) => {
  await prepare(page)
  const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))
  await page.route('**/api/report-media/links', route => {
    const ids = route.request().postDataJSON().map(item => item.id)
    // Фото карт ТО подписываются ссылками карты, а не общим эндпоинтом отчёта.
    if (ids.some(id => String(id).startsWith('mp'))) return route.fulfill({ status: 404, body: '' })
    return route.fulfill({ json: ids.map(id => ({ id, type: 'image', url: `/api/report-media/image/${id}/signed` })) })
  })
  await page.route('**/api/maintenance-plans/cards/m2/media-links', route => route.fulfill({ json: [{ id: 'mp1', url: '/api/maintenance-plans/cards/m2/media/mp1/sig' }] }))
  await page.route('**/api/maintenance-plans/cards/m2/photos/mp1', route => route.fulfill({ body: photo, contentType: 'image/webp' }))
  await page.route('**/api/vehicles/repair-journal*', route => route.fulfill({ json: [] }))
  await page.route('**/api/maintenance-plans/shift-report', route => route.fulfill({ json: [
    {
      id: 'm1', vehicleLabel: 'Sany SET150 №903', service: 'ТО-700',
      finishedAt: '2026-10-12T01:15:00+06:00', executors: ['Иванов Иван Иванович', 'Петров Пётр Петрович'],
    },
    {
      id: 'm2', vehicleLabel: 'Sany SET150 №904', service: 'ТО-1000',
      finishedAt: '2026-10-12T02:15:00+06:00', executors: ['Сидоров Сидор Сидорович'],
      operations: [{ id: 'o1', text: 'Проверить масло', status: 'done', condition: 'ok', executor: 'Сидоров Сидор Сидорович', note: '', photos: ['mp1'] }],
    },
  ] }))
  await page.goto('/?reportWork=missing&reportDate=2026-10-11&reportShift=2')
  await expect(page.locator('.shift-report__paper')).toContainText('ТО-700 — проведено')
  await expect(page.locator('.shift-report__paper')).toContainText('Иванов Иван Иванович')
  await expect(page.locator('.shift-report__paper')).toContainText('Проверить масло — Сидоров Сидор Сидорович')
  await page.getByLabel('Меню отчёта за смену', { exact: true }).click()
  const downloadEvent = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Скачать PDF', exact: true }).click()
  const download = await downloadEvent
  const { getDocument } = await import('pdfjs-dist/legacy/build/pdf.mjs')
  const loadingTask = getDocument({ data: new Uint8Array(await readFile(await download.path())), useSystemFonts: true })
  const pdf = await loadingTask.promise
  let text = ''
  for (let i = 1; i <= pdf.numPages; i++) text += (await (await pdf.getPage(i)).getTextContent()).items.map(x => x.str).join(' ')
  expect(text).toContain('ТО-700')
  expect(text).toContain('Иванов Иван Иванович')
  expect(text).toContain('Петров Пётр Петрович')
  expect(text).toContain('Проверить масло')
  expect(text).toContain('Сидоров Сидор Сидорович')
  await loadingTask.destroy()
})

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
  { id: 'w1', completedAt: '2026-10-11T21:00:00+06:00', description: 'Обвязка ДВС, сборка системы выпуска, замена масляных фильтров.', participants: [approved, pending], location: 'КАРЬЕР', operationalState: 'stopped', photos: [{ id: 'p1', fileName: 'Ремонт.jpg' }, { id: 'p4', fileName: 'Ремонт 2.jpg' }, { id: 'p5', fileName: 'Ремонт 3.jpg' }], videos: [{ id: 'vid1', fileName: 'Проверка.mp4' }] },
  { id: 'w2', completedAt: '2026-10-12T00:42:00+06:00', description: 'Проверка электрооборудования ДВС.', photos: [{ id: 'p2', fileName: 'Электрооборудование.webp' }], participants: [approved], location: 'КАРЬЕР', operationalState: 'stopped' },
  { id: 'w4', completedAt: '2026-10-12T01:00:00+06:00', description: 'Чистка контактных групп', participants: [pending], location: 'КАРЬЕР', operationalState: 'stopped', photos: [{ id: 'p3', fileName: 'Контакты.webp' }] },
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
    if (path === '/api/report-media/links') return route.fulfill({ json: route.request().postDataJSON().map(item => ({ ...item, url: `/api/report-media/${item.type}/${item.id}/signed-test-link` })) })
    if (path === '/api/vehicles/repair-journal') return route.fulfill({ json: journals })
    if (['/api/vehicles/work-photos/p1', '/api/vehicles/work-photos/p2', '/api/vehicles/work-photos/p3', '/api/vehicles/work-photos/p4', '/api/vehicles/work-photos/p5'].includes(path)) return route.fulfill({ body: photo, contentType: 'image/webp' })
    if (path === '/api/profile') return route.fulfill({ json: { hasAvatar: false } })
    return route.fulfill({ json: [] })
  })
}

test('profession visibility removes maintenance, media and performers from preview and PDF', async ({ page }) => {
  await prepare(page)
  await page.addInitScript(() => {
    const permission = ['reports.shift', 'menu.reports', 'menu.vehicles', 'ui.hidden.shift_report.maintenance', 'ui.hidden.shift_report.media', 'ui.hidden.shift_report.performers']
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ role: 'user', permission }))}.test`)
    localStorage.setItem('myapp.userRole', 'user')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(permission))
  })
  await page.route('**/api/maintenance-plans/shift-report', route => route.fulfill({ json: [{
    id: 'm-hidden', vehicleLabel: 'Скрытая техника', service: 'ТО-700', finishedAt: '2026-10-12T01:15:00+06:00', executors: ['Скрытый Исполнитель'],
  }] }))
  await page.goto('/?reportWork=w1&reportDate=2026-10-11&reportShift=2')
  await expect(page.locator('.shift-report__paper')).toContainText('Проверка электрооборудования ДВС.')
  await expect(page.locator('.shift-report__paper')).not.toContainText('ТО-700')
  await expect(page.locator('.shift-report__performers')).toHaveCount(0)
  await expect(page.locator('.shift-report__photos')).toHaveCount(0)
  await page.getByLabel('Меню отчёта за смену', { exact: true }).click()
  const downloaded = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Скачать PDF', exact: true }).click()
  const download = await downloaded
  const { getDocument } = await import('pdfjs-dist/legacy/build/pdf.mjs')
  const loadingTask = getDocument({ data: new Uint8Array(await readFile(await download.path())), useSystemFonts: true })
  const pdf = await loadingTask.promise
  let text = ''
  for (let i = 1; i <= pdf.numPages; i++) text += (await (await pdf.getPage(i)).getTextContent()).items.map(x => x.str).join(' ')
  expect(text).not.toContain('Иванов')
  expect(text).not.toContain('Исполнители')
  expect(text).not.toContain('ТО-700')
  await loadingTask.destroy()
})

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
    await expect(page.locator('.shift-report__performers').first()).toContainText('Иванов И. И.')
    await expect(page.locator('.shift-report__paper')).not.toContainText('Петров Пётр Петрович')
    const card = page.locator('.shift-report__card').first()
    await expect(card.locator('.shift-report__work-section')).toHaveCount(3)
    await expect(card.locator('.shift-report__work-section').first().locator('.shift-report__work')).toHaveCount(1)
    await expect(card.locator('.shift-report__work-section').last()).toContainText('Чистка контактных групп')
    await expect(card.locator('.shift-report__work-section').last()).toContainText('Исполнители пока не подтверждены')
    await expect(card.locator('.shift-report__photos img')).toHaveCount(5)
    await expect(card.locator('.shift-report__photos a').first()).toHaveAttribute('href', /\/api\/report-media\/image\/p1\//)
    await expect(card.locator('.shift-report__links a').first()).toHaveAttribute('href', /\/api\/report-media\/video\/vid1\//)
    for (const work of await card.locator('.shift-report__work').all()) {
      const photo = await work.locator('.shift-report__photos').boundingBox()
      const description = await work.locator('.shift-report__description').boundingBox()
      expect(description.y + description.height).toBeLessThanOrEqual(photo.y)
      await expect(work.locator('.shift-report__description')).toHaveCSS('font-weight', '700')
    }
    await expect(card.locator('.shift-report__performers').first()).toHaveCSS('font-style', 'italic')
    const downloadEvent = page.waitForEvent('download')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
    await page.getByRole('button', { name: 'Скачать PDF', exact: true }).click()
    const download = await downloadEvent
    expect(download.suggestedFilename()).toBe('Отчёт за смену 2026-10-11 (2).pdf')
    await download.saveAs(`test-results/shift-report-${width}.pdf`)
    const document = await PDFDocument.load(await readFile(await download.path()))
    expect(document.getPageCount()).toBeGreaterThan(0)
    const urls = document.getPages().flatMap(sheet => {
      const annotations = sheet.node.Annots()
      return annotations ? Array.from({ length: annotations.size() }, (_, index) => annotations.lookup(index).lookup(PDFName.of('A')).lookup(PDFName.of('URI')).decodeText()) : []
    })
    expect(urls.length).toBeGreaterThan(0)
    expect(urls.every(url => url.includes('/api/report-media/'))).toBe(true)
    expect(urls.some(url => url.includes('/video/vid1/'))).toBe(true)
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
