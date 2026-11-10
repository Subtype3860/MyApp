/** Кодирует canvas в blob указанного типа либо возвращает null, если браузер не умеет. */
function encode(canvas, type, quality) {
  return new Promise(resolve => {
    try {
      canvas.toBlob(blob => resolve(blob && blob.type === type ? blob : null), type, quality)
    } catch {
      resolve(null)
    }
  })
}

/** Декодирует файл: сначала createImageBitmap, затем <img> (нужен Safari и HEIC). */
async function decode(file) {
  if (typeof createImageBitmap === 'function') {
    try { return await createImageBitmap(file) } catch { /* пробуем через <img>. */ }
  }
  const url = URL.createObjectURL(file)
  try {
    const image = await new Promise((resolve, reject) => {
      const element = new Image()
      element.onload = () => resolve(element)
      element.onerror = () => reject(new Error('Браузер не смог прочитать изображение. Возможно, это HEIC — сделайте фото в формате JPEG.'))
      element.src = url
    })
    return image
  } finally {
    URL.revokeObjectURL(url)
  }
}

/**
 * Уменьшает фото и кодирует его в WebP, а если браузер этого не умеет (Safari до 17) —
 * в JPEG. Возвращает готовый File; имя и расширение соответствуют итоговому формату.
 */
export async function convertImageToWebp(file, { maxSide = 0, quality = 0.8 } = {}) {
  if (file.size > 32 * 1024 * 1024) {
    throw new Error(`Фото «${file.name}» превышает 32 МБ до конвертации.`)
  }
  const source = await decode(file)
  try {
    let width = source.width
    let height = source.height
    if (maxSide > 0 && Math.max(width, height) > maxSide) {
      const scale = maxSide / Math.max(width, height)
      width = Math.max(1, Math.round(width * scale))
      height = Math.max(1, Math.round(height * scale))
    }
    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Браузер не смог обработать изображение.')
    context.drawImage(source, 0, 0, width, height)

    const blob = await encode(canvas, 'image/webp', quality) ||
      await encode(canvas, 'image/jpeg', quality)
    if (!blob) throw new Error('Браузер не смог сжать фото. Попробуйте другой снимок.')

    const extension = blob.type === 'image/webp' ? 'webp' : 'jpg'
    const name = `${file.name.replace(/\.[^.]+$/, '')}.${extension}`
    return new File([blob], name, { type: blob.type, lastModified: file.lastModified })
  } finally {
    source.close?.()
  }
}
/** Считает файл изображением: Safari может не передавать MIME-тип (например, для HEIC). */
export function isImageFile(file) {
  return file.type.startsWith('image/') || (!file.type && /\.(jpe?g|png|webp|heic|heif)$/i.test(file.name || ''))
}