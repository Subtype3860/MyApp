import { test, expect } from '@playwright/test'

const url = '/games/tanks/index.html'
const player = { id: 'synthetic-one', name: 'Игрок 1', x: 0, z: 0, yaw: 0, turret: 0, hp: 100, score: 25, kills: 0, collected: 1, skills: { speed: 15 }, respawnIn: 0 }
const snapshot = { type: 'state', time: 1, players: [player, { ...player, id: 'synthetic-two', name: 'Игрок 2', x: 8, skills: {} }], pickups: [], bullets: [] }

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
