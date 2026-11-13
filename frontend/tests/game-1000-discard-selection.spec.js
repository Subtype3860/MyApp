import { test, expect } from '@playwright/test'

// На сносе выбор третьей карты заменяет самую раннюю, а не показывает ошибку.
test('discard selection keeps exactly two cards without an error', async ({ page }) => {
  await page.goto('/games/1000/index.html', { waitUntil: 'domcontentloaded' })
  await page.evaluate(() => {
    const app = window.__THOUSAND__
    app.startGame()
    app.clearTimer()
    const s = app.state
    s.phase = 'discard'
    s.bidder = 0
    s.turn = null
    s.trick = []
    s.trickComplete = false
    s.pendingMarriage = null
    // Имитируем забранный прикуп: на руке 12 карт.
    s.hands[0] = [...s.hands[0], 'H9', 'H8', 'H7']
    s.talon = []
    app.ui.render(s)
  })
  const cards = await page.evaluate(() =>
    [...document.querySelectorAll('#hand .hand-card')].map(el => el.dataset.card))
  expect(cards.length).toBeGreaterThanOrEqual(10)
  const [first, second, third] = cards
  for (const card of [first, second, third]) {
    await page.locator(`#hand .hand-card[data-card="${card}"]`).click()
  }
  const selected = await page.evaluate(() => window.__THOUSAND__.ui.selected.slice())
  expect(selected).toEqual([second, third])
  const toast = await page.locator('#toastRoot').innerText().catch(() => '')
  expect(toast).not.toContain('ровно две')
})
