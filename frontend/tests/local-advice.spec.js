import { test, expect } from '@playwright/test'
import { isAdviceFresh } from '../src/utils/localAdvice.js'

async function mount(page, component, props) {
  await page.route('**/api/**', route => route.fulfill({ json: [] }))
  await page.goto('/')
  await page.evaluate(async ({ component, props }) => {
    const { mount } = await import('/tests/fixtures/local-advice-harness.js')
    mount(component, props)
  }, { component, props })
}
const response = () => ({ advice: { recommendation: 'Требуется проверка механиком', status: 'insufficient_data', confidence: null,
  generatedAt: new Date(Date.now() - 1000).toISOString(), expiresAt: new Date(Date.now() + 300000).toISOString(), factors: ['Незакрытых неисправностей: 1.'] } })

test('freshness rejects missing, expired and future-generated advice', () => {
  expect(isAdviceFresh(null)).toBe(false)
  expect(isAdviceFresh(response())).toBe(true)
  expect(isAdviceFresh(response(), Date.now() + 600000)).toBe(false)
  expect(isAdviceFresh(response(), Date.now() - 600000)).toBe(false)
})

for (const kind of ['readiness', 'recurrence']) {
  test(`${kind}: recommendation is read only and clears on API failure, context change and offline`, async ({ page }) => {
    await mount(page, 'LocalAdvicePanel', { token: 'synthetic', title: 'Рекомендация', url: `/api/local-advice/${kind}` })
    let fail = false; const requests = []
    await page.route('**/api/local-advice/**', route => {
      requests.push(route.request().method())
      return route.fulfill(fail ? { status: 503, json: { message: 'Недоступно' } } : { json: response() })
    })
    await page.getByRole('button', { name: 'Рекомендация', exact: true }).click()
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toBeVisible()
    await expect(page.getByText('Уверенность: не оценена.', { exact: false })).toBeVisible()
    fail = true
    await page.getByRole('button', { name: 'Рекомендация', exact: true }).click()
    await expect(page.getByText('Недоступно', { exact: true })).toBeVisible()
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toHaveCount(0)
    fail = false
    await page.getByRole('button', { name: 'Рекомендация', exact: true }).click()
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toBeVisible()
    await page.evaluate(() => { window.adviceProps.url += '/changed' })
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: 'Рекомендация', exact: true }).click()
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toBeVisible()
    await page.evaluate(() => window.dispatchEvent(new Event('offline')))
    await expect(page.getByText('Требуется проверка механиком', { exact: true })).toHaveCount(0)
    expect(requests.every(method => method === 'GET')).toBe(true)
  })
}

for (const kind of ['repair', 'parts']) {
  test(`${kind}: draft requires per-field acceptance, is editable and never saves a request`, async ({ page }) => {
    await mount(page, 'LocalRequestDraft', { token: 'synthetic', kind, source: 'Узел: насос; Симптомы: шум', context: 'v1' })
    let calls = 0
    const primary = kind === 'repair' ? 'symptoms' : 'description'
    await page.route('**/api/local-advice/parse', route => {
      calls++; return route.fulfill({ json: { fields: [{ name: primary, value: 'шум', evidence: 'Симптомы: шум' }, { name: 'nodeName', value: 'насос', evidence: 'Узел: насос' }], limitations: [] } })
    })
    await page.getByRole('button', { name: 'Разобрать текст локально' }).click()
    await expect(page.getByRole('button', { name: 'Перенести выбранное в форму' })).toBeDisabled()
    const label = kind === 'repair' ? 'Симптомы' : 'Описание'
    await page.getByRole('textbox', { name: label, exact: true }).fill('проверенное описание')
    await page.getByRole('checkbox', { name: `Принять: ${label}`, exact: true }).check()
    await page.getByRole('button', { name: 'Перенести выбранное в форму' }).click()
    expect(await page.evaluate(() => window.appliedDraft)).toEqual({ [primary]: 'проверенное описание' })
    expect(calls).toBe(1)
  })
}

test('parser empty result and API error do not leave an applicable draft', async ({ page }) => {
  await mount(page, 'LocalRequestDraft', { token: 'synthetic', kind: 'repair', source: 'текст', context: 'v1' })
  await page.route('**/api/local-advice/parse', route => route.fulfill({ json: { fields: [], limitations: [] } }))
  await page.getByRole('button', { name: 'Разобрать текст локально' }).click()
  await expect(page.getByText('Недостаточно данных.', { exact: false })).toBeVisible()
  await page.route('**/api/local-advice/parse', route => route.fulfill({ status: 500, json: {} }))
  await page.getByRole('button', { name: 'Разобрать текст локально' }).click()
  await expect(page.getByText('Помощник недоступен.', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Перенести выбранное в форму' })).toHaveCount(0)
})

test('shift fact requires explicit choice and confirmation and reports conflicts', async ({ page }) => {
  await mount(page, 'ShiftOutcome', { token: 'synthetic', vehicleId: 'v1', date: '2026-10-01', shift: '1' })
  await page.getByText('Подтвердить фактический результат завершённой смены').click()
  await expect(page.getByRole('button', { name: 'Подтвердить факт смены' })).toBeDisabled()
  await page.getByRole('combobox').selectOption('ready')
  await expect(page.getByRole('button', { name: 'Подтвердить факт смены' })).toBeDisabled()
  await page.getByRole('checkbox').check()
  await page.route('**/api/local-advice/**', route => route.fulfill({ status: 409, json: { message: 'Уже подтверждено' } }))
  await page.getByRole('button', { name: 'Подтвердить факт смены' }).click()
  await expect(page.getByText('Уже подтверждено')).toBeVisible()
})
