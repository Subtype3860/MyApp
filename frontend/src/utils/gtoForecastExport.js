import { zipSync, strToU8 } from 'fflate'
import { createGtoForecastPdf } from './gtoForecastPdf'
import { responsibleShortName } from './gtoForecast'
import { formatReportDate } from './shiftReport'

const xml = value => String(value ?? '').replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' })[char])
const declaration = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'

/** Формирует XLSX с текстовыми ячейками (без исполнения пользовательских формул). */
export async function createGtoForecastExcel(report) {
  let layout
  await createGtoForecastPdf(report, value => { layout = value })
  const widths = layout.widths
  const merges = ['J1:K1', 'A2:F2', 'J2:K2', 'A3:F3']
  const rows = [
    { cells: ['', '', '', '', '', '', '', '', 'Ответственные лица:', 'ФИО', ''], styles: [7,7,7,7,7,7,6,6,11,11,11] },
    { cells: [report.organisation, '', '', '', '', '', '', '', 'Механик:', responsibleShortName(report.mechanic), ''], styles: [7,7,7,7,7,7,6,6,11,11,11] },
    { cells: [report.site ? 'Участок «' + report.site + '»' : '', '', '', '', '', '', '', 'Состояние оборудования на', formatReportDate(report.date), String(report.shift), ''], styles: [7,7,7,7,7,7,7,12,7,7,7] },
    { cells: layout.headers, heading: true },
  ]
  for (const group of layout.groups) {
    rows.push({ title: group.name, style: 8 })
    for (const cells of group.cells) rows.push({ cells })
    rows.push({ title: 'Выход к смене: ' + group.ready + ' из ' + group.rows.length, style: 9 })
  }
  rows.push({ title: 'СВОДКА ВЫХОДА', style: 10 })
  for (const group of layout.groups) rows.push({ title: group.name + ': ' + group.ready + ' из ' + group.rows.length, style: 6 })
  const blocks = value => Array.isArray(value) ? value : [{ text: value ?? '' }]
  const plain = value => blocks(value).map(block => block.spans ? block.spans.map(span => span.text).join('') : block.text).join('\n')
  const measure = document.createElement('canvas').getContext('2d')
  // Measure at 96 dpi and convert the resulting line count to points in Excel.
  function lineCount(value, width) {
    measure.font = 'bold 9.34px Arial'
    let count = 0
    for (const paragraph of String(value).split('\n')) {
      let line = ''; count++
      for (const word of paragraph.split(/\s+/).filter(Boolean)) {
        if (line && measure.measureText(line + ' ' + word).width > width) { count++; line = '' }
        for (const char of (line ? ' ' : '') + word) {
          if (line && measure.measureText(line + char).width > width) { count++; line = '' }
          line += char
        }
      }
    }
    return count
  }
  function richText(value) {
    return blocks(value).flatMap((block, index) => {
      const spans = block.spans || [block]
      return spans.map((span, spanIndex) => {
        const text = (index && !spanIndex ? '\n' : '') + (span.text ?? '')
        return '<r><rPr><rFont val="' + (block.fixedHours ? 'Courier New' : 'Arial') + '"/><sz val="7"/>' +
          (span.bold || block.fixedHours ? '<b/>' : '') + (span.italic ? '<i/>' : '') + (block.divider ? '<u/>' : '') +
          '<color rgb="' + (span.red ? 'FFCC1414' : 'FF1F2B3B') + '"/></rPr><t xml:space="preserve">' + xml(text) + '</t></r>'
      })
    }).join('')
  }
  const data = rows.map((row, index) => {
    const number = index + 1
    if (row.title != null) merges.push('A' + number + ':K' + number)
    const values = row.cells || [row.title]
    const height = Math.max(row.style === 10 ? 24 : 14, ...values.map((value, column) => {
      const merged = merges.find(ref => ref.split(':')[0] === String.fromCharCode(65 + column) + number)
      const last = merged ? merged.split(':')[1].charCodeAt(0) - 65 : column
      const width = widths.slice(column, last + 1).reduce((sum, width) => sum + width, 0) * 4 / 3 - 10
      return lineCount(plain(value), width) * 9 + 5
    }))
    if (height > 409) throw new Error('Слишком длинная строка для Excel. Сократите примечание или скачайте PDF.')
    return '<row r="' + number + '" ht="' + height + '" customHeight="1">' + values.map((value, column) => {
      if (plain(value).length > 32767) throw new Error('Текст ячейки превышает ограничение Excel (32767 символов).')
      let style = row.styles?.[column] ?? row.style ?? (row.heading ? 1 : 11)
      if (Array.isArray(value) && !row.heading) {
        if (value[0]?.fixedHours) style = 4
        else if (value[0]?.red) style = value[0]?.bold ? 3 : 2
      }
      if (index > 3 && row.cells && column === 5 && value !== '') { value = Number(String(value).replace('%','')) / 100; style = 5 }
      const attributes = 'r="' + String.fromCharCode(65 + column) + number + '" s="' + style + '"'
      if (typeof value === 'number') return '<c ' + attributes + '><v>' + value + '</v></c>'
      const content = Array.isArray(value) && !row.heading ? richText(value) : '<t xml:space="preserve">' + xml(plain(value)) + '</t>'
      return '<c ' + attributes + ' t="inlineStr"><is>' + content + '</is></c>'
    }).join('') + '</row>'
  }).join('')
  const font = (extra = '', color = 'FF1F2B3B', size = 7, name = 'Arial') => '<font><sz val="' + size + '"/><name val="' + name + '"/><color rgb="' + color + '"/>' + extra + '</font>'
  const fonts = [font(), font('<b/>'), font('', 'FFCC1414'), font('<b/>', 'FFCC1414'), font('<b/>', 'FF1F2B3B', 7, 'Courier New'), font('', 'FF145E6B', 8), font('', 'FF667380'), font('', 'FF145E6B', 12)]
  const xf = (fontId, fillId, borderId, numFmtId = 0, horizontal = null) => '<xf fontId="' + fontId + '" fillId="' + fillId + '" borderId="' + borderId + '" numFmtId="' + numFmtId + '" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyNumberFormat="1" applyAlignment="1"><alignment vertical="center" wrapText="1"' + (horizontal ? ' horizontal="' + horizontal + '"' : '') + '/></xf>'
  const styles = [xf(0,0,0), xf(1,2,1,0,'center'), xf(2,0,1), xf(3,0,1), xf(4,0,1), xf(0,0,1,164), xf(0,0,0), xf(1,0,0), xf(5,0,0), xf(6,0,0), xf(7,0,0), xf(0,0,1), xf(1,0,0,0,'right')]
  const styleXml = '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="0.##%"/></numFmts><fonts count="' + fonts.length + '">' + fonts.join('') + '</fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEBF2F5"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border/><border>' + ['left','right','top','bottom'].map(side => '<' + side + ' style="thin"><color rgb="FFC7D1D9"/></' + side + '>').join('') + '</border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="' + styles.length + '">' + styles.join('') + '</cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>'
  const files = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
    '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
    'xl/workbook.xml': '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Выход ГТО" sheetId="1" r:id="rId1"/></sheets><definedNames><definedName name="_xlnm.Print_Titles" localSheetId="0">&apos;Выход ГТО&apos;!$1:$4</definedName></definedNames></workbook>',
    'xl/_rels/workbook.xml.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
    'xl/styles.xml': styleXml,
    'xl/worksheets/sheet1.xml': `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetPr><pageSetUpPr fitToPage="1"/></sheetPr><sheetViews><sheetView workbookViewId="0" showGridLines="0"><pane ySplit="4" topLeftCell="A5" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>${widths.map((width, index) => `<col min="${index + 1}" max="${index + 1}" width="${width / 3.75}" customWidth="1"/>`).join('')}</cols><sheetData>${data}</sheetData><mergeCells count="${merges.length}">${merges.map(ref => `<mergeCell ref="${ref}"/>`).join('')}</mergeCells><pageMargins left="0.28" right="0.28" top="0.28" bottom="0.42" header="0" footer="0.15"/><pageSetup orientation="${layout.portrait ? 'portrait' : 'landscape'}" paperSize="9" fitToWidth="1" fitToHeight="0"/><headerFooter><oddFooter>&amp;R&amp;P / &amp;N</oddFooter></headerFooter></worksheet>`,
  }
  return new Blob([zipSync(Object.fromEntries(Object.entries(files).map(([name, content]) => [name, strToU8(declaration + content)])))], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
}

/** Устанавливает физическое разрешение PNG 300 dpi для печати в формате A4. */
async function withPrintResolution(blob) {
  const bytes = new Uint8Array(await blob.arrayBuffer())
  const chunk = new Uint8Array(21)
  const view = new DataView(chunk.buffer)
  view.setUint32(0, 9)
  chunk.set([112, 72, 89, 115], 4) // pHYs
  view.setUint32(8, 11811); view.setUint32(12, 11811); chunk[16] = 1
  let crc = 0xffffffff
  for (const byte of chunk.subarray(4, 17)) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0)
  }
  view.setUint32(17, (crc ^ 0xffffffff) >>> 0)
  // Replace any canvas-generated resolution chunk rather than duplicate it.
  const parts = [bytes.subarray(0, 33), chunk]
  for (let offset = 33; offset < bytes.length;) {
    const length = new DataView(bytes.buffer, bytes.byteOffset + offset, 4).getUint32(0) + 12
    if (String.fromCharCode(...bytes.subarray(offset + 4, offset + 8)) !== 'pHYs') parts.push(bytes.subarray(offset, offset + length))
    offset += length
  }
  return new Blob(parts, { type: 'image/png' })
}

/** Растеризует исходный PDF без изменения его оформления и разбиения страниц. */
export async function createGtoForecastImage(report) {
  const [{ getDocument, GlobalWorkerOptions }, { default: workerUrl }] = await Promise.all([
    import('pdfjs-dist'), import('pdfjs-dist/build/pdf.worker.min.mjs?url'),
  ])
  GlobalWorkerOptions.workerSrc = workerUrl
  const task = getDocument({ data: new Uint8Array(await (await createGtoForecastPdf(report)).arrayBuffer()), useSystemFonts: true })
  const pages = []
  try {
    const pdf = await task.promise
    for (let number = 1; number <= pdf.numPages; number++) {
      const page = await pdf.getPage(number)
      const viewport = page.getViewport({ scale: 300 / 72 })
      const canvas = document.createElement('canvas')
      canvas.width = Math.round(viewport.width); canvas.height = Math.round(viewport.height)
      try {
        await page.render({ canvasContext: canvas.getContext('2d'), viewport, background: '#ffffff' }).promise
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'))
        if (!blob) throw new Error('Не удалось сформировать изображение.')
        pages.push(await withPrintResolution(blob))
      } finally { canvas.width = canvas.height = 0; page.cleanup() }
    }
  } finally { await task.destroy() }
  if (pages.length === 1) return { blob: pages[0], extension: 'png' }
  const files = {}
  for (const [index, blob] of pages.entries()) files['gto-' + String(index + 1).padStart(3, '0') + '.png'] = new Uint8Array(await blob.arrayBuffer())
  return { blob: new Blob([zipSync(files, { level: 0 })], { type: 'application/zip' }), extension: 'zip' }
}
