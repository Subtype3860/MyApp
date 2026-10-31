/** Возвращает местную календарную дату в формате ГГГГ-ММ-ДД. */
export function localDate(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** Определяет отчётную дату и смену по местному времени. */
export function defaultReportSelection(now = new Date()) {
  const date = new Date(now)
  if (now.getHours() < 12) date.setDate(date.getDate() - 1)
  return { date: localDate(date), shift: now.getHours() < 12 ? '2' : '1' }
}

/** Возвращает начало и конец выбранной смены. */
export function shiftBounds(date, shift) {
  const start = new Date(`${date}T${String(shift) === '2' ? '20' : '08'}:00:00`)
  const end = new Date(start)
  if (String(shift) === '2') {
    end.setDate(end.getDate() + 1)
    end.setHours(8)
  } else end.setHours(20)
  return { start, end }
}

/** Форматирует отчётную дату для заголовка. */
export function formatReportDate(date) {
  return String(date).split('-').reverse().join('.')
}

/** Отбирает работы, относящиеся к выбранной смене. */
export function selectShiftWorks(journals, date, shift) {
  const { start, end } = shiftBounds(date, shift)
  return journals.flatMap(journal => (journal.works ?? [])
    .filter(work => {
      const at = new Date(work.completedAt || work.createdAt)
      return at >= start && at < end
    })
    .map(work => ({ ...work, vehicle: journal.vehicle, shiftState: stateAt(journal, end) })))
    .sort((a, b) => new Date(a.completedAt || a.createdAt) - new Date(b.completedAt || b.createdAt)
      || String(a.id).localeCompare(String(b.id)))
}

/** Группирует работы смены для отображения в отчёте. */
export function groupShiftWorks(works, locations = {}) {
  return ['КАРЬЕР', 'РММ'].map(location => {
    const categories = new Map()
    for (const work of works) {
      if ((locations[work.id] || work.location) !== location) continue
      const category = work.vehicle.typeName || 'Техника'
      if (!categories.has(category)) categories.set(category, new Map())
      const vehicles = categories.get(category)
      if (!vehicles.has(work.vehicle.id)) vehicles.set(work.vehicle.id, { vehicle: work.vehicle, works: [] })
      vehicles.get(work.vehicle.id).works.push(work)
    }
    return { location, categories: [...categories].sort(([a], [b]) => a.localeCompare(b, 'ru'))
      .map(([name, vehicles]) => ({ name, cards: [...vehicles.values()].sort((a, b) =>
        String(a.vehicle.garageNumber ?? a.vehicle.stateNumber).localeCompare(String(b.vehicle.garageNumber ?? b.vehicle.stateNumber), 'ru', { numeric: true })) })) }
  }).filter(group => group.categories.length)
}

/** Формирует подпись техники из модели и идентификационных номеров. */
export function vehicleTitle(vehicle) {
  return `${vehicle.modelName || 'Техника'} · ${vehicle.garageNumber != null ? `№ ${vehicle.garageNumber}` : vehicle.stateNumber || 'Без номера'}`
}

/** Определяет состояние карточки работ для отчёта. */
export function cardState(card) {
  const last = card.works.at(-1)
  if (last.shiftState) return last.shiftState
  if (last.operationalState === 'operating' || last.status === 'repaired') return { label: 'В работе', kind: 'ready' }
  if (last.operationalState === 'stopped') return { label: 'Не в работе', kind: 'stopped' }
  return { label: 'Состояние не указано', kind: 'unknown' }
}

/** Собирает фото или видео работ карточки отчёта. */
export function cardMedia(card, type) {
  return card.works.flatMap(work => (type === 'video' ? work.videos ?? [] : work.photos ?? [])
    .map(media => ({ ...media, source: work.workKind === 'auxiliary' ? 'auxiliary' : 'work', workId: work.id })))
}

/** Формирует ссылку перехода к вложению из отчёта. */
export function reportMediaLink(card, type, date, shift, mediaId) {
  const url = new URL(window.location.pathname, window.location.origin)
  url.searchParams.set('reportWork', card.works.map(work => work.id).join(','))
  url.searchParams.set('mediaType', type)
  if (card.auxiliary) url.searchParams.set('reportSource', 'auxiliary')
  url.searchParams.set('reportDate', date)
  url.searchParams.set('reportShift', shift)
  if (mediaId) url.searchParams.set('mediaId', mediaId)
  return url.href
}

// Resolve all known defects at shift end, so completing one repair does not hide another.
export function stateAt(journal, end) {
  const earlier = (journal.works ?? []).filter(work => new Date(work.completedAt || work.createdAt) < end)
    .sort((a, b) => new Date(a.completedAt || a.createdAt) - new Date(b.completedAt || b.createdAt))
  const latest = new Map(earlier.filter(work => work.defectId).map(work => [work.defectId, work]))
  const defects = (journal.defects ?? []).filter(defect => new Date(defect.createdAt || defect.downtimeStartedAt) < end)
  const states = defects.map(defect => {
    const work = latest.get(defect.id)
    return { status: work?.status, operationalState: work?.operationalState ?? defect.operationalState }
  }).filter(item => item.status !== 'repaired')
  if (states.some(item => item.operationalState === 'stopped')) return { label: 'Не в работе', kind: 'stopped' }
  if (states.length && states.every(item => item.operationalState === 'operating')) return { label: 'В работе с неисправностью', kind: 'ready' }
  if (states.length) return { label: 'Состояние не указано', kind: 'unknown' }
  if (defects.length) return { label: 'В работе', kind: 'ready' }
  return null
}

/** Возвращает исполнителей работы с учётом участников. */
export function workPerformers(work) {
  if (Array.isArray(work.participants)) {
    const seen = new Set()
    return work.participants.filter(participant => {
      if (participant.status !== 'approved' || seen.has(participant.userId)) return false
      seen.add(participant.userId)
      return true
    }).map(participant => [participant.position, participant.name].filter(Boolean).join(' · '))
  }
  return work.performerName ? [[work.performerPosition, work.performerName].filter(Boolean).join(' · ')] : []
}

/** Формирует строку с именами исполнителей работы. */
export function workPerformersLabel(work) {
  const performers = workPerformers(work)
  return performers.length ? `Исполнители: ${performers.join('; ')}` : 'Исполнители пока не подтверждены'
}

/** Отбирает вспомогательные работы выбранной смены. */
export function selectAuxiliaryWorks(works, date, shift) {
  const { start, end } = shiftBounds(date, shift)
  return works.filter(work => {
    const at = new Date(work.completedAt || work.createdAt)
    return at >= start && at < end
  }).sort((a, b) => new Date(a.completedAt || a.createdAt) - new Date(b.completedAt || b.createdAt)
    || String(a.id).localeCompare(String(b.id)))
}

/** Группирует вспомогательные работы для сменного отчёта. */
export function groupAuxiliaryWorks(works) {
  return ['КАРЬЕР', 'РММ'].map(location => {
    const cards = works.filter(work => work.location === location)
      .map(work => ({ id: work.id, auxiliary: true, title: work.title || 'Вспомогательная работа', works: [work] }))
    return { location, categories: cards.length ? [{ name: 'Вспомогательные работы', cards }] : [] }
  }).filter(group => group.categories.length)
}

/** Объединяет группы ремонтных и вспомогательных работ в отчёт. */
export function groupReportWorks(repairWorks, auxiliaryWorks, locations = {}) {
  const repairGroups = groupShiftWorks(repairWorks, locations)
  const auxiliaryGroups = groupAuxiliaryWorks(auxiliaryWorks)
  return ['КАРЬЕР', 'РММ'].map(location => ({ location,
    categories: [...(repairGroups.find(group => group.location === location)?.categories ?? []),
      ...(auxiliaryGroups.find(group => group.location === location)?.categories ?? [])] }))
    .filter(group => group.categories.length)
}

/** Возвращает заголовок карточки сменного отчёта. */
export function reportCardTitle(card) {
  return card.auxiliary ? card.title : vehicleTitle(card.vehicle)
}
