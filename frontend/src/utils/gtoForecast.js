import { defaultReportSelection, localDate } from './shiftReport'

export const forecastOptions = [
  { value: 'healthy', label: 'Исправен', ready: true },
  { value: 'faulty', label: 'Неисправен', ready: false },
]
/** Выбирает следующую смену относительно текущего отчётного периода. */
export function nextForecastSelection(now = new Date()) {
  const current = defaultReportSelection(now)
  if (current.shift === '1') return { date: current.date, shift: '2' }
  const date = new Date(`${current.date}T12:00:00`)
  date.setDate(date.getDate() + 1)
  return { date: localDate(date), shift: '1' }
}
/** Возвращает подпись выбранного состояния прогноза ГТО. */
export function forecastLabel(value) {
  return forecastOptions.find(option => option.value === value)?.label || 'Неисправен'
}
/** Собирает строки прогноза из журналов техники, открытых неисправностей, заявок и показаний ДВС. */
export function buildForecastRows(journals) {
  return journals.filter(journal => !journal.vehicle.isArchived).map(journal => {
    const completed = [...(journal.works ?? [])].filter(work => work.completedAt)
      .sort((a, b) => new Date(a.completedAt) - new Date(b.completedAt) || new Date(a.createdAt ?? 0) - new Date(b.createdAt ?? 0))
    const latest = new Map(completed.filter(work => work.defectId).map(work => [work.defectId, work]))
    const defects = (journal.defects ?? []).filter(defect => (latest.get(defect.id)?.status ?? defect.status) !== 'repaired')
    const isInRepair = journal.vehicle.operationalState === 'stopped'
    const requests = new Map()
    for (const request of [...(journal.partsRequests ?? []), ...(journal.works ?? []).flatMap(work => work.partsRequests ?? [])]) {
      if (!request.completedDate && !requests.has(request.id)) requests.set(request.id, request)
    }
    const partsStages = new Map()
    const stages = [...(journal.works ?? [])].filter(work => work.defectId && work.status === 'awaiting_parts')
      .sort((a, b) => new Date(b.completedAt || b.createdAt || 0) - new Date(a.completedAt || a.createdAt || 0)
        || new Date(b.createdAt || 0) - new Date(a.createdAt || 0))
    for (const stage of stages) if (!partsStages.has(stage.defectId)) partsStages.set(stage.defectId, stage)
    const noteBlocks = defects.flatMap(defect => {
      const text = defect.symptoms || defect.failureReason
      return text ? [{ text, kind: 'defect', bold: true, divider: false }] : []
    })
    for (const request of requests.values()) {
      const number = String(request.requestNumber ?? '').trim()
      const /** Нормализует текст заявки для исключения повторяющихся фрагментов примечания. */ normalize = value => String(value ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase('ru')
      const heading = [number, request.requestDate && `от ${String(request.requestDate).split('-').reverse().join('.')}`].filter(Boolean).join(' ')
      const seen = new Set(number ? [normalize(number)] : [])
      const body = [request.description, request.requiredParts].map(value => String(value ?? '').trim()).filter(value => {
        const key = normalize(value)
        if (!key || seen.has(key)) return false
        seen.add(key)
        return true
      }).join(' ')
      const text = [heading, body].filter(Boolean).join(' ')
      // Procurement downtime comes from the repair stage awaiting parts, not the original defect.
      const reason = partsStages.get(request.defectId)?.description
      const spans = []
      if (text) spans.push({ text })
      if (reason) spans.push({ text: `${text ? ' - ' : ''}${String(reason).replace(/\s+/g, ' ').trim()}`, kind: 'defect', italic: true })
      if (spans.length) noteBlocks.push({ text: spans.map(span => span.text).join(''), spans })
    }
    const dates = defects.filter(defect => (latest.get(defect.id)?.operationalState ?? defect.operationalState) === 'stopped')
      .map(defect => defect.downtimeStartedAt).filter(Boolean).sort()
    const readings = [...(journal.hours ?? [])].sort((a, b) => String(b.readingDate).localeCompare(String(a.readingDate)) || new Date(b.createdAt ?? 0) - new Date(a.createdAt ?? 0))
    const engineCount = journal.vehicle.engineCount ?? (/самосвал/i.test(journal.vehicle.typeName ?? '') ? 2 : 1)
    const engines = Array.from({length:engineCount},(_,index)=>{
      const number=index+1
      const reading=readings.find(item=>item.engineNumber===number || (engineCount===1 && !item.engineNumber))
      return { number, hours:reading?.engineHours ?? null, readingDate:reading?.readingDate ?? '', hoursAtReplacement:null }
    })
    const hours = readings[0]
    return {
      vehicle: journal.vehicle,
      engines,
      legacyHours: engineCount===2 ? readings.find(item=>!item.engineNumber)?.engineHours ?? null : null,
      hours: hours?.engineHours ?? null,
      hoursDate: hours?.readingDate ?? '',
      downtimeStartedAt: dates[0] ?? '',
      currentState: journal.vehicle.operationalState,
      isInRepair,
      openDefectIds: defects.map(defect => defect.id),
      noteBlocks,
      currentNotes: noteBlocks.map(block => block.text).join('\n'),
      forecast: isInRepair || journal.vehicle.operationalState === 'operating_with_defect' ? 'faulty' : 'healthy', ktg: '', maintenance: '', note: '', plannedRelease: '', climate: 'unknown',
    }
  })
}
/** Группирует и сортирует технику для отчёта, подсчитывая готовые к работе машины. */
export function forecastGroups(rows) {
  const groups = new Map()
  for (const row of rows) {
    const groupName = row.vehicle.groupName?.trim() || 'Группа не указана'
    if (!groups.has(groupName)) groups.set(groupName, [])
    groups.get(groupName).push(row)
  }
  return [...groups].sort(([a, aRows], [b, bRows]) =>
    (aRows[0].vehicle.groupReportSortOrder ?? 0) - (bRows[0].vehicle.groupReportSortOrder ?? 0) || a.localeCompare(b, 'ru')).map(([name, items]) => ({
    name, rows: items.sort((a, b) => String(a.vehicle.typeName ?? '').localeCompare(String(b.vehicle.typeName ?? ''), 'ru')
      || String(a.vehicle.garageNumber ?? a.vehicle.stateNumber).localeCompare(String(b.vehicle.garageNumber ?? b.vehicle.stateNumber), 'ru', { numeric: true })),
    ready: items.filter(row => forecastOptions.find(option => option.value === row.forecast)?.ready).length,
  }))
}
/** Возвращает подпись текущей исправности техники. */
export function currentStateLabel(state) {
  return state === 'healthy' ? 'Исправен' : 'Неисправен'
}
export const climateLabels = { unknown: 'Не указано', healthy: 'Исправна', faulty: 'Неисправна', absent: 'Отсутствует' }

/** Подготавливает блоки примечаний для строки прогноза. */
export function forecastNoteBlocks(row) {
  const blocks = row.noteBlocks ?? [{ text: row.currentNotes ?? '', kind: 'defect' }]
  if (row.forecast !== 'healthy') return blocks
  return blocks.filter(block => block.kind !== 'defect').flatMap(block => {
    if (!block.spans) return [block]
    const spans = block.spans.filter(span => span.kind !== 'defect')
    return spans.length ? [{ ...block, spans, text: spans.map(span => span.text).join('') }] : []
  })
}

/** Преобразует строку прогноза в данные для документа. */
export function forecastDocumentRow(row) {
  return row.forecast === 'healthy' ? { ...row, downtimeStartedAt: '', plannedRelease: '' } : row
}

/** Подготавливает примечания и выделения для PDF прогноза. */
export function forecastPdfNoteBlocks(row) {
  const blocks = forecastNoteBlocks(row).map(block => ({ ...block, spans: block.spans ? [...block.spans] : [{ text: block.text, bold: block.bold, italic: block.italic }] }))
  const note = String(row.note ?? '').trim()
  if (!note) return blocks
  const defect = blocks.find(block => block.kind === 'defect' && block.text?.trim())
  if (defect) {
    defect.spans.push({ text: ` (${note})` })
    defect.text += ` (${note})`
  } else blocks.unshift({ text: `(${note})`, spans: [{ text: `(${note})` }] })
  return blocks
}

/** Сокращает имя ответственного до принятого формата с инициалами. */
export function responsibleShortName(name) {
  const [surname, ...names] = String(name ?? '').trim().split(/\s+/).filter(Boolean)
  return [surname, ...names.map(part => `${Array.from(part)[0]}.`)].filter(Boolean).join(' ')
}

/** Форматирует показания ДВС либо моточасы на момент замены двигателя. */
export function engineHoursText(row, replacement = false) {
  if (!row.engines) return replacement ? '' : row.hours == null ? '' : String(row.hours)
  const values=row.engines.map(engine=>replacement ? engine.hoursAtReplacement : engine.hours)
  return values.every(value=>value == null || value==='') ? '' : values.map(value=>value == null ? '' : String(value)).join(' / ')
}

/** Форматирует зафиксированные показания моточасов для отчёта. */
export function fixedHoursText(row) {
  const values = row.engines ? row.engines.map(engine => engine.hours) : [row.hours]
  if (values.every(value => value == null || value === '')) return ''
  const slots = values.map(value => {
    const text = value == null ? '' : String(value)
    if (text.length > 5) throw new Error(`Наработка ${text} у техники № ${row.vehicle?.garageNumber ?? ''} не помещается в поле из 5 символов. Показание не обрезано.`)
    return text.padStart(5, ' ')
  })
  return slots.join(' / ')
}

/** Формирует заголовок показаний с учётом дат данных в группах техники. */
export function hoursReportHeader(groups) {
  const dates = groups.flatMap(group => group.rows.flatMap(row => row.engines
    ? row.engines.filter(engine => engine.hours != null && engine.hours !== '').map(engine => engine.readingDate)
    : row.hours != null && row.hours !== '' ? [row.hoursDate] : []))
    .filter(date => typeof date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(date))
    .sort()
  const latest = dates.at(-1)
  return latest ? `Наработка, м/ч\nна\n${latest.split('-').reverse().join('.')}` : 'Наработка, м/ч'
}
