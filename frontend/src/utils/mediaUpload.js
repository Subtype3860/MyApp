/** Преобразует загруженное изображение в WebP для отправки на сервер. */
export async function convertImageToWebp(file) {
  let bitmap
  try {
    if (file.size > 32 * 1024 * 1024) {
      throw new Error(`Фото «${file.name}» превышает 32 МБ до конвертации.`)
    }
    bitmap = await createImageBitmap(file)
    const canvas = document.createElement('canvas')
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Браузер не смог обработать изображение.')
    context.drawImage(bitmap, 0, 0)

    const blob = await new Promise((resolve, reject) => {
      canvas.toBlob(
        (result) => result
          ? resolve(result)
          : reject(new Error(`Не удалось преобразовать фото «${file.name}».`)),
        'image/webp',
        0.8,
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
