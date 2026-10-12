import notoSansUrl from '../assets/NotoSans.ttf?url'
import { cardMedia, cardState, formatReportDate, reportMediaLink, reportCardTitle, workPerformersLabel } from './shiftReport'

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

export async function createShiftReportPdf({ groups, date, shift, photos }) {
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
  function draw(text, size = 10, color = ink, x = left) {
    page.drawText(String(text), { x, y, size, font, color })
  }
  function newPage() {
    page = document.addPage([595.28, 841.89])
    y = 790
    draw('ОТЧЁТ ЗА СМЕНУ', 18)
    y -= 24
    draw(`${formatReportDate(date)}  ·  ${shift} смена`, 10, muted)
    y -= 32
  }
  function ensure(height) { if (y - height < 54) newPage() }
  function lines(text, size = 10) {
    const result = []
    for (const paragraph of String(text).replace(/[\x00-\x08\x0b\x0c\x0e-\x1f]/g, '').split('\n')) {
      let line = ''
      for (const word of paragraph.split(/\s+/).filter(Boolean)) {
        if (line && font.widthOfTextAtSize(`${line} ${word}`, size) > width) { result.push(line); line = '' }
        for (const char of word) {
          if (font.widthOfTextAtSize(line + char, size) > width) { result.push(line); line = '' }
          line += char
        }
        line += ' '
      }
      result.push(line.trimEnd())
    }
    return result
  }
  function link(label, url, x = left, linkY = y, linkWidth) {
    page.drawText(label, { x, y: linkY, size: 9, font, color: accent })
    const annotation = document.context.register(document.context.obj({
      Type: 'Annot', Subtype: 'Link', Rect: [x, linkY - 2, x + (linkWidth ?? font.widthOfTextAtSize(label, 9)), linkY + (linkWidth ? 110 : 12)],
      Border: [0, 0, 0], A: { S: 'URI', URI: PDFString.of(url) },
    }))
    page.node.addAnnot(annotation)
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
        const descriptions = card.works.flatMap(work => [
          ...lines(work.description || work.cause || 'Описание не указано.').map(text => ({ text, performer: false })),
          ...lines(workPerformersLabel(work), 9).map(text => ({ text, performer: true })),
          { text: '', performer: false },
        ])
        const media = cardMedia(card, 'image'), videos = cardMedia(card, 'video')
        const previews = media.slice(0, 2).filter(item => photos[item.id]?.blob)
        const titleLines = lines(reportCardTitle(card), 12)
        const height = 50 + (titleLines.length - 1) * 19 + descriptions.length * 15 + (previews.length ? 115 : 0) + (media.length || videos.length ? 24 : 0)
        ensure(Math.min(height, 660))
        for (const line of titleLines) { draw(line, 12); y -= 19 }
        if (!card.auxiliary) {
          const state = cardState(card)
          draw(state.label, 9, state.kind === 'stopped' ? rgb(.66, .20, .20) : muted); y -= 21
        }
        for (const line of descriptions) {
          if (y < 69) {
            newPage()
            for (const title of lines(`${reportCardTitle(card)} · продолжение`, 11)) { draw(title, 11); y -= 16 }
            y -= 6
          }
          draw(line.text, line.performer ? 9 : 10, line.performer ? muted : ink); y -= 15
        }
        if (previews.length) {
          ensure(140)
          for (let index = 0; index < previews.length; index++) {
            const item = previews[index]
            const embedded = await document.embedJpg(await photos[item.id].blob.arrayBuffer())
            const size = embedded.scale(Math.min(240 / embedded.width, 92 / embedded.height))
            const x = left + index * 254
            page.drawImage(embedded, { x, y: y - 92, width: size.width, height: size.height })
            link(`Фото ${index + 1}`, reportMediaLink(card, 'image', date, shift, item.id), x, y - 106, 240)
          }
          y -= 122
        }
        if (media.length || videos.length) {
          ensure(30)
          if (media.length) link(`Все фото · ${media.length}`, reportMediaLink(card, 'image', date, shift))
          if (videos.length) link(`Смотреть видео · ${videos.length}`, reportMediaLink(card, 'video', date, shift), left + 160)
          y -= 24
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
