import { test, expect } from '@playwright/test'
import { fileURLToPath } from 'node:url'
import { readFile } from 'node:fs/promises'
import { PDFDocument, PDFName } from 'pdf-lib'
import { selectAuxiliaryWorks, groupAuxiliaryWorks, reportMediaLink } from '../src/utils/shiftReport.js'

test.use({ timezoneId: 'Asia/Omsk' })
const confirmed = { id: 'participant', userId: 'worker', name: 'Петров Пётр Петрович', position: 'Разнорабочий', status: 'approved', reviewedBy: 'mechanic' }
const sample = { id: 'a1', title: 'Уборка территории', description: 'Собран мусор и очищены проходы.', location: 'РММ', workKind: 'auxiliary', completedAt: '2026-10-11T10:00:00+06:00', createdAt: '2026-10-11T10:00:00+06:00', participants: [confirmed], photos: [{ id: 'p1', fileName: 'Территория.webp' }], videos: [{ id: 'video1', fileName: 'Работы.webm' }] }

/** Подготавливает страницу и ответы API для сценариев браузерного теста. */
async function setup(page, { width = 1440, initial = [], mechanic = false, report = true, failUpload = false } = {}) {
  await page.setViewportSize({ width, height: 1000 })
  await page.clock.setFixedTime(new Date('2026-10-11T14:00:00+06:00'))
  await page.addInitScript(({ report }) => {
    localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({ sub: 'worker', role: 'user' }))}.test`)
    localStorage.setItem('myapp.userRole', 'User')
    localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.auxiliary_works', ...(report ? ['menu.vehicles', 'vehicles.report'] : [])]))
  }, { report })
  const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))
  const works = structuredClone(initial), requests = []
  let photoUploads = 0
  await page.route('**/api/**', route => {
    const request = route.request(), path = new URL(request.url()).pathname
    if (request.method() === 'POST') requests.push({ path, body: request.headers()['content-type']?.includes('application/json') && request.postData() ? request.postDataJSON() : null })
    if (path === '/api/auxiliary-works' && request.method() === 'POST') {
      const payload = request.postDataJSON()
      works.push({ ...payload, id: 'a-new', workKind: 'auxiliary', createdAt: payload.performedAt, completedAt: payload.performedAt, photos: [], videos: [], participants: [{ ...confirmed, status: 'pending', reviewedBy: null }] })
      return route.fulfill({ status: 201, json: { id: 'a-new' } })
    }
    if (path === '/api/auxiliary-works') return route.fulfill({ json: works })
    if (path.endsWith('/a-new/photos') && request.method() === 'POST') {
      photoUploads++
      if (failUpload && photoUploads === 1) return route.fulfill({ status: 500, json: { message: 'Ошибка загрузки фото.' } })
      works.at(-1).photos.push({ id: 'p1', fileName: 'Фото.webp' })
      return route.fulfill({ json: { id: 'p1' } })
    }
    if (path.endsWith('/a-new/videos') && request.method() === 'POST') {
      works.at(-1).videos.push({ id: 'video1', fileName: 'Видео.webm' })
      return route.fulfill({ json: { id: 'video1' } })
    }
    if (path.endsWith('/review')) {
      const participant = works[0].participants[0]
      participant.status = request.postDataJSON().approved ? 'approved' : 'rejected'
      participant.reviewedBy = 'mechanic'
      return route.fulfill({ json: {} })
    }
    if (path.endsWith('/participants') && request.method() === 'POST') {
      works[0].participants.push({ id: 'other', userId: 'worker', name: 'Другой работник', position: 'Разнорабочий', status: 'pending' })
      return route.fulfill({ json: {} })
    }
    if (path.endsWith('/participants')) return route.fulfill({ json: works[0].participants })
    if (path === '/api/auxiliary-works/photos/p1') return route.fulfill({ body: photo, contentType: 'image/webp' })
    if (path === '/api/auxiliary-works/videos/video1/stream-ticket') return route.fulfill({ json: { url: '/api/auxiliary-works/video-stream/test-ticket' } })
    if (path.includes('/video-stream/')) return route.fulfill({ status: 404 })
    if (path === '/api/vehicles/repair-journal' || path === '/api/vehicles') return route.fulfill({ json: [] })
    if (path === '/api/profile') return route.fulfill({ json: { id: 'worker', position: mechanic ? 'Механик' : 'Разнорабочий', hasAvatar: false } })
    return route.fulfill({ status: path.endsWith('/avatar') ? 404 : 200, json: [] })
  })
  return { works, requests }
}
/** Открывает меню навигации в браузерном тесте. */
async function openMenu(page, width) {
  if (width < 761) await page.getByRole('button', { name: 'Открыть меню' }).click()
}
/** Открывает раздел вспомогательных работ в браузерном тесте. */
async function openAuxiliary(page, width) {
  await page.goto('/')
  await openMenu(page, width)
  await page.getByRole('button', { name: 'Вспомогательные работы', exact: true }).click()
}

for (const width of [390, 1440]) {
  test(`employee records auxiliary work with photo and video at ${width}px`, async ({ page }) => {
    const { requests } = await setup(page, { width })
    await openAuxiliary(page, width)
    await page.getByLabel('Название работы', { exact: true }).fill('Уборка территории')
    await page.getByLabel('Место работ', { exact: true }).selectOption('РММ')
    await page.getByLabel('Выполненные работы', { exact: true }).fill('Собран мусор и очищены проходы.')
    await page.locator('.auxiliary-upload input').setInputFiles([
      { name: 'Фото.webp', mimeType: 'image/webp', buffer: await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url)) },
      { name: 'Работы.webm', mimeType: 'video/webm', buffer: Buffer.from([0x1a, 0x45, 0xdf, 0xa3, 0, 0, 0, 0]) },
    ])
    await expect(page.locator('.auxiliary-files li')).toHaveCount(2)
    await page.getByRole('button', { name: 'Сохранить работу' }).click()
    await expect(page.locator('.auxiliary-success')).toContainText('Работа сохранена')
    await expect(page.locator('.auxiliary-card')).toHaveCount(1)
    await expect(page.locator('.auxiliary-card')).toContainText('Ожидает подтверждения')
    expect(requests.map(item => item.path)).toEqual(['/api/auxiliary-works', '/api/auxiliary-works/a-new/photos', '/api/auxiliary-works/a-new/videos'])
    expect(requests[0].body).not.toHaveProperty('vehicleId')
    expect(requests[0].body.description).toBe('Собран мусор и очищены проходы.')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: `test-results/auxiliary-works-${width}.png`, fullPage: true })
    await openMenu(page, width)
    await page.getByRole('button', { name: 'Транспорт', exact: true }).click()
    await page.getByRole('button', { name: 'Отчёты', exact: true }).click()
    await page.getByRole('button', { name: 'Отчёт за смену', exact: true }).click()
    await expect(page.locator('.shift-report__paper')).toContainText('Вспомогательные работы')
    await expect(page.locator('.shift-report__paper')).toContainText('Уборка территории')
    await expect(page.locator('.shift-report__state')).toHaveCount(0)
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
    await expect(page.getByRole('button', { name: 'Скачать PDF' })).toBeEnabled()
  })
}

test('auxiliary-only report generates a PDF and its photo link opens the auxiliary viewer', async ({ page }) => {
  await setup(page, { initial: [sample] })
  await page.goto('/?reportWork=unused&reportDate=2026-10-11&reportShift=1')
  await expect(page.locator('.shift-report__paper')).toContainText('Разнорабочий · Петров Пётр Петрович')
  await expect(page.locator('.shift-report__photos img')).toBeVisible()
  const downloadEvent = page.waitForEvent('download')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
  await page.getByRole('button', { name: 'Скачать PDF' }).click()
  const download = await downloadEvent
  await download.saveAs('test-results/auxiliary-report.pdf')
  const document = await PDFDocument.load(await readFile(await download.path()))
  const urls = document.getPages().flatMap(sheet => {
    const annotations = sheet.node.get(PDFName.of('Annots'))
    return annotations ? annotations.asArray().map(ref => document.context.lookup(ref).get(PDFName.of('A')).get(PDFName.of('URI')).decodeText()) : []
  })
  expect(urls.some(url => url.includes('reportSource=auxiliary'))).toBe(true)
  const photoLink = urls.find(url => url.includes('mediaId=p1'))
  await page.goto(photoLink)
  await expect(page.locator('h1')).toHaveText('Вспомогательные работы')
  await expect(page.locator('.repair-media-dialog')).toBeVisible()
  await expect(page.locator('.repair-media-dialog img')).toBeVisible()
  await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
  await page.getByRole('button', { name: 'Видео · 1' }).click()
  await expect(page.locator('.repair-media-dialog video')).toHaveAttribute('src', '/api/auxiliary-works/video-stream/test-ticket')
})

test('failed media upload retries without creating duplicate work', async ({ page }) => {
  const { requests } = await setup(page, { failUpload: true })
  await openAuxiliary(page, 1440)
  await page.getByLabel('Название работы', { exact: true }).fill('Уборка')
  await page.getByLabel('Место работ', { exact: true }).selectOption('РММ')
  await page.getByLabel('Выполненные работы', { exact: true }).fill('Убран мусор')
  await page.locator('.auxiliary-upload input').setInputFiles(fileURLToPath(new URL('./fixtures/repair-cylinder.webp', import.meta.url)))
  await page.getByRole('button', { name: 'Сохранить работу' }).click()
  await expect(page.getByRole('alert')).toContainText('Запись сохранена')
  await page.getByRole('button', { name: 'Продолжить загрузку' }).click()
  await expect(page.locator('.auxiliary-success')).toContainText('Работа сохранена')
  expect(requests.filter(item => item.path === '/api/auxiliary-works')).toHaveLength(1)
  expect(requests.filter(item => item.path.endsWith('/photos'))).toHaveLength(2)
})

test('mechanic confirms auxiliary performers using the separate endpoint', async ({ page }) => {
  const work = { ...sample, participants: [{ ...confirmed, status: 'pending', reviewedBy: null }] }
  const { requests } = await setup(page, { initial: [work], mechanic: true })
  await openAuxiliary(page, 1440)
  await page.getByRole('button', { name: 'Подтвердить участие: Петров Пётр Петрович' }).click()
  await expect(page.locator('.work-participants__status--approved')).toHaveText('Подтверждено')
  expect(requests[0]).toEqual({ path: '/api/auxiliary-works/a1/participants/participant/review', body: { approved: true } })
})

test('auxiliary menu can be granted without access to transport', async ({ page }) => {
  await setup(page, { report: false })
  await openAuxiliary(page, 1440)
  await expect(page.getByRole('button', { name: 'Транспорт', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Сохранить работу' })).toBeVisible()
})

test('overnight auxiliary works use the same shift boundaries', () => {
  const works = [
    { ...sample, id: 'before', completedAt: '2026-10-11T19:59:59' },
    { ...sample, id: 'start', completedAt: '2026-10-11T20:00:00' },
    { ...sample, id: 'midnight', completedAt: '2026-10-12T00:30:00' },
    { ...sample, id: 'end', completedAt: '2026-10-12T08:00:00' },
  ]
  const selected = selectAuxiliaryWorks(works, '2026-10-11', '2')
  expect(selected.map(work => work.id)).toEqual(['start', 'midnight'])
  expect(groupAuxiliaryWorks(selected)[0].categories[0].cards).toHaveLength(2)
})

 test('repair and auxiliary work appear together in the same PDF', async ({ page }) => {
  await setup(page, { initial: [{ ...sample, photos: [] }] })
  const photo = await readFile(new URL('./fixtures/repair-cylinder.webp', import.meta.url))
  await page.route('**/api/vehicles/repair-journal*', route => route.fulfill({ json: [{
    vehicle: { id: 'v1', modelName: 'SANY SY1250H', garageNumber: 94, typeName: 'Экскаваторы' },
    works: [{ id: 'repair', description: 'Замена гидроцилиндра.', location: 'КАРЬЕР', workKind: 'repair',
      completedAt: '2026-10-11T09:00:00+06:00', operationalState: 'operating', participants: [{ ...confirmed, position: 'Слесарь' }],
      photos: [{ id: 'repair-photo', fileName: 'Гидроцилиндр.webp' }] }],
  }] }))
  await page.route('**/api/vehicles/work-photos/repair-photo', route => route.fulfill({ body: photo, contentType: 'image/webp' }))
  await page.goto('/?reportWork=repair&reportDate=2026-10-11&reportShift=1')
  await page.getByRole('button', { name: 'Закрыть', exact: true }).click()
  await expect(page.locator('.shift-report__location h3')).toHaveText(['КАРЬЕР', 'РММ'])
  await expect(page.locator('.shift-report__paper')).toContainText('Замена гидроцилиндра.')
  await expect(page.locator('.shift-report__paper')).toContainText('Уборка территории')
  const downloaded = page.waitForEvent('download')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
  await page.getByRole('button', { name: 'Скачать PDF' }).click()
  await (await downloaded).saveAs('test-results/combined-shift-report.pdf')
})

 test('long auxiliary descriptions and titles wrap across PDF pages', async ({ page }) => {
  await setup(page, { initial: [{ ...sample, title: 'Подготовка территории '.repeat(7), location: 'РММ', description: 'Площадка очищена, материалы отсортированы и перенесены. '.repeat(65), photos: [], videos: [] }] })
  await page.goto('/?reportWork=unused&reportDate=2026-10-11&reportShift=1')
  const downloaded = page.waitForEvent('download')
  if(await page.getByLabel('Меню отчёта за смену',{exact:true}).getAttribute('aria-expanded') !== 'true') await page.getByLabel('Меню отчёта за смену',{exact:true}).click()
  await page.getByRole('button', { name: 'Скачать PDF' }).click()
  const file = await downloaded
  const document = await PDFDocument.load(await readFile(await file.path()))
  expect(document.getPageCount()).toBeGreaterThan(1)
})
