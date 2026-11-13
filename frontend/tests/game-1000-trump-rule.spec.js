import { test, expect } from '@playwright/test'

// Правило: перебивать козырем не обязательно, если козырь уже на столе.
test('a trump on the table allows any discard when void in the led suit', async ({ page }) => {
  await page.goto('/games/1000/index.html', { waitUntil: 'domcontentloaded' })
  const result = await page.evaluate(() => {
    const E = window.ThousandEngine
    const base = {
      phase: 'playing', turn: 0, trickComplete: false, trump: 'H', pendingMarriage: null,
      hands: [['S9', 'DK', 'H9', 'H8']],
    }
    return {
      // Заход трефой, козыря на столе нет: обязан играть козырем.
      needTrump: E.legalCards({ ...base, trick: [{ player: 1, card: 'C7' }, { player: 2, card: 'D7' }] }, 0),
      // Козырь уже на столе: можно сбросить любую карту.
      trumpOnTable: E.legalCards({ ...base, trick: [{ player: 1, card: 'C7' }, { player: 2, card: 'H10' }] }, 0),
      // Есть заходная масть: отвечаем в масть.
      followSuit: E.legalCards({ ...base, hands: [['S9', 'S7', 'H9']], trick: [{ player: 1, card: 'S10' }] }, 0),
    }
  })
  expect(result.needTrump.sort()).toEqual(['H8', 'H9'])
  expect(result.trumpOnTable.sort()).toEqual(['DK', 'H8', 'H9', 'S9'])
  expect(result.followSuit.sort()).toEqual(['S7', 'S9'])
})
