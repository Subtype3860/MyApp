import { test, expect } from '@playwright/test'
import { maintenanceReportGroups } from '../src/utils/shiftReport'

test('maintenance report lists operations with executor and photos', () => {
  const records = [{
    id: 'c1', vehicleLabel: 'SANY №902', service: 'ТО-1000',
    finishedAt: '2026-11-11T05:00:00Z', notes: '',
    executors: ['Иванов Иван'],
    operations: [
      { id: 'o1', text: 'Проверить уровень масла', status: 'done', condition: 'ok', executor: 'Петров Пётр', photos: ['p1'] },
      { id: 'o2', text: 'Заменить фильтр', status: 'done', condition: 'ok', executor: '', photos: [] },
    ],
  }]
  const groups = maintenanceReportGroups(records, '2026-11-11', '1')
  expect(groups).toHaveLength(1)
  const [card] = groups[0].categories[0].cards
  expect(card.works).toHaveLength(2)
  expect(card.works[0].description).toContain('Проверить уровень масла')
  expect(card.works[0].description).toContain('Петров Пётр')
  expect(card.works[0].executors).toEqual(['Петров Пётр'])
  expect(card.works[0].photos).toEqual([{ id: 'p1', source: 'maintenance', cardId: 'c1' }])
  expect(card.cardId).toBe('c1')
})

test('maintenance report falls back to the service summary without operations', () => {
  const records = [{ id: 'c2', vehicleLabel: 'SANY №94', service: 'ТО-700', finishedAt: '2026-11-11T05:00:00Z', notes: 'Замечаний нет', executors: [] }]
  const [card] = maintenanceReportGroups(records, '2026-11-11', '1')[0].categories[0].cards
  expect(card.works).toHaveLength(1)
  expect(card.works[0].description).toContain('ТО-700')
  expect(card.works[0].description).toContain('Замечаний нет')
})
