import notoSansUrl from '../assets/NotoSans.ttf?url'

const itemsPerPage = 5
const maximumFontSize = 11
const minimumFontSize = 4
let templateBytesPromise
let templateDocumentPromise
let fontBytesPromise
let pdfLibrariesPromise

/** Загружает библиотеки формирования PDF с повторным использованием результата. */
function getPdfLibraries() {
  pdfLibrariesPromise ??= Promise.all([
    import('pdf-lib'),
    import('@pdf-lib/fontkit'),
  ])
    .then(([pdfLib, fontkitModule]) => ({
      PDFDocument: pdfLib.PDFDocument,
      PDFName: pdfLib.PDFName,
      TextAlignment: pdfLib.TextAlignment,
      clip: pdfLib.clip,
      endPath: pdfLib.endPath,
      fontkit: fontkitModule.default,
      popGraphicsState: pdfLib.popGraphicsState,
      pushGraphicsState: pdfLib.pushGraphicsState,
      rectangle: pdfLib.rectangle,
    }))
    .catch((error) => {
      pdfLibrariesPromise = undefined
      throw error
    })
  return pdfLibrariesPromise
}

/** Загружает ресурс и возвращает его двоичное содержимое. */
async function fetchBytes(url, token) {
  const response = await fetch(url, {
    headers: { Authorization: 'Bearer ' + token },
  })
  if (!response.ok) {
    throw new Error('Не удалось загрузить шаблон PDF.')
  }
  return new Uint8Array(await response.arrayBuffer())
}

/** Получает байты PDF-шаблона требования. */
function getTemplateBytes(token) {
  templateBytesPromise ??= fetchBytes(
    '/api/documents/components/template',
    token,
  ).catch((error) => {
    templateBytesPromise = undefined
    throw error
  })
  return templateBytesPromise
}

/** Получает байты шрифта для заполнения PDF. */
function getFontBytes() {
  fontBytesPromise ??= fetch(notoSansUrl)
    .then(async (response) => {
      if (!response.ok) throw new Error('Не удалось загрузить шрифт PDF.')
      return new Uint8Array(await response.arrayBuffer())
    })
    .catch((error) => {
      fontBytesPromise = undefined
      throw error
    })
  return fontBytesPromise
}

/** Загружает PDF-шаблон как документ для дальнейшего заполнения. */
function getTemplateDocument(templateBytes, PDFDocument) {
  templateDocumentPromise ??= PDFDocument.load(templateBytes)
    .catch((error) => {
      templateDocumentPromise = undefined
      throw error
    })
  return templateDocumentPromise
}

/** Форматирует дату для поля PDF-документа. */
function formatDate(value) {
  const [year, month, day] = String(value).split('-')
  return year && month && day ? `${day}.${month}.${year}` : String(value)
}

/** Форматирует количество материала для отображения. */
function formatQuantity(value) {
  const quantity = Number(value)
  return Number.isFinite(quantity)
    ? quantity.toLocaleString('en-US', {
        useGrouping: false,
        maximumFractionDigits: 20,
      })
    : ''
}

/** Разбивает текст на строки с учётом ширины и метрик шрифта. */
function wrapText(text, font, fontSize, width) {
  if (!text) return ['']

  const lines = []
  for (const paragraph of text.split(/\r?\n/)) {
    const words = paragraph.split(/\s+/).filter(Boolean)
    if (words.length === 0) {
      lines.push('')
      continue
    }

    let line = ''
    for (const word of words) {
      const candidate = line ? `${line} ${word}` : word
      if (font.widthOfTextAtSize(candidate, fontSize) <= width) {
        line = candidate
        continue
      }

      if (line) {
        lines.push(line)
        line = ''
      }
      if (font.widthOfTextAtSize(word, fontSize) <= width) {
        line = word
        continue
      }

      let fragment = ''
      for (const character of word) {
        const candidateFragment = fragment + character
        if (
          fragment &&
          font.widthOfTextAtSize(candidateFragment, fontSize) > width
        ) {
          lines.push(fragment)
          fragment = character
        } else {
          fragment = candidateFragment
        }
      }
      line = fragment
    }
    if (line) lines.push(line)
  }
  return lines.length ? lines : ['']
}

/** Проверяет, помещается ли текст в заданную область PDF. */
function fitsField(text, font, fontSize, width, height, multiline) {
  const availableWidth = Math.max(width - 2, 1)
  const availableHeight = Math.max(height - 2, 1)
  if (!multiline) {
    return (
      font.widthOfTextAtSize(text, fontSize) <= availableWidth &&
      font.heightAtSize(fontSize) <= availableHeight
    )
  }

  const lineCount = wrapText(
    text,
    font,
    fontSize,
    availableWidth,
  ).length
  return font.heightAtSize(fontSize) * 1.2 * lineCount <= availableHeight
}

/** Приводит прямоугольник поля к формату координат отрисовки. */
function normalizeRectangle(rectangle) {
  return {
    x: rectangle.width < 0
      ? rectangle.x + rectangle.width
      : rectangle.x,
    y: rectangle.height < 0
      ? rectangle.y + rectangle.height
      : rectangle.y,
    width: Math.abs(rectangle.width),
    height: Math.abs(rectangle.height),
  }
}

/** Извлекает расположение полей из PDF-шаблона. */
function getFieldLayouts(templateDocument) {
  const form = templateDocument.getForm()
  const pageIndices = new Map(
    templateDocument.getPages().map((page, index) => [page.ref, index]),
  )
  return new Map(form.getFields().map((field) => {
    return [
      field.getName(),
      field.acroField.getWidgets().map((widget) => {
        const rectangle = widget.getRectangle()
        return {
          alignment: field.getAlignment?.() ?? 0,
          pageIndex: pageIndices.get(widget.P()) ?? 0,
          rectangle: normalizeRectangle(rectangle),
        }
      }),
    ]
  }))
}

/** Размещает текст в области поля PDF с выбранным оформлением. */
function drawFieldText(
  page,
  layout,
  value,
  font,
  operators,
  multiline,
  alignment,
) {
  const text = String(value ?? '')
  const fieldRectangle = layout?.rectangle
  if (!text || !fieldRectangle) return

  let fontSize = maximumFontSize
  while (
    fontSize > minimumFontSize &&
    !fitsField(
      text,
      font,
      fontSize,
      fieldRectangle.width,
      fieldRectangle.height,
      multiline,
    )
  ) {
    fontSize -= 0.5
  }

  const bounds = {
    x: fieldRectangle.x + 1,
    y: fieldRectangle.y + 1,
    width: Math.max(fieldRectangle.width - 2, 1),
    height: Math.max(fieldRectangle.height - 2, 1),
  }
  const textAlignment = alignment ?? layout.alignment
  const lines = multiline
    ? wrapText(text, font, fontSize, bounds.width)
    : [text.replace(/\r?\n/g, ' ')]
  const lineHeight = font.heightAtSize(fontSize) * 1.2

  page.pushOperators(
    operators.pushGraphicsState(),
    operators.rectangle(
      fieldRectangle.x,
      fieldRectangle.y,
      fieldRectangle.width,
      fieldRectangle.height,
    ),
    operators.clip(),
    operators.endPath(),
  )
  lines.forEach((line, index) => {
    const textWidth = font.widthOfTextAtSize(line, fontSize)
    const x = textAlignment === 1
      ? bounds.x + (bounds.width - textWidth) / 2
      : textAlignment === 2
        ? bounds.x + bounds.width - textWidth
        : bounds.x
    const y = multiline
      ? bounds.y + bounds.height - lineHeight * (index + 1)
      : bounds.y + (
          bounds.height -
          font.heightAtSize(fontSize, { descender: false })
        ) / 2

    page.drawText(line, { x, y, size: fontSize, font })
  })
  page.pushOperators(operators.popGraphicsState())
}

/** Заполняет страницу шаблона данными требования. */
function fillPage(
  pages,
  fieldLayouts,
  request,
  items,
  itemOffset,
  font,
  operators,
  TextAlignment,
) {
  const /** Рисует текст в PDF с заданными координатами и оформлением. */ draw = (
    name,
    value,
    multiline = false,
    alignment,
  ) => {
    const layouts = fieldLayouts.get(name) ?? []
    layouts.forEach((layout) => {
      drawFieldText(
        pages[layout.pageIndex],
        layout,
        value,
        font,
        operators,
        multiline,
        alignment,
      )
    })
  }

  draw('date_n', formatDate(request.date))
  const vehicleNumbers = request.vehicleNumbers ?? []
  for (let index = 0; index < itemsPerPage; index++) {
    const vehicleNumber = vehicleNumbers.length <= 1
      ? vehicleNumbers[0] ?? request.vehicleNumber
      : vehicleNumbers[itemOffset + index] ?? ''
    draw(
      `car_${index + 1}`,
      index < items.length ? vehicleNumber : '',
      false,
      TextAlignment.Center,
    )
  }

  draw('job', request.jobName, true)
  draw('d_1', request.issuerPosition, true)
  draw('f_1', request.issuerName, true)
  draw('d_2', request.authorPosition, true)
  draw('f_2', request.authorName, true)

  for (let index = 0; index < itemsPerPage; index++) {
    const item = items[index]
    draw(`text${index + 1}`, item?.name, true)
    draw(`u_${index + 1}`, item?.unit, true)
    draw(`a_${index + 1}`, formatQuantity(item?.quantity))
  }
}

/** Создаёт заполненный PDF-документ требования из данных сервера. */
export async function createPdfDocument(request, token) {
  const [templateBytes, fontBytes, libraries] = await Promise.all([
    getTemplateBytes(token),
    getFontBytes(),
    getPdfLibraries(),
  ])
  const {
    PDFDocument,
    PDFName,
    TextAlignment,
    fontkit,
    ...operators
  } = libraries
  const pages = request.pages?.length
    ? request.pages
    : Array.from(
        { length: Math.ceil(request.items.length / itemsPerPage) },
        (_, index) =>
          request.items.slice(
            index * itemsPerPage,
            (index + 1) * itemsPerPage,
          ),
      )
  const templateDocument = await getTemplateDocument(
    templateBytes,
    PDFDocument,
  )
  const fieldLayouts = getFieldLayouts(templateDocument)
  const output = await PDFDocument.create()
  output.setTitle('Требование-накладная')
  output.registerFontkit(fontkit)
  const font = await output.embedFont(fontBytes, { subset: false })
  const templatePageIndices = templateDocument.getPageIndices()
  let itemOffset = 0

  for (const items of pages) {
    const documentPages = await output.copyPages(
      templateDocument,
      templatePageIndices,
    )
    documentPages.forEach((page) => {
      page.node.delete(PDFName.of('Annots'))
    })
    fillPage(
      documentPages,
      fieldLayouts,
      request,
      items,
      itemOffset,
      font,
      operators,
      TextAlignment,
    )
    itemOffset += items.length

    documentPages.forEach((page) => output.addPage(page))
  }

  return new Blob(
    [await output.save()],
    { type: 'application/pdf' },
  )
}

/** Формирует PDF требования и открывает его для просмотра. */
export function renderPdfDocument(previewWindow, pdfBlob) {
  // Прямая навигация вместо <iframe src="blob:...">: встраивание blob-фреймов
  // блокируется директивой frame-src (CSP) и ограничениями встраивания браузера,
  // из-за чего пользователь видит «Этот контент заблокирован». Навигация уже
  // открытого окна от этих ограничений не зависит.
  previewWindow.location.href = URL.createObjectURL(pdfBlob)
}
