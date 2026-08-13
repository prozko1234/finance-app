/// The service worker's half of charge reminders, pulled into the generated Workbox worker by
/// `workbox.importScripts` in vite.config.ts.
///
/// A plain script in `public/` rather than a custom `injectManifest` worker: the only thing
/// needed here is two event listeners, and swapping the whole precaching strategy to get them
/// would put the app's offline behaviour at risk for the sake of forty lines.

self.addEventListener('push', (event) => {
  // A push with no readable body still deserves a notification: on iOS a subscription whose
  // payload fails to decrypt would otherwise be silently dropped, and the browser then revokes
  // permission for a worker that receives a push and shows nothing.
  let data = { title: 'Сьогодні списується', body: 'Загляни в застосунок.' }
  try {
    if (event.data) data = { ...data, ...event.data.json() }
  } catch {
    /* not JSON — the defaults above are already a usable notification */
  }

  event.waitUntil(
    self.registration.showNotification(data.title, {
      body: data.body,
      icon: '/pwa-192x192.png',
      badge: '/pwa-64x64.png',
      // Same tag for one day's charges, so a retry replaces the notification instead of
      // stacking a second copy of it on the lock screen.
      tag: data.tag || 'charges',
      renotify: false,
      data: { url: '/' },
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  // Focus the app if it is already open — opening a second tab of a PWA is how someone ends
  // up confirming the same charge twice.
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      for (const w of windows) {
        if ('focus' in w) return w.focus()
      }
      return self.clients.openWindow(event.notification.data?.url || '/')
    }),
  )
})
