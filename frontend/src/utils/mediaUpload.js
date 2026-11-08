/** Преобразует загруженное изображение в WebP для отправки на сервер. */
export async function convertImageToWebp(file, { maxSide = 0, quality = 0.8 } = {}) {
  let bitmap
  try {
    if (file.size > 32 * 1024 * 1024) {
      throw new Error(`Фото «${file.name}» превышает 32 МБ до конвертации.`)
    }
    bitmap = await createImageBitmap(file)
    let width = bitmap.width
    let height = bitmap.height
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
    context.drawImage(bitmap, 0, 0, width, height)

    const blob = await new Promise((resolve, reject) => {
      canvas.toBlob(
        (result) => result
          ? resolve(result)
          : reject(new Error(`Не удалось преобразовать фото «${file.name}».`)),
        'image/webp',
        quality,
      )
    })
    if (blob.type !== 'image/webp') {
      throw new Error('Браузер не поддерживает преобразование изображений в WebP.')
    }

    const webpName = `${file.name.replace(/\.[^.]+$/, '')}.webp`
    return new File([blob], webpName, {
      type: 'image/webp',
      lastModified: file.lastModified,
    })
  } finally {
    bitmap?.close()
  }
}
