import { unzipSync, strFromU8 } from 'fflate'

const /** Убирает различия регистра, пробелов и обозначений в заголовке Excel для распознавания столбца. */ normalize = value => String(value ?? '').toLowerCase().replace(/[\s№._/\-]+/g, '')
const /** Находит XML-элементы по локальному имени независимо от пространства имён. */ nodes = (xml, name) => [...xml.getElementsByTagNameNS('*', name)]
const /** Объединяет текст найденных XML-элементов. */ text = (xml, name) => nodes(xml, name).map(node => node.textContent).join('')
/** Распознаёт дату ДД.ММ в названии листа, допуская пробелы и пунктуацию, но отклоняя неверные даты. */
export function sheetDate(name, year) {
  // Accept punctuation/spacing noise, but require exactly two intact numbers.
  // Do not strip all non-digits: that could turn an ambiguous title into a date.
  const match = name.match(/^[\s\p{P}]*(\d{1,2})[\s\p{P}]+(\d{1,2})[\s\p{P}]*$/u)
  if (!match) return null
  const day = Number(match[1]), month = Number(match[2])
  const date = new Date(year, month - 1, day)
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return null
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}
/** Преобразует буквенное обозначение столбца Excel в индекс с нуля. */
export function columnIndex(letters) {
  return [...letters].reduce((value, letter) => value * 26 + letter.charCodeAt(0) - 64, 0) - 1
}
/** Преобразует индексы строки и столбца с нуля в адрес ячейки Excel. */
export function cellAddress(row, column) {
  let letters = ''
  for (let n = column + 1; n; n = Math.floor((n - 1) / 26)) letters = String.fromCharCode(65 + (n - 1) % 26) + letters
  return `${letters}${row + 1}`
}
/** Разбирает диапазон Excel и проверяет границы и ограничение в 20 000 ячеек. */
export function parseRange(value) {
  const match = value.trim().toUpperCase().match(/^([A-Z]{1,3})([1-9]\d*):([A-Z]{1,3})([1-9]\d*)$/)
  if (!match) throw new Error('Укажите диапазон, например A1:Z50.')
  const bounds = { left: columnIndex(match[1]), top: +match[2] - 1, right: columnIndex(match[3]), bottom: +match[4] - 1 }
  if (bounds.left > bounds.right || bounds.top > bounds.bottom || bounds.right >= 16384 || bounds.bottom >= 1048576 || (bounds.right - bounds.left + 1) * (bounds.bottom - bounds.top + 1) > 20000) throw new Error('Диапазон должен содержать не более 20 000 ячеек.')
  return bounds
}
// Only a trailing, separated number is an equipment identifier; model digits are not IDs.
export function garageNumber(value) {
  const match = String(value ?? '').trim().match(/(?:^|[\s/№#])(\d+)$/)
  return match && Number.isSafeInteger(+match[1]) ? +match[1] : null
}
/** Распознаёт заголовок моточасов и номер ДВС; общий счётчик обозначается нулём. */
function engineHeader(value) {
  const key = normalize(value)
  if (/^(engine[12]|двс[12])$/.test(key)) return +key.slice(-1)
  if (!/^(мч|моточас|наработка)/.test(key)) return null
  return /двс2/.test(key) ? 2 : /двс1/.test(key) ? 1 : 0
}
/** Проверяет, обозначает ли заголовок столбец с техникой или бортовым номером. */
function vehicleHeader(value) {
  return /^(асамосвалы|самосвалы|экскаваторы|бульдозеры|грейдеры|техника|наименование|бортовойномер|гаражныйномер|garagenumber)/.test(normalize(value))
}
/** Находит в диапазоне листа блоки техники с соседними столбцами моточасов. */
export function detectBlocks(sheet, bounds) {
  const blocks = []
  for (let row = bounds.top; row <= bounds.bottom; row++) {
    let block = null
    for (let col = bounds.left; col <= bounds.right; col++) {
      const value = sheet.cells[cellAddress(row, col)]?.value
      if (vehicleHeader(value)) {
        block = { headerRow: row, vehicleColumn: col, engines: [] }
        blocks.push(block)
      } else if (block && engineHeader(value) !== null) block.engines.push({ column: col, number: engineHeader(value) })
    }
  }
  return blocks.filter(block => block.engines.length)
}
/** Извлекает показания по датам листов и заданным столбцам, отдельно собирая ошибки разбора. */
export function extractDailyRows(sheets, range = 'A1:Z50', year = new Date().getFullYear(), mappings = {}) {
  const bounds = parseRange(range), rows = [], issues = []
  for (const sheet of sheets) {
    const date = sheetDate(sheet.name, year)
    if (!date) { issues.push(`${sheet.name}: название листа должно содержать действительную дату ДД.ММ.`); continue }
    const blocks = mappings[sheet.name] ?? detectBlocks(sheet, bounds)
    if (!blocks.length) { issues.push(`${sheet.name}: столбцы не распознаны. Укажите их вручную.`); continue }
    for (const block of blocks) {
      const cols = [block.vehicleColumn, ...block.engines.map(e => e.column)]
      if (!Number.isInteger(block.headerRow) || block.headerRow < bounds.top || block.headerRow >= bounds.bottom ||
          !block.engines.length || cols.some(c => !Number.isInteger(c) || c < bounds.left || c > bounds.right) || new Set(cols).size !== cols.length) {
        issues.push(`${sheet.name}: проверьте строку заголовков и разные столбцы внутри выбранного диапазона.`); continue
      }
      if (new Set(block.engines.map(e => e.number)).size !== block.engines.length) { issues.push(`${sheet.name}: неоднозначные столбцы ДВС.`); continue }
      const endRow = Math.min(bounds.bottom + 1, ...blocks.filter(b => b.vehicleColumn === block.vehicleColumn && b.headerRow > block.headerRow).map(b => b.headerRow))
      for (let row = block.headerRow + 1; row < endRow; row++) {
        const label = sheet.cells[cellAddress(row, block.vehicleColumn)]?.value
        if (label == null || String(label).trim() === '') continue
        const garage = garageNumber(label)
        if (garage == null) { issues.push(`${sheet.name}!${cellAddress(row, block.vehicleColumn)}: не распознан бортовой номер «${label}».`); continue }
        for (const engine of block.engines) {
          const address = cellAddress(row, engine.column), cell = sheet.cells[address]
          const raw = String(cell?.value ?? '').trim()
          const valid = !cell?.error && (!raw || /^\d+(?:[.,]\d+)?$/.test(raw))
          // Invalid input remains a row so downstream blanks cannot silently carry across it.
          rows.push({ readingDate: date, garageNumber: garage, engineNumber: engine.number, engineHours: valid && raw ? Number(raw.replace(',', '.')) : null, invalid: !valid, source: `${sheet.name}!${address}` })
        }
      }
    }
  }
  if (rows.length > 10000) throw new Error('В файле более 10 000 показаний. Разделите его на части.')
  return { rows, issues }
}

/** Читает XLSX в браузере, ограничивая размер файла и распакованных XML-данных. */
export async function readDailyWorkbook(file) {
  if (!/\.xlsx$/i.test(file.name) || !file.size || file.size > 5 * 1024 * 1024) throw new Error('Выберите XLSX до 5 МБ.')
  let total = 0
  const entries = unzipSync(new Uint8Array(await file.arrayBuffer()), { /** Отбирает необходимые XML-части XLSX и ограничивает их суммарный распакованный размер. */ filter(entry) {
    if (!/^xl\/(workbook\.xml|_rels\/workbook\.xml\.rels|sharedStrings\.xml|worksheets\/[^/]+\.xml)$/.test(entry.name)) return false
    total += entry.originalSize
    if (total > 20 * 1024 * 1024) throw new Error('Распакованный файл превышает 20 МБ.')
    return true
  } })
  /** Читает XML-часть XLSX и отклоняет повреждённую разметку, DTD и объявления сущностей. */
  function xml(path, optional = false) {
    if (!entries[path]) { if (optional) return null; throw new Error(`В XLSX отсутствует ${path}.`) }
    const source = strFromU8(entries[path])
    if (/<!DOCTYPE|<!ENTITY/i.test(source)) throw new Error('Неподдерживаемая XML-разметка.')
    const doc = new DOMParser().parseFromString(source, 'application/xml')
    if (doc.getElementsByTagName('parsererror').length) throw new Error('Повреждён XML внутри XLSX.')
    return doc
  }
  const shared = xml('xl/sharedStrings.xml', true)
  const strings = shared ? nodes(shared, 'si').map(si => text(si, 't')) : []
  const rels = new Map(nodes(xml('xl/_rels/workbook.xml.rels'), 'Relationship').filter(r => r.getAttribute('TargetMode') !== 'External').map(r => [r.getAttribute('Id'), r.getAttribute('Target')]))
  const sheets = nodes(xml('xl/workbook.xml'), 'sheet')
  if (sheets.length > 366) throw new Error('В книге более 366 листов.')
  return sheets.map(sheet => {
    const target = rels.get(sheet.getAttributeNS('http://schemas.openxmlformats.org/officeDocument/2006/relationships', 'id'))
    if (!target) throw new Error('Не найдена связь листа XLSX.')
    const path = target.startsWith('/') ? target.slice(1) : `xl/${target.replace(/^\.\//, '')}`
    const cells = {}
    for (const cell of nodes(xml(path), 'c')) {
      const type = cell.getAttribute('t'), raw = text(cell, 'v'), formula = nodes(cell, 'f').length > 0
      cells[cell.getAttribute('r')] = { value: type === 's' ? strings[+raw] : type === 'inlineStr' ? text(cell, 't') : raw, error: type === 'e' || type === 'b' || (formula && !raw) || (type === 's' && strings[+raw] === undefined) }
    }
    return { name: sheet.getAttribute('name'), cells }
  })
}
