import { test, expect } from '@playwright/test'
import { TankMotion } from '../public/games/tanks/motion.js'
import { LocalTankPrediction } from '../public/games/tanks/prediction.js'
import { readFile } from 'node:fs/promises'

const url = '/games/tanks/index.html'
const player = { id: 'synthetic-one', name: 'Игрок 1', x: 0, z: 0, yaw: 0, turret: 0, hp: 100, score: 25, kills: 0, collected: 1, skills: { speed: 15 }, respawnIn: 0 }
const snapshot = { type: 'state', time: 1, players: [player, { ...player, id: 'synthetic-two', name: 'Игрок 2', x: 8, skills: {} }], pickups: [], bullets: [] }

test('desktop renders input immediately without waiting for the next server snapshot', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-27T00:00:00Z') })
  await page.clock.pauseAt(new Date('2026-10-27T00:00:01Z'))
  await page.addInitScript(() => localStorage.setItem('myapp.authToken', 'synthetic-token'))
  await page.route('**/api/tanks/ticket', route => route.fulfill({ json: { ticket: 'synthetic-ticket' } }))
  await page.route('**/games/tanks/render.js', async route => {
    const source = await readFile(new URL('../public/games/tanks/render.js', import.meta.url), 'utf8')
    await route.fulfill({ contentType: 'text/javascript', body: source.replace('return {render,groundPoint};', 'return {render(world,...args){globalThis.__tankX=world.player.x;return render(world,...args);},groundPoint};') })
  })
  await page.routeWebSocket('**/api/tanks/socket', ws => ws.onMessage(message => {
    if (JSON.parse(message).ticket) {
      ws.send(JSON.stringify({ type: 'joined', selfId: player.id, capacity: 8 }))
      ws.send(JSON.stringify({ ...snapshot, players: [{ ...player, skills: {}, ackSeq: 0, spawnId: 1 }] }))
    }
    // Deliberately withhold subsequent snapshots: movement must still start locally.
  }))
  await page.goto(url)
  await page.locator('#start').click()
  await page.clock.runFor(50)
  expect(await page.evaluate(() => globalThis.__tankX)).toBe(0)
  await page.keyboard.down('KeyD')
  await page.clock.runFor(32)
  expect(await page.evaluate(() => globalThis.__tankX)).toBeGreaterThan(0)
  await page.keyboard.up('KeyD')
})

test('own tank responds before the delayed server reply and preserves server-owned values', () => {
  const prediction = new LocalTankPrediction()
  const authoritative = { ...player, skills: {}, ackSeq: 0, spawnId: 1 }
  prediction.accept(authoritative, [authoritative], 0)
  prediction.record(1, { x: 1, z: 0 }, 0)
  const immediate = prediction.advance(0.016, { x: 1, z: 0, aim: null }, 16)
  expect(immediate.x).toBeCloseTo(0.096)
  expect(authoritative.x).toBe(0)
  for (let now = 32; now <= 240; now += 16) prediction.advance(0.016, { x: 1, z: 0, aim: null }, now)
  const reply = { ...authoritative, x: 0.3, ackSeq: 1, hp: 60, score: 125 }
  prediction.accept(reply, [reply], 240)
  const corrected = prediction.advance(0.016, { x: 0, z: 0, aim: null }, 256)
  expect(corrected.hp).toBe(60)
  expect(corrected.score).toBe(125)
  expect(prediction.pending).toHaveLength(0)
})

test('prediction respects obstacles, stops when snapshots freeze and resets on respawn', () => {
  const prediction = new LocalTankPrediction()
  const authoritative = { ...player, x: 0, z: 16, skills: {}, ackSeq: 0, spawnId: 1 }
  prediction.accept(authoritative, [authoritative], 0)
  for (let now = 0; now <= 240; now += 16) prediction.advance(0.016, { x: 0, z: 1, aim: null }, now)
  const blocked = prediction.advance(0.016, { x: 0, z: 1, aim: null }, 256)
  expect(blocked.z).toBeLessThan(16.16)
  const frozen = prediction.advance(0.05, { x: 1, z: 0, aim: null }, 1000)
  expect(frozen.x).toBe(blocked.x)
  const respawn = { ...authoritative, x: -40, z: 0, hp: 100, spawnId: 2 }
  prediction.accept(respawn, [respawn], 1100)
  expect(prediction.advance(0, { x: 0, z: 0, aim: null }, 1100).x).toBe(-40)
  prediction.accept(player, [player], 1200)
  expect(prediction.advance(0.016, { x: 1, z: 0, aim: null }, 1216)).toBeNull()
})

test('network movement is smooth between packets and cannot change server health or score', () => {
  const motion = new TankMotion()
  motion.push(snapshot, 0)
  const next = { ...snapshot, time: 1.05, players: [{ ...player, x: 1, hp: 60, score: 125 }], bullets: [{ x: 0, z: 0, vx: 25, vz: 0 }] }
  motion.push(next, 50)
  const middle = motion.sample(75)
  expect(middle.players[0].x).toBeCloseTo(0.5)
  expect(middle.players[0].hp).toBe(60)
  expect(middle.players[0].score).toBe(125)
  expect(motion.sample(100).players[0].x).toBe(1)
  expect(motion.sample(1000).bullets[0].x).toBe(1.25)
  expect(next.players[0].x).toBe(1)
})

test('spawn, death and large corrections do not slide through obstacles', () => {
  const motion = new TankMotion()
  motion.push(snapshot, 0)
  motion.push({ ...snapshot, players: [{ ...player, x: 40, z: 40 }] }, 50)
  expect(motion.sample(51).players[0].x).toBe(40)
  motion.push({ ...snapshot, players: [{ ...player, x: 41, hp: 0 }] }, 100)
  expect(motion.sample(101).players[0].hp).toBe(0)
  motion.push({ ...snapshot, players: [{ ...player, x: 42, hp: 100 }] }, 150)
  expect(motion.sample(151).players[0].x).toBe(42)
  motion.clear()
  expect(motion.sample(200)).toBeNull()
})

test('no account cannot enter an online arena', async ({ page }) => {
  await page.goto(url)
  await page.locator('#start').click()
  await expect(page.locator('#menu-description')).toContainText('Войдите в Atlas')
  await expect(page.locator('#overlay')).toBeVisible()
})

test('admission and API errors never start a local simulation', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('myapp.authToken', 'synthetic-token'))
  let status = 403
  await page.route('**/api/tanks/ticket', route => route.fulfill({ status, json: {} }))
  await page.goto(url)
  await page.locator('#start').click()
  await expect(page.locator('#menu-description')).toContainText('Допуск к игре выдаёт администратор')
  status = 401
  await page.locator('#start').click()
  await expect(page.locator('#menu-description')).toContainText('Сессия завершена')
  status = 503
  await page.locator('#start').click()
  await expect(page.locator('#menu-description')).toContainText('сервер недоступен')
  await expect(page.locator('#overlay')).toBeVisible()
})

test('server snapshots display peers and skills; access revocation stops play', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('myapp.authToken', 'synthetic-token'))
  await page.route('**/api/tanks/ticket', route => route.fulfill({ json: { ticket: 'synthetic-one-use-ticket' } }))
  let socket
  const inputs = []
  await page.routeWebSocket('**/api/tanks/socket', ws => {
    socket = ws
    ws.onMessage(message => {
      const input = JSON.parse(message)
      if (input.ticket) {
        ws.send(JSON.stringify({ type: 'joined', selfId: player.id, capacity: 8 }))
        ws.send(JSON.stringify(snapshot))
      } else inputs.push(input)
    })
  })
  await page.goto(url)
  await page.locator('#start').click()
  await expect(page.locator('#overlay')).toBeHidden()
  await expect(page.locator('.online-board')).toContainText('Игрок 2')
  await expect(page.locator('#skills')).toContainText('Турбодвигатель')
  await page.keyboard.down('KeyD')
  await expect.poll(() => inputs.some(input => input.x === 1)).toBe(true)
  await page.keyboard.up('KeyD')
  expect(inputs.every(input => !('hp' in input) && !('score' in input))).toBe(true)
  await socket.close({ code: 4403, reason: 'access_revoked' })
  await expect(page.locator('#overlay')).toBeVisible()
  await expect(page.locator('#menu-description')).toContainText('отозвал допуск')
  await expect(page.locator('#hp-text')).toHaveText('Нет связи')
  await expect(page.locator('.online-board')).toBeEmpty()
})

test('a stale connection clears the arena and requires manual reconnect', async ({ page }) => {
  await page.clock.install()
  await page.addInitScript(() => localStorage.setItem('myapp.authToken', 'synthetic-token'))
  await page.route('**/api/tanks/ticket', route => route.fulfill({ json: { ticket: 'synthetic-ticket' } }))
  await page.routeWebSocket('**/api/tanks/socket', ws => ws.onMessage(message => {
    if (JSON.parse(message).ticket) {
      ws.send(JSON.stringify({ type: 'joined', selfId: player.id, capacity: 8 }))
      ws.send(JSON.stringify(snapshot))
    }
  }))
  await page.goto(url)
  await page.locator('#start').click()
  await expect(page.locator('#overlay')).toBeHidden()
  await page.clock.runFor(4500)
  await expect(page.locator('#menu-description')).toContainText('Сервер перестал отвечать')
  await expect(page.locator('#hp-text')).toHaveText('Нет связи')
  await expect(page.locator('#skills')).toBeEmpty()
})
