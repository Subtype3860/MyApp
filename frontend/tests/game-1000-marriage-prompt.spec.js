import { test, expect } from '@playwright/test'

// Заход королём при необъявленном марьяже спрашивает подтверждение и объявляет марьяж.
test('leading with a king offers to announce the marriage', async ({ page }) => {
  await page.goto('/games/1000/index.html', { waitUntil: 'domcontentloaded' })
  await page.evaluate(() => {
    const app = window.__THOUSAND__
    app.startGame()
    app.clearTimer()
    const s = app.state
    s.phase = 'playing'
    s.turn = 0
    s.trick = []
    s.trickComplete = false
    s.trickNumber = 2
    s.pendingMarriage = null
    s.trump = null
    s.declared = [[], [], []]
    s.marriages = []
    s.hands = [['HK', 'HQ', 'S9', 'C9', 'D9'], ['S8', 'S7'], ['C8', 'C7']]
    app.ui.render(s)
  })
  await page.locator('#hand .hand-card[data-card="HK"]').click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: /Объявить/ }).click()
  const result = await page.evaluate(() => {
    const s = window.__THOUSAND__.state
    return { trump: s.trump, marriages: s.marriages, pending: s.pendingMarriage, trick: s.trick.length }
  })
  expect(result.trump).toBe('H')
  expect(result.marriages.length).toBe(1)
  expect(result.pending).toBeNull()
  expect(result.trick).toBe(1)
})

test('declining the marriage prompt plays the king without a marriage', async ({ page }) => {
  await page.goto('/games/1000/index.html', { waitUntil: 'domcontentloaded' })
  await page.evaluate(() => {
    const app = window.__THOUSAND__
    app.startGame()
    app.clearTimer()
    const s = app.state
    s.phase = 'playing'
    s.turn = 0
    s.trick = []
    s.trickComplete = false
    s.trickNumber = 2
    s.pendingMarriage = null
    s.trump = null
    s.declared = [[], [], []]
    s.marriages = []
    s.hands = [['HK', 'HQ', 'S9', 'C9', 'D9'], ['S8', 'S7'], ['C8', 'C7']]
    app.ui.render(s)
  })
  await page.locator('#hand .hand-card[data-card="HK"]').click()
  await page.getByRole('dialog').getByRole('button', { name: 'Без марьяжа' }).click()
  const result = await page.evaluate(() => {
    const s = window.__THOUSAND__.state
    return { trump: s.trump, marriages: s.marriages, trick: s.trick.length }
  })
  expect(result.trump).toBeNull()
  expect(result.marriages.length).toBe(0)
  expect(result.trick).toBe(1)
})
