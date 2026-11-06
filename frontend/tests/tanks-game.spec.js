import { test, expect } from '@playwright/test'
import { createWorld, stepWorld, applyPickup, collides } from '../public/games/tanks/world.js'
import { HALF_SIZE, OBSTACLES } from '../public/games/tanks/world.js'

test('expanded map has reachable outer ground and solid obstacles', () => {
  expect(HALF_SIZE).toBe(64)
  expect(OBSTACLES).toHaveLength(40)
  expect(collides(32, 0)).toBe(false)
  expect(collides(64, 0)).toBe(true)
  for (const obstacle of OBSTACLES) expect(collides(obstacle.x, obstacle.z)).toBe(true)
  const w = createWorld(() => 0.5)
  expect(w.pickups.some(p => Math.abs(p.x) > 32 || Math.abs(p.z) > 32)).toBe(true)
  for (const pickup of w.pickups) expect(collides(pickup.x, pickup.z, 1.5)).toBe(false)
})

test('driving collects a timed skill and refreshes its duration', () => {
  const w = createWorld(() => 0.5)
  for (let i = 0; i < 15; i++) stepWorld(w, 0.05, { x: 1 })
  expect(w.collected).toBe(1)
  expect(w.player.skills.speed).toBeGreaterThan(17)
  expect(w.score).toBe(25)
  applyPickup(w, 'speed')
  expect(w.player.skills.speed).toBe(18)
  w.player.skills.speed = 0.01
  stepWorld(w, 0.05)
  expect(w.player.skills.speed).toBeUndefined()
})

test('repair caps health, shield reduces incoming damage and walls block movement', () => {
  const w = createWorld(() => 0.5)
  w.player.hp = 90
  applyPickup(w, 'repair')
  expect(w.player.hp).toBe(100)
  applyPickup(w, 'shield')
  w.bullets.push({ x: 0, z: 0, vx: 0, vz: 0, life: 1, enemy: true, damage: 20 })
  stepWorld(w, 0.01)
  expect(w.player.hp).toBe(96)
  w.player.x = 62
  for (let i = 0; i < 20; i++) stepWorld(w, 0.05, { x: 1 })
  expect(w.player.x).toBeLessThanOrEqual(62.15)
  expect(collides(-12, -9)).toBe(true)
})

test('double damage and double shot work, victory and defeat end simulation', () => {
  const w = createWorld(() => 0.5)
  applyPickup(w, 'damage')
  applyPickup(w, 'double')
  stepWorld(w, 0.01, { aim: { x: 0, z: -10 }, fire: true })
  expect(w.bullets).toHaveLength(2)
  expect(w.bullets.every(b => b.damage === 40)).toBe(true)
  w.enemies = []
  w.wave = 5
  w.waveDelay = 0
  stepWorld(w, 0.01)
  expect(w.status).toBe('won')
  const score = w.score
  stepWorld(w, 0.05, { x: 1 })
  expect(w.score).toBe(score)
  const lost = createWorld()
  lost.player.hp = 0
  stepWorld(lost, 0.01)
  expect(lost.status).toBe('lost')
})

test('shells destroy enemy tanks and award points only once', () => {
  const w = createWorld(() => 0.5)
  w.enemies = [{ id: 100, x: 0, z: -5, yaw: 0, turret: 0, hp: 40, cooldown: 100 }]
  for (let i = 0; i < 30; i++) stepWorld(w, 0.05, { aim: { x: 0, z: -5 }, fire: true })
  expect(w.kills).toBe(1)
  expect(w.score).toBe(100)
  expect(w.enemies).toHaveLength(0)
})

test('3D arena starts, driving collects a skill and pause freezes the battle', async ({ page }) => {
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.clock.install()
  await page.goto('/games/tanks/index.html?mode=solo')
  await expect(page.locator('#unsupported')).toBeHidden()
  await page.locator('#start').click()
  await page.keyboard.down('KeyD')
  await page.clock.runFor(750)
  await page.keyboard.up('KeyD')
  await expect(page.locator('#collected')).toHaveText('1')
  await expect(page.locator('#skills')).toContainText('Турбодвигатель')
  await page.keyboard.press('KeyP')
  await expect(page.locator('#overlay')).toBeVisible()
  const health = await page.locator('#hp-text').textContent()
  await page.clock.runFor(5000)
  await expect(page.locator('#hp-text')).toHaveText(health)
  await page.locator('#start').click()
  await expect(page.locator('#overlay')).toBeHidden()
  expect(errors).toEqual([])
})

test('phone controls move the tank and release cleanly', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.clock.install()
  await page.goto('/games/tanks/index.html?mode=solo')
  await page.locator('#start').click()
  await expect(page.locator('#stick')).toBeVisible()
  const pad = await page.locator('#stick').boundingBox()
  await page.mouse.move(pad.x + pad.width - 10, pad.y + pad.height / 2)
  await page.mouse.down()
  await page.clock.runFor(750)
  await page.mouse.up()
  await expect(page.locator('#collected')).toHaveText('1')
  await expect(page.locator('#stick-thumb')).toHaveAttribute('style', '')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})
