// Requests are deliberately not persisted in localStorage, service workers or offline queues.
export async function requestLocalAdvice(url, token, body, signal) {
  const response = await fetch(url, {
    method: body === undefined ? 'GET' : 'POST', cache: 'no-store',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: signal ?? AbortSignal.timeout(15000),
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(error?.message || 'Помощник недоступен. Повторите запрос.')
  }
  return response.json()
}

export function isAdviceFresh(result, now = Date.now()) {
  const advice = result?.advice
  return Boolean(advice && Date.parse(advice.generatedAt) <= now && Date.parse(advice.expiresAt) > now)
}
