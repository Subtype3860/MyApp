/* Service worker «Атлас — Ремонт»: офлайн-оболочка и кэш статики. */
const SHELL = 'atlas-shell-v2'
const ASSETS = 'atlas-assets-v2'
const SHELL_FILES = ['/', '/index.html', '/manifest.webmanifest', '/icons/icon-192.png', '/icons/icon-512.png']

self.addEventListener('install', event => {
  event.waitUntil(caches.open(SHELL).then(cache => cache.addAll(SHELL_FILES)).then(() => self.skipWaiting()))
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => Promise.all(keys.filter(key => key !== SHELL && key !== ASSETS).map(key => caches.delete(key))))
      .then(() => self.clients.claim())
  )
})

self.addEventListener('fetch', event => {
  const request = event.request
  if (request.method !== 'GET') return
  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api/')) return

  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request).then(response => {
        const copy = response.clone()
        caches.open(SHELL).then(cache => cache.put('/index.html', copy))
        return response
      }).catch(() => caches.match('/index.html').then(hit => hit || caches.match('/')))
    )
    return
  }

  if (url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/')) {
    event.respondWith(
      caches.match(request).then(hit => hit || fetch(request).then(response => {
        const copy = response.clone()
        caches.open(ASSETS).then(cache => cache.put(request, copy))
        return response
      }))
    )
  }
})
