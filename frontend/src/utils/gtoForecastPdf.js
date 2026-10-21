import notoSansUrl from '../assets/NotoSans.ttf?url'
import boldFontUrl from '../assets/LiberationSans-Bold.ttf?url'
import italicFontUrl from '../assets/LiberationSans-Italic.ttf?url'
import { climateLabels, forecastLabel, forecastPdfNoteBlocks, forecastDocumentRow, responsibleShortName, engineHoursText } from './gtoForecast'
import { formatReportDate, vehicleTitle } from './shiftReport'
import { fixedHoursText, hoursReportHeader } from './gtoForecast'

/** Создаёт PDF прогноза ГТО с группами техники, показаниями и примечаниями. */
export async function createGtoForecastPdf({ date, shift, mechanic, organisation, site, groups }) {
  const [{ PDFDocument, rgb, StandardFonts }, { default: fontkit }, fontResponse, boldResponse, italicResponse] = await Promise.all([
    import('pdf-lib'), import('@pdf-lib/fontkit'), fetch(notoSansUrl), fetch(boldFontUrl), fetch(italicFontUrl),
  ])
  if (!fontResponse.ok || !boldResponse.ok || !italicResponse.ok) throw new Error('Не удалось загрузить шрифт.')
  const pdf = await PDFDocument.create()
  pdf.registerFontkit(fontkit)
  const regularBytes = await fontResponse.arrayBuffer(), boldBytes = await boldResponse.arrayBuffer(), italicBytes = await italicResponse.arrayBuffer()
  const font = await pdf.embedFont(regularBytes, { subset: true })
  const boldFont = await pdf.embedFont(boldBytes, { subset: true })
  const italicFont = await pdf.embedFont(italicBytes, { subset: true })
  const hoursFont = await pdf.embedFont(StandardFonts.CourierBold)
  const italicMetrics = fontkit.create(new Uint8Array(italicBytes))
  const regularMetrics = fontkit.create(new Uint8Array(regularBytes)), boldMetrics = fontkit.create(new Uint8Array(boldBytes))
  const red = rgb(.8, .08, .08)
  const ink = rgb(.12, .17, .23), muted = rgb(.4, .45, .5), accent = rgb(.08, .37, .42)
  const widths = [28, 124, 63, 65, 94, 28, 35, 186, 94, 64, 69].map(width => width * .94)
  // Five positions per engine plus " / ": exactly thirteen monospaced positions.
  const hoursWidth = hoursFont.widthOfTextAtSize('00000 / 00000', 7) + 8
  widths[7] += widths[2] - hoursWidth
  widths[2] = hoursWidth
  // Fit the ten-character status labels using the actual regular/bold font metrics.
  const statusWidth = Math.max(...['Неисправно', 'Неисправен'].flatMap(text =>
    [font, boldFont].map(face => face.widthOfTextAtSize(text, 7)))) + 8.1
  widths[7] += widths[4] - statusWidth
  widths[4] = statusWidth
  const headers = ['№', 'Техника / номер', hoursReportHeader(groups), 'М/ч при замене ДВС', 'Исправно /\nНеисправно', 'КТГ', 'ТО', 'Неисправность / примечание', 'Плановый выход', 'Начало простоя', 'Система кондиционирования и отопления'].map(text => [{ text, bold: true }])
  const left = 20, rowHeight = 8, rowPadding = 3, tableWidth = widths.reduce((a,b)=>a+b,0)
  let page, y
  let pageHasRows = false
  let pageHeight = 595.28
  const bottom = 30
  const /** Нормализует пробельные символы перед размещением текста в PDF. */ clean = text => String(text ?? '').replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, '')
  /** Разбивает текст на строки, помещающиеся в заданную ширину PDF-ячейки. */
  function wrap(text, width, size = 7, textFont = font) {
    const lines = []
    for (const paragraph of clean(text).split('\n')) {
      let line = ''
      for (const word of paragraph.split(/\s+/).filter(Boolean)) {
        if (line && textFont.widthOfTextAtSize(`${line} ${word}`, size) > width) { lines.push(line); line = '' }
        for (const char of (line ? ' ' : '') + word) {
          if (textFont.widthOfTextAtSize(line + char, size) > width) { lines.push(line); line = '' }
          line += char
        }
      }
      lines.push(line)
    }
    return lines.length ? lines : ['']
  }
  /** Рисует строки текста в PDF, добавляя страницу при нехватке места. */
  function textLines(text, size = 10, color = ink) {
    for (const line of wrap(text, tableWidth, size)) {
      if (y < 40) newPage()
      page.drawText(line, { x: left, y, font, size, color }); y -= size + 5
    }
  }
  /** Рисует текст с уменьшенными межстрочными отступами в PDF. */
  function compactText(text, size, color) {
    for (const line of wrap(text, tableWidth, size)) {
      y -= size
      page.drawText(line, { x: left, y, font, size, color })
      y -= 3
    }
  }
  /** Переносит фрагменты текста с сохранением их выделения. */
  function wrapSpans(spans, width) {
    const lines = []; let runs = [], used = 0
    const /** Добавляет накопленную строку в результат переноса текста и очищает буфер. */ flush = () => { lines.push({ runs }); runs = []; used = 0 }
    for (const span of spans) {
      const face = span.italic ? italicFont : span.bold ? boldFont : font
      for (const token of clean(span.text).split(/(\s+)/).filter(Boolean)) {
        if (used && used + face.widthOfTextAtSize(token, 7) > width) flush()
        for (const char of token) {
          if (!used && /\s/.test(char)) continue
          const advance = face.widthOfTextAtSize(char, 7)
          if (used + advance > width) flush()
          const last = runs.at(-1)
          if (last && last.italic === span.italic && last.bold === span.bold) last.text += char
          else runs.push({ ...span, text: char })
          used += advance
        }
      }
    }
    if (runs.length || !lines.length) flush()
    return lines
  }
  const cellLineCache = new Map()
  /** Подготавливает строки и стили содержимого ячейки PDF-таблицы. */
  function cellLines(cells) {
    return cells.map((cell, i) => {
      const key = `${widths[i]}:${JSON.stringify(cell)}`
      if (cellLineCache.has(key)) return cellLineCache.get(key)
      const blocks = Array.isArray(cell) ? cell : [{ text: cell ?? '' }]
      const result = blocks.flatMap(block => {
        const item = typeof block === 'object' ? block : { text: block }
        if (item.fixedHours) return [{ text: item.text, fixedHours: true, bold: true }]
        if (item.spans) return wrapSpans(item.spans, widths[i] - 8)
        const wrapped = wrap(item.text, widths[i] - 8, 7, item.italic ? italicFont : item.bold ? boldFont : font)
        return wrapped.map((text, index) => ({ text, bold: item.bold, italic: item.italic, red: item.red, divider: item.divider && index === wrapped.length - 1 }))
      })
      cellLineCache.set(key, result)
      return result
    })
  }
  /** Рисует линию сетки таблицы в PDF. */
  function tableLine(cells, fill, continuation = false, keepTogether = true) {
    const lines = cellLines(cells)
    const count = Math.max(1, ...lines.map(item=>item.length))
    const fullHeight = count * rowHeight + rowPadding
    // Keep ordinary rows together; only a row taller than a page may split.
    if (!fill && keepTogether && fullHeight > y - bottom && fullHeight <= pageHeight - 150) newPage()

    let offset = 0
    while (offset < count) {
      if (y - bottom < rowHeight + rowPadding) newPage()
      const batch = Math.min(count - offset, Math.max(1, Math.floor((y - bottom - rowPadding) / rowHeight)))
      const height = batch * rowHeight + rowPadding
      let x = left
      for (let i=0;i<widths.length;i++) {
        page.drawRectangle({x,y:y-height,width:widths[i],height,borderWidth:.4,borderColor:rgb(.78,.82,.85),...(fill ? {color:fill} : {})})
        const cellLines = lines[i].slice(offset, offset + batch)
        if (!cellLines.length && offset && i === 0 && continuation) cellLines.push({ text: '↳' })
        const centerMetrics = cellLines.every(line => line.bold) ? boldMetrics : regularMetrics
        const fontCenter = (centerMetrics.ascent + centerMetrics.descent) * 7 / centerMetrics.unitsPerEm / 2
        const firstBaseline = fill ? y - 8 : y - height / 2 + (cellLines.length - 1) * rowHeight / 2 - fontCenter
        for (let j=0;j<cellLines.length;j++) {
          const line = cellLines[j]
          const baseline = firstBaseline-j*rowHeight
          const textFont = line.fixedHours ? hoursFont : line.italic ? italicFont : line.bold ? boldFont : font
          if (line.runs) {
            let runX = x+4
            for (const run of line.runs) {
              const face = run.italic ? italicFont : run.bold ? boldFont : font
              page.drawText(run.text,{x:runX,y:baseline,font:face,size:7,color:ink})
              runX += face.widthOfTextAtSize(run.text,7)
            }
          }
          if(line.text) page.drawText(line.text,{x:fill ? x + (widths[i] - textFont.widthOfTextAtSize(line.text,7)) / 2 : x+4,y:baseline,font:textFont,size:7,color:line.red ? red : ink})
          if(line.divider) {
            const metrics = line.italic ? italicMetrics : line.bold ? boldMetrics : regularMetrics
            const run = metrics.layout(line.text)
            const bottom = Math.min(0, ...run.glyphs.map((glyph, index) => glyph.bbox.minY + (run.positions[index]?.yOffset ?? 0)))
            const dividerY = baseline + bottom * 7 / metrics.unitsPerEm
            page.drawLine({start:{x:x+4,y:dividerY},end:{x:x+widths[i]-4,y:dividerY},thickness:.5,color:ink})
          }
        }
        x+=widths[i]
      }
      if (!fill) pageHasRows = true
      y-=height; offset+=batch
      if(offset<count)newPage()
    }
  }
  const companyLines = [
    ...wrap(organisation, 280, 7, boldFont).filter(Boolean),
    ...(site ? wrap(`Участок «${site}»`, 280, 7, boldFont) : []),
  ]
  const responsibleWidth = 220, labelWidth = 88, responsibleLineHeight = 9
  const mechanicLines = wrap(responsibleShortName(mechanic), responsibleWidth - labelWidth - 8, 7).filter(Boolean)
  const nameRowHeight = Math.max(1, mechanicLines.length) * responsibleLineHeight + 4
  const responsibleHeight = 13 + nameRowHeight
  const reportHeaderHeight = Math.max(companyLines.length * 8 + 8, responsibleHeight + 12)
  /** Создаёт следующую страницу PDF и обновляет позицию вывода. */
  function newPage(withTableHeader = true) {
    page = pdf.addPage([841.89, pageHeight]); pageHasRows = false
    const top = pageHeight - 20
    y = top - reportHeaderHeight
    companyLines.forEach((text, index) => page.drawText(text, {
      x: left, y: y + 4 + (companyLines.length - 1 - index) * 8, font: boldFont, size: 7, color: ink,
    }))
    const boxX = left + tableWidth - responsibleWidth
    page.drawRectangle({ x: boxX, y: top - responsibleHeight, width: responsibleWidth, height: responsibleHeight, borderWidth: .5, borderColor: ink })
    page.drawLine({start:{x:boxX+labelWidth,y:top},end:{x:boxX+labelWidth,y:top-responsibleHeight},thickness:.5,color:ink})
    page.drawLine({start:{x:boxX,y:top-13},end:{x:boxX+responsibleWidth,y:top-13},thickness:.5,color:ink})
    page.drawText('Ответственные лица:',{x:boxX+3,y:top-9,font,size:6.5,color:ink})
    page.drawText('ФИО',{x:boxX+labelWidth+4,y:top-9,font,size:6.5,color:ink})
    page.drawText('Механик:',{x:boxX+3,y:top-22,font,size:7,color:ink})
    mechanicLines.forEach((text,index)=>page.drawText(text,{x:boxX+labelWidth+4,y:top-22-index*responsibleLineHeight,font,size:7,color:ink}))
    page.drawText('Состояние оборудования на',{x:left+tableWidth*.60,y:y+4,font:boldFont,size:7,color:ink})
    page.drawText(formatReportDate(date),{x:left+tableWidth*.82,y:y+4,font:boldFont,size:7,color:ink})
    page.drawText(String(shift),{x:left+tableWidth*.95,y:y+4,font:boldFont,size:7,color:ink})
    if (withTableHeader) tableLine(headers,rgb(.92,.95,.96))
  }
  /** Преобразует строку прогноза в содержимое ячеек PDF-таблицы. */
  function rowCells(source, index) {
    const row = forecastDocumentRow(source)
    const noteBlocks = forecastPdfNoteBlocks(row)
    return [
        index, vehicleTitle(row.vehicle),
        [{ text: fixedHoursText(row), fixedHours: true }],
        [{ text: engineHoursText(row, true), red: true }],
        [{ text: forecastLabel(row.forecast), bold: row.forecast === 'faulty', red: row.forecast === 'faulty' }],
        row.ktg != null && row.ktg !== '' ? `${row.ktg}%` : '',
        [{ text: row.maintenance ?? '', bold: true, red: true }],
        noteBlocks,row.plannedRelease,
        row.downtimeStartedAt ? new Date(row.downtimeStartedAt).toLocaleDateString('ru-RU') : '',[{ text: row.climate === 'unknown' ? '' : climateLabels[row.climate], bold: row.climate === 'faulty', red: row.climate === 'faulty' }],
      ]
  }
  const summaryTexts = groups.map(group => `${group.name}: ${group.ready} из ${group.rows.length}`)
  const summaryHeight = 30 + summaryTexts.reduce((height, text) => height + wrap(text, tableWidth, 9).length * 14, 0)
  const headerHeight = 20 + reportHeaderHeight + Math.max(...cellLines(headers).map(lines => lines.length)) * rowHeight + rowPadding
  const contentHeight = headerHeight + groups.reduce((height, group) => height + 25 + group.rows.reduce((sum, row) =>
    sum + Math.max(1, ...cellLines(rowCells(row, 1)).map(lines => lines.length)) * rowHeight + rowPadding, 0), 0) + summaryHeight + bottom
  // Prefer a single A4 page; a tall table can use portrait orientation with the same column proportions.
  const portrait = contentHeight > 595.28 && contentHeight <= 1190.55
  if (portrait) pageHeight = 1190.55
  newPage()
  for (const group of groups) {
    const firstRowHeight = group.rows.length ? Math.max(...cellLines(rowCells(group.rows[0], 1)).map(lines => lines.length)) * rowHeight + rowPadding : 0
    if (pageHasRows && y - bottom < 18 + Math.min(firstRowHeight, pageHeight - 170)) newPage()
    y -= 2; compactText(group.name,8,accent)
    let index=0
    for (const row of group.rows) {
      const cells = rowCells(row, ++index)
      const height = Math.max(1, ...cellLines(cells).map(lines => lines.length)) * rowHeight + rowPadding
      if (index > 1 && index === group.rows.length && height + 14 > y - bottom && height + 14 <= pageHeight - 150) newPage()
      tableLine(cells,null,true,index > 1)
    }
    const total = `Выход к смене: ${group.ready} из ${group.rows.length}`
    if (y - bottom < 14) newPage(false)
    y -= 2; compactText(total,7,muted)
  }
  if (y - bottom < summaryHeight) newPage(false)
  y -= 10
  textLines('СВОДКА ВЫХОДА',12,accent)
  for (const text of summaryTexts) textLines(text,9)
  for(const [i,item] of pdf.getPages().entries())item.drawText(`${i+1} / ${pdf.getPageCount()}`,{x:780,y:18,font,size:8,color:muted})
  if (portrait) {
    const fitted = await PDFDocument.create()
    const embeddedPages = await fitted.embedPdf(await pdf.save())
    for (const embedded of embeddedPages) {
      const target = fitted.addPage([595.28, 841.89])
      target.drawPage(embedded, { x: 0, y: 0, width: 595.28, height: 841.89 })
    }
    return new Blob([await fitted.save()], {type:'application/pdf'})
  }
  return new Blob([await pdf.save()], {type:'application/pdf'})
}
