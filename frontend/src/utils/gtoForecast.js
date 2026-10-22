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
    const stages = [...(journal.works ?? [])].filter(work => work.defectId)
      .sort((a, b) => new Date(b.completedAt || b.createdAt || 0) - new Date(a.completedAt || a.createdAt || 0)
        || new Date(b.createdAt || 0) - new Date(a.createdAt || 0))
    for (const stage of stages) if (!partsStages.has(stage.defectId)) partsStages.set(stage.defectId, stage)
    const noteBlocks = defects.flatMap(defect => {
      const stage = partsStages.get(defect.id)
      const waiting = (stage?.status ?? defect.status) === 'awaiting_parts'
      const text = waiting ? `ОЗЧ Требуется: ${String(stage?.description ?? '').trim() || 'Описание не указано'}` : defect.symptoms || defect.failureReason
      return text ? [{ text, defectId: defect.id, kind: 'defect', bold: true, divider: false }] : []
    })
    for (const request of requests.values()) {
      const number = String(request.requestNumber ?? '').trim()
      const /** Нормализует текст заявки для исключения повторяющихся фрагментов примечания. */ normalize = value => String(value ?? '').trim().replace(/\s+/g, ' ').toLocaleLowerCase('ru')
      const description = String(request.description ?? '').trim()
      // Older forms copied the first 100 characters of the body into requestNumber.
      const body = [number && !normalize(description).startsWith(normalize(number)) ? number : '', description || number].filter(Boolean).join(' ')
      const seen = new Set([normalize(body)])
      const stage = partsStages.get(request.defectId)
      const requirement = stage ? stage.description : (journal.defects ?? []).find(defect => defect.id === request.defectId)?.symptoms
      const required = [request.requiredParts, requirement].map(value => String(value ?? '').trim()).filter(value => {
        const key = normalize(value)
        if (!key || seen.has(key)) return false
        seen.add(key)
        return true
      }).join('; ')
      const details = []
      if (body) details.push({ text: body })
      if (required && !defects.some(defect => defect.id === request.defectId && (stage?.status ?? defect.status) === 'awaiting_parts')) details.push({ text: `Требуется: ${required}`, kind: 'required', italic: true })
      if (!details.length) continue
      let block = request.defectId ? noteBlocks.find(item => item.defectId === request.defectId) : null
      if (!block) {
        block = { defectId: request.defectId, spans: [] }
        noteBlocks.push(block)
      } else if (!block.spans) {
        block.spans = [{ text: block.text, kind: 'defect', bold: true }, { text: ' — ', kind: 'defect' }]
        delete block.kind
        delete block.bold
      } else block.spans.push({ text: ' ' })
      block.spans.push({ text: '(' }, ...details.flatMap((detail, index) => index ? [{ text: '; ' }, detail] : [detail]), { text: ')' })
      block.text = block.spans.map(span => span.text).join('')
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
      forecast: isInRepair ? 'faulty' : 'healthy', ktg: '', maintenance: '', note: '', plannedRelease: '', climate: 'unknown',
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
    ready: items.filter(row => !isReportFaulty(row)).length,
  }))
}
/** Возвращает подпись текущей исправности техники. */
export function currentStateLabel(state) {
  return state === 'stopped' ? 'Неисправен' : 'Исправен'
}
/** Определяет исправность по остановке машины; для старых снимков без состояния использует сохранённое значение. */
export function isReportFaulty(row) {
  const state = row.currentState ?? row.vehicle?.operationalState
  return state == null ? row.forecast === 'faulty' : state === 'stopped'
}
export const climateLabels = { unknown: 'Не указано', healthy: 'Исправна', faulty: 'Неисправна', absent: 'Отсутствует' }

/** Подготавливает блоки примечаний для строки прогноза. */
export function forecastNoteBlocks(row) {
  return row.noteBlocks ?? [{ text: row.currentNotes ?? '', kind: 'defect' }]
}

/** Показывает остановленную технику и технику с открытыми заявками независимо от исправности. */
export function hasReportRequests(row) {
  return row.isInRepair || row.openDefectIds?.length > 0 || forecastNoteBlocks(row).some(block => block.text?.trim())
}

/** Преобразует строку прогноза в данные для документа. */
export function forecastDocumentRow(row) {
  const forecast = isReportFaulty(row) ? 'faulty' : 'healthy'
  return forecast === 'healthy' ? { ...row, forecast, downtimeStartedAt: '', plannedRelease: '' } : { ...row, forecast }
}

/** Подготавливает примечания и выделения для PDF прогноза. */
export function forecastPdfNoteBlocks(row) {
  const blocks = forecastNoteBlocks(row).map(block => ({ ...block, spans: block.spans ? [...block.spans] : [{ text: block.text, bold: block.bold, italic: block.italic }] }))
  const note = String(row.note ?? '').trim()
  if (!note) return blocks
  const defect = blocks.find(block => (block.kind === 'defect' && block.text?.trim()) || block.spans?.some(span => span.kind === 'defect' && span.bold))
  if (defect) {
    const index = defect.spans.findIndex(span => span.kind === 'defect' && span.bold)
    if (index >= 0) defect.spans.splice(index + 1, 0, { text: ` (${note})` })
    else defect.spans.push({ text: ` (${note})` })
    defect.text = defect.spans.map(span => span.text).join('')
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
