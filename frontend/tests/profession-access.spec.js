import { test, expect } from '@playwright/test'
import { pageBlockVisible, reportPresentation } from '../src/utils/pageVisibility.js'
const token = (role, permission) => `test.${Buffer.from(JSON.stringify({ role, permission })).toString('base64url')}.test`
test('presentation hides media and names, administrators retain all blocks', () => {
  const restricted = token('user', ['ui.hidden.shift_report.media', 'ui.hidden.shift_report.performers'])
  expect(pageBlockVisible(restricted, 'shift_report.media')).toBe(false)
  expect(pageBlockVisible(token('administrator', ['ui.hidden.shift_report.media']), 'shift_report.media')).toBe(true)
  const groups = [{ categories: [{ cards: [{ works: [{ photos: [{ id: 'p' }], videos: [{ id: 'v' }] }] }] }] }]
  const card = reportPresentation(groups, restricted)[0].categories[0].cards[0]
  expect(card.hidePerformers).toBe(true)
  expect(card.works[0].photos).toEqual([])
  expect(card.works[0].videos).toEqual([])
  expect(groups[0].categories[0].cards[0].works[0].photos).toHaveLength(1)
})
for (const width of [390, 1440]) test(`administrator applies profession policy at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 1000 })
  let body
  await page.route('**/api/**', route => {
    if (route.request().method() === 'PUT') {
      body = route.request().postDataJSON()
      return route.fulfill({ json: { message: 'Настройки сохранены' } })
    }
    return route.fulfill({ json: { professions: [
      { id: 'p1', name: 'Механик', revision: 2, permissions: ['reports.shift'], hiddenBlocks: [] },
      { id: 'p2', name: 'Слесарь', revision: 0, permissions: null, hiddenBlocks: [] },
    ], permissions: [{ key: 'menu.reports', label: 'Отчёты', children: [{ key: 'reports.shift', label: 'Отчёт за смену' }] }],
    blocks: [{ key: 'shift_report.media', label: 'Отчёт за смену: фото и видео' }] } })
  })
  await page.goto('/')
  await page.evaluate(async () => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Settings } = await import('/src/components/ProfessionAccessSettings.vue')
    document.body.innerHTML = '<div id="policy-test"></div>'
    createApp({ render: () => h(Settings, { token: 'test' }) }).mount('#policy-test')
  })
  await page.getByRole('button', { name: 'Настроить Механик', exact: true }).click()
  await page.getByLabel('Слесарь', { exact: true }).check()
  await page.getByLabel('Отчёт за смену: фото и видео', { exact: true }).uncheck()
  await page.getByRole('button', { name: 'Применить к выбранным профессиям' }).click()
  await expect(page.getByRole('status')).toHaveText('Настройки сохранены')
  expect(body).toEqual({ professions: [{ id: 'p1', revision: 2 }, { id: 'p2', revision: 0 }], permissions: ['reports.shift'], hiddenBlocks: ['shift_report.media'] })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
