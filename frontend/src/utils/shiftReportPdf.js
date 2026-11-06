import notoSansUrl from '../assets/NotoSans.ttf?url'
import { cardMedia, cardState, formatReportDate, reportCardTitle, cardWorkSections } from './shiftReport'

// Resize authenticated previews before embedding; originals and videos stay in the application.
export async function compactPhoto(blob) {
  const bitmap = await createImageBitmap(blob)
  try {
    const scale = Math.min(1, 720 / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.round(bitmap.width * scale))
    canvas.height = Math.max(1, Math.round(bitmap.height * scale))
    const context = canvas.getContext('2d')
    context.fillStyle = '#ffffff'
    context.fillRect(0, 0, canvas.width, canvas.height)
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    return await new Promise((resolve, reject) => canvas.toBlob(result => result ? resolve(result) : reject(new Error('Не удалось подготовить фото.')), 'image/jpeg', 0.76))
  } finally { bitmap.close() }
}

/** Создаёт PDF сменного отчёта с карточками работ и ссылками на вложения. */
export async function createShiftReportPdf({ groups, date, shift, photos, mediaLinks }) {
  const [{ PDFDocument, PDFName, PDFString, rgb }, { default: fontkit }, fontResponse] = await Promise.all([
    import('pdf-lib'), import('@pdf-lib/fontkit'), fetch(notoSansUrl),
  ])
  if (!fontResponse.ok) throw new Error('Не удалось загрузить шрифт отчёта.')
  const document = await PDFDocument.create()
  document.registerFontkit(fontkit)
  const font = await document.embedFont(await fontResponse.arrayBuffer(), { subset: true })
  const ink = rgb(.12, .17, .23), muted = rgb(.40, .45, .50), accent = rgb(.08, .37, .42)
  let page, y
  const width = 499, left = 48
  /** Рисует текст в PDF с заданными координатами и оформлением. */
  function draw(text, size = 10, color = ink, x = left) {
    page.drawText(String(text), { x, y, size, font, color })
  }
  /** Создаёт следующую страницу PDF и обновляет позицию вывода. */
  function newPage() {
    page = document.addPage([595.28, 841.89])
    y = 790
    draw('ОТЧЁТ ЗА СМЕНУ', 18)
    y -= 24
    draw(`${formatReportDate(date)}  ·  ${shift} смена`, 10, muted)
    y -= 32
  }
  /** Создаёт новую страницу, если оставшейся высоты недостаточно для следующего блока. */
  function ensure(height) { if (y - height < 54) newPage() }
  /** Разбивает текст на строки для вывода в PDF сменного отчёта. */
  function lines(text, size = 10, availableWidth = width) {
    const result = []
    for (const paragraph of String(text).replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, '').split('\n')) {
      let line = ''
      for (const word of paragraph.split(/\s+/).filter(Boolean)) {
        if (line && font.widthOfTextAtSize(`${line} ${word}`, size) > availableWidth) { result.push(line); line = '' }
        for (const char of word) {
          if (font.widthOfTextAtSize(line + char, size) > availableWidth) { result.push(line); line = '' }
          line += char
        }
        line += ' '
      }
      result.push(line.trimEnd())
    }
    return result
  }
  /** Добавляет ссылку на вложение в PDF сменного отчёта. */
  function link(label, url, x = left, linkY = y, linkWidth) {
    if (label === '▶') page.drawSvgPath('M 0 0 L 0 9 L 8 4.5 Z', { x, y: linkY + 9, color: accent })
    else if (label) page.drawText(label, { x, y: linkY, size: 9, font, color: accent })
    const annotation = document.context.register(document.context.obj({
      Type: 'Annot', Subtype: 'Link', Rect: [x, linkY - 2, x + (linkWidth ?? (label === '▶' ? 12 : font.widthOfTextAtSize(label, 9))), linkY + (linkWidth ? 92 : 12)],
      Border: [0, 0, 0], A: { S: 'URI', URI: PDFString.of(url) },
    }))
    page.node.addAnnot(annotation)
  }
  function mediaUrl(type, id) {
    const url = mediaLinks?.[`${type}:${id}`]
    if (!url) throw new Error('Не удалось создать публичную ссылку на вложение.')
    return url
  }
  newPage()
  for (const group of groups) {
    ensure(110)
    draw(group.location, 14, accent); y -= 25
    for (const category of group.categories) {
      const categoryLines = lines(category.name, 11)
      ensure(85 + categoryLines.length * 16)
      for (const line of categoryLines) { draw(line, 11, muted); y -= 16 }
      y -= 6
      for (const card of category.cards) {
        function ensureCard(height) {
          if (y - height >= 54) return
          newPage()
          for (const title of lines(`${reportCardTitle(card)} · продолжение`, 11)) { draw(title, 11); y -= 16 }
          y -= 6
        }
        function textBlock(text, performer = false, indent = 0, marker = '') {
          lines(text, performer ? 9 : 10, width - indent).forEach((line, index) => {
            ensureCard(15)
            if (index === 0 && marker) draw(marker, performer ? 9 : 10, performer ? muted : ink, left + indent - 12)
            draw(line, performer ? 9 : 10, performer ? muted : ink, left + indent)
            y -= 15
          })
        }
        const titleLines = lines(reportCardTitle(card), 12)
        ensure(titleLines.length * 19 + 180)
        for (const line of titleLines) { draw(line, 12); y -= 19 }
        if (!card.auxiliary) {
          const state = cardState(card)
          draw(state.label, 9, state.kind === 'stopped' ? rgb(.66, .20, .20) : muted); y -= 21
        }
        const sections = cardWorkSections(card)
        for (let sectionIndex = 0; sectionIndex < sections.length; sectionIndex++) {
          const section = sections[sectionIndex]
          if (sectionIndex) {
            ensureCard(165)
            page.drawLine({ start: { x: left, y }, end: { x: left + width, y }, thickness: .5, dashArray: [3, 3], color: muted })
            y -= 18
          }
          for (const work of section.works) {
            const workCard = { ...card, works: [work] }
            const media = cardMedia(workCard, 'image'), videos = cardMedia(workCard, 'video')
            const previews = media.slice(0, 2).filter(item => photos[item.id]?.blob)
            const description = work.description || work.cause || 'Описание не указано.'
            ensureCard((previews.length ? 104 : 0) + Math.min(lines(description, 10, width - 12).length, 3) * 15 + 12)
            if (previews.length) {
              for (let index = 0; index < previews.length; index++) {
                const item = previews[index]
                const embedded = await document.embedJpg(await photos[item.id].blob.arrayBuffer())
                const size = embedded.scale(Math.min(240 / embedded.width, 92 / embedded.height))
                const x = left + index * 254
                page.drawImage(embedded, { x, y: y - 92, width: size.width, height: size.height })
                link('', mediaUrl('image', item.id), x, y - 92, size.width)
              }
              y -= 104
            }
            textBlock(description, false, 12, '•')
            const remaining = media.filter(item => !previews.some(preview => preview.id === item.id))
            const shortcuts = [...remaining.map(item => ({ item, type: 'image', label: '+' })), ...videos.map(item => ({ item, type: 'video', label: '▶' }))]
            for (let offset = 0; offset < shortcuts.length; offset += 12) {
              ensureCard(24)
              shortcuts.slice(offset, offset + 12).forEach(({ item, type, label }, index) => link(label, mediaUrl(type, item.id), left + index * 30))
              y -= 24
            }
            y -= 10
          }
          const performers = section.performers
          ensureCard(performers.groups.length ? 45 : 15)
          if (performers.groups.length) {
            textBlock('Исполнители:', true)
            for (const group of performers.groups) {
              ensureCard(30)
              textBlock(group.position + ':', true, 12, '-')
              textBlock(group.names.join('; '), true, 24, '+')
            }
          } else textBlock('Исполнители пока не подтверждены', true)
          y -= 12
        }
        page.drawLine({ start: { x: left, y }, end: { x: left + width, y }, thickness: .5, color: rgb(.86, .89, .90) })
        y -= 22
      }
    }
  }
  const pages = document.getPages()
  pages.forEach((sheet, index) => sheet.drawText(`${index + 1} / ${pages.length}`, { x: 510, y: 28, size: 8, font, color: muted }))
  document.setTitle(`Отчёт за ${formatReportDate(date)}, ${shift} смена`)
  return new Blob([await document.save()], { type: 'application/pdf' })
}
