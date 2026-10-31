import { test, expect } from '@playwright/test'

test('complete a shift, earn the bonus and retain progress', async ({ page }) => {
  await page.clock.install()
  await page.goto('/games/mechanic/index.html')
  const systems = ['Электрика', 'Охлаждение', 'Тормоза', 'Топливная система', 'Электрика']
  for (let order = 0; order < 5; order++) {
    await page.getByRole('button', { name: 'Принять заказ' }).click()
    await page.getByRole('button', { name: systems[order] }).click()
    await page.getByRole('button', { name: 'Купить:' }).click()
    // Freeze time after advancing the visible pointer into its target zone.
    for (let hit = 0; hit < 3; hit++) {
      await page.clock.runFor(1200)
      await page.getByRole('button', { name: 'Зафиксировать' }).click()
    }
    await page.getByRole('button', { name: 'Выдать машину' }).click()
  }
  await expect(page.locator('#completed')).toHaveText('5')
  await expect(page.locator('#goal-title')).toContainText('Смена удалась')
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('smena.mechanic.v1')))
  expect(saved.money).toBe(4600)
  expect(saved.goalPaid).toBe(true)
  await page.reload()
  await expect(page.locator('#completed')).toHaveText('5')
  await page.getByRole('button', { name: 'Улучшить инструмент' }).click()
  await expect(page.locator('#upgrade')).toContainText('установлен')
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('smena.mechanic.v1')).money)).toBe(3900)
})

test('wrong diagnosis, missed repair and reload preserve the current order', async ({ page }) => {
  await page.clock.install()
  await page.goto('/games/mechanic/index.html')
  await page.getByRole('button', { name: 'Принять заказ' }).click()
  await page.getByRole('button', { name: 'Тормоза' }).click()
  await expect(page.getByRole('button', { name: 'Тормоза' })).toBeDisabled()
  await page.getByRole('button', { name: 'Электрика' }).click()
  await page.getByRole('button', { name: 'Купить:' }).click()
  await page.getByRole('button', { name: 'Зафиксировать' }).click()
  await page.reload()
  const saved = await page.evaluate(() => JSON.parse(localStorage.getItem('smena.mechanic.v1')))
  expect(saved.money).toBe(420)
  expect(saved.job.misses).toBe(1)
  expect(saved.job.time).toBe(50)
  await expect(page.getByRole('button', { name: 'Зафиксировать' })).toBeVisible()
  await page.getByRole('button', { name: 'Новая игра', exact: true }).click()
  await page.getByRole('button', { name: 'Продолжить игру' }).click()
  await expect(page.getByRole('button', { name: 'Зафиксировать' })).toBeVisible()
  await page.getByRole('button', { name: 'Новая игра', exact: true }).click()
  await page.getByRole('button', { name: 'Начать заново' }).click()
  await expect(page.locator('#completed')).toHaveText('0')
  await expect(page.getByRole('button', { name: 'Принять заказ' })).toBeVisible()
})

test('mobile layout fits and corrupted saves recover safely', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => localStorage.setItem('smena.mechanic.v1', '{broken'))
  await page.goto('/games/mechanic/index.html')
  await expect(page.getByRole('button', { name: 'Принять заказ' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  await page.getByRole('button', { name: 'Принять заказ' }).click()
  await expect(page.getByRole('button', { name: 'Электрика' })).toBeVisible()
})
