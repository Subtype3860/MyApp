import { test, expect } from '@playwright/test'
import { maintenanceReportGroups } from '../src/utils/shiftReport'

test('maintenance report lists sections with approved participants and photos', () => {
  const records = [{
    id: 'c1', vehicleLabel: 'SANY №902', service: 'ТО-1000',
    finishedAt: '2026-11-11T05:00:00Z', notes: '', executors: ['Иванов Иван'],
    sections: [
      { name: 'ДВС', participants: [{ userId: 'u1', name: 'Петров Пётр', status: 'approved' }, { userId: 'u2', name: 'Сидоров Сидор', status: 'pending' }], photos: ['p1'] },
      { name: 'Шасси', participants: [], photos: [] },
    ],
  }]
  const groups = maintenanceReportGroups(records, '2026-11-11', '1')
  const [card] = groups[0].categories[0].cards
  expect(card.cardId).toBe('c1')
  expect(card.works).toHaveLength(2)
  // В отчёт попадают только подтверждённые участники раздела.
  expect(card.works[0].description).toBe('ДВС — Петров Пётр')
  expect(card.works[0].section).toBe('ДВС')
  expect(card.works[0].executors).toEqual(['Петров Пётр'])
  expect(card.works[0].photos).toEqual([{ id: 'p1', source: 'maintenance', cardId: 'c1' }])
  expect(card.works[1].description).toBe('Шасси')
})

test('maintenance report falls back to the service summary without sections', () => {
  const records = [{ id: 'c2', vehicleLabel: 'SANY №94', service: 'ТО-700', finishedAt: '2026-11-11T05:00:00Z', notes: 'Замечаний нет', executors: [] }]
  const [card] = maintenanceReportGroups(records, '2026-11-11', '1')[0].categories[0].cards
  expect(card.works).toHaveLength(1)
  expect(card.works[0].description).toContain('ТО-700')
  expect(card.works[0].description).toContain('Замечаний нет')
})

test('report media links ignore maintenance photos', async () => {
  const { fetchReportMediaLinks } = await import('../src/utils/reportMedia.js')
  const originalFetch = globalThis.fetch
  const originalWindow = globalThis.window
  globalThis.window = { location: { origin: 'https://boora.ru' } }
  const bodies = []
  globalThis.fetch = async (url, options = {}) => {
    const target = String(url)
    if (target.includes('/api/report-media/links')) {
      bodies.push(JSON.parse(options.body))
      return { ok: true, json: async () => [{ id: 'w1', type: 'image', url: '/api/report-media/w1/sig' }] }
    }
    if (target.includes('/api/maintenance-plans/cards/c1/media-links')) {
      return { ok: true, json: async () => [{ id: 'p1', url: '/api/maintenance-plans/cards/c1/media/p1/sig' }] }
    }
    throw new Error('unexpected ' + target)
  }
  try {
    const cards = [
      { id: 'maintenance:c1', maintenance: true, cardId: 'c1', works: [{ photos: [{ id: 'p1', source: 'maintenance', cardId: 'c1' }] }] },
      { id: 'w', works: [{ photos: [{ id: 'w1' }] }] },
    ]
    const links = await fetchReportMediaLinks(cards, 'token')
    expect(bodies[0].map(item => item.id)).toEqual(['w1'])
    expect(links['image:p1']).toContain('/media/p1/')
    expect(links['image:w1']).toContain('/api/report-media/')
  } finally {
    globalThis.fetch = originalFetch
    globalThis.window = originalWindow
  }
})
