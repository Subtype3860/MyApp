/** Получает публичные ссылки только для вложений выбранного отчёта. */
export async function fetchReportMediaLinks(cards, token, signal) {
  const items = new Map()
  for (const card of cards) for (const work of card.works) {
    for (const [type, files] of [['image', work.photos ?? []], ['video', work.videos ?? []]]) {
      for (const file of files) if (file.source !== 'maintenance') items.set(`${type}:${file.id}`, { id: file.id, type })
    }
  }
  const references = [...items.values()], links = {}
  for (let offset = 0; offset < references.length; offset += 200) {
    const response = await fetch('/api/report-media/links', {
      method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(references.slice(offset, offset + 200)), signal,
    })
    if (!response.ok) throw new Error('Не удалось создать ссылки на вложения отчёта. Повторите попытку.')
    for (const item of await response.json()) {
      if (!item.url?.startsWith('/api/report-media/')) throw new Error('Некорректная ссылка на вложение.')
      links[`${item.type}:${item.id}`] = new URL(item.url, window.location.origin).href
    }
  }
  // Фото карт ТО отдаются по подписанным ссылкам карты.
  for (const card of cards.filter(item => item.maintenance && item.cardId)) {
    const photoIds = card.works.flatMap(work => (work.photos ?? []).map(photo => photo.id))
    if (!photoIds.length) continue
    const response = await fetch(`/api/maintenance-plans/cards/${card.cardId}/media-links`, {
      method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(photoIds), signal,
    })
    if (!response.ok) throw new Error('Не удалось создать ссылки на фото карты ТО.')
    for (const item of await response.json()) {
      links[`image:${item.id ?? item.photoId}`] = new URL(item.url, window.location.origin).href
    }
  }
  if (references.some(item => !links[`${item.type}:${item.id}`])) throw new Error('Не для всех вложений созданы ссылки.')
  return links
}
