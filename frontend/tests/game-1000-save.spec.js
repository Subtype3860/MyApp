import { test, expect } from '@playwright/test'

// Офлайн-партия сохраняется и продолжается после перезагрузки страницы.
test('offline game can be saved and resumed', async ({ page }) => {
  await page.goto('/games/1000/index.html', { waitUntil: 'domcontentloaded' })
  const seed = await page.evaluate(() => {
    const app = window.__THOUSAND__
    app.startGame()
    app.clearTimer()
    return app.state.seed
  })
  const saved = await page.evaluate(() => {
    const raw = JSON.parse(localStorage.getItem('thousand.save') || 'null')
    return raw ? { version: raw.v, seed: raw.state && raw.state.seed } : null
  })
  expect(saved).not.toBeNull()
  expect(saved.version).toBe(1)
  expect(saved.seed).toBe(seed)

  await page.reload({ waitUntil: 'domcontentloaded' })
  const resume = page.locator('#resumeBtn')
  await expect(resume).toBeVisible()
  await resume.click()
  const restored = await page.evaluate(() => {
    const app = window.__THOUSAND__
    return app.state ? { seed: app.state.seed, phase: app.state.phase, hand: app.state.hands[0].length } : null
  })
  expect(restored).not.toBeNull()
  expect(restored.seed).toBe(seed)
  expect(restored.hand).toBeGreaterThan(0)
  await expect(page.locator('#gameScreen')).toBeVisible()
})
