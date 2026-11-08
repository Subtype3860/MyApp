/** Настройки представления из подписанной сервером сессии. Старые сессии сохраняют видимость. */
export function pageBlockVisible(token, key) {
  try {
    const part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const claims = JSON.parse(atob(part))
    const role = claims.role || claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
    if (role === 'administrator') return true
    const permissions = Array.isArray(claims.permission) ? claims.permission : [claims.permission]
    return !permissions.includes(`ui.hidden.${key}`)
  } catch { return true }
}

export function reportPresentation(groups, token) {
  const media = pageBlockVisible(token, 'shift_report.media')
  const performers = pageBlockVisible(token, 'shift_report.performers')
  return groups.map(group => ({ ...group, categories: group.categories.map(category => ({
    ...category, cards: category.cards.map(card => ({ ...card, hidePerformers: !performers,
      works: card.works.map(work => media ? work : { ...work, photos: [], videos: [] }),
    })),
  })) }))
}
